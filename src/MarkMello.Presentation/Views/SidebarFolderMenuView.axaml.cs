using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

public partial class SidebarFolderMenuView : MenuCardView
{
    public SidebarFolderMenuView() => InitializeComponent();

    protected override bool IsOpen(ShellViewModel viewModel) => viewModel.IsFolderMenuOpen;
}
