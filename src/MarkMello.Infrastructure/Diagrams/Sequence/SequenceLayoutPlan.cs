using MarkMello.Application.Abstractions;
using Naiad;
using Naiad.Diagrams.Sequence;

namespace MarkMello.Infrastructure.Diagrams.Sequence;

/// <summary>
/// Where every part of a sequence diagram goes once its labels are measured.
/// Naiad 1.4 draws participants 100 px wide and 150 px apart whatever their
/// names, so long names and message labels spill over each other and off
/// the canvas. Here a participant is as wide as <c>sequence.width</c> or its
/// name, neighbours are <c>actorMargin</c> apart, and a gap widens until the
/// labels that sit in it fit.
/// </summary>
internal sealed class SequenceLayoutPlan
{
    public const double TitleFontSize = 16;
    public const double NoteHeight = 40;
    public const double NoteFold = 8;
    public const double SelfLoopWidth = 40;
    public const double SelfLoopHeight = 30;
    public const double SelfLabelOffset = 45;

    // Space kept around labels: inside a participant box, between a message
    // label and the lifelines it spans, inside a note.
    private const double BoxLabelPadding = 10;
    private const double MessageLabelMargin = 12;
    private const double NotePadding = 12;
    private const double NoteMinWidth = 120;
    private const double NoteGap = 10;

    private readonly Dictionary<string, int> _indices;
    private readonly double[] _centers;
    private readonly double[] _widths;
    private readonly Func<string, double> _textWidth;

    public SequenceLayoutPlan(SequenceModel model, SequenceDiagramSettings settings,
        RenderOptions options, IDiagramTextMeasurer measurer)
    {
        Model = model;
        _textWidth = text => measurer.Measure(text, options.FontFamily, options.FontSize, bold: false).Width;
        _indices = model.Participants.Select(static (participant, index) => (participant.Id, index))
            .ToDictionary(static item => item.Id, static item => item.index, StringComparer.Ordinal);
        MessageLabels = BuildMessageLabels(model);

        _widths = model.Participants
            .Select(participant => Math.Max(settings.Width, _textWidth(participant.DisplayName) + BoxLabelPadding * 2))
            .ToArray();
        var gaps = Enumerable.Range(1, _widths.Length - 1)
            .Select(index => (_widths[index - 1] + _widths[index]) / 2 + settings.ActorMargin)
            .ToArray();
        WidenGaps(gaps);

        _centers = new double[_widths.Length];
        _centers[0] = options.Padding + _widths[0] / 2;
        for (var index = 1; index < _centers.Length; index++)
        {
            _centers[index] = _centers[index - 1] + gaps[index - 1];
        }

        var minX = options.Padding;
        var maxX = _centers[^1] + _widths[^1] / 2;
        for (var index = 0; index < model.Elements.Count; index++)
        {
            if (model.Elements[index] is Note note)
            {
                var (x, width) = NoteBounds(note);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x + width);
            }
            else if (model.Elements[index] is Message message && message.FromId == message.ToId)
            {
                var labelWidth = MessageLabels[index] is { } label ? _textWidth(label) : 0;
                maxX = Math.Max(maxX, Center(message.FromId) + Math.Max(SelfLoopWidth, SelfLabelOffset + labelWidth));
            }
        }

        // A note left of the first participant moves the whole diagram right
        // instead of being clipped at the canvas edge.
        var shift = Math.Max(0, options.Padding - minX);
        for (var index = 0; index < _centers.Length; index++)
        {
            _centers[index] += shift;
        }

        Width = maxX + shift + options.Padding;
        if (!string.IsNullOrEmpty(model.Title))
        {
            var titleWidth = measurer.Measure(model.Title, options.FontFamily, TitleFontSize, bold: true).Width;
            Width = Math.Max(Width, titleWidth + options.Padding * 2);
        }

        var textHeight = measurer.Measure("Ag", options.FontFamily, options.FontSize, bold: false).Height;
        Vertical = new SequenceVerticalLayout(model, settings, options, textHeight, MessageLabels);
    }

    public SequenceModel Model { get; }

    /// <summary>The label drawn for each message, by element index; <see langword="null"/> when Naiad draws none.</summary>
    public IReadOnlyList<string?> MessageLabels { get; }

    public SequenceVerticalLayout Vertical { get; }

    public double Width { get; }

    public double Center(string id) => _centers[_indices[id]];

    public double Center(int index) => _centers[index];

    public double ParticipantWidth(int index) => _widths[index];

    /// <summary>Left edge and width of a note, sized to its text.</summary>
    public (double X, double Width) NoteBounds(Note note)
    {
        var index = _indices[note.ParticipantId];
        var center = _centers[index];
        var textWidth = _textWidth(note.Text) + NotePadding * 2;
        var width = Math.Max(NoteMinWidth, textWidth);
        switch (note.Position)
        {
            case NotePosition.RightOf:
                return (center + _widths[index] / 2 + NoteGap, width);
            case NotePosition.LeftOf:
                return (center - _widths[index] / 2 - NoteGap - width, width);
        }

        // Naiad treats "over A, B" with an unknown B as "over A".
        if (note.OverParticipantId2 is { } secondId && _indices.TryGetValue(secondId, out var second))
        {
            var left = Math.Min(center - _widths[index] / 2, _centers[second] - _widths[second] / 2);
            var right = Math.Max(center + _widths[index] / 2, _centers[second] + _widths[second] / 2);
            width = Math.Max(right - left, textWidth);
            center = (left + right) / 2;
        }

        return (center - width / 2, width);
    }

    /// <summary>
    /// Opens the gaps between neighbouring lifelines until every label fits:
    /// first the needs of a single gap, then labels spanning several gaps,
    /// which share any shortfall evenly.
    /// </summary>
    private void WidenGaps(double[] gaps)
    {
        void Require(int gap, double width)
        {
            if (gap >= 0 && gap < gaps.Length)
            {
                gaps[gap] = Math.Max(gaps[gap], width);
            }
        }

        void RequireSpan(int from, int to, double width)
        {
            var left = Math.Min(from, to);
            var right = Math.Max(from, to);
            var extra = Math.Max(0, width - gaps[left..right].Sum()) / (right - left);
            for (var gap = left; gap < right; gap++)
            {
                gaps[gap] += extra;
            }
        }

        for (var index = 0; index < Model.Elements.Count; index++)
        {
            switch (Model.Elements[index])
            {
                case Message message when message.FromId == message.ToId:
                    var labelWidth = MessageLabels[index] is { } label ? _textWidth(label) : 0;
                    Require(_indices[message.FromId],
                        Math.Max(SelfLoopWidth, SelfLabelOffset + labelWidth) + MessageLabelMargin);
                    break;
                case Note note:
                    var participant = _indices[note.ParticipantId];
                    var noteWidth = Math.Max(NoteMinWidth, _textWidth(note.Text) + NotePadding * 2);
                    var beside = _widths[participant] / 2 + NoteGap + noteWidth + NoteGap;
                    if (note.Position == NotePosition.RightOf)
                    {
                        Require(participant, beside);
                    }
                    else if (note.Position == NotePosition.LeftOf)
                    {
                        Require(participant - 1, beside);
                    }
                    else if (note.OverParticipantId2 is null || !_indices.ContainsKey(note.OverParticipantId2))
                    {
                        Require(participant, noteWidth / 2 + NoteGap);
                        Require(participant - 1, noteWidth / 2 + NoteGap);
                    }

                    break;
            }
        }

        for (var index = 0; index < Model.Elements.Count; index++)
        {
            switch (Model.Elements[index])
            {
                case Message message when message.FromId != message.ToId && MessageLabels[index] is { } label:
                    RequireSpan(_indices[message.FromId], _indices[message.ToId],
                        _textWidth(label) + MessageLabelMargin * 2);
                    break;
                case Note { Position: NotePosition.Over, OverParticipantId2: { } secondId } note
                    when _indices.TryGetValue(secondId, out var second) && second != _indices[note.ParticipantId]:
                    // The note spans the two boxes; open the gaps between them
                    // rather than let the text hang over their outer lifelines.
                    var first = _indices[note.ParticipantId];
                    var boxes = (_widths[first] + _widths[second]) / 2;
                    RequireSpan(first, second, _textWidth(note.Text) + NotePadding * 2 - boxes);
                    break;
            }
        }
    }

    /// <summary>
    /// Naiad numbers every message when <c>autonumber</c> is on, but a message
    /// to oneself shows its number only next to a text.
    /// </summary>
    private static string?[] BuildMessageLabels(SequenceModel model)
    {
        var labels = new string?[model.Elements.Count];
        var number = 0;
        for (var index = 0; index < model.Elements.Count; index++)
        {
            if (model.Elements[index] is not Message message)
            {
                continue;
            }

            number++;
            var hasText = !string.IsNullOrEmpty(message.Text);
            if (message.FromId == message.ToId)
            {
                labels[index] = !hasText ? null : model.AutoNumber ? $"{number}. {message.Text}" : message.Text;
            }
            else if (model.AutoNumber)
            {
                labels[index] = hasText ? $"{number}. {message.Text}" : $"{number}.";
            }
            else
            {
                labels[index] = hasText ? message.Text : null;
            }
        }

        return labels;
    }
}
