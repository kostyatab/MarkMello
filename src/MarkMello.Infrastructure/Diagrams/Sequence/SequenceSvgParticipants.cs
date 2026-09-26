using System.Xml.Linq;
using Naiad.Diagrams.Sequence;
using static MarkMello.Infrastructure.Diagrams.Sequence.SequenceSvgGeometry;

namespace MarkMello.Infrastructure.Diagrams.Sequence;

/// <summary>Participant boxes and stick figures, top and bottom, and the lifelines between them.</summary>
internal static class SequenceSvgParticipants
{
    /// <summary>A row of participants, drawn by Naiad as box + label or circle + four lines + label.</summary>
    public static void Rewrite(SequenceSvgCursor cursor, SequenceLayoutPlan plan, double top)
    {
        var participants = plan.Model.Participants;
        for (var index = 0; index < participants.Count; index++)
        {
            var center = plan.Center(index);
            if (participants[index].Type == ParticipantType.Actor)
            {
                RewriteActor(cursor, center, top);
            }
            else
            {
                var width = plan.ParticipantWidth(index);
                var box = cursor.Take("rect", fill: "#ECECFF");
                Set(box, "x", center - width / 2);
                Set(box, "y", top);
                Set(box, "width", width);
                var label = cursor.Take("text");
                Set(label, "x", center);
                Set(label, "y", top + 20);
            }
        }
    }

    public static void RewriteLifelines(SequenceSvgCursor cursor, SequenceLayoutPlan plan)
    {
        for (var index = 0; index < plan.Model.Participants.Count; index++)
        {
            var center = plan.Center(index);
            SetLine(cursor.Take("line", stroke: "#999"), center, plan.Vertical.LifelineStart, center, plan.Vertical.FooterTop);
        }
    }

    private static void RewriteActor(SequenceSvgCursor cursor, double center, double top)
    {
        var head = cursor.Take("circle", fill: "#ECECFF");
        Set(head, "cx", center);
        Set(head, "cy", top + 9);
        SetLine(cursor.Take("line"), center, top + 18, center, top + 31);
        SetLine(cursor.Take("line"), center - 10, top + 22, center + 10, top + 22);
        SetLine(cursor.Take("line"), center, top + 31, center - 8, top + 40);
        SetLine(cursor.Take("line"), center, top + 31, center + 8, top + 40);
        var label = cursor.Take("text");
        Set(label, "x", center);
        Set(label, "y", top + 44);
        // Naiad writes "top", which is not an SVG baseline: the viewer would
        // sit the name on the legs. "hanging" hangs it below them.
        label.SetAttributeValue("dominant-baseline", "hanging");
    }
}
