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

    // Иконка шапки GitHub alert заметно выше строчных букв заголовка и вплотную
    // к нему — читается как одна метка.
    private const double AlertIconSizeRatio = 1.25;

    private const double ParagraphGap = 1;
    private const double HorizontalRuleGap = 1;

    // Блоки, чей вид ещё не пересмотрен (MM-61…MM-64): прежние пиксели при
    // тексте 14 переведены в em от 14.
    private const double TableGap = 1.4;
    private const double TightListItemGap = 6.0 / 14;
    private const double ListItemTextIndentRatio = 12.0 / 14;
    private const double TaskCheckboxIndentAfterNumberRatio = 3.0 / 14;
    private const double AlertHeaderGapRatio = 4.0 / 14;
    private const double AlertIconTitleGapRatio = 4.0 / 14;
    private const double QuoteHorizontalPaddingRatio = 20.0 / 14;
    private const double QuoteVerticalPaddingRatio = 4.0 / 14;
    private const double FootnoteRowGapRatio = 8.0 / 14;
    private const double FootnoteRuleTopRatio = 1;
    private const double FootnoteRuleBottomRatio = 16.0 / 14;

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

    public double AlertHeaderGap => Em(AlertHeaderGapRatio);

    public double AlertIconTitleGap => Em(AlertIconTitleGapRatio);

    public double QuoteHorizontalPadding => Em(QuoteHorizontalPaddingRatio);

    public double QuoteVerticalPadding => Em(QuoteVerticalPaddingRatio);

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
