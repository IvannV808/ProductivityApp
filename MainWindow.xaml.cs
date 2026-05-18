using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProductivityApp;

public partial class MainWindow : Window
{
    private static readonly DayOfWeek[] ScheduleDays =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    private static readonly string[] DayLabels = ["Mon", "Tue", "Wed", "Thur", "Fri", "Sat", "Sun"];

    private static readonly string[] BlockColors =
    [
        "#8EC5FF",
        "#A7D8A0",
        "#F6C177",
        "#D9B8FF",
        "#FFAAA5",
        "#85DCCF",
        "#F5E27A",
        "#B4C6FF"
    ];

    private readonly DispatcherTimer _blockTimer;
    private readonly HashSet<string> _activeCloseAttempts = [];
    private int _nextColorIndex;

    public MainWindow()
    {
        InitializeComponent();
        AppDataStore.EnsureDatabasesExist();

        _nextColorIndex = AppDataStore.GetScheduledBlocks().Count % BlockColors.Length;
        RenderSchedule();

        _blockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _blockTimer.Tick += BlockTimer_Tick;
        _blockTimer.Start();
    }

    private void AppConfigButton_Click(object sender, RoutedEventArgs e)
    {
        AppConfigWindow window = new()
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void MetricsButton_Click(object sender, RoutedEventArgs e)
    {
        MetricsWindow window = new()
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void RenderSchedule()
    {
        ScheduleGrid.Children.Clear();
        ScheduleGrid.RowDefinitions.Clear();
        ScheduleGrid.ColumnDefinitions.Clear();

        for (int column = 0; column < ScheduleDays.Length; column++)
        {
            ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        ScheduleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });

        for (int slot = 0; slot < 96; slot++)
        {
            ScheduleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        }

        for (int dayIndex = 0; dayIndex < DayLabels.Length; dayIndex++)
        {
            TextBlock header = new()
            {
                Text = DayLabels[dayIndex],
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 42, 49)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(header, dayIndex);
            Grid.SetRow(header, 0);
            ScheduleGrid.Children.Add(header);
        }

        for (int slot = 0; slot < 96; slot++)
        {
            int minutes = slot * 15;
            for (int dayIndex = 0; dayIndex < ScheduleDays.Length; dayIndex++)
            {
                Button slotButton = CreateSlotButton(ScheduleDays[dayIndex], minutes);
                Grid.SetColumn(slotButton, dayIndex);
                Grid.SetRow(slotButton, slot + 1);
                ScheduleGrid.Children.Add(slotButton);
            }

            TextBlock timeLabel = new()
            {
                Text = minutes % 60 == 0 ? TimeHelpers.FormatMinutes(minutes) : string.Empty,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(101, 112, 128)),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 1, 0, 0)
            };
            Grid.SetColumn(timeLabel, ScheduleDays.Length);
            Grid.SetRow(timeLabel, slot + 1);
            ScheduleGrid.Children.Add(timeLabel);
        }

        foreach (ScheduledBlock block in AppDataStore.GetScheduledBlocks())
        {
            RenderScheduledBlock(block);
        }

        StatusText.Text = $"{AppDataStore.GetScheduledBlocks().Count} scheduled blocks active";
    }

    private static Button CreateSlotButton(DayOfWeek day, int startMinutes)
    {
        Button button = new()
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(223, 228, 236)),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(0),
            Tag = new ScheduleSlot(day, startMinutes),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += SlotButton_Click;
        return button;
    }

    private static void SlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ScheduleSlot slot } button &&
            Window.GetWindow(button) is MainWindow mainWindow)
        {
            mainWindow.OpenScheduleBlockWindow(slot.Day, slot.StartMinutes);
        }
    }

    private void OpenScheduleBlockWindow(DayOfWeek day, int startMinutes)
    {
        ScheduleBlockWindow window = new(day, startMinutes, BlockColors[_nextColorIndex])
        {
            Owner = this
        };

        if (window.ShowDialog() != true || window.ScheduledBlock is null)
        {
            return;
        }

        AppDataStore.AddScheduledBlock(window.ScheduledBlock);
        _nextColorIndex = (_nextColorIndex + 1) % BlockColors.Length;
        RenderSchedule();
    }

    private void RenderScheduledBlock(ScheduledBlock block)
    {
        int dayColumn = Array.IndexOf(ScheduleDays, block.Day);
        if (dayColumn < 0)
        {
            return;
        }

        int startSlot = Math.Clamp(block.StartMinutes / 15, 0, 95);
        int endSlot = Math.Clamp((int)Math.Ceiling(block.EndMinutes / 15.0), startSlot + 1, 96);
        int rowSpan = Math.Max(1, endSlot - startSlot);
        Color color = (Color)ColorConverter.ConvertFromString(block.ColorHex);

        Border blockPanel = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(210, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(83, 91, 105)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(3),
            Padding = new Thickness(6, 3, 6, 3),
            CornerRadius = new CornerRadius(4),
            ToolTip = $"{block.TimeRangeText}\n{string.Join(", ", block.TargetProcessNames)}",
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"{block.TimeRangeText}\n{string.Join(", ", block.TargetProcessNames)}",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(24, 29, 36))
            }
        };

        Grid.SetColumn(blockPanel, dayColumn);
        Grid.SetRow(blockPanel, startSlot + 1);
        Grid.SetRowSpan(blockPanel, rowSpan);
        Panel.SetZIndex(blockPanel, 5);
        ScheduleGrid.Children.Add(blockPanel);
    }

    private async void BlockTimer_Tick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.Now;
        int currentMinutes = (now.Hour * 60) + now.Minute;
        string currentProcessName = Process.GetCurrentProcess().ProcessName;

        List<ScheduledBlock> activeBlocks = AppDataStore.GetScheduledBlocks()
            .Where(block => block.Day == now.DayOfWeek &&
                            block.StartMinutes <= currentMinutes &&
                            currentMinutes < block.EndMinutes)
            .ToList();

        foreach (ScheduledBlock block in activeBlocks)
        {
            foreach (string processName in block.TargetProcessNames)
            {
                if (string.Equals(processName, currentProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await CloseMatchingProcessesAsync(block, processName);
            }
        }
    }

    private async Task CloseMatchingProcessesAsync(ScheduledBlock block, string processName)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return;
        }

        foreach (Process process in processes)
        {
            string closeKey = $"{block.Id}:{process.Id}";
            if (!_activeCloseAttempts.Add(closeKey))
            {
                process.Dispose();
                continue;
            }

            DateTime detectedAt = DateTime.Now;
            string displayName = processName;

            try
            {
                if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    displayName = process.MainWindowTitle;
                }

                bool closeStarted = process.CloseMainWindow();
                if (closeStarted)
                {
                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4));
                    }
                    catch (TimeoutException)
                    {
                    }
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4));
                    }
                    catch (TimeoutException)
                    {
                    }
                }

                AppDataStore.RecordClosure(new AppClosureEvent
                {
                    ScheduleBlockId = block.Id,
                    ProcessName = processName,
                    DisplayName = displayName,
                    Day = DateTime.Now.DayOfWeek,
                    Date = DateOnly.FromDateTime(DateTime.Now),
                    DetectedAt = detectedAt,
                    ClosedAt = DateTime.Now
                });
            }
            catch
            {
                // Some protected or elevated processes cannot be closed by a normal desktop app.
            }
            finally
            {
                process.Dispose();
                _activeCloseAttempts.Remove(closeKey);
            }
        }
    }
}

public sealed record ScheduleSlot(DayOfWeek Day, int StartMinutes);
