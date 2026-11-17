using System.Windows;
using System.Windows.Controls;
using WaNut.App.ViewModels;

namespace WaNut.App.Views;

public partial class DiscordView : UserControl
{
    private DiscordViewModel? _model;

    public DiscordView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as DiscordViewModel);
        IsVisibleChanged += (_, _) => UpdatePolling();
        Unloaded += (_, _) => _model?.StopPolling();
    }

    private void Attach(DiscordViewModel? model)
    {
        if (_model is not null)
        {
            _model.StopPolling();
            _model.SecretsCleared -= OnSecretsCleared;
        }

        _model = model;
        TokenBox.Clear();
        WebhookBox.Clear();
        if (_model is not null)
        {
            _model.SecretsCleared += OnSecretsCleared;
        }

        UpdatePolling();
    }

    // Only ask the bridge how it is doing while this tab is on screen.
    private void UpdatePolling()
    {
        if (IsVisible)
        {
            _model?.StartPolling();
        }
        else
        {
            _model?.StopPolling();
        }
    }

    // PasswordBox doesn't bind, on purpose: the typed text goes straight to the view model and nowhere else.
    private void OnSecretChanged(object sender, RoutedEventArgs e) => _model?.SetSecrets(TokenBox.Password, WebhookBox.Password);

    private void OnSecretsCleared(object? sender, EventArgs e)
    {
        TokenBox.Clear();
        WebhookBox.Clear();
    }
}
