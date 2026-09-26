using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

public partial class NewTabMenuView : MenuCardView
{
    public NewTabMenuView() => InitializeComponent();

    protected override bool IsOpen(ShellViewModel viewModel) => viewModel.IsNewTabMenuOpen;
}
