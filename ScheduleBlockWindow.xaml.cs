using System.Windows;

namespace ProductivityApp;

public partial class ScheduleBlockWindow : Window
{
    private readonly DayOfWeek _day;
    private readonly string _colorHex;
    private readonly List<SelectableTargetApp> _targetApps;

    public ScheduledBlock? ScheduledBlock { get; private set; }

    public ScheduleBlockWindow(DayOfWeek day, int startMinutes, string colorHex)
    {
        InitializeComponent();

        _day = day;
        _colorHex = colorHex;
        _targetApps = AppDataStore.GetTargetApps()
            .Select(app => new SelectableTargetApp(app))
            .ToList();

        DayText.Text = day.ToString();
        StartTimeBox.Text = TimeHelpers.FormatMinutes(startMinutes);
        EndTimeBox.Text = TimeHelpers.FormatMinutes(Math.Min(startMinutes + 15, 24 * 60));
        TargetAppsList.ItemsSource = _targetApps;
        EmptyAppsText.Visibility = _targetApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (!TimeHelpers.TryParseTime(StartTimeBox.Text, out int startMinutes) ||
            !TimeHelpers.TryParseTime(EndTimeBox.Text, out int endMinutes))
        {
            ValidationText.Text = "Enter valid start and end times.";
            return;
        }

        if (endMinutes == 0 && startMinutes > 0 && EndTimeBox.Text.Contains("AM", StringComparison.OrdinalIgnoreCase))
        {
            endMinutes = 24 * 60;
        }

        if (endMinutes <= startMinutes)
        {
            ValidationText.Text = "End time must be after start time.";
            return;
        }

        List<string> selectedProcesses = _targetApps
            .Where(app => app.IsSelected)
            .Select(app => app.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedProcesses.Count == 0)
        {
            ValidationText.Text = "Choose at least one target app.";
            return;
        }

        ScheduledBlock = new ScheduledBlock
        {
            Day = _day,
            StartMinutes = startMinutes,
            EndMinutes = endMinutes,
            TargetProcessNames = selectedProcesses,
            ColorHex = _colorHex
        };

        DialogResult = true;
    }
}

public sealed class SelectableTargetApp
{
    public SelectableTargetApp(TargetApp targetApp)
    {
        ProcessName = targetApp.ProcessName;
        DisplayName = targetApp.DisplayName;
    }

    public string ProcessName { get; }
    public string DisplayName { get; }
    public bool IsSelected { get; set; }
}
