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

    /// <summary>Выделение маркером (<c>&lt;mark&gt;</c>): поля и скругление в долях текста.</summary>
    public const double HighlightVerticalPadding = 0.1;
    public const double HighlightHorizontalPadding = 0.15;
    public const double HighlightCornerRadius = 0.2;

    /// <summary>
    /// Индексы (<c>&lt;sub&gt;</c>, <c>&lt;sup&gt;</c>), как на GitHub: кегль в долях
    /// текста, сдвиг базовой линии — в долях текста (.5em и .25em кегля индекса).
    /// </summary>
    public const double ScriptFontScale = 0.75;
    public const double SuperscriptRaise = 0.375;
    public const double SubscriptDrop = 0.1875;

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

    /// <summary>
    /// Список, как в Notion: колонка маркеров 1.6em, у пункта ещё .15em до текста.
    /// Маркер прижат к правому краю колонки; колонка шире, если номер не влезает.
    /// </summary>
    private const double ListMarkerColumnRatio = 1.6;
    private const double ListItemPaddingRatio = 0.15;
    private const double TightListItemGapRatio = 0.25;
    private const double LooseListItemGapRatio = 0.6;
    private const double ListItemParagraphGapRatio = 0.5;
    private const double NestedListGapRatio = 0.25;

    /// <summary>
    /// Список определений — как списки: термин через .75em от предыдущего
    /// определения, определение с отступом колонки маркеров 1.6em в .15em от
    /// термина, определения одного термина — через .25em, абзацы в определении —
    /// через .5em.
    /// </summary>
    private const double DefinitionTermGapRatio = 0.75;
    private const double DefinitionIndentRatio = ListMarkerColumnRatio;
    private const double DefinitionGapRatio = 0.15;
    private const double DefinitionsGapRatio = TightListItemGapRatio;

    // Точка маркированного списка дальше от текста, чем пробел после «•»: браузер
    // рисует disc с таким зазором, 5 px при 14.
    private const double BulletMarkerGapRatio = 5.0 / 14;

    // Чекбокс в колонке маркера: текст задачи в 6 px от него при 14 — там же, где
    // текст обычного пункта. В нумерованном списке чекбокс после номера, в 3 px.
    private const double TaskCheckboxTextGapRatio = 6.0 / 14;
    private const double TaskCheckboxAfterNumberRatio = 3.0 / 14;

    /// <summary>
    /// Сноски — книжные: блок отделён просветом как перед H2 и короткой линией,
    /// текст мельче и мягче. Размеры внутри блока — в долях кегля сноски, как em в
    /// CSS блока; просвет над блоком и над кодом в сноске — в долях размера текста.
    /// </summary>
    private const double FootnotesGapRatio = 2.29;
    public const double FootnoteFontScale = 0.875;
    public const double FootnoteLineHeightRatio = 1.5;
    private const double FootnoteRuleWidthRatio = 4;
    private const double FootnoteRuleGapRatio = 1;
    // Как пункт списка: сноска в .15em от края, колонка номера 1.6em вместе с
    // отступом .5em до текста — текст сноски начинается в 1.75em.
    private const double FootnoteIndentRatio = 0.15;
    private const double FootnoteNumberColumnRatio = 1.6;
    private const double FootnoteNumberGapRatio = 0.5;
    private const double FootnoteGapRatio = 0.45;
    // Код в сноске ближе к тексту, чем в документе: .6em, как на кадре холста.
    private const double FootnoteCodeGapRatio = 0.6;

    /// <summary>Таблица: сетка, кегль текста, ячейка 7 × 9 px при 14, как в Notion.</summary>
    private const double TableGap = 1.4;
    public const double TableLineHeightRatio = 1.5;
    private const double TableCellVerticalPaddingRatio = 0.5;
    private const double TableCellHorizontalPaddingRatio = 0.643;

    /// <summary>
    /// Front matter — свойства, как в Notion: две колонки между линиями сверху и
    /// снизу. Поля, отступ ключа и межстрочный — в долях кегля блока, колонка
    /// ключей и высота строки — в долях размера текста.
    /// </summary>
    public const double FrontMatterFontScale = 0.875;
    public const double FrontMatterValueLineHeightRatio = 1.45;
    private const double FrontMatterKeyColumnRatio = 10;
    private const double FrontMatterRowMinHeightRatio = 2.125;
    private const double FrontMatterPaddingRatio = 1;
    private const double FrontMatterKeyGapRatio = 1;
    // До H1 — как перед H2, а не больший из двух просветов.
    private const double FrontMatterGapRatio = 2.29;

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

    /// <param name="preferences">Настройки чтения.</param>
    /// <param name="layoutScale">
    /// Масштаб отрисовки: зазоры между строками текста считаются в целых
    /// пикселях экрана (<see cref="GetTextGap"/>).
    /// </param>
    public MarkdownDocumentMetrics(ReadingPreferences preferences, double layoutScale = 1)
    {
        FontSize = preferences.FontSize;
        LineHeightRatio = preferences.LineHeight;
        LayoutScale = layoutScale > 0 ? layoutScale : 1;
    }

    public double LayoutScale { get; }

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

    public double ListMarkerColumnWidth => Em(ListMarkerColumnRatio);

    public double ListItemPadding => Em(ListItemPaddingRatio);

    public double BulletMarkerGap => Em(BulletMarkerGapRatio);

    /// <summary>Шаг между пунктами: .25em, в loose-списке — .6em (<see cref="GetTextGap"/>).</summary>
    public double GetListItemGap(bool isLoose)
        => GetTextGap(Em(isLoose ? LooseListItemGapRatio : TightListItemGapRatio), BodyLineHeight);

    /// <summary>
    /// Зазор под строкой текста — в целых пикселях экрана и меньше на то, на что
    /// раскладка округляет высоту строки вверх (22.4 → 23 при 14 / 1.6). Иначе
    /// излишек копился бы на каждом пункте длинного списка, а дробный шаг сетки
    /// при округлении ещё и раздувал бы её строки. Так пункт с зазором той же
    /// высоты, что в макете.
    /// </summary>
    public double GetTextGap(double gap, double lineHeight)
    {
        var roundedLine = Math.Ceiling(lineHeight * LayoutScale) / LayoutScale;
        return Math.Max(0, RoundToDevicePixels(gap - (roundedLine - lineHeight), LayoutScale));
    }

    /// <summary>
    /// Просвет между блоками одного пункта: абзац — через .5em, вложенный список —
    /// через .25em, остальное — в обычном ритме.
    /// </summary>
    public double GapInsideListItem(MarkdownBlock previous, MarkdownBlock next) => next switch
    {
        MarkdownParagraphBlock => Math.Max(GetTextGap(Em(ListItemParagraphGapRatio), BodyLineHeight), GetSpacing(previous).Bottom),
        MarkdownListBlock => Math.Max(GetTextGap(Em(NestedListGapRatio), BodyLineHeight), GetSpacing(previous).Bottom),
        _ => GapBetween(previous, next)
    };

    /// <summary>Над каждым термином, кроме первого (<see cref="GetTextGap"/>).</summary>
    public double DefinitionTermGap => GetTextGap(Em(DefinitionTermGapRatio), BodyLineHeight);

    public double DefinitionIndent => Em(DefinitionIndentRatio);

    /// <summary>От термина до его первого определения.</summary>
    public double DefinitionGap => GetTextGap(Em(DefinitionGapRatio), BodyLineHeight);

    /// <summary>Между определениями одного термина.</summary>
    public double DefinitionsGap => GetTextGap(Em(DefinitionsGapRatio), BodyLineHeight);

    /// <summary>Просвет между блоками одного определения: абзац — через .5em, остальное — в обычном ритме.</summary>
    public double GapInsideDefinition(MarkdownBlock previous, MarkdownBlock next) => next switch
    {
        MarkdownParagraphBlock => Math.Max(GetTextGap(Em(ListItemParagraphGapRatio), BodyLineHeight), GetSpacing(previous).Bottom),
        _ => GapBetween(previous, next)
    };

    public double TaskCheckboxTextGap => Em(TaskCheckboxTextGapRatio);

    public double TaskCheckboxAfterNumber => Em(TaskCheckboxAfterNumberRatio);

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

    public double TableFontSize => FontSize;

    public double TableLineHeight => TableFontSize * TableLineHeightRatio;

    /// <summary>
    /// Поля ячейки — в целых пикселях экрана (<paramref name="layoutScale"/> —
    /// масштаб отрисовки): линии сетки остаются на пикселях, а дробное поле при
    /// округлении раскладки дало бы ячейке с картинкой лишний пиксель высоты.
    /// Половина пикселя — всегда вверх. При 14 и масштабе 1 — ровно 7 × 9.
    /// </summary>
    public Thickness GetTableCellPadding(double layoutScale)
        => new(
            RoundToDevicePixels(Em(TableCellHorizontalPaddingRatio), layoutScale),
            RoundToDevicePixels(Em(TableCellVerticalPaddingRatio), layoutScale));

    private static double RoundToDevicePixels(double value, double layoutScale)
        => Math.Round(value * layoutScale, MidpointRounding.AwayFromZero) / layoutScale;

    public double FrontMatterFontSize => Em(FrontMatterFontScale);

    /// <summary>Межстрочный ключа — как у текста документа, от кегля блока.</summary>
    public double FrontMatterKeyLineHeight => FrontMatterFontSize * LineHeightRatio;

    public double FrontMatterValueLineHeight => FrontMatterFontSize * FrontMatterValueLineHeightRatio;

    public double FrontMatterKeyColumnWidth => Em(FrontMatterKeyColumnRatio);

    public double FrontMatterRowMinHeight => Em(FrontMatterRowMinHeightRatio);

    /// <summary>От линии до строк — сверху и снизу поровну.</summary>
    public double FrontMatterPadding => FrontMatterFontSize * FrontMatterPaddingRatio;

    public double FrontMatterKeyGap => FrontMatterFontSize * FrontMatterKeyGapRatio;

    public double FootnoteFontSize => Em(FootnoteFontScale);

    public double FootnoteLineHeight => FootnoteFontSize * FootnoteLineHeightRatio;

    public double FootnoteRuleWidth => FootnoteFontSize * FootnoteRuleWidthRatio;

    /// <summary>От верха линии до первой сноски: линия лежит в этом просвете.</summary>
    public double FootnoteRuleGap => FootnoteFontSize * FootnoteRuleGapRatio;

    public double FootnoteIndent => FootnoteFontSize * FootnoteIndentRatio;

    /// <summary>Колонка номера без отступа до текста.</summary>
    public double FootnoteNumberColumnWidth => FootnoteFontSize * (FootnoteNumberColumnRatio - FootnoteNumberGapRatio);

    public double FootnoteNumberGap => FootnoteFontSize * FootnoteNumberGapRatio;

    /// <summary>Между сносками и между абзацами и списком внутри сноски.</summary>
    public double FootnoteGap => FootnoteFontSize * FootnoteGapRatio;

    /// <summary>Шаг между сносками (<see cref="GetTextGap"/>).</summary>
    public double FootnoteRowGap => GetTextGap(FootnoteGap, FootnoteLineHeight);

    /// <summary>
    /// Просвет между блоками одной сноски: абзацы и список — через .45em кегля
    /// сноски, код — через .6em текста, остальное — в обычном ритме.
    /// </summary>
    public double GapInsideFootnote(MarkdownBlock previous, MarkdownBlock next) => next switch
    {
        MarkdownParagraphBlock or MarkdownListBlock => Math.Max(GetTextGap(FootnoteGap, FootnoteLineHeight), GetSpacing(previous).Bottom),
        MarkdownCodeBlock => Math.Max(Em(FootnoteCodeGapRatio), GetSpacing(previous).Bottom),
        _ => GapBetween(previous, next)
    };

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
        MarkdownTableBlock => new(Em(TableGap), Em(TableGap)),
        MarkdownFrontMatterBlock => new(0, Em(FrontMatterGapRatio)),
        MarkdownFootnotesBlock => new(Em(FootnotesGapRatio), 0),
        _ => new(Em(ParagraphGap), 0)
    };

    /// <summary>
    /// Просвет между соседними блоками: больший из двух, а не сумма. Заголовок
    /// сразу под front matter теряет свой просвет — до него ровно нижний просвет
    /// свойств.
    /// </summary>
    public double GapBetween(MarkdownBlock previous, MarkdownBlock next)
        => previous is MarkdownFrontMatterBlock && next is MarkdownHeadingBlock { Level: 1 }
            ? GetSpacing(previous).Bottom
            : Math.Max(GetSpacing(previous).Bottom, GetSpacing(next).Top);
}

internal readonly record struct MarkdownBlockSpacing(double Top, double Bottom);
