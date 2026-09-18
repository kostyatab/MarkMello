using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using MarkMello.Domain;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// A click on a code block's copy button swaps the icon for a check mark and
/// renames the button for screen readers, then puts both back; the check mark
/// only ever confirms text that really reached the clipboard.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class CodeBlockCopyConfirmationTests
{
    private const string Code = "dotnet test";

    private readonly AvaloniaHeadlessFixture _fixture;

    public CodeBlockCopyConfirmationTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task ClickCopiesTheCodeAndConfirmsItUntilTheResetTimer()
    {
        return _fixture.RunAsync(async () =>
        {
            var view = CreateView();
            var window = new Window { Width = 600, Height = 400, Content = view };
            window.Show();
            window.UpdateLayout();

            var button = FindCopyButton(view);
            var idleName = AutomationProperties.GetName(button);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => button.Classes.Contains("copied"));

            Assert.Contains("copied", button.Classes);
            Assert.Equal(Code, await window.Clipboard!.TryGetTextAsync());
            var copiedName = AutomationProperties.GetName(button);
            Assert.False(string.IsNullOrWhiteSpace(copiedName));
            Assert.NotEqual(idleName, copiedName);

            await WaitUntilAsync(() => !button.Classes.Contains("copied"));

            Assert.DoesNotContain("copied", button.Classes);
            Assert.Equal(idleName, AutomationProperties.GetName(button));

            window.Close();
        });
    }

    [Fact]
    public Task WithoutAClipboardTheClickShowsNoConfirmation()
    {
        return _fixture.RunAsync(async () =>
        {
            // Not in a window: there is no top level, so no clipboard to copy to.
            var view = CreateView();
            var button = FindCopyButton(view);
            var idleName = AutomationProperties.GetName(button);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();

            Assert.DoesNotContain("copied", button.Classes);
            Assert.Equal(idleName, AutomationProperties.GetName(button));
        });
    }

    private static MarkdownDocumentView CreateView()
        => new()
        {
            ReadingPreferences = ReadingPreferences.Default,
            Document = new RenderedMarkdownDocument([new MarkdownCodeBlock("bash", Code)])
        };

    private static Button FindCopyButton(MarkdownDocumentView view)
    {
        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        var block = Assert.IsType<Border>(Assert.Single(root.Children));
        var contentGrid = Assert.IsType<Grid>(block.Child);
        return Assert.IsType<Button>(contentGrid.Children[1]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // Well past the 1.5 s confirmation, so the reset has time to fire.
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(25);
        }
    }
}
