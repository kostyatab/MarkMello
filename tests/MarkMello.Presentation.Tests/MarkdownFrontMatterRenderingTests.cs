using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// YAML front matter в начале файла. Регрессия: расширения
/// <c>UseYamlFrontMatter</c> в пайплайне не было, и метаданные показывались
/// горизонтальной линией плюс заголовком H2 из-за setext-разбора.
/// </summary>
public sealed class MarkdownFrontMatterRenderingTests
{
    [Fact]
    public void FlatFrontMatterBecomesATableWithoutAHeaderRow()
    {
        var document = Render("""
            ---
            id: MM-46
            title: YAML front matter
            type: bug
            ---

            # Дальше документ
            """);

        Assert.Equal(2, document.Blocks.Count);

        var table = Assert.IsType<MarkdownTableBlock>(document.Blocks[0]);
        Assert.Empty(table.Header);
        Assert.Equal(
            [("id", "MM-46"), ("title", "YAML front matter"), ("type", "bug")],
            table.Rows.Select(ReadPair));

        var heading = Assert.IsType<MarkdownHeadingBlock>(document.Blocks[1]);
        Assert.Equal(1, heading.Level);
    }

    [Fact]
    public void TheKeyColumnIsBoldAndTheValueIsPlainText()
    {
        var table = RenderTable("""
            ---
            id: MM-46
            ---
            """);

        var row = Assert.Single(table.Rows);
        var strong = Assert.IsType<MarkdownStrongInline>(Assert.Single(row[0].Inlines));
        Assert.Equal("id", Assert.IsType<MarkdownTextInline>(Assert.Single(strong.Inlines)).Text);
        Assert.IsType<MarkdownTextInline>(Assert.Single(row[1].Inlines));
    }

    [Fact]
    public void AValueIsTakenAsIsWithoutYamlSemantics()
    {
        var table = RenderTable("""
            ---
            tags: [a, b]
            title: "Ссылки: внешние"
            empty:
            ---
            """);

        Assert.Equal(
            [("tags", "[a, b]"), ("title", "\"Ссылки: внешние\""), ("empty", "")],
            table.Rows.Select(ReadPair));
    }

    [Theory]
    // Вложенность.
    [InlineData("""
        ---
        build:
          runtime: net10
        ---
        """)]
    // Список блоком.
    [InlineData("""
        ---
        tags:
        - a
        - b
        ---
        """)]
    // Многострочное значение.
    [InlineData("""
        ---
        description: |
          Первая строка
          Вторая строка
        ---
        """)]
    // Строка, которая вообще не пара.
    [InlineData("""
        ---
        просто строка
        ---
        """)]
    // Строка из дефисов: закрывающим забором Markdig её не считает (нужно ровно
    // три), значит это содержимое, и потерять его нельзя.
    [InlineData("""
        ---
        ------
        title: Doc
        ---
        """)]
    // Комментарий YAML.
    [InlineData("""
        ---
        # комментарий
        title: Doc
        ---
        """)]
    // Двоеточие без пробела: по YAML это одна строка-скаляр, а не пара.
    [InlineData("""
        ---
        title:Doc
        ---
        """)]
    public void FrontMatterThatIsNotFlatPairsBecomesACodeBlockWithTheWholeText(string markdown)
    {
        var document = Render(markdown);

        var code = Assert.IsType<MarkdownCodeBlock>(Assert.Single(document.Blocks));
        Assert.Null(code.Info);

        var expected = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim('\n')
            .Split('\n')[1..^1];
        Assert.Equal(string.Join('\n', expected), code.Code);
    }

    /// <summary>
    /// Front matter без единой строки Markdig за front matter не считает — обе
    /// строки остаются горизонтальными линиями. Нам важно, что пустой таблицы
    /// и пустого блока кода на этом месте нет.
    /// </summary>
    [Fact]
    public void EmptyFrontMatterProducesNeitherATableNorAnEmptyBlock()
    {
        var document = Render("""
            ---
            ---

            Текст.
            """);

        Assert.DoesNotContain(document.Blocks, static block => block is MarkdownTableBlock or MarkdownCodeBlock);
        Assert.IsType<MarkdownParagraphBlock>(document.Blocks[^1]);
    }

    [Fact]
    public void ThreeDashesInTheMiddleOfADocumentStayAHorizontalRule()
    {
        var document = Render("""
            Текст до.

            ---

            key: value
            """);

        Assert.IsType<MarkdownParagraphBlock>(document.Blocks[0]);
        Assert.IsType<MarkdownHorizontalRuleBlock>(document.Blocks[1]);
        Assert.IsType<MarkdownParagraphBlock>(document.Blocks[2]);
        Assert.Equal(3, document.Blocks.Count);
    }

    /// <summary>
    /// Выделение, поиск и копирование идут по текстовой карте документа
    /// (ADR-0001): ключи и значения front matter должны быть в ней так же, как
    /// текст обычной таблицы.
    /// </summary>
    [Fact]
    public void FrontMatterTextIsPartOfTheDocumentTextMap()
    {
        var textMap = MarkdownDocumentTextMap.Create(Render("""
            ---
            id: MM-46
            type: bug
            ---

            Текст.
            """));

        Assert.Contains("id", textMap.Text, StringComparison.Ordinal);
        Assert.Contains("MM-46", textMap.Text, StringComparison.Ordinal);
        Assert.Contains("type", textMap.Text, StringComparison.Ordinal);
        Assert.Contains("bug", textMap.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Принятая цена расширения: горизонтальная линия в самой первой строке файла
    /// открывает front matter, если ниже есть вторая такая линия — Markdig ищет
    /// закрывающий забор по всему документу. Текст между ними плоскими парами не
    /// является, поэтому показывается блоком кода и не теряется. Так же ведёт себя
    /// preview VS Code.
    /// </summary>
    [Fact]
    public void AHorizontalRuleOnTheVeryFirstLineOpensFrontMatter()
    {
        var document = Render("""
            ---

            # Заголовок

            Текст

            ---

            Подвал
            """);

        var code = Assert.IsType<MarkdownCodeBlock>(document.Blocks[0]);
        Assert.Contains("# Заголовок", code.Code, StringComparison.Ordinal);
        Assert.Contains("Текст", code.Code, StringComparison.Ordinal);

        var paragraph = Assert.IsType<MarkdownParagraphBlock>(document.Blocks[^1]);
        Assert.Equal("Подвал", Assert.IsType<MarkdownTextInline>(Assert.Single(paragraph.Inlines)).Text);
    }

    private static (string Key, string Value) ReadPair(IReadOnlyList<MarkdownTableCell> row)
    {
        var strong = Assert.IsType<MarkdownStrongInline>(Assert.Single(row[0].Inlines));

        return (
            Assert.IsType<MarkdownTextInline>(Assert.Single(strong.Inlines)).Text,
            row[1].Inlines.Count == 0
                ? string.Empty
                : Assert.IsType<MarkdownTextInline>(Assert.Single(row[1].Inlines)).Text);
    }

    private static MarkdownTableBlock RenderTable(string markdown)
        => Assert.IsType<MarkdownTableBlock>(Assert.Single(Render(markdown).Blocks));

    private static RenderedMarkdownDocument Render(string markdown)
        => new MarkdigMarkdownDocumentRenderer().Render(markdown);
}
