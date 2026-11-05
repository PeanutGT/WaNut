using System.Windows;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class VpsWindow : Window
{
    public VpsWindow(VpsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += saved => DialogResult = saved;
        Loaded += (_, _) => NameBox.Focus();
    }

    public VpsViewModel ViewModel { get; }

    // PasswordBox doesn't bind, on purpose: the typed text goes straight to the view model and nowhere else.
    private void OnPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.Password = PasswordBox.Password;

    private void OnPassphraseChanged(object sender, RoutedEventArgs e) => ViewModel.Passphrase = PassphraseBox.Password;
}
