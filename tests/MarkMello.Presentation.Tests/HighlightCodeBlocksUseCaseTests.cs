using MarkMello.Application.Abstractions;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Докраска блоков кода (ADR-0010 §4, §5) с фейковым движком: обход вложенных
/// блоков, кэш, пороги, отмена и ошибки движка.
/// </summary>
public sealed class HighlightCodeBlocksUseCaseTests
{
    [Fact]
    public void ExecuteHighlightsCodeBlocksAtEveryNestingLevel()
    {
        const string markdown = """
            ```cs
            top
            ```

            > ```cs
            > quote
            > ```

            - item

              ```cs
              list
              ```

            Term
            :   ```cs
                definition
                ```

            Text[^1]

            [^1]: Note

                ```cs
                footnote
                ```
            """;
        var highlighter = new FakeHighlighter();
        var useCase = new HighlightCodeBlocksUseCase(highlighter);

        var document = useCase.Execute(Render(markdown), CancellationToken.None);

        var codes = CollectCodeBlocks(document.Blocks);
        Assert.Equal(["top", "quote", "list", "definition", "footnote"], codes.Select(code => code.Code));
        Assert.All(codes, code => Assert.Equal(MarkdownCodeTokenKind.Keyword, Assert.Single(code.Tokens!).Kind));
        Assert.Equal(5, highlighter.Calls.Count);
    }

    [Fact]
    public void LanguageIsTheFirstWordOfTheLabelInLowerCase()
    {
        var highlighter = new FakeHighlighter();
        var useCase = new HighlightCodeBlocksUseCase(highlighter);

        useCase.Execute(Render("```CSharp title=\"x.cs\"\nvar x = 1;\n```"), CancellationToken.None);

        Assert.Equal("csharp", Assert.Single(highlighter.Calls).Language);
    }

    [Fact]
    public void UnchangedBranchesKeepTheirInstances()
    {
        var useCase = new HighlightCodeBlocksUseCase(new FakeHighlighter());
        var source = Render("# Title\n\n> quote\n\n```cs\ncode\n```");

        var document = useCase.Execute(source, CancellationToken.None);

        Assert.Same(source.Blocks[0], document.Blocks[0]);
        Assert.Same(source.Blocks[1], document.Blocks[1]);
        Assert.NotSame(source.Blocks[2], document.Blocks[2]);
    }

    [Fact]
    public void DocumentWithoutLabelledCodeIsReturnedAsIsWithoutCallingTheEngine()
    {
        var highlighter = new FakeHighlighter();
        var useCase = new HighlightCodeBlocksUseCase(highlighter);
        var source = Render("# Title\n\n```\nno label\n```\n\n    indented");

        Assert.False(useCase.NeedsHighlighting(source));
        Assert.Same(source, useCase.Execute(source, CancellationToken.None));
        Assert.Empty(highlighter.Calls);
    }

    [Fact]
    public void BlocksOverTheThresholdsAreNotHighlighted()
    {
        var highlighter = new FakeHighlighter();
        var useCase = new HighlightCodeBlocksUseCase(highlighter);
        var tooLong = new MarkdownCodeBlock("cs", new string('x', HighlightCodeBlocksUseCase.MaxCodeLength + 1));
        var tooManyLines = new MarkdownCodeBlock("cs", string.Join('\n', Enumerable.Repeat("x", HighlightCodeBlocksUseCase.MaxLineCount + 1)));
        var source = new RenderedMarkdownDocument([tooLong, tooManyLines]);

        Assert.False(useCase.NeedsHighlighting(source));
        Assert.Same(source, useCase.Execute(source, CancellationToken.None));
        Assert.Empty(highlighter.Calls);
    }

    [Fact]
    public void UnknownLanguageLeavesTheBlockPlainAndIsAskedOnce()
    {
        var highlighter = new FakeHighlighter { KnownLanguages = [] };
        var useCase = new HighlightCodeBlocksUseCase(highlighter);
        var source = Render("```nope\ncode\n```");

        Assert.True(useCase.NeedsHighlighting(source));
        var first = useCase.Execute(source, CancellationToken.None);
        var second = useCase.Execute(Render("```nope\ncode\n```"), CancellationToken.None);

        Assert.Same(source, first);
        Assert.Null(Assert.IsType<MarkdownCodeBlock>(Assert.Single(second.Blocks)).Tokens);
        Assert.Single(highlighter.Calls);
        Assert.False(useCase.NeedsHighlighting(source));
    }

    [Fact]
    public void RepeatedTextIsTakenFromTheCache()
    {
        var highlighter = new FakeHighlighter();
        var useCase = new HighlightCodeBlocksUseCase(highlighter);

        useCase.Execute(Render("```cs\ncode\n```"), CancellationToken.None);
        var reparsed = Render("```cs\ncode\n```\n\n```cs\nother\n```");

        var cachedOnly = useCase.ApplyCached(reparsed);
        Assert.NotNull(Assert.IsType<MarkdownCodeBlock>(cachedOnly.Blocks[0]).Tokens);
        Assert.Null(Assert.IsType<MarkdownCodeBlock>(cachedOnly.Blocks[1]).Tokens);
        Assert.Single(highlighter.Calls);

        useCase.Execute(reparsed, CancellationToken.None);
        Assert.Equal(["code", "other"], highlighter.Calls.Select(call => call.Code));
    }

    [Fact]
    public void SameTextInAnotherLanguageIsHighlightedAgain()
    {
        var highlighter = new FakeHighlighter();
        var useCase = new HighlightCodeBlocksUseCase(highlighter);

        useCase.Execute(Render("```cs\ncode\n```\n\n```ts\ncode\n```"), CancellationToken.None);

        Assert.Equal(["cs", "ts"], highlighter.Calls.Select(call => call.Language));
    }

    [Fact]
    public void CacheEvictsTheOldestEntries()
    {
        var highlighter = new FakeHighlighter();
        var useCase = new HighlightCodeBlocksUseCase(highlighter);
        var blocks = Enumerable.Range(0, HighlightCodeBlocksUseCase.CacheCapacity + 1)
            .Select(index => (MarkdownBlock)new MarkdownCodeBlock("cs", $"code {index}"))
            .ToList();

        useCase.Execute(new RenderedMarkdownDocument(blocks), CancellationToken.None);
        var first = new RenderedMarkdownDocument([new MarkdownCodeBlock("cs", "code 0")]);
        var last = new RenderedMarkdownDocument([new MarkdownCodeBlock("cs", $"code {HighlightCodeBlocksUseCase.CacheCapacity}")]);

        Assert.True(useCase.NeedsHighlighting(first));
        Assert.False(useCase.NeedsHighlighting(last));
    }

    [Fact]
    public void CancellationStopsTheWalkAndCachesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        var highlighter = new FakeHighlighter { OnHighlight = cancellation.Cancel };
        var useCase = new HighlightCodeBlocksUseCase(highlighter);
        var source = Render("```cs\nfirst\n```\n\n```cs\nsecond\n```");

        Assert.ThrowsAny<OperationCanceledException>(() => useCase.Execute(source, cancellation.Token));

        Assert.Equal(["first"], highlighter.Calls.Select(call => call.Code));
        Assert.True(useCase.NeedsHighlighting(source));
    }

    [Fact]
    public void EngineFailureLeavesTheBlockPlain()
    {
        var useCase = new HighlightCodeBlocksUseCase(new FakeHighlighter { Throw = true });
        var source = Render("```cs\ncode\n```");

        var document = useCase.Execute(source, CancellationToken.None);

        Assert.Same(source, document);
        Assert.False(useCase.NeedsHighlighting(source));
    }

    [Fact]
    public void TimeoutLeavesTheBlockPlainButIsNotRemembered()
    {
        var highlighter = new FakeHighlighter { ThrowTimeout = true };
        var useCase = new HighlightCodeBlocksUseCase(highlighter);
        var source = Render("```cs\ncode\n```");

        Assert.Same(source, useCase.Execute(source, CancellationToken.None));
        Assert.True(useCase.NeedsHighlighting(source));

        useCase.Execute(source, CancellationToken.None);
        Assert.Equal(2, highlighter.Calls.Count);
    }

    [Fact]
    public void CacheIsBoundedByTheAmountOfCode()
    {
        var useCase = new HighlightCodeBlocksUseCase(new FakeHighlighter());
        var blockLength = HighlightCodeBlocksUseCase.MaxCodeLength;
        var fitting = HighlightCodeBlocksUseCase.CacheCharacterBudget / blockLength;
        var blocks = Enumerable.Range(0, fitting + 1)
            .Select(index => (MarkdownBlock)new MarkdownCodeBlock("cs", index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + new string('x', blockLength - 5)))
            .ToList();

        useCase.Execute(new RenderedMarkdownDocument(blocks), CancellationToken.None);

        Assert.True(fitting + 1 < HighlightCodeBlocksUseCase.CacheCapacity);
        Assert.True(useCase.NeedsHighlighting(new RenderedMarkdownDocument([blocks[0]])));
        Assert.False(useCase.NeedsHighlighting(new RenderedMarkdownDocument([blocks[^1]])));
    }

    [Fact]
    public void AlreadyHighlightedBlocksAreSkipped()
    {
        var highlighter = new FakeHighlighter();
        var useCase = new HighlightCodeBlocksUseCase(highlighter);
        var highlighted = useCase.Execute(Render("```cs\ncode\n```"), CancellationToken.None);

        Assert.False(useCase.NeedsHighlighting(highlighted));
        Assert.Same(highlighted, useCase.Execute(highlighted, CancellationToken.None));
        Assert.Same(highlighted, useCase.ApplyCached(highlighted));
        Assert.Single(highlighter.Calls);
    }

    private static RenderedMarkdownDocument Render(string markdown)
        => new MarkdigMarkdownDocumentRenderer().Render(markdown);

    private static List<MarkdownCodeBlock> CollectCodeBlocks(IReadOnlyList<MarkdownBlock> blocks)
    {
        var result = new List<MarkdownCodeBlock>();
        foreach (var block in blocks)
        {
            switch (block)
            {
                case MarkdownCodeBlock code:
                    result.Add(code);
                    break;
                case MarkdownQuoteBlock quote:
                    result.AddRange(CollectCodeBlocks(quote.Blocks));
                    break;
                case MarkdownListBlock list:
                    result.AddRange(list.Items.SelectMany(item => CollectCodeBlocks(item.Blocks)));
                    break;
                case MarkdownFootnotesBlock footnotes:
                    result.AddRange(footnotes.Footnotes.SelectMany(note => CollectCodeBlocks(note.Blocks)));
                    break;
                case MarkdownDefinitionListBlock definitionList:
                    result.AddRange(definitionList.Items
                        .SelectMany(item => item.Definitions)
                        .SelectMany(definition => CollectCodeBlocks(definition.Blocks)));
                    break;
            }
        }

        return result;
    }

    private sealed class FakeHighlighter : ICodeHighlighter
    {
        public List<(string Language, string Code)> Calls { get; } = [];

        public HashSet<string>? KnownLanguages { get; init; }

        public bool Throw { get; init; }

        public bool ThrowTimeout { get; init; }

        public Action? OnHighlight { get; init; }

        public IReadOnlyList<MarkdownCodeToken>? Highlight(
            string language,
            string code,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls.Add((language, code));
            OnHighlight?.Invoke();
            if (Throw)
            {
                throw new InvalidOperationException("engine failure");
            }

            if (ThrowTimeout)
            {
                throw new TimeoutException();
            }

            return KnownLanguages is null || KnownLanguages.Contains(language)
                ? [new MarkdownCodeToken(0, code.Length, MarkdownCodeTokenKind.Keyword)]
                : null;
        }
    }
}
