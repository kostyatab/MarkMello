using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownHeadingAnchorSluggerTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownHeadingAnchorSluggerTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public void CreateAnchorKeepsCyrillicLettersAndUsesHyphensForSpaces()
    {
        var anchor = MarkdownHeadingAnchorSlugger.CreateAnchor("Зачем нужна эта документация");

        Assert.Equal("зачем-нужна-эта-документация", anchor);
    }

    [Fact]
    public void TryNormalizeFragmentDecodesPercentEncodedAnchor()
    {
        var encoded = "#%D0%97%D0%B0%D1%87%D0%B5%D0%BC-%D0%BD%D1%83%D0%B6%D0%BD%D0%B0";

        var ok = MarkdownHeadingAnchorSlugger.TryNormalizeFragment(encoded, out var anchor);

        Assert.True(ok);
        Assert.Equal("зачем-нужна", anchor);
    }

    [Fact]
    public Task DocumentViewRegistersDuplicateHeadingAnchorsWithStableNumericSuffix()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = new MarkdownDocumentView
            {
                Document = new RenderedMarkdownDocument(
                [
                    new MarkdownHeadingBlock(2, [new MarkdownTextInline("Раздел")]),
                    new MarkdownHeadingBlock(2, [new MarkdownTextInline("Раздел")])
                ]),
                ReadingPreferences = ReadingPreferences.Default
            };

            Assert.True(view.HasHeadingAnchor("#раздел"));
            Assert.True(view.HasHeadingAnchor("#раздел-1"));
        }, CancellationToken.None);
    }
}
