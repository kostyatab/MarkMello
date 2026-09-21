using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

public partial class TabsOverflowMenuView : MenuCardView
{
    public TabsOverflowMenuView() => InitializeComponent();

    protected override bool IsOpen(ShellViewModel viewModel) => viewModel.IsTabsOverflowMenuOpen;
}
