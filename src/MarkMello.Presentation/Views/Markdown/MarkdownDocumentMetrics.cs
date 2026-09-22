using Avalonia;
using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

/// <summary>
/// Все размеры документа — в долях размера текста (em) из
/// <see cref="ReadingPreferences.FontSize"/>: смена 14 на 18 сохраняет пропорции.
/// Это единственное место, где заданы коэффициенты; жёсткие пиксели остаются
/// только у толщины рамок и линий.
/// </summary>
/// <remarks>
/// Ритм блоков: у блока есть просвет сверху (и у немногих — снизу), первый блок
/// в контейнере стоит без просвета, а соседние просветы не складываются —
/// между блоками берётся больший из нижнего просвета верхнего блока и верхнего
/// просвета нижнего (<see cref="GapBetween"/>).
/// </remarks>
internal sealed class MarkdownDocumentMetrics
{
    /// <summary>Межстрочный заголовков.</summary>
    public const double HeadingLineHeightRatio = 1.3;

    /// <summary>Инлайн-код: кегль в долях текста, поля и скругление — в долях кегля кода, как em в CSS.</summary>
    public const double InlineCodeFontScale = 0.85;
    public const double InlineCodeVerticalPadding = 0.2;
    public const double InlineCodeHorizontalPadding = 0.4;
    public const double InlineCodeCornerRadius = 0.3;

    /// <summary>Клавиша (<c>&lt;kbd&gt;</c>): всё в долях текста.</summary>
    public const double KeyboardFontScale = 0.8;
    public const double KeyboardHorizontalPadding = 0.36;
    public const double KeyboardCornerRadius = 0.36;
    public const double KeyboardGap = 0.15;

    /// <summary>Подчёркивание ссылки: толщина и отступ от базовой линии.</summary>
    public const double LinkUnderlineThickness = 0.07;
    public const double LinkUnderlineOffset = 0.2;

    // Иконка чекбокса task list: рамка Lucide занимает 20/24 сетки, то есть почти
    // 1 em — выше заглавных букв, и галочка внутри контура читается с первого взгляда.
    private const double TaskCheckboxSizeRatio = 1.15;

    // Иконка GitHub alert стоит в своей колонке слева от текста, чуть ниже верха
    // строки заголовка.
    private const double AlertIconSizeRatio = 1.15;

    private const double ParagraphGap = 1;
    private const double HorizontalRuleGap = 1;

    // Блоки, чей вид ещё не пересмотрен (MM-62…MM-64): прежние пиксели при
    // тексте 14 переведены в em от 14.
    private const double TableGap = 1.4;
    private const double TightListItemGap = 6.0 / 14;
    private const double ListItemTextIndentRatio = 12.0 / 14;
    private const double TaskCheckboxIndentAfterNumberRatio = 3.0 / 14;
    private const double FootnoteRowGapRatio = 8.0 / 14;
    private const double FootnoteRuleTopRatio = 1;
    private const double FootnoteRuleBottomRatio = 16.0 / 14;

    /// <summary>Блок кода: лист с рамкой, язык и «Копировать» — в верхнем поле.</summary>
    public const double CodeBlockFontScale = 0.85;
    public const double CodeBlockLineHeightRatio = 1.5;
    private const double CodeBlockCornerRadiusRatio = 0.625;
    private const double CodeBlockSidePaddingRatio = 1.15;
    private const double CodeBlockBottomPaddingRatio = 1;
    private const double CodeBlockTopPaddingWithLanguageRatio = 2;
    private const double CodeBlockTopPaddingWithoutLanguageRatio = 0.85;
    private const double CodeBlockHeadTopRatio = 0.55;
    private const double CodeBlockCopyRightWithLanguageRatio = 0.6;
    private const double CodeBlockCopyRightWithoutLanguageRatio = 0.45;
    // 2.6em кегля кода, как margin у pre на холсте.
    private const double CodeBlockCodeClearanceWithoutLanguageRatio = 2.6 * CodeBlockFontScale;
    public const double CodeBlockLanguageFontScale = 0.786;
    private const double CodeCopyButtonSizeRatio = 1.71;
    private const double CodeCopyIconSizeRatio = 0.93;
    private const double CodeCopyCornerRadiusRatio = 6.0 / 14;

    /// <summary>Цитата: плашка, полоса слева и значок кавычек в правом верхнем углу.</summary>
    private const double QuoteCornerRadiusRatio = 6.0 / 14;
    private const double QuoteVerticalPaddingRatio = 0.55;
    private const double QuoteLeftPaddingRatio = 0.9;
    private const double QuoteRightPaddingRatio = 2.4;
    private const double QuoteMarkSizeRatio = 1.1;
    private const double QuoteMarkTopRatio = 0.7;
    private const double QuoteMarkRightRatio = 0.8;
    private const double NestedQuoteLeftPaddingRatio = 0.9;
    private const double NestedQuoteGapRatio = 0.5;

    /// <summary>Alert: плашка цвета вида, иконка в колонке слева, заголовок над текстом.</summary>
    private const double AlertCornerRadiusRatio = 4.0 / 14;
    private const double AlertTopPaddingRatio = 0.85;
    private const double AlertRightPaddingRatio = 1;
    private const double AlertBottomPaddingRatio = 0.85;
    private const double AlertLeftPaddingRatio = 0.8;
    private const double AlertIconTopRatio = 0.2;
    private const double AlertIconColumnGapRatio = 0.6;
    private const double AlertTitleGapRatio = 0.15;

    /// <summary>Картинка: скругление и подпись из alt.</summary>
    public const double ImageCornerRadiusRatio = 0.125;
    public const double ImageCaptionFontScale = 0.875;
    public const double ImageCaptionLineHeightRatio = 1.4;
    private const double ImageCaptionTopRatio = 0.4;
    private const double ImageCaptionLeftRatio = 0.2;

    /// <summary>Картинка в строке опущена ниже базовой линии.</summary>
    public const double InlineImageBaselineDrop = 0.15;

    /// <summary>
    /// «Место под картинку» — пунктирная рамка на месте битой картинки и
    /// неудавшейся диаграммы: иконка, под ней подпись и путь или сообщение.
    /// </summary>
    public const double MissingFrameCornerRadiusRatio = 0.3;
    private const double MissingFrameMinHeightRatio = 8;
    private const double MissingFramePaddingRatio = 1;
    private const double MissingIconSizeRatio = 1.7;
    private const double MissingIconGapRatio = 0.5;
    private const double MissingTextGapRatio = 0.1;
    public const double MissingPathFontScale = 0.8;
    private const double DiagramErrorTopPaddingRatio = 1.5;
    private const double DiagramErrorSidePaddingRatio = 1.15;
    public const double DiagramErrorMessageFontScale = 0.857;
    public const double DiagramErrorMessageLineHeightRatio = 1.5;
    private const double DiagramErrorMessageGapRatio = 0.15;
    private const double DiagramErrorSourceGapRatio = 1.15;

    public MarkdownDocumentMetrics(ReadingPreferences preferences)
    {
        FontSize = preferences.FontSize;
        LineHeightRatio = preferences.LineHeight;
    }

    /// <summary>Размер текста — 1em документа.</summary>
    public double FontSize { get; }

    /// <summary>Межстрочный текста из настроек.</summary>
    public double LineHeightRatio { get; }

    public double BodyLineHeight => FontSize * LineHeightRatio;

    public double Em(double ratio) => FontSize * ratio;

    public double InlineCodeFontSize => Em(InlineCodeFontScale);

    public double KeyboardFontSize => Em(KeyboardFontScale);

    public double TaskCheckboxSize => Math.Round(Em(TaskCheckboxSizeRatio));

    public double AlertIconSize => Math.Round(Em(AlertIconSizeRatio));

    /// <summary>Подчёркивание ссылки в пикселях от кегля текста ссылки — одной линией и под кодом с клавишами внутри неё.</summary>
    public static double GetLinkUnderlineThickness(double fontSize) => fontSize * LinkUnderlineThickness;

    /// <summary>Центр линии подчёркивания под базовой линией: верх линии в .2em, линия рисуется по центру толщины.</summary>
    public static double GetLinkUnderlineCenterOffset(double fontSize) => fontSize * (LinkUnderlineOffset + LinkUnderlineThickness / 2);

    public double LooseListItemGap => Em(ParagraphGap);

    public double TightListItemSpacing => Em(TightListItemGap);

    public double ListItemTextIndent => Em(ListItemTextIndentRatio);

    public double TaskCheckboxIndentAfterNumber => Em(TaskCheckboxIndentAfterNumberRatio);

    public double CodeBlockFontSize => Em(CodeBlockFontScale);

    public double CodeBlockLineHeight => CodeBlockFontSize * CodeBlockLineHeightRatio;

    public double CodeBlockCornerRadius => Em(CodeBlockCornerRadiusRatio);

    public double CodeBlockSidePadding => Em(CodeBlockSidePaddingRatio);

    public double CodeBlockBottomPadding => Em(CodeBlockBottomPaddingRatio);

    public double GetCodeBlockTopPadding(bool hasLanguage)
        => Em(hasLanguage ? CodeBlockTopPaddingWithLanguageRatio : CodeBlockTopPaddingWithoutLanguageRatio);

    /// <summary>Верх строки с языком и «Копировать» от внутреннего края рамки.</summary>
    public double CodeBlockHeadTop => Em(CodeBlockHeadTopRatio);

    public double GetCodeCopyRight(bool hasLanguage)
        => Em(hasLanguage ? CodeBlockCopyRightWithLanguageRatio : CodeBlockCopyRightWithoutLanguageRatio);

    /// <summary>Без языка код не доходит до «Копировать» справа на эту величину.</summary>
    public double CodeBlockCodeClearanceWithoutLanguage => Em(CodeBlockCodeClearanceWithoutLanguageRatio);

    public double CodeBlockLanguageFontSize => Em(CodeBlockLanguageFontScale);

    public double CodeCopyButtonSize => Em(CodeCopyButtonSizeRatio);

    public double CodeCopyIconSize => Em(CodeCopyIconSizeRatio);

    public double CodeCopyCornerRadius => Em(CodeCopyCornerRadiusRatio);

    public double QuoteCornerRadius => Em(QuoteCornerRadiusRatio);

    public Thickness QuotePadding
        => new(Em(QuoteLeftPaddingRatio), Em(QuoteVerticalPaddingRatio), Em(QuoteRightPaddingRatio), Em(QuoteVerticalPaddingRatio));

    public double QuoteMarkSize => Em(QuoteMarkSizeRatio);

    public double QuoteMarkTop => Em(QuoteMarkTopRatio);

    public double QuoteMarkRight => Em(QuoteMarkRightRatio);

    public double NestedQuoteLeftPadding => Em(NestedQuoteLeftPaddingRatio);

    public double NestedQuoteGap => Em(NestedQuoteGapRatio);

    public double AlertCornerRadius => Em(AlertCornerRadiusRatio);

    public Thickness AlertPadding
        => new(Em(AlertLeftPaddingRatio), Em(AlertTopPaddingRatio), Em(AlertRightPaddingRatio), Em(AlertBottomPaddingRatio));

    public double AlertIconTop => Em(AlertIconTopRatio);

    public double AlertIconColumnGap => Em(AlertIconColumnGapRatio);

    public double AlertTitleGap => Em(AlertTitleGapRatio);

    public double ImageCornerRadius => Em(ImageCornerRadiusRatio);

    public double ImageCaptionFontSize => Em(ImageCaptionFontScale);

    public double ImageCaptionLineHeight => ImageCaptionFontSize * ImageCaptionLineHeightRatio;

    public Thickness ImageCaptionMargin => new(Em(ImageCaptionLeftRatio), Em(ImageCaptionTopRatio), 0, 0);

    public double MissingFrameCornerRadius => Em(MissingFrameCornerRadiusRatio);

    public double MissingFrameMinHeight => Em(MissingFrameMinHeightRatio);

    public double MissingFramePadding => Em(MissingFramePaddingRatio);

    public double MissingIconSize => Em(MissingIconSizeRatio);

    public double MissingIconGap => Em(MissingIconGapRatio);

    public double MissingTextGap => Em(MissingTextGapRatio);

    public double MissingPathFontSize => Em(MissingPathFontScale);

    public Thickness DiagramErrorPadding
        => new(Em(DiagramErrorSidePaddingRatio), Em(DiagramErrorTopPaddingRatio), Em(DiagramErrorSidePaddingRatio), Em(DiagramErrorSidePaddingRatio));

    public double DiagramErrorMessageFontSize => Em(DiagramErrorMessageFontScale);

    public double DiagramErrorMessageLineHeight => DiagramErrorMessageFontSize * DiagramErrorMessageLineHeightRatio;

    public double DiagramErrorMessageGap => Em(DiagramErrorMessageGapRatio);

    public double DiagramErrorSourceGap => Em(DiagramErrorSourceGapRatio);

    public double FootnoteRowGap => Em(FootnoteRowGapRatio);

    public double FootnoteRuleTop => Em(FootnoteRuleTopRatio);

    public double FootnoteRuleBottom => Em(FootnoteRuleBottomRatio);

    public static double GetHeadingFontScale(int level) => level switch
    {
        1 => 1.875,
        2 => 1.5,
        3 => 1.25,
        4 => 1.125,
        _ => 1
    };

    /// <summary>Просвет над заголовком в долях размера текста.</summary>
    public static double GetHeadingGapRatio(int level) => level switch
    {
        1 => 2.5,
        2 => 2.29,
        3 => 2,
        4 => 1.75,
        _ => 1.5
    };

    public double GetHeadingFontSize(int level) => Em(GetHeadingFontScale(level));

    public double GetHeadingLineHeight(int level) => GetHeadingFontSize(level) * HeadingLineHeightRatio;

    /// <summary>Просветы блока сверху и снизу.</summary>
    public MarkdownBlockSpacing GetSpacing(MarkdownBlock block) => block switch
    {
        MarkdownHeadingBlock heading => new(Em(GetHeadingGapRatio(heading.Level)), 0),
        MarkdownHorizontalRuleBlock => new(Em(HorizontalRuleGap), Em(HorizontalRuleGap)),
        MarkdownTableBlock or MarkdownFrontMatterBlock => new(Em(TableGap), Em(TableGap)),
        _ => new(Em(ParagraphGap), 0)
    };

    /// <summary>Просвет между соседними блоками: больший из двух, а не сумма.</summary>
    public double GapBetween(MarkdownBlock previous, MarkdownBlock next)
        => Math.Max(GetSpacing(previous).Bottom, GetSpacing(next).Top);
}

internal readonly record struct MarkdownBlockSpacing(double Top, double Bottom);
