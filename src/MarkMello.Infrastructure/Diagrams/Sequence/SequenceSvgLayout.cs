using System.Text;
using System.Xml.Linq;
using MarkMello.Application.Abstractions;
using Naiad;
using Naiad.Diagrams.Sequence;
using static MarkMello.Infrastructure.Diagrams.Sequence.SequenceSvgGeometry;

namespace MarkMello.Infrastructure.Diagrams.Sequence;

/// <summary>
/// Re-lays out Naiad 1.4's <c>sequenceDiagram</c> SVG so that names and
/// labels fit their boxes and the canvas, honouring the <c>sequence.width</c>,
/// <c>actorMargin</c> and <c>messageMargin</c> init keys. Naiad's shapes stay;
/// only their coordinates change (ADR-0005 Decision 13).
///
/// The rewrite runs only when it provably matches what Naiad drew: the
/// source is parsed here (Naiad's parser is internal), the model is rendered
/// again through Naiad's public <see cref="SequenceRenderer"/>, and that SVG
/// must equal the one being fixed. Any mismatch or failure returns Naiad's
/// SVG untouched.
/// </summary>
internal static class SequenceSvgLayout
{
    public static string Apply(string source, string svg, RenderOptions options, IDiagramTextMeasurer measurer)
    {
        var (diagramSource, settings) = SequenceDiagramSettings.Read(source);
        if (!diagramSource.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase))
        {
            return svg;
        }

        try
        {
            var model = SequenceSourceParser.Parse(diagramSource);
            if (model is null || model.Participants.Count == 0 || !RendersAs(model, options, svg))
            {
                return svg;
            }

            var plan = new SequenceLayoutPlan(model, settings, options, measurer);
            var document = XDocument.Parse(svg);
            var root = document.Root!;
            Rewrite(root, plan);
            root.SetAttributeValue("viewBox", $"0 0 {Format(plan.Width)} {Format(plan.Vertical.Height)}");
            root.SetAttributeValue("style", $"max-width: {Format(plan.Width)}px;");
            return document.ToString(SaveOptions.DisableFormatting);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // The layout is an improvement, never a requirement: whatever
            // goes wrong here, the diagram Naiad drew is still a diagram.
            return svg;
        }
    }

    private static bool RendersAs(SequenceModel model, RenderOptions options, string svg)
    {
        var document = new SequenceRenderer().Render(model, options);
        if (!options.AllowHtmlElements)
        {
            // Mermaid.Render drops the icon import the same way.
            document.FontAwesomeImport = null;
        }

        var builder = new StringBuilder(svg.Length);
        document.ToXml(builder);
        return builder.Equals(svg.AsSpan());
    }

    private static void Rewrite(XElement root, SequenceLayoutPlan plan)
    {
        var model = plan.Model;
        var vertical = plan.Vertical;
        var cursor = new SequenceSvgCursor(root.Elements()
            .Where(static element => element.Name.LocalName is not ("style" or "defs"))
            .ToList());

        if (!string.IsNullOrEmpty(model.Title))
        {
            Set(cursor.Take("text"), "x", plan.Width / 2);
        }

        SequenceSvgParticipants.Rewrite(cursor, plan, vertical.HeaderTop);
        SequenceSvgParticipants.RewriteLifelines(cursor, plan);
        foreach (var (participantId, top, bottom) in vertical.Activations)
        {
            var bar = cursor.Take("rect", fill: "#F4F4F4");
            Set(bar, "x", plan.Center(participantId) - 5);
            Set(bar, "y", top);
            Set(bar, "height", bottom - top);
        }

        for (var index = 0; index < model.Elements.Count; index++)
        {
            var y = vertical.ElementY(index);
            switch (model.Elements[index])
            {
                case Message message when message.FromId == message.ToId:
                    RewriteSelfMessage(cursor, plan.Center(message.FromId), y, plan.MessageLabels[index] is not null);
                    break;
                case Message message:
                    RewriteMessage(cursor, message, plan.Center(message.FromId), plan.Center(message.ToId), y,
                        plan.MessageLabels[index] is not null);
                    break;
                case Note note:
                    RewriteNote(cursor, plan.NoteBounds(note), y);
                    break;
            }
        }

        SequenceSvgParticipants.Rewrite(cursor, plan, vertical.FooterTop);
        if (!cursor.AtEnd)
        {
            throw new InvalidOperationException("Sequence SVG has more elements than its model draws.");
        }
    }

    private static void RewriteMessage(SequenceSvgCursor cursor, Message message, double from, double to, double y, bool labelled)
    {
        SetLine(cursor.Take("line", stroke: "#333"), from, y, to, y);
        var back = to - Math.Sign(to - from) * 8;
        switch (message.Type)
        {
            case MessageType.SolidOpen or MessageType.DottedOpen:
                SetLine(cursor.Take("line"), back, y - 4, to, y);
                SetLine(cursor.Take("line"), back, y + 4, to, y);
                break;
            case MessageType.SolidCross or MessageType.DottedCross:
                SetLine(cursor.Take("line"), to - 4, y - 4, to + 4, y + 4);
                SetLine(cursor.Take("line"), to - 4, y + 4, to + 4, y - 4);
                break;
            default:
                cursor.Take("polygon").SetAttributeValue("points", Points((to, y), (back, y - 4), (back, y + 4)));
                break;
        }

        if (labelled)
        {
            var label = cursor.Take("text");
            Set(label, "x", (from + to) / 2);
            Set(label, "y", y - 8);
        }
    }

    private static void RewriteSelfMessage(SequenceSvgCursor cursor, double center, double y, bool labelled)
    {
        var right = center + SequenceLayoutPlan.SelfLoopWidth;
        var bottom = y + SequenceLayoutPlan.SelfLoopHeight;
        cursor.Take("path").SetAttributeValue("d", Path(closed: false, (center, y), (right, y), (right, bottom), (center, bottom)));
        if (labelled)
        {
            var label = cursor.Take("text");
            Set(label, "x", center + SequenceLayoutPlan.SelfLabelOffset);
            Set(label, "y", y + SequenceLayoutPlan.SelfLoopHeight / 2);
        }
    }

    private static void RewriteNote(SequenceSvgCursor cursor, (double X, double Width) bounds, double y)
    {
        var (left, width) = bounds;
        var right = left + width;
        var fold = right - SequenceLayoutPlan.NoteFold;
        var bottom = y + SequenceLayoutPlan.NoteHeight;
        cursor.Take("path", fill: "#FFFFCC").SetAttributeValue("d",
            Path(closed: true, (left, y), (fold, y), (right, y + SequenceLayoutPlan.NoteFold), (right, bottom), (left, bottom)));
        SetLine(cursor.Take("line"), fold, y, fold, y + SequenceLayoutPlan.NoteFold);
        SetLine(cursor.Take("line"), fold, y + SequenceLayoutPlan.NoteFold, right, y + SequenceLayoutPlan.NoteFold);
        var label = cursor.Take("text");
        Set(label, "x", left + width / 2);
        Set(label, "y", y + SequenceLayoutPlan.NoteHeight / 2);
    }
}
