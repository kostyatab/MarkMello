using MarkMello.Domain;
using MarkMello.Infrastructure.Highlighting;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Движок подсветки на TextMateSharp (ADR-0010 §2, §3): метки языков, разбор
/// скоупов в виды токенов, ленивая загрузка.
/// </summary>
public sealed class TextMateCodeHighlighterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void CSharp()
    {
        const string code = """
            // Reads the file.
            public async Task<int> CountAsync(string path)
            {
                var name = $"Hello {path}!";
                return name.Length + 42;
            }
            """;

        var tokens = Highlight("csharp", code);

        AssertToken(tokens, code, "// Reads the file.", MarkdownCodeTokenKind.Comment);
        AssertToken(tokens, code, "public async", MarkdownCodeTokenKind.Keyword);
        AssertToken(tokens, code, "Task", MarkdownCodeTokenKind.Type);
        AssertToken(tokens, code, "CountAsync", MarkdownCodeTokenKind.Function);
        AssertToken(tokens, code, "$\"Hello ", MarkdownCodeTokenKind.StringLiteral);
        AssertToken(tokens, code, "42", MarkdownCodeTokenKind.Constant);
        AssertPlain(tokens, code, "{path}");
        AssertPlain(tokens, code, "name");
        AssertToken(tokens, code, "Length", MarkdownCodeTokenKind.Member);
    }

    [Fact]
    public void MembersAfterADotAndInDeclarations()
    {
        const string code = """
            var total = invoice.TotalAmount ?? null;
            class A { private int _count; public int Count { get; set; } }
            """;

        var tokens = Highlight("cs", code);

        AssertToken(tokens, code, "TotalAmount", MarkdownCodeTokenKind.Member);
        AssertToken(tokens, code, "_count", MarkdownCodeTokenKind.Member);
        AssertToken(tokens, code, "Count {", MarkdownCodeTokenKind.Member, length: "Count".Length);
        AssertToken(tokens, code, "null", MarkdownCodeTokenKind.Keyword);
        AssertPlain(tokens, code, "invoice");
    }

    [Fact]
    public void TypeScriptTemplateSubstitutionIsNotAString()
    {
        const string code = "const raw = await readFile(`themes/${name}.json`, \"utf8\") as Theme;";

        var tokens = Highlight("ts", code);

        AssertToken(tokens, code, "const", MarkdownCodeTokenKind.Keyword);
        AssertToken(tokens, code, "readFile", MarkdownCodeTokenKind.Function);
        AssertToken(tokens, code, "`themes/", MarkdownCodeTokenKind.StringLiteral);
        AssertToken(tokens, code, ".json`", MarkdownCodeTokenKind.StringLiteral);
        AssertToken(tokens, code, "as", MarkdownCodeTokenKind.Keyword);
        AssertToken(tokens, code, "Theme", MarkdownCodeTokenKind.Type);
        AssertPlain(tokens, code, "${name}");
        AssertPlain(tokens, code, "=");
    }

    [Fact]
    public void Bash()
    {
        const string code = """
            # Build before a release
            if [ -n "$CI" ]; then
              echo "On CI: $REF" && exit 0
            fi
            """;

        var tokens = Highlight("sh", code);

        AssertToken(tokens, code, "# Build before a release", MarkdownCodeTokenKind.Comment);
        AssertToken(tokens, code, "if", MarkdownCodeTokenKind.Keyword);
        AssertToken(tokens, code, "echo", MarkdownCodeTokenKind.Function);
        AssertToken(tokens, code, "\"On CI: ", MarkdownCodeTokenKind.StringLiteral);
        AssertPlain(tokens, code, "$CI");
        AssertPlain(tokens, code, "$REF");
    }

    [Fact]
    public void Python()
    {
        const string code = """
            class Point(Base):
                def norm(self, k: int = 2) -> float:
                    return f"{self.x}" if k else None  # done
            """;

        var tokens = Highlight("py", code);

        AssertToken(tokens, code, "class", MarkdownCodeTokenKind.Keyword);
        AssertToken(tokens, code, "Point", MarkdownCodeTokenKind.Type);
        AssertToken(tokens, code, "norm", MarkdownCodeTokenKind.Function);
        AssertToken(tokens, code, "None", MarkdownCodeTokenKind.Keyword);
        AssertToken(tokens, code, "x", MarkdownCodeTokenKind.Member);
        AssertToken(tokens, code, "# done", MarkdownCodeTokenKind.Comment);
        AssertToken(tokens, code, "f\"", MarkdownCodeTokenKind.StringLiteral);
    }

    [Fact]
    public void JsonKeysAreTypesAndValuesAreStringsOrConstants()
    {
        const string code = """
            { "theme": "dark", "lineHeight": 1.6, "trace": false }
            """;

        var tokens = Highlight("json", code);

        AssertToken(tokens, code, "\"theme\"", MarkdownCodeTokenKind.Type);
        AssertToken(tokens, code, "\"dark\"", MarkdownCodeTokenKind.StringLiteral);
        AssertToken(tokens, code, "1.6", MarkdownCodeTokenKind.Constant);
        AssertToken(tokens, code, "false", MarkdownCodeTokenKind.Keyword);
    }

    [Fact]
    public void Yaml()
    {
        const string code = """
            jobs:
              test:
                timeout-minutes: 20   # minutes
                name: "tests"
            """;

        var tokens = Highlight("yml", code);

        AssertToken(tokens, code, "jobs", MarkdownCodeTokenKind.Type);
        AssertToken(tokens, code, "timeout-minutes", MarkdownCodeTokenKind.Type);
        AssertToken(tokens, code, "20", MarkdownCodeTokenKind.Constant);
        AssertToken(tokens, code, "# minutes", MarkdownCodeTokenKind.Comment);
        AssertToken(tokens, code, "\"tests\"", MarkdownCodeTokenKind.StringLiteral);
    }

    [Fact]
    public void CrlfLinesHighlightLikeLf()
    {
        const string code = "jobs:\r\n  timeout-minutes: 20   # minutes\r\n  name: \"tests\"\r\n";

        var tokens = Highlight("yml", code);

        AssertToken(tokens, code, "20", MarkdownCodeTokenKind.Constant);
        AssertToken(tokens, code, "# minutes", MarkdownCodeTokenKind.Comment);
        AssertToken(tokens, code, "\"tests\"", MarkdownCodeTokenKind.StringLiteral);
        Assert.DoesNotContain(tokens, token => code.AsSpan(token.Start, token.End - token.Start).Contains('\r'));
    }

    [Fact]
    public void DiffLinesAreInsertedAndDeleted()
    {
        const string code = """
             ## Reading
            -| Line height | 1.7 |
            +| Line height | 1.6 |
            """;

        var tokens = Highlight("diff", code);

        AssertToken(tokens, code, "-| Line height | 1.7 |", MarkdownCodeTokenKind.Deleted);
        AssertToken(tokens, code, "+| Line height | 1.6 |", MarkdownCodeTokenKind.Inserted);
        AssertPlain(tokens, code, " ## Reading");
    }

    [Theory]
    [InlineData("dotnet --version", "dotnet")]
    [InlineData("git clone repo && cd repo", "git")]
    [InlineData("npm test | grep fail", "grep")]
    [InlineData("FOO=1 make build", "make")]
    [InlineData("$ npm install", "npm")]
    [InlineData("sudo apt install jq", "apt")]
    [InlineData("./build.sh --release", "./build.sh")]
    [InlineData("VERSION=$(git describe --tags)", "git")]
    [InlineData("if true; then make; fi", "make")]
    [InlineData("echo \"built $(date)\"", "date")]
    [InlineData("echo `whoami`", "whoami")]
    public void ShellCommandNamesAreFunctions(string code, string command)
    {
        var tokens = Highlight("bash", code);

        AssertToken(tokens, code, command, MarkdownCodeTokenKind.Function);
    }

    [Theory]
    [InlineData("dotnet --version", "--version")]
    [InlineData("FOO=1 make build", "FOO=1")]
    [InlineData("FOO=1 make build", "build")]
    [InlineData("docker run \\\n  --rm image", "image")]
    [InlineData("echo \"a; b\"", "b")]
    [InlineData("VERSION=$(git describe --tags)", "describe")]
    public void ShellArgumentsStayPlain(string code, string text)
    {
        var tokens = Highlight("bash", code);

        Assert.DoesNotContain(tokens, token =>
            token.Kind == MarkdownCodeTokenKind.Function
            && code.Substring(token.Start, token.Length) == text);
    }

    [Fact]
    public void ShellCommentsAndStringsAreNotCommands()
    {
        const string code = "# run npm here\necho \"git push\"";

        var tokens = Highlight("sh", code);

        AssertToken(tokens, code, "# run npm here", MarkdownCodeTokenKind.Comment);
        AssertToken(tokens, code, "\"git push\"", MarkdownCodeTokenKind.StringLiteral);
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("cs")]
    [InlineData("c#")]
    [InlineData("bash")]
    [InlineData("shell")]
    [InlineData("zsh")]
    [InlineData("yaml")]
    [InlineData("js")]
    [InlineData("javascript")]
    [InlineData("typescript")]
    [InlineData("python")]
    [InlineData("ps1")]
    [InlineData("powershell")]
    [InlineData("sql")]
    [InlineData("xml")]
    [InlineData("html")]
    [InlineData("dockerfile")]
    [InlineData("jsonc")]
    [InlineData("go")]
    [InlineData("rust")]
    public void KnownLabelsAreHighlighted(string label)
    {
        Assert.NotNull(new TextMateCodeHighlighter().Highlight(label, "x = 1 # a", Timeout, CancellationToken.None));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("text")]
    [InlineData("mermaid")]
    public void UnknownLabelIsNotHighlightedAndDoesNotStartTheEngine(string label)
    {
        var highlighter = new TextMateCodeHighlighter();

        Assert.Null(highlighter.Highlight(label, "code", Timeout, CancellationToken.None));
        Assert.False(highlighter.IsEngineCreated);
    }

    /// <summary>
    /// Грамматика bash подключает в heredoc markdown, html, ruby и python, а
    /// markdown — ещё десятки языков: загрузка всей цепочки стоила 56 МБ.
    /// Реестр отдаёт только грамматики пакета самого языка блока.
    /// </summary>
    [Fact]
    public void OnlyGrammarsOfTheBlockLanguagePackageAreLoaded()
    {
        using var catalog = TextMateGrammarCatalog.LoadEmbedded();
        var options = new TextMateRegistryOptions(catalog) { Package = catalog.FindPackage("source.shell") };

        Assert.NotNull(options.GetGrammar("source.shell"));
        Assert.Null(options.GetGrammar("text.html.markdown"));
        Assert.Null(options.GetGrammar("source.ruby"));
    }

    [Fact]
    public void BashHeredocWithAnotherLanguageIsStillHighlighted()
    {
        const string code = """
            cat <<EOF > script.rb
            puts "hi"
            EOF
            echo done
            """;

        var tokens = Highlight("bash", code);

        AssertToken(tokens, code, "echo", MarkdownCodeTokenKind.Function);
    }

    [Fact]
    public void TokensAreOrderedAndDoNotOverlap()
    {
        const string code = "public static void Main() { var a = \"b\"; /* c */ return; }";

        var tokens = Highlight("cs", code);

        Assert.NotEmpty(tokens);
        for (var index = 1; index < tokens.Count; index++)
        {
            Assert.True(tokens[index - 1].End <= tokens[index].Start);
        }

        Assert.All(tokens, token => Assert.True(token.Length > 0 && token.End <= code.Length));
    }

    [Fact]
    public void ExhaustedBudgetIsReportedAsTimeout()
    {
        Assert.Throws<TimeoutException>(() =>
            new TextMateCodeHighlighter().Highlight("cs", "var a = 1;", TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public void CancellationIsReported()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new TextMateCodeHighlighter().Highlight("cs", "var a = 1;", Timeout, cancellation.Token));
    }

    private static IReadOnlyList<MarkdownCodeToken> Highlight(string label, string code)
    {
        var tokens = new TextMateCodeHighlighter().Highlight(label, code, Timeout, CancellationToken.None);
        Assert.NotNull(tokens);
        return tokens;
    }

    /// <summary>Отрезок <paramref name="text"/> целиком покрыт токеном вида <paramref name="kind"/>.</summary>
    private static void AssertToken(
        IReadOnlyList<MarkdownCodeToken> tokens,
        string code,
        string text,
        MarkdownCodeTokenKind kind,
        int? length = null)
    {
        var start = code.IndexOf(text, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{text}' not found");
        var end = start + (length ?? text.Length);
        var covering = tokens.Where(token => token.Start <= start && token.End >= end).ToList();
        Assert.True(covering.Count == 1, $"'{text}' is not covered by one token: {Describe(tokens, code)}");
        Assert.Equal(kind, covering[0].Kind);
    }

    private static void AssertPlain(IReadOnlyList<MarkdownCodeToken> tokens, string code, string text)
    {
        var start = code.IndexOf(text, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{text}' not found");
        var end = start + text.Length;
        Assert.DoesNotContain(tokens, token => token.Start < end && token.End > start);
    }

    private static string Describe(IReadOnlyList<MarkdownCodeToken> tokens, string code)
        => string.Join(", ", tokens.Select(token => $"{token.Kind}:'{code.Substring(token.Start, token.Length)}'"));
}
