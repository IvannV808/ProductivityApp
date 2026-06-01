using System.Windows;
using System.Windows.Input;
using WpfDataObject = System.Windows.DataObject;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ProductivityApp;

public partial class ExitChallengeWindow : Window
{
    private readonly string _challenge;
    private readonly bool _hasMasterPassword;

    public ExitChallengeWindow(string challenge)
    {
        InitializeComponent();

        _challenge = challenge;
        _hasMasterPassword = AppDataStore.HasMasterPassword();

        ChallengeText.Text = challenge;
        PasswordInput.IsEnabled = _hasMasterPassword;
        PasswordUnavailableText.Visibility = _hasMasterPassword ? Visibility.Collapsed : Visibility.Visible;

        WpfDataObject.AddPastingHandler(ChallengeInput, OnPaste);
        ChallengeInput.CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Paste,
            (_, e) => e.Handled = true,
            (_, e) =>
            {
                e.CanExecute = false;
                e.Handled = true;
            }));
    }

    private void ChallengeInput_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        bool isPasteShortcut =
            (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.V) ||
            (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && e.Key == Key.Insert);

        if (isPasteShortcut)
        {
            e.Handled = true;
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(ChallengeInput.Text, _challenge, StringComparison.Ordinal) ||
            (_hasMasterPassword && AppDataStore.ValidateMasterPassword(PasswordInput.Password)))
        {
            DialogResult = true;
            return;
        }

        ValidationText.Text = _hasMasterPassword
            ? "The randomized string or master password does not match."
            : "The randomized string does not match.";

        if (!string.IsNullOrEmpty(ChallengeInput.Text))
        {
            ChallengeInput.SelectAll();
            ChallengeInput.Focus();
        }
        else
        {
            PasswordInput.Focus();
        }
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
    }
}
