using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

internal sealed class MarkdownFormattedTextLayout : IDisposable
{
    private readonly MarkdownDisplayLayoutModel _displayModel;
    private readonly MarkdownInlineCodePadMetrics _codePadMetrics;
    private readonly MarkdownFootnoteReferenceMetrics? _footnoteMetrics;
    private readonly List<FormattedLine> _lines = new();

    public MarkdownFormattedTextLayout(
        MarkdownStyledText styledText,
        IReadOnlyDictionary<int, MarkdownInlineImageState>? inlineImages,
        FontFamily baseFontFamily,
        FontFamily inlineCodeFontFamily,
        double baseFontSize,
        FontWeight baseFontWeight,
        FontStyle baseFontStyle,
        double lineHeight,
        double letterSpacing,
        TextWrapping textWrapping,
        TextAlignment textAlignment,
        double maxWidth,
        IBrush foreground,
        TextDecorationCollection? linkDecorations,
        MarkdownInlineImagePlaceholderBrushes imagePlaceholderBrushes,
        IBrush? footnoteReferenceForeground = null,
        FontFeatureCollection? baseFontFeatures = null,
        IBrush? inlineCodeForeground = null,
        IBrush? keyboardForeground = null,
        MarkdownBackReferenceIcon? backReferenceIcon = null)
    {
        _displayModel = MarkdownDisplayLayoutModel.Create(styledText);
        var textProperties = new MarkdownTextRunPropertiesFactory(
            baseFontFamily,
            inlineCodeFontFamily,
            baseFontSize,
            baseFontWeight,
            baseFontStyle,
            foreground,
            linkDecorations,
            baseFontFeatures,
            inlineCodeForeground,
            keyboardForeground);
        var padMetrics = MarkdownInlineCodePadMetrics.Create(
            inlineCodeFontFamily,
            baseFontSize * MarkdownDocumentMetrics.InlineCodeFontScale,
            baseFontWeight,
            baseFontStyle,
            foreground);
        _codePadMetrics = padMetrics;
        // Клавиши — только в тех абзацах, где они есть: замер подписи лишний остальным.
        var keyboardMetrics = _displayModel.CodeBoxes.Any(static box => box.IsKeyboard)
            ? new MarkdownKeyboardKeyMetrics(textProperties.KeyboardTypeface, textProperties.KeyboardFontSize, baseFontSize, padMetrics.BoxHeight)
            : null;
        var imageMetrics = MarkdownInlineImageMetrics.Create(
            baseFontFamily,
            baseFontSize,
            baseFontWeight,
            baseFontStyle,
            lineHeight,
            imagePlaceholderBrushes);
        // Метрики сносок нужны редкому абзацу — пробный layout только для него.
        _footnoteMetrics = styledText.FootnoteReferences.Count == 0
            ? null
            : MarkdownFootnoteReferenceMetrics.Create(
                baseFontFamily,
                baseFontSize,
                baseFontWeight,
                baseFontStyle,
                footnoteReferenceForeground ?? foreground);
        var backReferenceMetrics = styledText.BackReferenceNumber is not null && backReferenceIcon is { } icon
            ? MarkdownBackReferenceMetrics.Create(baseFontFamily, baseFontSize, baseFontWeight, baseFontStyle, icon)
            : null;
        var source = new MarkdownTextSource(
            _displayModel,
            styledText.Images,
            inlineImages ?? EmptyInlineImages,
            textProperties,
            padMetrics,
            imageMetrics,
            maxWidth,
            _footnoteMetrics,
            keyboardMetrics,
            backReferenceMetrics);
        var paragraphProperties = new GenericTextParagraphProperties(
            new GenericTextRunProperties(
                new Typeface(baseFontFamily, baseFontStyle, baseFontWeight),
                baseFontSize,
                textDecorations: null,
                foreground,
                backgroundBrush: null,
                BaselineAlignment.Baseline,
                CultureInfo.CurrentUICulture,
                baseFontFeatures),
            textAlignment,
            textWrapping,
            lineHeight,
            letterSpacing);

        BuildLines(source, paragraphProperties, imageMetrics);
    }

    public double WidthIncludingTrailingWhitespace { get; private set; }

    /// <summary>Ширина самой длинной строки без пробелов в её конце.</summary>
    public double Width { get; private set; }

    public double Height { get; private set; }

    public int CanonicalLength => _displayModel.CanonicalLength;

    public IReadOnlyList<MarkdownDisplayCodeBox> CodeBoxes => _displayModel.CodeBoxes;

    public IReadOnlyList<MarkdownFormattedTextLineMetrics> GetLineMetrics()
    {
        if (_lines.Count == 0)
        {
            return Array.Empty<MarkdownFormattedTextLineMetrics>();
        }

        var result = new List<MarkdownFormattedTextLineMetrics>(_lines.Count);
        foreach (var line in _lines)
        {
            result.Add(new MarkdownFormattedTextLineMetrics(
                new Rect(
                    line.TextLine.Start,
                    line.Y,
                    Math.Max(1, line.TextLine.WidthIncludingTrailingWhitespace),
                    Math.Max(1, line.TextLine.Height))));
        }

        return result;
    }

    public void Draw(DrawingContext context)
    {
        foreach (var line in _lines)
        {
            line.TextLine.Draw(context, new Point(0, line.Y));
        }
    }

    public IReadOnlyList<Rect> GetCodeBoxRects(MarkdownDisplayCodeBox box)
    {
        if (box.DisplayLength <= 0 || _lines.Count == 0)
        {
            return Array.Empty<Rect>();
        }

        var displayEnd = box.DisplayStart + box.DisplayLength;
        var rects = new List<Rect>();

        foreach (var line in _lines)
        {
            var lineStart = line.TextLine.FirstTextSourceIndex;
            var lineEnd = lineStart + line.TextLine.Length;
            var overlapStart = Math.Max(box.DisplayStart, lineStart);
            var overlapEnd = Math.Min(displayEnd, lineEnd);
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            // Плашка кода и клавиша одной высоты и стоят по центру строки: их верх
            // вровень, а при тесном межстрочном плашка выходит за строку поровну
            // сверху и снизу, но строку не раздвигает.
            foreach (var bounds in line.TextLine.GetTextBounds(overlapStart, overlapEnd - overlapStart))
            {
                var centeredY = line.Y + (line.TextLine.Height - _codePadMetrics.BoxHeight) / 2;
                rects.Add(new Rect(
                    bounds.Rectangle.X,
                    centeredY,
                    bounds.Rectangle.Width,
                    _codePadMetrics.BoxHeight));
            }
        }

        return rects;
    }

    public IReadOnlyList<Rect> GetSelectionRects(DocumentTextRange canonicalRange)
    {
        var canonicalStart = Math.Clamp(canonicalRange.Start, 0, CanonicalLength);
        var canonicalEnd = Math.Clamp(canonicalRange.End, canonicalStart, CanonicalLength);
        if (canonicalEnd <= canonicalStart)
        {
            return Array.Empty<Rect>();
        }

        var displayStart = _displayModel.GetDisplayStartForCanonicalCaret(canonicalStart);
        var displayEnd = _displayModel.GetDisplayEndForCanonicalCaret(canonicalEnd);
        return displayEnd <= displayStart
            ? Array.Empty<Rect>()
            : GetDisplayRects(displayStart, displayEnd - displayStart);
    }

    public int GetCanonicalCaretOffset(Point point)
    {
        if (_lines.Count == 0)
        {
            return 0;
        }

        var hit = GetCharacterHit(point);
        var displayCaret = hit.FirstCharacterIndex + hit.TrailingLength;
        return _displayModel.GetCanonicalCaretForDisplayCaret(displayCaret);
    }

    /// <summary>
    /// Offset символа под точкой — в отличие от каретки, не зависит от того, в какую
    /// половину символа попала точка. Метка сноски на экране — один символ, и по
    /// каретке в её правой половине попадание вело бы уже за неё.
    /// </summary>
    public int GetCanonicalCharacterOffset(Point point)
    {
        if (_lines.Count == 0)
        {
            return 0;
        }

        return _displayModel.GetCanonicalCaretForDisplayCaret(GetCharacterHit(point).FirstCharacterIndex);
    }

    private CharacterHit GetCharacterHit(Point point)
    {
        var lineIndex = FindLineIndex(point.Y);
        var line = _lines[lineIndex];
        // Distances are measured from the paragraph edge: a centred or
        // right-aligned line starts at TextLine.Start, not at 0.
        var lineStart = line.TextLine.Start;
        var localX = Math.Clamp(point.X, lineStart, lineStart + Math.Max(line.TextLine.WidthIncludingTrailingWhitespace, 0));
        return line.TextLine.GetCharacterHitFromDistance(localX);
    }

    /// <summary>
    /// Попала ли точка в иконку возврата к метке в конце сноски — вместе с зазором
    /// перед ней и на всю высоту строки, чтобы в неё было легко попасть мышью.
    /// </summary>
    public bool IsPointOnBackReference(Point point)
    {
        var segments = _displayModel.Segments;
        if (segments.Count == 0 || segments[^1].Kind != MarkdownDisplaySegmentKind.FootnoteBackReference)
        {
            return false;
        }

        var displayIndex = segments[^1].DisplayStart;
        foreach (var line in _lines)
        {
            var lineStart = line.TextLine.FirstTextSourceIndex;
            if (displayIndex < lineStart || displayIndex >= lineStart + line.TextLine.Length)
            {
                continue;
            }

            foreach (var bounds in line.TextLine.GetTextBounds(displayIndex, 1))
            {
                var rect = new Rect(bounds.Rectangle.X, line.Y, bounds.Rectangle.Width, line.TextLine.Height);
                if (rect.Contains(point))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool IsPointInsideText(Point point)
    {
        if (_lines.Count == 0)
        {
            return false;
        }

        var lineIndex = FindLineIndex(point.Y);
        var line = _lines[lineIndex];
        return point.Y >= line.Y
            && point.Y <= line.Y + line.TextLine.Height
            && point.X >= line.TextLine.Start
            && point.X <= line.TextLine.Start + line.TextLine.WidthIncludingTrailingWhitespace;
    }

    public void Dispose()
    {
        foreach (var line in _lines)
        {
            line.TextLine.Dispose();
        }

        _lines.Clear();
        _footnoteMetrics?.Dispose();
    }

    private void BuildLines(
        MarkdownTextSource source,
        TextParagraphProperties paragraphProperties,
        MarkdownInlineImageMetrics imageMetrics)
    {
        if (_displayModel.DisplayLength == 0)
        {
            return;
        }

        var formatter = TextFormatter.Current;
        TextLineBreak? previousLineBreak = null;
        var index = 0;
        var y = 0d;

        while (index < _displayModel.DisplayLength)
        {
            var line = FormatLineFittingImages(formatter, source, index, paragraphProperties, previousLineBreak, imageMetrics);
            if (line is null)
            {
                break;
            }

            _lines.Add(new FormattedLine(line, y));
            WidthIncludingTrailingWhitespace = Math.Max(WidthIncludingTrailingWhitespace, line.WidthIncludingTrailingWhitespace);
            Width = Math.Max(Width, line.Width);
            y += line.Height;

            // Length already includes trailing newline TextSource positions; adding NewLineLength
            // skips the next visual line and breaks multiline selection hit-testing.
            var consumed = line.Length;
            if (consumed <= 0)
            {
                break;
            }

            index = line.FirstTextSourceIndex + consumed;
            previousLineBreak = line.TextLineBreak;
        }

        Height = y;
    }

    /// <summary>
    /// Строка с картинкой, которая не помещается в межстрочный: Avalonia оставила
    /// бы строке заданную высоту, и картинка наехала бы на соседнюю строку. Такая
    /// строка форматируется заново с межстрочным, в который помещается и
    /// картинка, и обычный зазор строки под базовой линией. Над картинкой Avalonia
    /// центрирует содержимое, поэтому сверху остаётся столько же лишнего места,
    /// сколько снизу, — строка немного выше, чем в браузере, но не налезает.
    /// </summary>
    private static TextLine? FormatLineFittingImages(
        TextFormatter formatter,
        MarkdownTextSource source,
        int index,
        TextParagraphProperties properties,
        TextLineBreak? previousLineBreak,
        MarkdownInlineImageMetrics imageMetrics)
    {
        var line = formatter.FormatLine(source, index, source.MaxWidth, properties, previousLineBreak);
        if (line is null
            || double.IsNaN(properties.LineHeight)
            || properties.LineHeight <= 0
            || !HasImageOutsideTheLine(line, properties.LineHeight, imageMetrics.StrutDescent))
        {
            return line;
        }

        var natural = formatter.FormatLine(source, index, source.MaxWidth, WithLineHeight(properties, double.NaN), previousLineBreak);
        if (natural is null)
        {
            return line;
        }

        var height = Math.Max(
            Math.Max(properties.LineHeight, natural.Height),
            2 * (imageMetrics.StrutDescent + natural.Baseline) - natural.Height);
        natural.Dispose();
        line.Dispose();
        return formatter.FormatLine(source, index, source.MaxWidth, WithLineHeight(properties, height), previousLineBreak);
    }

    private static bool HasImageOutsideTheLine(TextLine line, double lineHeight, double strutDescent)
    {
        var strutAscent = lineHeight - strutDescent;
        foreach (var run in line.TextRuns)
        {
            if (run is MarkdownInlineImageTextRun image
                && (image.Baseline > strutAscent || image.Size.Height - image.Baseline > strutDescent))
            {
                return true;
            }
        }

        return false;
    }

    private static GenericTextParagraphProperties WithLineHeight(TextParagraphProperties properties, double lineHeight)
        => new(
            properties.DefaultTextRunProperties,
            properties.TextAlignment,
            properties.TextWrapping,
            lineHeight,
            properties.LetterSpacing);

    private IReadOnlyList<Rect> GetDisplayRects(int displayStart, int displayLength)
    {
        if (displayLength <= 0 || _lines.Count == 0)
        {
            return Array.Empty<Rect>();
        }

        var displayEnd = displayStart + displayLength;
        var rects = new List<Rect>();

        foreach (var line in _lines)
        {
            var lineStart = line.TextLine.FirstTextSourceIndex;
            var lineEnd = lineStart + line.TextLine.Length;
            var overlapStart = Math.Max(displayStart, lineStart);
            var overlapEnd = Math.Min(displayEnd, lineEnd);
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            foreach (var bounds in line.TextLine.GetTextBounds(overlapStart, overlapEnd - overlapStart))
            {
                rects.Add(bounds.Rectangle.Translate(new Vector(0, line.Y)));
            }
        }

        return rects;
    }

    private int FindLineIndex(double y)
    {
        if (y <= 0)
        {
            return 0;
        }

        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];
            if (y < line.Y + line.TextLine.Height || index == _lines.Count - 1)
            {
                return index;
            }
        }

        return _lines.Count - 1;
    }

    private readonly record struct FormattedLine(TextLine TextLine, double Y);

    private static IReadOnlyDictionary<int, MarkdownInlineImageState> EmptyInlineImages { get; } =
        new Dictionary<int, MarkdownInlineImageState>();
}

internal readonly record struct MarkdownFormattedTextLineMetrics(Rect Bounds);

internal sealed class MarkdownTextSource : ITextSource
{
    private readonly MarkdownDisplayLayoutModel _displayModel;
    private readonly IReadOnlyList<MarkdownInlineImageSpan> _images;
    private readonly IReadOnlyDictionary<int, MarkdownInlineImageState> _imageStates;
    private readonly MarkdownTextRunPropertiesFactory _propertiesFactory;
    private readonly MarkdownInlineCodePadMetrics _padMetrics;
    private readonly MarkdownInlineImageMetrics _imageMetrics;
    private readonly MarkdownFootnoteReferenceMetrics? _footnoteMetrics;
    private readonly MarkdownKeyboardKeyMetrics? _keyboardMetrics;
    private readonly MarkdownBackReferenceMetrics? _backReferenceMetrics;

    // Поле клавиши по индексу сегмента её левого и правого поля; считается один
    // раз на абзац, а не на каждую раскладку строки.
    private Dictionary<int, double>? _keyboardSideWidths;

    public MarkdownTextSource(
        MarkdownDisplayLayoutModel displayModel,
        IReadOnlyList<MarkdownInlineImageSpan> images,
        IReadOnlyDictionary<int, MarkdownInlineImageState> imageStates,
        MarkdownTextRunPropertiesFactory propertiesFactory,
        MarkdownInlineCodePadMetrics padMetrics,
        MarkdownInlineImageMetrics imageMetrics,
        double maxWidth = 100_000,
        MarkdownFootnoteReferenceMetrics? footnoteMetrics = null,
        MarkdownKeyboardKeyMetrics? keyboardMetrics = null,
        MarkdownBackReferenceMetrics? backReferenceMetrics = null)
    {
        _displayModel = displayModel;
        _images = images;
        _imageStates = imageStates;
        _propertiesFactory = propertiesFactory;
        _padMetrics = padMetrics;
        _imageMetrics = imageMetrics;
        _footnoteMetrics = footnoteMetrics;
        _keyboardMetrics = keyboardMetrics;
        _backReferenceMetrics = backReferenceMetrics;
        MaxWidth = maxWidth;
    }

    public double MaxWidth { get; }

    public TextRun GetTextRun(int textSourceIndex)
    {
        if (textSourceIndex >= _displayModel.DisplayLength)
        {
            return new TextEndOfParagraph(1);
        }

        var segmentIndex = _displayModel.FindSegmentIndex(textSourceIndex);
        if (segmentIndex < 0)
        {
            return new TextEndOfParagraph(1);
        }

        var segment = _displayModel.Segments[segmentIndex];
        var localOffset = textSourceIndex - segment.DisplayStart;

        return segment.Kind switch
        {
            MarkdownDisplaySegmentKind.Text => new TextCharacters(
                segment.Text.AsMemory(localOffset),
                _propertiesFactory.Get(segment.Style)),
            MarkdownDisplaySegmentKind.LineBreak => new TextEndOfLine(1),
            MarkdownDisplaySegmentKind.Image => CreateImageRun(segment),
            MarkdownDisplaySegmentKind.CodePaddingLeft or MarkdownDisplaySegmentKind.CodePaddingRight
                when segment.Style.IsKeyboard && _keyboardMetrics is not null
                => MarkdownSpacerTextRun.Create(GetKeyboardSideWidth(_keyboardMetrics, segmentIndex), _padMetrics),
            MarkdownDisplaySegmentKind.CodePaddingLeft => MarkdownSpacerTextRun.Left(_padMetrics),
            MarkdownDisplaySegmentKind.CodePaddingRight => MarkdownSpacerTextRun.Right(_padMetrics),
            MarkdownDisplaySegmentKind.KeyboardGap when _keyboardMetrics is not null
                => MarkdownSpacerTextRun.Create(_keyboardMetrics.Gap, _padMetrics),
            MarkdownDisplaySegmentKind.FootnoteReference when _footnoteMetrics is not null
                => new MarkdownFootnoteReferenceTextRun(segment.Text, _footnoteMetrics),
            MarkdownDisplaySegmentKind.FootnoteBackReference when _backReferenceMetrics is not null
                => new MarkdownBackReferenceTextRun(_backReferenceMetrics),
            MarkdownDisplaySegmentKind.FootnoteBackReference => MarkdownSpacerTextRun.Create(0, _padMetrics),
            _ => new TextEndOfParagraph(1)
        };
    }

    private double GetKeyboardSideWidth(MarkdownKeyboardKeyMetrics metrics, int paddingSegmentIndex)
    {
        _keyboardSideWidths ??= MeasureKeyboardSides(metrics);
        return _keyboardSideWidths.TryGetValue(paddingSegmentIndex, out var width) ? width : 0;
    }

    /// <summary>
    /// Подпись клавиши — текст между её левым и правым полем; оба поля одной ширины.
    /// </summary>
    private Dictionary<int, double> MeasureKeyboardSides(MarkdownKeyboardKeyMetrics metrics)
    {
        var widths = new Dictionary<int, double>();
        var segments = _displayModel.Segments;
        var label = new System.Text.StringBuilder();
        for (var index = 0; index < segments.Count; index++)
        {
            if (segments[index] is not { Kind: MarkdownDisplaySegmentKind.CodePaddingLeft, Style.IsKeyboard: true })
            {
                continue;
            }

            label.Clear();
            var right = index + 1;
            for (; right < segments.Count && segments[right].Kind == MarkdownDisplaySegmentKind.Text; right++)
            {
                label.Append(segments[right].Text);
            }

            var side = metrics.GetSideWidth(label.ToString());
            widths[index] = side;
            widths[right] = side;
            index = right;
        }

        return widths;
    }

    private MarkdownInlineImageTextRun CreateImageRun(MarkdownDisplaySegment segment)
    {
        if ((uint)segment.ImageIndex >= (uint)_images.Count)
        {
            return MarkdownInlineImageTextRun.Placeholder(segment.Text, _imageMetrics, failed: true);
        }

        var image = _images[segment.ImageIndex];
        _imageStates.TryGetValue(image.Index, out var state);
        return MarkdownInlineImageTextRun.Create(image, state, _imageMetrics, MaxWidth);
    }
}

internal sealed class MarkdownTextRunPropertiesFactory
{
    /// <summary>
    /// Код показывается символ в символ: лигатуры моноширинного шрифта рисуют
    /// <c>-|</c> как <c>⊣</c>, а <c>=&gt;</c> как <c>⇒</c>, и читатель видит не то,
    /// что написано в файле. У JetBrains Mono они живут в <c>calt</c>, у других
    /// шрифтов бывают и в <c>liga</c>.
    /// </summary>
    internal static FontFeatureCollection CodeFontFeatures { get; } =
    [
        FontFeature.Parse("-liga"),
        FontFeature.Parse("-calt")
    ];

    /// <summary>Цифры одной ширины — номера в колонке стоят ровно.</summary>
    internal static FontFeatureCollection TabularNumberFontFeatures { get; } =
    [
        FontFeature.Parse("tnum")
    ];

    private readonly Dictionary<MarkdownInlineStyleState, TextRunProperties> _cache = new();
    private readonly FontFamily _baseFontFamily;
    private readonly FontFamily _inlineCodeFontFamily;
    private readonly double _fontSize;
    private readonly FontWeight _fontWeight;
    private readonly FontStyle _fontStyle;
    private readonly IBrush _foreground;
    private readonly TextDecorationCollection? _linkDecorations;
    private readonly FontFeatureCollection? _baseFontFeatures;
    private readonly IBrush _inlineCodeForeground;
    private readonly IBrush _keyboardForeground;

    /// <param name="inlineCodeForeground">Цвет инлайн-кода; без него — цвет текста.</param>
    /// <param name="keyboardForeground">Цвет подписи клавиши; без него — цвет текста.</param>
    public MarkdownTextRunPropertiesFactory(
        FontFamily baseFontFamily,
        FontFamily inlineCodeFontFamily,
        double fontSize,
        FontWeight fontWeight,
        FontStyle fontStyle,
        IBrush foreground,
        TextDecorationCollection? linkDecorations,
        FontFeatureCollection? baseFontFeatures = null,
        IBrush? inlineCodeForeground = null,
        IBrush? keyboardForeground = null)
    {
        _baseFontFamily = baseFontFamily;
        _inlineCodeFontFamily = inlineCodeFontFamily;
        _fontSize = fontSize;
        _fontWeight = fontWeight;
        _fontStyle = fontStyle;
        _foreground = foreground;
        _linkDecorations = linkDecorations;
        _baseFontFeatures = baseFontFeatures;
        _inlineCodeForeground = inlineCodeForeground ?? foreground;
        _keyboardForeground = keyboardForeground ?? foreground;
    }

    /// <summary>
    /// Подпись клавиши — шрифтом текста, мельче его и всегда прямым нормальным
    /// начертанием, даже внутри жирного или курсива.
    /// </summary>
    public Typeface KeyboardTypeface => new(_baseFontFamily, FontStyle.Normal, FontWeight.Normal);

    public double KeyboardFontSize => _fontSize * MarkdownDocumentMetrics.KeyboardFontScale;

    public TextRunProperties Get(MarkdownInlineStyleState style)
    {
        if (_cache.TryGetValue(style, out var properties))
        {
            return properties;
        }

        properties = style.IsKeyboard
            ? new GenericTextRunProperties(
                KeyboardTypeface,
                KeyboardFontSize,
                ResolveDecorations(style),
                _keyboardForeground,
                backgroundBrush: null,
                BaselineAlignment.Baseline,
                CultureInfo.CurrentUICulture,
                _baseFontFeatures)
            : new GenericTextRunProperties(
                new Typeface(
                    style.IsCode ? _inlineCodeFontFamily : _baseFontFamily,
                    style.IsItalic ? FontStyle.Italic : _fontStyle,
                    style.IsBold ? FontWeight.Bold : _fontWeight),
                style.IsCode ? _fontSize * MarkdownDocumentMetrics.InlineCodeFontScale : _fontSize,
                ResolveDecorations(style),
                style.IsCode ? _inlineCodeForeground : _foreground,
                backgroundBrush: null,
                BaselineAlignment.Baseline,
                CultureInfo.CurrentUICulture,
                style.IsCode ? CodeFontFeatures : _baseFontFeatures);
        _cache.Add(style, properties);
        return properties;
    }

    /// <summary>
    /// The strikethrough line has no explicit stroke, so it is drawn in the
    /// run foreground and follows the theme text colour.
    /// </summary>
    private TextDecorationCollection? ResolveDecorations(MarkdownInlineStyleState style)
    {
        var linkDecorations = style.IsLink ? _linkDecorations : null;
        if (!style.IsStrikethrough)
        {
            return linkDecorations;
        }

        if (linkDecorations is null)
        {
            return TextDecorations.Strikethrough;
        }

        var combined = new TextDecorationCollection();
        combined.AddRange(linkDecorations);
        combined.AddRange(TextDecorations.Strikethrough);
        return combined;
    }
}

/// <summary>
/// Метрики метки сноски: номер мельче основного текста и поднят над его базовой
/// линией, как <c>&lt;sup&gt;</c> в браузере. Раскладки номеров кэшируются: каждая
/// строится один раз на абзац, а не на каждую перерисовку, и освобождается вместе
/// с раскладкой абзаца.
/// </summary>
internal sealed class MarkdownFootnoteReferenceMetrics : IDisposable
{
    private const double FontSizeRatio = 0.75;

    // Подъём — .5em кегля номера, как vertical-align: .5em у sup.
    private const double RaiseRatio = 0.5 * FontSizeRatio;

    private readonly Dictionary<string, TextLayout> _numberLayouts = new(StringComparer.Ordinal);

    private MarkdownFootnoteReferenceMetrics(
        TextRunProperties baseProperties,
        Typeface typeface,
        double fontSize,
        double raise,
        double height,
        double baseline,
        IBrush foreground)
    {
        BaseProperties = baseProperties;
        Typeface = typeface;
        FontSize = fontSize;
        Raise = raise;
        Height = height;
        Baseline = baseline;
        Foreground = foreground;
    }

    /// <summary>Свойства основного текста: метка занимает в строке столько же места по высоте.</summary>
    public TextRunProperties BaseProperties { get; }

    /// <summary>Начертание номера: средний (500) в любом тексте.</summary>
    public const FontWeight NumberWeight = FontWeight.Medium;

    /// <summary>Гарнитура номера.</summary>
    public Typeface Typeface { get; }

    /// <summary>Кегль номера.</summary>
    public double FontSize { get; }

    /// <summary>Подъём базовой линии номера над базовой линией строки.</summary>
    public double Raise { get; }

    /// <summary>Высота и базовая линия основного текста.</summary>
    public double Height { get; }

    public double Baseline { get; }

    public IBrush Foreground { get; }

    public static MarkdownFootnoteReferenceMetrics Create(
        FontFamily fontFamily,
        double fontSize,
        FontWeight fontWeight,
        FontStyle fontStyle,
        IBrush foreground)
    {
        var typeface = new Typeface(fontFamily, fontStyle, fontWeight);
        using var probe = CreateTextLayout("M", typeface, fontSize, foreground);

        // Номер средним начертанием в любом тексте — и в жирном, и в обычном.
        var numberTypeface = new Typeface(fontFamily, fontStyle, NumberWeight);
        var baseProperties = new GenericTextRunProperties(
            typeface,
            fontSize,
            textDecorations: null,
            foreground,
            backgroundBrush: null,
            BaselineAlignment.Baseline,
            CultureInfo.CurrentUICulture);

        return new MarkdownFootnoteReferenceMetrics(
            baseProperties,
            numberTypeface,
            fontSize * FontSizeRatio,
            fontSize * RaiseRatio,
            Math.Max(1, probe.Height),
            Math.Clamp(probe.Baseline, 0, Math.Max(1, probe.Height)),
            foreground);
    }

    /// <summary>Раскладка номера; принадлежит метрикам, освобождать её не нужно.</summary>
    public TextLayout GetNumberLayout(string number)
    {
        if (!_numberLayouts.TryGetValue(number, out var layout))
        {
            layout = CreateTextLayout(number, Typeface, FontSize, Foreground);
            _numberLayouts.Add(number, layout);
        }

        return layout;
    }

    public void Dispose()
    {
        foreach (var layout in _numberLayouts.Values)
        {
            layout.Dispose();
        }

        _numberLayouts.Clear();
    }

    private static TextLayout CreateTextLayout(string text, Typeface typeface, double fontSize, IBrush foreground)
        => new(
            text,
            typeface,
            fontSize,
            foreground,
            TextAlignment.Left,
            TextWrapping.NoWrap,
            textTrimming: null,
            textDecorations: null,
            flowDirection: FlowDirection.LeftToRight,
            maxWidth: double.PositiveInfinity,
            maxHeight: double.PositiveInfinity,
            lineHeight: double.NaN,
            letterSpacing: 0,
            maxLines: 0,
            textStyleOverrides: null);
}

/// <summary>
/// Метка сноски в строке: номер верхним индексом. По высоте и базовой линии run
/// совпадает с основным текстом, поэтому строка с меткой не выше соседних; номер
/// рисуется выше базовой линии, в пределах межстрочного интервала.
/// </summary>
internal sealed class MarkdownFootnoteReferenceTextRun : DrawableTextRun
{
    // Метка узкая — по паре пикселей с боков, чтобы в неё было проще попасть мышью.
    private const double HorizontalPadding = 1;

    private readonly TextLayout _numberLayout;
    private readonly MarkdownFootnoteReferenceMetrics _metrics;
    private readonly Size _size;
    private readonly double _textOffsetY;

    public MarkdownFootnoteReferenceTextRun(string number, MarkdownFootnoteReferenceMetrics metrics)
    {
        _metrics = metrics;
        _numberLayout = metrics.GetNumberLayout(number);
        _size = new Size(_numberLayout.WidthIncludingTrailingWhitespace + HorizontalPadding * 2, metrics.Height);
        _textOffsetY = metrics.Baseline - metrics.Raise - _numberLayout.Baseline;
    }

    public override int Length => 1;

    public override ReadOnlyMemory<char> Text => " ".AsMemory();

    public override TextRunProperties Properties => _metrics.BaseProperties;

    public override Size Size => _size;

    public override double Baseline => _metrics.Baseline;

    public override void Draw(DrawingContext drawingContext, Point origin)
        => _numberLayout.Draw(drawingContext, new Point(origin.X + HorizontalPadding, origin.Y + _textOffsetY));
}

/// <summary>
/// Поля инлайн-кода. <paramref name="Height"/> и <paramref name="Baseline"/> —
/// строки текста кода: по ним поля стоят в строке и не раздвигают её.
/// <paramref name="BoxHeight"/> — высота плашки вместе с полями сверху и снизу;
/// клавиша той же высоты.
/// </summary>
internal readonly record struct MarkdownInlineCodePadMetrics(
    double LeftWidth,
    double RightWidth,
    double Height,
    double Baseline,
    double BoxHeight)
{
    /// <param name="fontSize">Кегль кода: поля — в его долях, как em в CSS.</param>
    public static MarkdownInlineCodePadMetrics Create(
        FontFamily fontFamily,
        double fontSize,
        FontWeight fontWeight,
        FontStyle fontStyle,
        IBrush foreground)
    {
        using var probe = new TextLayout(
            "M",
            new Typeface(fontFamily, fontStyle, fontWeight),
            fontSize,
            foreground,
            TextAlignment.Left,
            TextWrapping.NoWrap,
            textTrimming: null,
            textDecorations: null,
            flowDirection: FlowDirection.LeftToRight,
            maxWidth: double.PositiveInfinity,
            maxHeight: double.PositiveInfinity,
            lineHeight: double.NaN,
            letterSpacing: 0,
            maxLines: 0,
            textStyleOverrides: null);

        var height = Math.Max(1, probe.Height);
        var baseline = Math.Clamp(probe.Baseline, 0, height);
        var side = fontSize * MarkdownDocumentMetrics.InlineCodeHorizontalPadding;
        var boxHeight = height + 2 * fontSize * MarkdownDocumentMetrics.InlineCodeVerticalPadding;
        return new MarkdownInlineCodePadMetrics(side, side, height, baseline, boxHeight);
    }
}

/// <summary>
/// Размеры клавиши (<c>&lt;kbd&gt;</c>) в долях размера текста: поля по бокам,
/// ширина не меньше высоты — «O» и «S» квадратные — и зазор между клавишами
/// вплотную. Ширина подписи замеряется один раз на подпись.
/// </summary>
internal sealed class MarkdownKeyboardKeyMetrics
{
    /// <summary>Толщина рамки клавиши.</summary>
    public const double KeyboardBorderThickness = 1;

    private readonly Dictionary<string, double> _sideWidths = new(StringComparer.Ordinal);
    private readonly Typeface _typeface;
    private readonly double _keyFontSize;
    private readonly double _padding;
    private readonly double _minWidth;

    public MarkdownKeyboardKeyMetrics(Typeface typeface, double keyFontSize, double textFontSize, double height)
    {
        _typeface = typeface;
        _keyFontSize = keyFontSize;
        // Поле .36em — внутри рамки, как padding в CSS: рамка 1 px добавляется к нему.
        _padding = textFontSize * MarkdownDocumentMetrics.KeyboardHorizontalPadding + KeyboardBorderThickness;
        _minWidth = height;
        Gap = textFontSize * MarkdownDocumentMetrics.KeyboardGap;
    }

    /// <summary>Зазор между клавишами, стоящими вплотную.</summary>
    public double Gap { get; }

    /// <summary>Поле с каждой стороны подписи: не меньше заданного, а узкую подпись — по центру квадрата.</summary>
    public double GetSideWidth(string text)
    {
        if (_sideWidths.TryGetValue(text, out var side))
        {
            return side;
        }

        using var layout = new TextLayout(
            text,
            _typeface,
            _keyFontSize,
            Brushes.Black,
            TextAlignment.Left,
            TextWrapping.NoWrap);
        side = Math.Max(_padding, (_minWidth - layout.WidthIncludingTrailingWhitespace) / 2);
        _sideWidths.Add(text, side);
        return side;
    }
}

/// <summary>
/// Вид заглушки строчной картинки, пока та грузится или не загрузилась: пунктир
/// «места под картинку» и иконка image-off у битой. Своих ресурсов у раскладки
/// нет, поэтому кисти и геометрию темы приносит фрагмент.
/// </summary>
internal readonly record struct MarkdownInlineImagePlaceholderBrushes(
    IBrush Border,
    IBrush Icon,
    Geometry? IconGeometry = null);

/// <param name="StrutDescent">
/// Сколько обычная строка текста занимает под базовой линией при заданном
/// межстрочном: столько же остаётся под строкой, раздвинутой картинкой.
/// </param>
internal readonly record struct MarkdownInlineImageMetrics(
    double MaxHeight,
    double Baseline,
    double TextFontSize,
    double StrutDescent,
    MarkdownInlineImagePlaceholderBrushes Placeholder)
{
    public static MarkdownInlineImageMetrics Create(
        FontFamily fontFamily,
        double fontSize,
        FontWeight fontWeight,
        FontStyle fontStyle,
        double lineHeight,
        MarkdownInlineImagePlaceholderBrushes placeholder)
    {
        using var probe = new TextLayout(
            "M",
            new Typeface(fontFamily, fontStyle, fontWeight),
            fontSize,
            placeholder.Icon,
            TextAlignment.Left,
            TextWrapping.NoWrap,
            textTrimming: null,
            textDecorations: null,
            flowDirection: FlowDirection.LeftToRight,
            maxWidth: double.PositiveInfinity,
            maxHeight: double.PositiveInfinity,
            lineHeight: double.NaN,
            letterSpacing: 0,
            maxLines: 0,
            textStyleOverrides: null);

        var normalizedLineHeight = double.IsNaN(lineHeight) || lineHeight <= 0
            ? Math.Max(fontSize * 1.25, probe.Height)
            : lineHeight;
        var maxHeight = normalizedLineHeight * 0.9;
        var baseline = Math.Clamp(
            probe.Baseline + Math.Max(0, (maxHeight - probe.Height) / 2),
            0,
            maxHeight);

        // Межстрочный Avalonia делит поровну над и под текстом.
        var strutDescent = normalizedLineHeight - (probe.Baseline + (normalizedLineHeight - probe.Height) / 2);

        return new MarkdownInlineImageMetrics(maxHeight, baseline, fontSize, strutDescent, placeholder);
    }
}

internal sealed record MarkdownInlineImageState(IImage? Image, Stream? BackingStream, bool Failed)
{
    public static MarkdownInlineImageState FailedState { get; } = new(null, null, true);
}

/// <summary>
/// Картинка в строке текста: в своём размере (не шире строки), чуть ниже
/// базовой линии. Пока грузится — пунктирная рамка, битая — рамка с иконкой
/// image-off; размер рамки зависит от alt, чтобы строка не прыгала после загрузки.
/// </summary>
internal sealed class MarkdownInlineImageTextRun : DrawableTextRun
{
    // Ширина заглушки в долях размера текста: на знак alt, поля и пределы.
    private const double PlaceholderCharWidthRatio = 0.41;
    private const double PlaceholderHorizontalPaddingRatio = 0.57;
    private const double PlaceholderMinWidthRatio = 2.43;
    private const double PlaceholderMaxWidthRatio = 8.57;
    private const double PlaceholderHeightRatio = 0.85;
    private const double PlaceholderIconRatio = 0.7;

    private readonly MarkdownInlineImageMetrics _metrics;
    private readonly IImage? _image;
    private readonly bool _failed;
    private readonly Size _size;
    private readonly double _baseline;

    private MarkdownInlineImageTextRun(
        string label,
        MarkdownInlineImageMetrics metrics,
        IImage? image,
        bool failed,
        double maxWidth)
    {
        _metrics = metrics;
        _image = image;
        _failed = failed;
        _size = image is null
            ? ResolvePlaceholderSize(string.IsNullOrWhiteSpace(label) ? "image" : label, metrics)
            : ResolveImageSize(image, maxWidth);
        _baseline = image is null
            ? Math.Min(_size.Height, metrics.Baseline)
            : Math.Max(0, _size.Height - metrics.TextFontSize * MarkdownDocumentMetrics.InlineImageBaselineDrop);
    }

    public static MarkdownInlineImageTextRun Create(
        MarkdownInlineImageSpan image,
        MarkdownInlineImageState? state,
        MarkdownInlineImageMetrics metrics,
        double maxWidth = double.PositiveInfinity)
        => new(image.PlaceholderText, metrics, state?.Image, state?.Failed == true, maxWidth);

    public static MarkdownInlineImageTextRun Placeholder(string label, MarkdownInlineImageMetrics metrics, bool failed)
        => new(label, metrics, null, failed, double.PositiveInfinity);

    public override int Length => 1;

    public override ReadOnlyMemory<char> Text => " ".AsMemory();

    public override TextRunProperties Properties => EmptyTextRunProperties.Instance;

    public override Size Size => _size;

    public override double Baseline => _baseline;

    public override void Draw(DrawingContext drawingContext, Point origin)
    {
        var rect = new Rect(origin, _size);
        if (_image is not null)
        {
            drawingContext.DrawImage(_image, new Rect(_image.Size), rect);
            return;
        }

        var placeholder = _metrics.Placeholder;
        MarkdownMissingContentFrame.Draw(
            drawingContext,
            MarkdownMissingContentFrame.CreatePen(placeholder.Border),
            rect,
            _metrics.TextFontSize * MarkdownDocumentMetrics.MissingFrameCornerRadiusRatio);

        if (!_failed || placeholder.IconGeometry is not { } icon)
        {
            return;
        }

        var side = Math.Min(rect.Width, rect.Height) * PlaceholderIconRatio;
        var iconOrigin = new Point(rect.X + (rect.Width - side) / 2, rect.Y + (rect.Height - side) / 2);
        using (drawingContext.PushTransform(Matrix.CreateTranslation(iconOrigin.X, iconOrigin.Y)))
        {
            LucideIcon.Draw(drawingContext, icon, LucideIcon.CreatePen(placeholder.Icon), new Size(side, side));
        }
    }

    private static Size ResolveImageSize(IImage image, double maxWidth)
    {
        var natural = image.Size;
        var scale = double.IsFinite(maxWidth) && maxWidth > 0
            ? Math.Min(1d, maxWidth / Math.Max(1, natural.Width))
            : 1d;
        return new Size(
            Math.Max(1, natural.Width * scale),
            Math.Max(1, natural.Height * scale));
    }

    private static Size ResolvePlaceholderSize(string label, MarkdownInlineImageMetrics metrics)
    {
        var em = metrics.TextFontSize;
        var width = Math.Clamp(
            em * (label.Length * PlaceholderCharWidthRatio + PlaceholderHorizontalPaddingRatio * 2),
            em * PlaceholderMinWidthRatio,
            em * PlaceholderMaxWidthRatio);
        return new Size(width, metrics.MaxHeight * PlaceholderHeightRatio);
    }

    private sealed class EmptyTextRunProperties : TextRunProperties
    {
        public static EmptyTextRunProperties Instance { get; } = new();

        public override Typeface Typeface { get; } = new(Typeface.Default.FontFamily);

        public override double FontRenderingEmSize => 1;

        public override TextDecorationCollection? TextDecorations => null;

        public override IBrush? ForegroundBrush => null;

        public override IBrush? BackgroundBrush => null;

        public override BaselineAlignment BaselineAlignment => BaselineAlignment.Baseline;

        public override CultureInfo CultureInfo => CultureInfo.InvariantCulture;
    }
}

internal sealed class MarkdownSpacerTextRun : DrawableTextRun
{
    private readonly double _width;
    private readonly double _height;
    private readonly double _baseline;

    private MarkdownSpacerTextRun(double width, double height, double baseline)
    {
        _width = width;
        _height = height;
        _baseline = baseline;
    }

    public static MarkdownSpacerTextRun Left(MarkdownInlineCodePadMetrics metrics)
        => new(metrics.LeftWidth, metrics.Height, metrics.Baseline);

    public static MarkdownSpacerTextRun Right(MarkdownInlineCodePadMetrics metrics)
        => new(metrics.RightWidth, metrics.Height, metrics.Baseline);

    /// <summary>Пустое место заданной ширины высотой в строку кода.</summary>
    public static MarkdownSpacerTextRun Create(double width, MarkdownInlineCodePadMetrics metrics)
        => new(width, metrics.Height, metrics.Baseline);

    public override int Length => 1;

    public override ReadOnlyMemory<char> Text => " ".AsMemory();

    public override TextRunProperties Properties => EmptyTextRunProperties.Instance;

    public override Size Size => new(_width, _height);

    public override double Baseline => _baseline;

    public override void Draw(DrawingContext drawingContext, Point origin)
    {
    }

    private sealed class EmptyTextRunProperties : TextRunProperties
    {
        public static EmptyTextRunProperties Instance { get; } = new();

        public override Typeface Typeface { get; } = new(Typeface.Default.FontFamily);

        public override double FontRenderingEmSize => 1;

        public override TextDecorationCollection? TextDecorations => null;

        public override IBrush? ForegroundBrush => null;

        public override IBrush? BackgroundBrush => null;

        public override BaselineAlignment BaselineAlignment => BaselineAlignment.Baseline;

        public override CultureInfo CultureInfo => CultureInfo.InvariantCulture;
    }
}

/// <summary>Иконка возврата к метке сноски: геометрия Lucide и её цвет.</summary>
internal readonly record struct MarkdownBackReferenceIcon(Geometry Geometry, IBrush Foreground);

/// <summary>
/// Иконка возврата к метке в конце сноски: 1em текста сноски, в .35em от
/// последнего слова, низ на .12em ниже базовой линии. По высоте и базовой линии
/// run совпадает с текстом, поэтому строку не раздвигает.
/// </summary>
internal sealed class MarkdownBackReferenceMetrics
{
    public const double GapRatio = 0.35;
    public const double IconSizeRatio = 1;
    public const double DropRatio = 0.12;

    private MarkdownBackReferenceMetrics(
        TextRunProperties baseProperties,
        double gap,
        double iconSize,
        double drop,
        double height,
        double baseline,
        Pen pen,
        Geometry geometry)
    {
        BaseProperties = baseProperties;
        Gap = gap;
        IconSize = iconSize;
        Drop = drop;
        Height = height;
        Baseline = baseline;
        Pen = pen;
        Geometry = geometry;
    }

    public TextRunProperties BaseProperties { get; }

    public double Gap { get; }

    public double IconSize { get; }

    /// <summary>На сколько низ иконки ниже базовой линии.</summary>
    public double Drop { get; }

    public double Height { get; }

    public double Baseline { get; }

    public Pen Pen { get; }

    public Geometry Geometry { get; }

    public static MarkdownBackReferenceMetrics Create(
        FontFamily fontFamily,
        double fontSize,
        FontWeight fontWeight,
        FontStyle fontStyle,
        MarkdownBackReferenceIcon icon)
    {
        var typeface = new Typeface(fontFamily, fontStyle, fontWeight);
        using var probe = new TextLayout("M", typeface, fontSize, icon.Foreground);
        var baseProperties = new GenericTextRunProperties(
            typeface,
            fontSize,
            textDecorations: null,
            icon.Foreground,
            backgroundBrush: null,
            BaselineAlignment.Baseline,
            CultureInfo.CurrentUICulture);

        return new MarkdownBackReferenceMetrics(
            baseProperties,
            fontSize * GapRatio,
            fontSize * IconSizeRatio,
            fontSize * DropRatio,
            Math.Max(1, probe.Height),
            Math.Clamp(probe.Baseline, 0, Math.Max(1, probe.Height)),
            LucideIcon.CreatePen(icon.Foreground),
            icon.Geometry);
    }
}

internal sealed class MarkdownBackReferenceTextRun : DrawableTextRun
{
    private readonly MarkdownBackReferenceMetrics _metrics;

    public MarkdownBackReferenceTextRun(MarkdownBackReferenceMetrics metrics) => _metrics = metrics;

    public override int Length => 1;

    public override ReadOnlyMemory<char> Text => " ".AsMemory();

    public override TextRunProperties Properties => _metrics.BaseProperties;

    public override Size Size => new(_metrics.Gap + _metrics.IconSize, _metrics.Height);

    public override double Baseline => _metrics.Baseline;

    public override void Draw(DrawingContext drawingContext, Point origin)
    {
        var top = origin.Y + _metrics.Baseline + _metrics.Drop - _metrics.IconSize;
        using (drawingContext.PushTransform(Matrix.CreateTranslation(origin.X + _metrics.Gap, top)))
        {
            LucideIcon.Draw(drawingContext, _metrics.Geometry, _metrics.Pen, new Size(_metrics.IconSize, _metrics.IconSize));
        }
    }
}
