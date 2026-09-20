using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Состав расширений Markdig. Регрессия MM-22: пайплайн собирался через
/// <c>UseAdvancedExtensions()</c>, а конвертер в доменную модель понимал не все
/// 19 расширений. Разметка остальных теряла текст — <c>{x}</c>, <c>$x$</c>,
/// сокращения и маркеры контейнеров пропадали из документа. Теперь список явный,
/// а разметка расширений вне списка остаётся исходным текстом, как на GitHub.
/// </summary>
public sealed class MarkdownExtensionPipelineTests
{
    /// <summary>
    /// Однострочные случаи: читатель видит ровно то, что написал автор.
    /// </summary>
    [Theory]
    // GenericAttributes: `{x}` в обычной прозе — частый случай, текст исчезал целиком.
    [InlineData("Set {x} to y.")]
    // Mathematics: `MathInline` — LeafInline без ветки, выпадал молча.
    [InlineData("Area is $x^2$ here.")]
    // Abbreviations: определение и само слово пропадали.
    [InlineData("*[HTML]: Hyper Text Markup Language")]
    // EmphasisExtras сверх зачёркивания: `==` и `++` становились жирным.
    [InlineData("Write ==m== and ++i++ here.")]
    // Citations и CustomContainers: `""cite""` и `::m::` становились жирным.
    [InlineData("Read \"\"cite\"\" and ::m:: here.")]
    // EmphasisExtras, Superscript: `^u^` становился курсивом.
    [InlineData("Use ^u^ here.")]
    // Footers: маркер `^^` съедал парсер.
    [InlineData("^^ text")]
    // Тильда в прозе не открывает зачёркивание — ни как число, ни как путь.
    [InlineData("от ~5 до ~10 минут")]
    [InlineData("путь ~/Documents тут")]
    public void MarkupOfExtensionsOutsideThePipelineStaysPlainText(string markdown)
    {
        Assert.Equal(markdown, RenderText(markdown));
    }

    /// <summary>
    /// Блочные маркеры: Markdig снимал их при разборе ещё до конвертера, и
    /// восстанавливать было нечего. Без расширений строки остаются текстом.
    /// </summary>
    [Theory]
    // Mathematics: `$$…$$` становился блоком кода без языка, строки `$$` съедены.
    [InlineData("$$\nx^2 + y\n$$", "$$ x^2 + y $$")]
    // CustomContainers: строки с `:::` пропадали, содержимое шло абзацами.
    [InlineData("::: note\ncontent\n:::", "::: note content :::")]
    public void BlockMarkersOfExtensionsOutsideThePipelineStayPlainText(string markdown, string expectedText)
    {
        Assert.Equal(expectedText, RenderText(markdown));
    }

    /// <summary>
    /// Figures: маркеры `^^^` пропадали, картинка оставалась, а подпись уходила
    /// обычным абзацем. Теперь весь блок — один абзац с маркерами и картинкой.
    /// </summary>
    [Fact]
    public void FigureMarkersStayPlainTextAroundTheImage()
    {
        var inlines = RenderParagraphInlines("^^^\n![img](a.png)\n^^^ caption");

        Assert.Equal(5, inlines.Count);
        Assert.Equal("^^^", Assert.IsType<MarkdownTextInline>(inlines[0]).Text);
        Assert.Equal("a.png", Assert.IsType<MarkdownImageInline>(inlines[2]).Url);
        Assert.Equal("^^^ caption", Assert.IsType<MarkdownTextInline>(inlines[4]).Text);
    }

    /// <summary>
    /// Abbreviations: слово из определения выпадало из абзаца — «Write  here.».
    /// </summary>
    [Fact]
    public void AnAbbreviatedWordStaysInTheParagraph()
    {
        var text = RenderText("*[HTML]: Hyper Text Markup Language\n\nWrite HTML here.");

        Assert.Equal("*[HTML]: Hyper Text Markup Language\n\nWrite HTML here.", text);
    }

    /// <summary>
    /// GenericAttributes: атрибут после разметки и в заголовке пропадал без следа.
    /// </summary>
    [Fact]
    public void AttributeBracesAfterStrongStayPlainText()
    {
        var inlines = RenderParagraphInlines("**bold**{.red}");

        Assert.Equal(2, inlines.Count);
        Assert.IsType<MarkdownStrongInline>(inlines[0]);
        Assert.Equal("{.red}", Assert.IsType<MarkdownTextInline>(inlines[1]).Text);
    }

    [Fact]
    public void AttributeBracesInAHeadingStayPlainText()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render("# Heading {#id}");

        var heading = Assert.IsType<MarkdownHeadingBlock>(Assert.Single(document.Blocks));
        Assert.Equal("Heading {#id}", Assert.IsType<MarkdownTextInline>(Assert.Single(heading.Inlines)).Text);
    }

    /// <summary>
    /// AutoIdentifiers: `[Intro]` подхватывал якорь заголовка и становился ссылкой
    /// с пустым URL. Якоря заголовков даёт свой <c>MarkdownHeadingAnchorSlugger</c>,
    /// расширение в пайплайне не нужно.
    /// </summary>
    [Fact]
    public void AShortcutReferenceToAHeadingStaysPlainTextInsteadOfAnEmptyLink()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render("# Intro\n\nSee [Intro].");

        var paragraph = Assert.IsType<MarkdownParagraphBlock>(document.Blocks[1]);
        Assert.DoesNotContain(paragraph.Inlines, inline => inline is MarkdownLinkInline);
        Assert.Equal("See [Intro].", PlainText(paragraph.Inlines));
    }

    /// <summary>
    /// Одиночная тильда зачёркивает, как на GitHub: <c>Subscript</c> включён ради
    /// её разбора, а ветка эмфазиса отдаёт зачёркивание при любом числе тильд.
    /// </summary>
    [Theory]
    [InlineData("~s~")]
    [InlineData("~~s~~")]
    public void TildeStrikesThroughWithAnyNumberOfDelimiters(string markdown)
    {
        var strikethrough = Assert.IsType<MarkdownStrikethroughInline>(Assert.Single(RenderParagraphInlines(markdown)));

        Assert.Equal("s", Assert.IsType<MarkdownTextInline>(Assert.Single(strikethrough.Inlines)).Text);
    }

    /// <summary>
    /// Grid-таблицы остались в явном списке расширений.
    /// </summary>
    [Fact]
    public void GridTablesStillBecomeATable()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(
            """
            +---+---+
            | a | b |
            +===+===+
            | 1 | 2 |
            +---+---+
            """);

        var table = Assert.IsType<MarkdownTableBlock>(Assert.Single(document.Blocks));
        Assert.Equal(["a", "b"], table.Header.Select(cell => PlainText(cell.Inlines)));
        Assert.Equal(["1", "2"], Assert.Single(table.Rows).Select(cell => PlainText(cell.Inlines)));
    }

    /// <summary>
    /// Definition list остался в явном списке: без расширения термин и определение
    /// слиплись бы в один абзац. Своего оформления у него нет — два абзаца, см. MM-18.
    /// </summary>
    [Fact]
    public void ADefinitionListStillBecomesTwoParagraphs()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render("Term\n\n:   Definition");

        Assert.Equal(2, document.Blocks.Count);
        Assert.Equal("Term", PlainText(Assert.IsType<MarkdownParagraphBlock>(document.Blocks[0]).Inlines));
        Assert.Equal("Definition", PlainText(Assert.IsType<MarkdownParagraphBlock>(document.Blocks[1]).Inlines));
    }

    private static string RenderText(string markdown)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        return MarkdownDocumentTextMap.Create(document).Text.TrimEnd('\n');
    }

    private static IReadOnlyList<MarkdownInline> RenderParagraphInlines(string markdown)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        return Assert.IsType<MarkdownParagraphBlock>(Assert.Single(document.Blocks)).Inlines;
    }

    private static string PlainText(IReadOnlyList<MarkdownInline> inlines)
        => string.Concat(inlines.Select(inline => inline switch
        {
            MarkdownTextInline text => text.Text,
            MarkdownStrongInline strong => PlainText(strong.Inlines),
            MarkdownEmphasisInline emphasis => PlainText(emphasis.Inlines),
            _ => string.Empty,
        }));
}
