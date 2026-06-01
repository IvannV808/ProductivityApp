using System.Windows;

namespace ProductivityApp;

public partial class ScheduleBlockWindow : Window
{
    private readonly string _colorHex;
    private readonly string? _existingBlockId;
    private readonly DateTime? _createdAt;
    private readonly List<SelectableTargetApp> _targetApps;

    public ScheduledBlock? ScheduledBlock { get; private set; }

    public ScheduleBlockWindow(DayOfWeek day, int startMinutes, string colorHex)
        : this(new ScheduledBlock
        {
            Day = day,
            StartMinutes = startMinutes,
            EndMinutes = Math.Min(startMinutes + 15, 24 * 60),
            ColorHex = colorHex
        }, isEdit: false)
    {
    }

    public ScheduleBlockWindow(ScheduledBlock block)
        : this(block, ScheduleBlockEditMode.Full)
    {
    }

    public ScheduleBlockWindow(ScheduledBlock block, ScheduleBlockEditMode editMode)
        : this(block, isEdit: true, editMode)
    {
    }

    private ScheduleBlockWindow(ScheduledBlock block, bool isEdit)
        : this(block, isEdit, ScheduleBlockEditMode.Full)
    {
    }

    private ScheduleBlockWindow(ScheduledBlock block, bool isEdit, ScheduleBlockEditMode editMode)
    {
        InitializeComponent();

        _colorHex = block.ColorHex;
        _existingBlockId = isEdit ? block.Id : null;
        _createdAt = isEdit ? block.CreatedAt : null;

        List<TargetApp> availableTargetApps = AppDataStore.GetTargetApps().ToList();
        HashSet<string> availableProcessNames = availableTargetApps
            .Select(app => app.ProcessName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string processName in block.TargetProcessNames.Where(processName => !availableProcessNames.Contains(processName)))
        {
            availableTargetApps.Add(new TargetApp
            {
                ProcessName = processName,
                DisplayName = RunningAppNameResolver.GetFriendlyDisplayName(processName, processName)
            });
        }

        _targetApps = availableTargetApps
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(app =>
            {
                bool isSelected = block.TargetProcessNames.Contains(app.ProcessName, StringComparer.OrdinalIgnoreCase);
                return new SelectableTargetApp(
                    app,
                    isSelected,
                    canChangeSelection: editMode != ScheduleBlockEditMode.AddOnly || !isSelected);
            })
            .ToList();

        Title = editMode == ScheduleBlockEditMode.AddOnly ? "Edit-Add Schedule Block" : isEdit ? "Edit Schedule Block" : "Schedule Block";
        DayText.Text = block.Day.ToString();
        DescriptionText.Text = editMode == ScheduleBlockEditMode.AddOnly
            ? "Add target apps to this block. Apps already in the block cannot be removed here."
            : "Choose a time range and the apps to close during that block.";
        PopulateTimeOptions(block.StartMinutes, block.EndMinutes);
        TargetAppsList.ItemsSource = _targetApps;
        EmptyAppsText.Visibility = _targetApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateTimeOptions(int selectedStartMinutes, int selectedEndMinutes)
    {
        List<TimeOption> startOptions = Enumerable.Range(0, 96)
            .Select(slot => new TimeOption(slot * 15))
            .ToList();
        List<TimeOption> endOptions = Enumerable.Range(1, 96)
            .Select(slot => new TimeOption(slot * 15))
            .ToList();

        StartTimeCombo.ItemsSource = startOptions;
        EndTimeCombo.ItemsSource = endOptions;
        StartTimeCombo.DisplayMemberPath = nameof(TimeOption.DisplayText);
        EndTimeCombo.DisplayMemberPath = nameof(TimeOption.DisplayText);
        StartTimeCombo.SelectedValuePath = nameof(TimeOption.Minutes);
        EndTimeCombo.SelectedValuePath = nameof(TimeOption.Minutes);
        StartTimeCombo.SelectedValue = Math.Clamp(selectedStartMinutes, 0, 23 * 60 + 45);
        EndTimeCombo.SelectedValue = Math.Clamp(selectedEndMinutes, 15, 24 * 60);

        ColorCombo.ItemsSource = BlockColorOption.All;
        ColorCombo.DisplayMemberPath = nameof(BlockColorOption.DisplayText);
        ColorCombo.SelectedValuePath = nameof(BlockColorOption.Hex);
        ColorCombo.SelectedValue = BlockColorOption.All.Any(option => option.Hex == _colorHex)
            ? _colorHex
            : BlockColorOption.All[0].Hex;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (StartTimeCombo.SelectedValue is not int startMinutes ||
            EndTimeCombo.SelectedValue is not int endMinutes)
        {
            ValidationText.Text = "Choose start and end times.";
            return;
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
            Id = _existingBlockId ?? Guid.NewGuid().ToString("N"),
            Day = Enum.TryParse(DayText.Text, out DayOfWeek day) ? day : DayOfWeek.Monday,
            StartMinutes = startMinutes,
            EndMinutes = endMinutes,
            TargetProcessNames = selectedProcesses,
            ColorHex = ColorCombo.SelectedValue as string ?? BlockColorOption.All[0].Hex,
            CreatedAt = _createdAt ?? DateTime.Now
        };

        DialogResult = true;
    }
}

public sealed class SelectableTargetApp
{
    public SelectableTargetApp(TargetApp targetApp, bool isSelected = false, bool canChangeSelection = true)
    {
        ProcessName = targetApp.ProcessName;
        DisplayName = targetApp.DisplayName;
        IsSelected = isSelected;
        CanChangeSelection = canChangeSelection;
    }

    public string ProcessName { get; }
    public string DisplayName { get; }
    public bool IsSelected { get; set; }
    public bool CanChangeSelection { get; }
}

public enum ScheduleBlockEditMode
{
    Full,
    AddOnly
}

public sealed record TimeOption(int Minutes)
{
    public string DisplayText => TimeHelpers.FormatMinutes(Minutes);
}
