using System.Windows;

namespace ProductivityApp;

public partial class PasswordSettingsWindow : Window
{
    private readonly bool _hasMasterPassword;

    public PasswordSettingsWindow()
    {
        InitializeComponent();

        _hasMasterPassword = AppDataStore.HasMasterPassword();
        StatusText.Text = _hasMasterPassword
            ? "Change or clear the password used to unlock active blocks and exit from the tray."
            : "Set the password used to unlock active blocks and exit from the tray.";
        CurrentPasswordPanel.Visibility = _hasMasterPassword ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = _hasMasterPassword ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (_hasMasterPassword && !AppDataStore.ValidateMasterPassword(CurrentPasswordInput.Password))
        {
            ValidationText.Text = "The current password does not match.";
            CurrentPasswordInput.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPasswordInput.Password))
        {
            ValidationText.Text = "Enter a new master password.";
            NewPasswordInput.Focus();
            return;
        }

        if (NewPasswordInput.Password.Length < 6)
        {
            ValidationText.Text = "Use at least 6 characters.";
            NewPasswordInput.Focus();
            return;
        }

        if (!string.Equals(NewPasswordInput.Password, ConfirmPasswordInput.Password, StringComparison.Ordinal))
        {
            ValidationText.Text = "The new passwords do not match.";
            ConfirmPasswordInput.Focus();
            return;
        }

        AppDataStore.SetMasterPassword(NewPasswordInput.Password);
        DialogResult = true;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (!AppDataStore.ValidateMasterPassword(CurrentPasswordInput.Password))
        {
            ValidationText.Text = "Enter the current password before clearing it.";
            CurrentPasswordInput.Focus();
            return;
        }

        AppDataStore.ClearMasterPassword();
        DialogResult = true;
    }
}
