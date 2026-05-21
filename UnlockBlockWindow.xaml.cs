using System.Windows;
using System.Windows.Input;
using WpfDataObject = System.Windows.DataObject;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ProductivityApp;

public partial class UnlockBlockWindow : Window
{
    private readonly string _challenge;

    public UnlockBlockWindow(string challenge)
    {
        InitializeComponent();
        _challenge = challenge;
        ChallengeText.Text = challenge;

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

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(ChallengeInput.Text, _challenge, StringComparison.Ordinal))
        {
            DialogResult = true;
            return;
        }

        ValidationText.Text = "The unlock string does not match.";
        ChallengeInput.SelectAll();
        ChallengeInput.Focus();
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
    }
}
