using System.Globalization;
using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

internal sealed class MarkdownDisplayLayoutModel
{
    private const char LeftCodePaddingMarker = '\uE000';
    private const char RightCodePaddingMarker = '\uE001';
    private const char KeyboardGapMarker = '\uE002';
    private const char BackReferenceMarker = '\uE003';
    private const char LeftHighlightPaddingMarker = '\uE004';
    private const char RightHighlightPaddingMarker = '\uE005';

    private readonly int[] _displayCaretToCanonicalCaret;
    private readonly int[] _canonicalCaretToDisplayStart;
    private readonly int[] _canonicalCaretToDisplayEnd;

    private MarkdownDisplayLayoutModel(
        int canonicalLength,
        IReadOnlyList<MarkdownDisplaySegment> segments,
        IReadOnlyList<MarkdownDisplayCodeBox> codeBoxes,
        IReadOnlyList<MarkdownDisplayHighlightBox> highlightBoxes,
        int[] displayCaretToCanonicalCaret,
        int[] canonicalCaretToDisplayStart,
        int[] canonicalCaretToDisplayEnd)
    {
        CanonicalLength = canonicalLength;
        Segments = segments;
        CodeBoxes = codeBoxes;
        HighlightBoxes = highlightBoxes;
        _displayCaretToCanonicalCaret = displayCaretToCanonicalCaret;
        _canonicalCaretToDisplayStart = canonicalCaretToDisplayStart;
        _canonicalCaretToDisplayEnd = canonicalCaretToDisplayEnd;
    }

    public int CanonicalLength { get; }

    public int DisplayLength => _displayCaretToCanonicalCaret.Length - 1;

    public IReadOnlyList<MarkdownDisplaySegment> Segments { get; }

    public IReadOnlyList<MarkdownDisplayCodeBox> CodeBoxes { get; }

    /// <summary>Фон выделения маркером (<c>&lt;mark&gt;</c>) вместе с полями по бокам.</summary>
    public IReadOnlyList<MarkdownDisplayHighlightBox> HighlightBoxes { get; }

    /// <summary>Есть ли индексы (<c>&lt;sub&gt;</c>, <c>&lt;sup&gt;</c>): их глифы рисуются отдельно.</summary>
    public bool HasScripts { get; private init; }

    public int GetDisplayStartForCanonicalCaret(int canonicalCaret)
        => _canonicalCaretToDisplayStart[Math.Clamp(canonicalCaret, 0, CanonicalLength)];

    public int GetDisplayEndForCanonicalCaret(int canonicalCaret)
        => _canonicalCaretToDisplayEnd[Math.Clamp(canonicalCaret, 0, CanonicalLength)];

    public int GetCanonicalCaretForDisplayCaret(int displayCaret)
        => _displayCaretToCanonicalCaret[Math.Clamp(displayCaret, 0, DisplayLength)];

    /// <summary>Пробел или перевод строки на экране в позиции <paramref name="displayIndex"/>.</summary>
    public bool IsWhitespaceAt(int displayIndex)
    {
        var segmentIndex = FindSegmentIndex(displayIndex);
        if (segmentIndex < 0)
        {
            return false;
        }

        var segment = Segments[segmentIndex];
        return segment.Kind switch
        {
            MarkdownDisplaySegmentKind.Text => char.IsWhiteSpace(segment.Text[displayIndex - segment.DisplayStart]),
            MarkdownDisplaySegmentKind.LineBreak => true,
            _ => false
        };
    }

    public int FindSegmentIndex(int displayIndex)
    {
        if (Segments.Count == 0)
        {
            return -1;
        }

        var low = 0;
        var high = Segments.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var candidate = Segments[mid];
            if (displayIndex < candidate.DisplayStart)
            {
                high = mid - 1;
                continue;
            }

            if (displayIndex >= candidate.DisplayEnd)
            {
                low = mid + 1;
                continue;
            }

            return mid;
        }

        return -1;
    }

    public static MarkdownDisplayLayoutModel Create(MarkdownStyledText styledText)
    {
        ArgumentNullException.ThrowIfNull(styledText);

        var builder = new Builder(styledText);
        return builder.Build();
    }

    private sealed class Builder
    {
        private readonly MarkdownStyledText _styledText;
        private readonly List<MarkdownDisplaySegment> _segments = new();
        private readonly List<MarkdownDisplayCodeBox> _codeBoxes = new();
        private readonly List<MarkdownDisplayHighlightBox> _highlightBoxes = new();
        private int _highlightStart = -1;
        private int _highlightEnd = -1;
        private int _nextHighlight;
        private bool _hasScripts;
        private readonly List<int> _displayCaretToCanonicalCaret = new() { 0 };
        private int _displayOffset;
        private int _canonicalOffset;

        public Builder(MarkdownStyledText styledText)
        {
            _styledText = styledText;
        }

        public MarkdownDisplayLayoutModel Build()
        {
            var text = _styledText.Text;
            var spans = _styledText.Spans;
            var images = _styledText.Images;
            var footnotes = _styledText.FootnoteReferences;
            var highlights = _styledText.Highlights;
            var spanIndex = 0;
            var imageIndex = 0;
            var footnoteIndex = 0;
            var index = 0;

            while (index < text.Length)
            {
                SyncHighlight(index);

                while (spanIndex < spans.Count && spans[spanIndex].Range.End <= index)
                {
                    spanIndex++;
                }

                while (imageIndex < images.Count && images[imageIndex].Range.End <= index)
                {
                    imageIndex++;
                }

                while (footnoteIndex < footnotes.Count && footnotes[footnoteIndex].Range.End <= index)
                {
                    footnoteIndex++;
                }

                if (imageIndex < images.Count && images[imageIndex].Range.Start == index)
                {
                    var image = images[imageIndex];
                    AppendImageSegment(image);
                    index = image.Range.End;
                    continue;
                }

                if (footnoteIndex < footnotes.Count && footnotes[footnoteIndex].Range.Start == index)
                {
                    var footnote = footnotes[footnoteIndex];
                    AppendFootnoteReferenceSegment(footnote);
                    index = footnote.Range.End;
                    continue;
                }

                MarkdownInlineStyleState style;
                var end = text.Length;
                if (spanIndex < spans.Count)
                {
                    var span = spans[spanIndex];
                    if (index < span.Range.Start)
                    {
                        style = MarkdownInlineStyleState.Default;
                        end = span.Range.Start;
                    }
                    else
                    {
                        style = span.Style;
                        end = span.Range.End;
                    }
                }
                else
                {
                    style = MarkdownInlineStyleState.Default;
                }

                if (imageIndex < images.Count && images[imageIndex].Range.Start > index)
                {
                    end = Math.Min(end, images[imageIndex].Range.Start);
                }

                if (footnoteIndex < footnotes.Count && footnotes[footnoteIndex].Range.Start > index)
                {
                    end = Math.Min(end, footnotes[footnoteIndex].Range.Start);
                }

                // Кусок не переходит границу выделения: у соседних выделений вплотную
                // свои поля.
                if (_highlightEnd > index)
                {
                    end = Math.Min(end, _highlightEnd);
                }
                else if (_nextHighlight < highlights.Count && highlights[_nextHighlight].Start > index)
                {
                    end = Math.Min(end, highlights[_nextHighlight].Start);
                }

                if (end <= index)
                {
                    end = Math.Min(text.Length, index + 1);
                }

                _hasScripts |= style.IsScript;
                if (style.IsBoxed)
                {
                    AppendCodeSegment(text[index..end], style);
                }
                else
                {
                    AppendTextSegments(text[index..end], style);
                }

                index = end;
            }

            CloseHighlight();

            // Каретки, у которых есть место в тексте: иконка возврата в конце сноски
            // стоит после них, и ни выделение, ни подсветка поиска на неё не заходят.
            var textCaretCount = _displayCaretToCanonicalCaret.Count;
            if (_styledText.BackReferenceNumber is not null)
            {
                AppendPadding(MarkdownDisplaySegmentKind.FootnoteBackReference, BackReferenceMarker, MarkdownInlineStyleState.Default);
            }

            var canonicalLength = _styledText.Text.Length;
            var canonicalCaretToDisplayStart = new int[canonicalLength + 1];
            var canonicalCaretToDisplayEnd = new int[canonicalLength + 1];
            Array.Fill(canonicalCaretToDisplayStart, -1);

            for (var displayCaret = 0; displayCaret < textCaretCount; displayCaret++)
            {
                var canonicalCaret = _displayCaretToCanonicalCaret[displayCaret];
                if (canonicalCaretToDisplayStart[canonicalCaret] < 0)
                {
                    canonicalCaretToDisplayStart[canonicalCaret] = displayCaret;
                }

                canonicalCaretToDisplayEnd[canonicalCaret] = displayCaret;
            }

            FillCompressedSegmentCaretMaps(canonicalCaretToDisplayStart, canonicalCaretToDisplayEnd);

            return new MarkdownDisplayLayoutModel(
                canonicalLength,
                _segments,
                _codeBoxes,
                _highlightBoxes,
                _displayCaretToCanonicalCaret.ToArray(),
                canonicalCaretToDisplayStart,
                canonicalCaretToDisplayEnd)
            {
                HasScripts = _hasScripts
            };
        }

        /// <summary>
        /// Выделение маркером — поля по бокам на экране, как padding у
        /// <c>&lt;mark&gt;</c>. Жирный, код или метка сноски внутри не разрывают
        /// выделение; вложенное выделение рисуется внешним.
        /// </summary>
        private void SyncHighlight(int canonicalIndex)
        {
            if (_highlightEnd >= 0 && canonicalIndex >= _highlightEnd)
            {
                CloseHighlight();
            }

            var highlights = _styledText.Highlights;
            while (_nextHighlight < highlights.Count && highlights[_nextHighlight].Start < canonicalIndex)
            {
                _nextHighlight++;
            }

            if (_highlightEnd < 0
                && _nextHighlight < highlights.Count
                && highlights[_nextHighlight].Start == canonicalIndex)
            {
                _highlightStart = _displayOffset;
                _highlightEnd = highlights[_nextHighlight].End;
                _nextHighlight++;
                AppendPadding(MarkdownDisplaySegmentKind.HighlightPaddingLeft, LeftHighlightPaddingMarker, MarkdownInlineStyleState.Default);
            }
        }

        private void CloseHighlight()
        {
            if (_highlightEnd < 0)
            {
                return;
            }

            AppendPadding(MarkdownDisplaySegmentKind.HighlightPaddingRight, RightHighlightPaddingMarker, MarkdownInlineStyleState.Default);
            _highlightBoxes.Add(new MarkdownDisplayHighlightBox(_highlightStart, _displayOffset - _highlightStart));
            _highlightStart = -1;
            _highlightEnd = -1;
        }

        private void AppendCodeSegment(string text, MarkdownInlineStyleState style)
        {
            // Клавиши вплотную (<kbd>Ctrl</kbd><kbd>C</kbd>) разделены небольшим
            // зазором — вне рамок обеих.
            if (style.IsKeyboard
                && _codeBoxes.Count > 0
                && _codeBoxes[^1] is { IsKeyboard: true } previous
                && previous.CanonicalRange.End == _canonicalOffset
                && previous.DisplayEnd == _displayOffset)
            {
                AppendPadding(MarkdownDisplaySegmentKind.KeyboardGap, KeyboardGapMarker, style);
            }

            var canonicalStart = _canonicalOffset;
            var displayStart = _displayOffset;

            AppendPadding(MarkdownDisplaySegmentKind.CodePaddingLeft, LeftCodePaddingMarker, style);
            AppendTextSegments(text, style);
            AppendPadding(MarkdownDisplaySegmentKind.CodePaddingRight, RightCodePaddingMarker, style);

            _codeBoxes.Add(new MarkdownDisplayCodeBox(
                new DocumentTextRange(canonicalStart, _canonicalOffset),
                displayStart,
                _displayOffset - displayStart,
                style.IsKeyboard));
        }

        private void AppendPadding(MarkdownDisplaySegmentKind kind, char marker, MarkdownInlineStyleState style)
        {
            _segments.Add(new MarkdownDisplaySegment(
                kind,
                _displayOffset,
                1,
                DocumentTextRange.Empty,
                marker.ToString(),
                style));

            _displayOffset++;
            _displayCaretToCanonicalCaret.Add(_canonicalOffset);
        }

        private void AppendImageSegment(MarkdownInlineImageSpan image)
        {
            _segments.Add(new MarkdownDisplaySegment(
                MarkdownDisplaySegmentKind.Image,
                _displayOffset,
                1,
                image.Range,
                image.PlaceholderText,
                image.Style,
                image.Index));

            _displayOffset++;
            _canonicalOffset = image.Range.End;
            _displayCaretToCanonicalCaret.Add(_canonicalOffset);
        }

        /// <summary>
        /// Метка сноски: «[1]» в тексте — один символ на экране, номер верхним
        /// индексом. Как и картинка, выделяется и подсвечивается целиком.
        /// </summary>
        private void AppendFootnoteReferenceSegment(MarkdownFootnoteReferenceSpan footnote)
        {
            _segments.Add(new MarkdownDisplaySegment(
                MarkdownDisplaySegmentKind.FootnoteReference,
                _displayOffset,
                1,
                footnote.Range,
                footnote.Number.ToString(CultureInfo.InvariantCulture),
                MarkdownInlineStyleState.Default));

            _displayOffset++;
            _canonicalOffset = footnote.Range.End;
            _displayCaretToCanonicalCaret.Add(_canonicalOffset);
        }

        private void AppendTextSegments(string text, MarkdownInlineStyleState style)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var chunkStart = 0;
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] != '\n')
                {
                    continue;
                }

                if (index > chunkStart)
                {
                    AppendTextRun(text[chunkStart..index], style);
                }

                AppendLineBreak();
                chunkStart = index + 1;
            }

            if (chunkStart < text.Length)
            {
                AppendTextRun(text[chunkStart..], style);
            }
        }

        private void AppendTextRun(string text, MarkdownInlineStyleState style)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var canonicalRange = new DocumentTextRange(_canonicalOffset, _canonicalOffset + text.Length);
            _segments.Add(new MarkdownDisplaySegment(
                MarkdownDisplaySegmentKind.Text,
                _displayOffset,
                text.Length,
                canonicalRange,
                text,
                style));

            for (var index = 0; index < text.Length; index++)
            {
                _displayOffset++;
                _canonicalOffset++;
                _displayCaretToCanonicalCaret.Add(_canonicalOffset);
            }
        }

        private void AppendLineBreak()
        {
            var canonicalRange = new DocumentTextRange(_canonicalOffset, _canonicalOffset + 1);
            _segments.Add(new MarkdownDisplaySegment(
                MarkdownDisplaySegmentKind.LineBreak,
                _displayOffset,
                1,
                canonicalRange,
                "\n",
                MarkdownInlineStyleState.Default));

            _displayOffset++;
            _canonicalOffset++;
            _displayCaretToCanonicalCaret.Add(_canonicalOffset);
        }

        private void FillCompressedSegmentCaretMaps(int[] canonicalCaretToDisplayStart, int[] canonicalCaretToDisplayEnd)
        {
            foreach (var segment in _segments)
            {
                if (segment.CanonicalRange.IsEmpty)
                {
                    continue;
                }

                // Метка сноски захватывается целиком, только если граница выделения
                // внутри «[1]»: выделение соседнего слова вплотную к метке её не красит.
                var (first, last) = segment.Kind switch
                {
                    MarkdownDisplaySegmentKind.Image => (segment.CanonicalRange.Start, segment.CanonicalRange.End),
                    MarkdownDisplaySegmentKind.FootnoteReference => (segment.CanonicalRange.Start + 1, segment.CanonicalRange.End - 1),
                    _ => (0, -1)
                };

                for (var caret = first; caret <= last; caret++)
                {
                    canonicalCaretToDisplayStart[caret] = segment.DisplayStart;
                    canonicalCaretToDisplayEnd[caret] = segment.DisplayEnd;
                }
            }
        }
    }
}

/// <summary>Рамка inline-кода или клавиши (<paramref name="IsKeyboard"/>).</summary>
internal readonly record struct MarkdownDisplayCodeBox(
    DocumentTextRange CanonicalRange,
    int DisplayStart,
    int DisplayLength,
    bool IsKeyboard = false)
{
    public int DisplayEnd => DisplayStart + DisplayLength;
}

/// <summary>Фон выделения маркером на экране: от левого поля до правого включительно.</summary>
internal readonly record struct MarkdownDisplayHighlightBox(int DisplayStart, int DisplayLength)
{
    public int DisplayEnd => DisplayStart + DisplayLength;
}

internal readonly record struct MarkdownDisplaySegment(
    MarkdownDisplaySegmentKind Kind,
    int DisplayStart,
    int DisplayLength,
    DocumentTextRange CanonicalRange,
    string Text,
    MarkdownInlineStyleState Style,
    int ImageIndex = -1)
{
    public int DisplayEnd => DisplayStart + DisplayLength;
}

internal enum MarkdownDisplaySegmentKind
{
    Text,
    LineBreak,
    Image,
    CodePaddingLeft,
    CodePaddingRight,
    FootnoteReference,

    /// <summary>Зазор между клавишами, стоящими вплотную.</summary>
    KeyboardGap,

    /// <summary>Иконка возврата к метке в конце сноски — только на экране.</summary>
    FootnoteBackReference,

    /// <summary>Поля выделения маркером (<c>&lt;mark&gt;</c>) по бокам.</summary>
    HighlightPaddingLeft,
    HighlightPaddingRight
}
