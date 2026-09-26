using Naiad.Diagrams.Sequence;

namespace MarkMello.Infrastructure.Diagrams.Sequence;

/// <summary>
/// Reads a <c>sequenceDiagram</c> into Naiad's public model. Naiad 1.x keeps
/// its own sequence parser internal, so this one follows its grammar line for
/// line: the same statements in the same order of precedence, blocks
/// (<c>loop</c>, <c>alt</c>, <c>par</c> …) skipped the way Naiad skips them,
/// so the model is the flat list Naiad lays out.
///
/// Anything it does not recognise returns <see langword="null"/> rather than
/// a guess: the caller then keeps Naiad's SVG as is. Where this grammar and
/// Naiad's still disagree on a line, <see cref="SequenceSvgLayout"/> notices,
/// because it re-renders the model and compares.
/// </summary>
internal static class SequenceSourceParser
{
    private static readonly (string Token, MessageType Type)[] Arrows =
    [
        ("-->>", MessageType.DottedArrow),
        ("->>", MessageType.SolidArrow),
        ("--x", MessageType.DottedCross),
        ("-x", MessageType.SolidCross),
        ("--)", MessageType.DottedAsync),
        ("-)", MessageType.SolidAsync),
        ("-->", MessageType.DottedOpen),
        ("->", MessageType.SolidOpen),
    ];

    private static readonly string[] BlockKeywords =
        ["alt", "else", "loop", "par", "and", "opt", "critical", "break", "rect", "end"];

    /// <param name="source">Diagram text as Naiad receives it: trimmed, init directives stripped.</param>
    public static SequenceModel? Parse(string source)
    {
        var lines = source.Split('\n');
        if (!IsHeader(Line(lines[0])))
        {
            return null;
        }

        var model = new SequenceModel();
        var known = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < lines.Length; index++)
        {
            var line = Line(lines[index]);
            if (line is null)
            {
                return null;
            }

            if (TryParticipant(line) is { } participant)
            {
                // Naiad adds a repeated declaration as a second lifeline under
                // the same id; no layout can place that, so leave it to Naiad.
                if (!known.Add(participant.Id))
                {
                    return null;
                }

                model.Participants.Add(participant);
            }
            else if (TryMessage(line) is { } message)
            {
                AddImplicit(model, known, message.FromId);
                AddImplicit(model, known, message.ToId);
                model.Elements.Add(message);
            }
            else if (TryNote(line) is { } note)
            {
                model.Elements.Add(note);
            }
            else if (TryActivation(line) is { } activation)
            {
                model.Elements.Add(activation);
            }
            else if (IsAutoNumber(line))
            {
                model.AutoNumber = true;
            }
            else if (TryTitle(line) is { } title)
            {
                model.Title = title;
            }
            else if (!IsSkipped(line))
            {
                return null;
            }
        }

        return model;
    }

    /// <summary>
    /// A line without its terminator, or <see langword="null"/> when a stray
    /// carriage return is left inside: Naiad's text runs stop there.
    /// </summary>
    private static string? Line(string raw)
    {
        var line = raw.EndsWith('\r') ? raw[..^1] : raw;
        return line.Contains('\r', StringComparison.Ordinal) ? null : line;
    }

    private static void AddImplicit(SequenceModel model, HashSet<string> known, string id)
    {
        if (known.Add(id))
        {
            model.Participants.Add(new Participant { Id = id });
        }
    }

    private static bool IsHeader(string? line)
    {
        if (line is null)
        {
            return false;
        }

        var cursor = new LineCursor(line);
        cursor.SkipInline();
        if (!cursor.Literal("sequenceDiagram"))
        {
            return false;
        }

        cursor.SkipInline();
        return cursor.AtEnd;
    }

    private static Participant? TryParticipant(string line)
    {
        var cursor = new LineCursor(line);
        cursor.SkipInline();
        ParticipantType type;
        if (cursor.Literal("actor"))
        {
            type = ParticipantType.Actor;
        }
        else if (cursor.Literal("participant"))
        {
            type = ParticipantType.Participant;
        }
        else
        {
            return null;
        }

        if (!cursor.RequiredWhitespace() || cursor.Identifier() is not { } id)
        {
            return null;
        }

        string? alias = null;
        var beforeAlias = cursor;
        if (cursor.RequiredWhitespace() && cursor.Literal("as") && cursor.RequiredWhitespace() && !cursor.AtEnd)
        {
            alias = cursor.Rest();
        }
        else
        {
            cursor = beforeAlias;
        }

        return cursor.AtEnd ? new Participant { Id = id, Alias = alias, Type = type } : null;
    }

    private static Message? TryMessage(string line)
    {
        var cursor = new LineCursor(line);
        cursor.SkipInline();
        if (cursor.Identifier() is not { } from)
        {
            return null;
        }

        cursor.SkipInline();
        MessageType? type = null;
        foreach (var (token, arrow) in Arrows)
        {
            if (cursor.Literal(token))
            {
                type = arrow;
                break;
            }
        }

        if (type is null)
        {
            return null;
        }

        var activate = cursor.Literal("+");
        var deactivate = cursor.Literal("-");
        cursor.SkipInline();
        if (cursor.Identifier() is not { } to)
        {
            return null;
        }

        cursor.SkipInline();
        string? text = null;
        if (cursor.Literal(":"))
        {
            cursor.SkipInline();
            text = cursor.Rest();
        }

        return cursor.AtEnd
            ? new Message { FromId = from, ToId = to, Text = text, Type = type.Value, Activate = activate, Deactivate = deactivate }
            : null;
    }

    private static Note? TryNote(string line)
    {
        var cursor = new LineCursor(line);
        cursor.SkipInline();
        if (!cursor.Literal("Note") && !cursor.Literal("note"))
        {
            return null;
        }

        if (!cursor.RequiredWhitespace())
        {
            return null;
        }

        NotePosition position;
        if (cursor.Literal("right of"))
        {
            position = NotePosition.RightOf;
        }
        else if (cursor.Literal("left of"))
        {
            position = NotePosition.LeftOf;
        }
        else if (cursor.Literal("over"))
        {
            position = NotePosition.Over;
        }
        else
        {
            return null;
        }

        if (!cursor.RequiredWhitespace() || cursor.Identifier() is not { } participant)
        {
            return null;
        }

        string? second = null;
        var beforeSecond = cursor;
        if (cursor.Literal(","))
        {
            cursor.SkipInline();
            second = cursor.Identifier();
            if (second is null)
            {
                cursor = beforeSecond;
            }
        }

        cursor.SkipInline();
        if (!cursor.Literal(":"))
        {
            return null;
        }

        cursor.SkipInline();
        var text = cursor.Rest();
        return new Note { Text = text, Position = position, ParticipantId = participant, OverParticipantId2 = second };
    }

    private static Activation? TryActivation(string line)
    {
        var cursor = new LineCursor(line);
        cursor.SkipInline();
        bool isActivate;
        if (cursor.Literal("activate"))
        {
            isActivate = true;
        }
        else if (cursor.Literal("deactivate"))
        {
            isActivate = false;
        }
        else
        {
            return null;
        }

        return cursor.RequiredWhitespace() && cursor.Identifier() is { } id && cursor.AtEnd
            ? new Activation { ParticipantId = id, IsActivate = isActivate }
            : null;
    }

    private static bool IsAutoNumber(string line)
    {
        var cursor = new LineCursor(line);
        cursor.SkipInline();
        return cursor.Literal("autonumber") && cursor.AtEnd;
    }

    private static string? TryTitle(string line)
    {
        var cursor = new LineCursor(line);
        cursor.SkipInline();
        if (!cursor.Literal("title"))
        {
            return null;
        }

        cursor.SkipInline();
        return cursor.Rest();
    }

    /// <summary>
    /// Lines Naiad reads and drops: block keywords with whatever follows them,
    /// comments, and blank lines.
    /// </summary>
    private static bool IsSkipped(string line)
    {
        var cursor = new LineCursor(line);
        cursor.SkipInline();
        foreach (var keyword in BlockKeywords)
        {
            if (cursor.StartsWith(keyword))
            {
                return true;
            }
        }

        return cursor.AtEnd || cursor.StartsWith("%%");
    }

    /// <summary>A position within one line, advanced by the grammar's primitives.</summary>
    private struct LineCursor(string line)
    {
        private int _position;

        public readonly bool AtEnd => _position == line.Length;

        public void SkipInline()
        {
            while (_position < line.Length && line[_position] is ' ' or '\t')
            {
                _position++;
            }
        }

        /// <summary>At least one whitespace character, all of them consumed.</summary>
        public bool RequiredWhitespace()
        {
            var start = _position;
            while (_position < line.Length && char.IsWhiteSpace(line[_position]))
            {
                _position++;
            }

            return _position > start;
        }

        public readonly bool StartsWith(string text)
            => line.AsSpan(_position).StartsWith(text, StringComparison.Ordinal);

        public bool Literal(string text)
        {
            if (!StartsWith(text))
            {
                return false;
            }

            _position += text.Length;
            return true;
        }

        public string? Identifier()
        {
            var start = _position;
            while (_position < line.Length && (char.IsLetterOrDigit(line[_position]) || line[_position] == '_'))
            {
                _position++;
            }

            return _position > start ? line[start.._position] : null;
        }

        public string Rest()
        {
            var rest = line[_position..];
            _position = line.Length;
            return rest;
        }
    }
}
