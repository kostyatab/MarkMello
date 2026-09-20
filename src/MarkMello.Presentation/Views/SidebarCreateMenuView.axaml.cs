using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

public partial class SidebarCreateMenuView : MenuCardView
{
    public SidebarCreateMenuView() => InitializeComponent();

    protected override bool IsOpen(ShellViewModel viewModel) => viewModel.IsCreateMenuOpen;
}
