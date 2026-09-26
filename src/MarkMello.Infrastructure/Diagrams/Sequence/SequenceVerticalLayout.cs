using Naiad;
using Naiad.Diagrams.Sequence;

namespace MarkMello.Infrastructure.Diagrams.Sequence;

/// <summary>
/// Rows of a sequence diagram. Naiad steps every message and note by a fixed
/// 50 px, so a note's box runs into the label of the message after it. Here
/// consecutive rows are at least <c>messageMargin</c> apart and never closer
/// than what they draw above and below their line, plus a clearance.
/// </summary>
internal sealed class SequenceVerticalLayout
{
    public const double TitleOffset = 30;

    // Naiad's figures: box height, the stick figure with the label under it,
    // and the gap between a message label and its line.
    private const double BoxHeight = 40;
    private const double ActorHeaderHeight = 64;
    private const double ActorLabelTop = 44;
    private const double MessageLabelLift = 8;
    private const double ArrowHalfHeight = 4;
    private const double RowClearance = 12;

    private readonly double[] _elementY;

    public SequenceVerticalLayout(SequenceModel model, SequenceDiagramSettings settings,
        RenderOptions options, double textHeight, IReadOnlyList<string?> messageLabels)
    {
        HeaderTop = options.Padding + (string.IsNullOrEmpty(model.Title) ? 0 : TitleOffset);
        HeaderHeight = model.Participants.Any(static participant => participant.Type == ParticipantType.Actor)
            ? Math.Max(ActorHeaderHeight, ActorLabelTop + textHeight + 4)
            : BoxHeight;
        LifelineStart = HeaderTop + HeaderHeight;

        _elementY = new double[model.Elements.Count];
        var rows = new List<int>();
        var y = LifelineStart;
        var below = 0.0;
        for (var index = 0; index < model.Elements.Count; index++)
        {
            var element = model.Elements[index];
            if (element is Activation)
            {
                continue;
            }

            var (above, extent) = Extent(element, messageLabels[index], textHeight);
            y += Math.Max(settings.MessageMargin, (rows.Count == 0 ? 0 : below) + above + RowClearance);
            _elementY[index] = y;
            rows.Add(index);
            below = extent;
        }

        FooterTop = y + Math.Max(settings.MessageMargin, below + RowClearance);

        // Naiad gives a zero-height activation the position of whatever comes
        // after it; keep that, measured in the new rows.
        var next = FooterTop;
        for (var index = model.Elements.Count - 1; index >= 0; index--)
        {
            if (model.Elements[index] is Activation)
            {
                _elementY[index] = next;
            }
            else
            {
                next = _elementY[index];
            }
        }

        Height = FooterTop + HeaderHeight + options.Padding;
        Activations = CalculateActivations(model);
    }

    public double HeaderTop { get; }
    public double HeaderHeight { get; }
    public double LifelineStart { get; }
    public double FooterTop { get; }
    public double Height { get; }

    /// <summary>Activation bars in the order Naiad draws them.</summary>
    public IReadOnlyList<(string ParticipantId, double Top, double Bottom)> Activations { get; }

    /// <summary>The row of a message or note; for an activation, the row it opens or closes at.</summary>
    public double ElementY(int index) => _elementY[index];

    /// <summary>How far an element draws above and below its row line.</summary>
    private static (double Above, double Below) Extent(SequenceElement element, string? label, double textHeight)
        => element switch
        {
            Note => (0, SequenceLayoutPlan.NoteHeight),
            Message message when message.FromId == message.ToId => (0, SequenceLayoutPlan.SelfLoopHeight),
            _ => (label is null ? ArrowHalfHeight : textHeight + MessageLabelLift, ArrowHalfHeight),
        };

    /// <summary>
    /// Naiad's activation bookkeeping, replayed on the new rows so the bars
    /// come out in its order. <c>+</c> opens the target's bar, <c>-</c> closes
    /// the sender's; an <c>activate</c> line takes the row of the last
    /// message; a bar left open runs to the footer.
    /// </summary>
    private List<(string, double, double)> CalculateActivations(SequenceModel model)
    {
        var spans = new Dictionary<string, List<(double Top, double Bottom)>>();
        var open = new Dictionary<string, double>();
        double? lastMessageY = null;

        void Close(string id, double y)
        {
            if (!open.TryGetValue(id, out var top))
            {
                return;
            }

            if (!spans.TryGetValue(id, out var list))
            {
                list = [];
                spans[id] = list;
            }

            list.Add((top, y));
            open.Remove(id);
        }

        for (var index = 0; index < model.Elements.Count; index++)
        {
            var y = _elementY[index];
            switch (model.Elements[index])
            {
                case Message message:
                    lastMessageY = y;
                    if (message.Activate)
                    {
                        open[message.ToId] = y;
                    }

                    if (message.Deactivate)
                    {
                        Close(message.FromId, y);
                    }

                    break;
                case Activation activation when activation.IsActivate:
                    open[activation.ParticipantId] = lastMessageY ?? y;
                    break;
                case Activation activation:
                    Close(activation.ParticipantId, lastMessageY ?? y);
                    break;
            }
        }

        foreach (var id in open.Keys.ToList())
        {
            Close(id, FooterTop);
        }

        return spans.SelectMany(static pair => pair.Value.Select(span => (pair.Key, span.Top, span.Bottom))).ToList();
    }
}
