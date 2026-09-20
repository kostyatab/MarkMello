using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

public partial class TreeContextMenuView : MenuCardView
{
    public TreeContextMenuView() => InitializeComponent();

    protected override bool IsOpen(ShellViewModel viewModel) => viewModel.IsTreeContextMenuOpen;
}
