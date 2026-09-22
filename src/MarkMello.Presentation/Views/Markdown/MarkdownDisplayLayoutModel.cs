using System.Globalization;
using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

internal sealed class MarkdownDisplayLayoutModel
{
    private const char LeftCodePaddingMarker = '\uE000';
    private const char RightCodePaddingMarker = '\uE001';
    private const char KeyboardGapMarker = '\uE002';
    private const char BackReferenceMarker = '\uE003';

    private readonly int[] _displayCaretToCanonicalCaret;
    private readonly int[] _canonicalCaretToDisplayStart;
    private readonly int[] _canonicalCaretToDisplayEnd;

    private MarkdownDisplayLayoutModel(
        int canonicalLength,
        IReadOnlyList<MarkdownDisplaySegment> segments,
        IReadOnlyList<MarkdownDisplayCodeBox> codeBoxes,
        int[] displayCaretToCanonicalCaret,
        int[] canonicalCaretToDisplayStart,
        int[] canonicalCaretToDisplayEnd)
    {
        CanonicalLength = canonicalLength;
        Segments = segments;
        CodeBoxes = codeBoxes;
        _displayCaretToCanonicalCaret = displayCaretToCanonicalCaret;
        _canonicalCaretToDisplayStart = canonicalCaretToDisplayStart;
        _canonicalCaretToDisplayEnd = canonicalCaretToDisplayEnd;
    }

    public int CanonicalLength { get; }

    public int DisplayLength => _displayCaretToCanonicalCaret.Length - 1;

    public IReadOnlyList<MarkdownDisplaySegment> Segments { get; }

    public IReadOnlyList<MarkdownDisplayCodeBox> CodeBoxes { get; }

    public int GetDisplayStartForCanonicalCaret(int canonicalCaret)
        => _canonicalCaretToDisplayStart[Math.Clamp(canonicalCaret, 0, CanonicalLength)];

    public int GetDisplayEndForCanonicalCaret(int canonicalCaret)
        => _canonicalCaretToDisplayEnd[Math.Clamp(canonicalCaret, 0, CanonicalLength)];

    public int GetCanonicalCaretForDisplayCaret(int displayCaret)
        => _displayCaretToCanonicalCaret[Math.Clamp(displayCaret, 0, DisplayLength)];

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
            var spanIndex = 0;
            var imageIndex = 0;
            var footnoteIndex = 0;
            var index = 0;

            while (index < text.Length)
            {
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

                if (end <= index)
                {
                    end = Math.Min(text.Length, index + 1);
                }

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
                _displayCaretToCanonicalCaret.ToArray(),
                canonicalCaretToDisplayStart,
                canonicalCaretToDisplayEnd);
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
    FootnoteBackReference
}
