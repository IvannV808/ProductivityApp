using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrawingIcon = System.Drawing.Icon;
using WinForms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace ProductivityApp;

public partial class MainWindow : Window
{
    private const double HeaderHeight = 42;
    private const double DefaultSlotHeight = 26;
    private const double TimeColumnWidth = 70;
    private const double DayColumnMinWidth = 105;
    private const double BlockWidthRatio = 0.78;
    private const double ResizeEdgeSize = 7;

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

    private static readonly string[] ClosureMessages =
    [
        "Please use your time more wisely, your future family is relying on you.",
        "No time to be dinking off! GET WORKING!",
        "Only soft people play when its time to work and you're not soft!",
        "Big gains for small work. Lets goo!"
    ];

    private readonly DispatcherTimer _blockTimer;
    private readonly DispatcherTimer _usageTimer;
    private readonly DispatcherTimer _unlockTimer;
    private readonly ForegroundUsageTracker _usageTracker = new();
    private readonly HashSet<string> _activeCloseAttempts = [];
    private readonly WinForms.NotifyIcon _trayIcon;
    private readonly Dictionary<string, DateTime> _blockUnlockExpirations = [];
    private List<ScheduledBlock> _scheduledBlocks = [];
    private BlockDragState? _activeDrag;
    private MetricsWindow? _metricsWindow;
    private double _slotHeight = DefaultSlotHeight;
    private DateTime _nextMotivationMessageAt = DateTime.MinValue;
    private bool _isExitRequested;

    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
        _trayIcon = CreateTrayIcon();
        AppDataStore.EnsureDatabasesExist();

        RenderSchedule();

        _blockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _blockTimer.Tick += BlockTimer_Tick;
        _blockTimer.Start();

        _usageTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _usageTimer.Tick += (_, _) => _usageTracker.Sample();
        _usageTimer.Start();

        _unlockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _unlockTimer.Tick += (_, _) => RefreshUnlockTimerStatus();
        _unlockTimer.Start();
        Closing += MainWindow_Closing;
    }

    private void SetWindowIcon()
    {
        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (!System.IO.File.Exists(iconPath))
        {
            return;
        }

        try
        {
            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
        }
        catch
        {
        }
    }

    private WinForms.NotifyIcon CreateTrayIcon()
    {
        WinForms.ContextMenuStrip menu = new();
        WinForms.ToolStripMenuItem metricsItem = new("Metrics");
        metricsItem.Click += (_, _) => Dispatcher.Invoke(ShowMetricsWindow);
        WinForms.ToolStripMenuItem exitItem = new("Exit");
        exitItem.Click += (_, _) => ExitFromTray();
        menu.Items.Add(metricsItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        WinForms.NotifyIcon notifyIcon = new()
        {
            Text = "Productivity App",
            ContextMenuStrip = menu,
            Visible = true
        };

        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        try
        {
            notifyIcon.Icon = System.IO.File.Exists(iconPath)
                ? new DrawingIcon(iconPath)
                : System.Drawing.SystemIcons.Application;
        }
        catch
        {
            notifyIcon.Icon = System.Drawing.SystemIcons.Application;
        }

        notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                ShowMainWindowFromTray();
            }
        };

        return notifyIcon;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExitRequested)
        {
            _blockTimer.Stop();
            _usageTimer.Stop();
            _unlockTimer.Stop();
            _usageTracker.FinishCurrentSession();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void ShowMainWindowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _isExitRequested = true;
        Close();
        System.Windows.Application.Current.Shutdown();
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
        ShowMetricsWindow();
    }

    private void ShowMetricsWindow()
    {
        if (_metricsWindow is null)
        {
            _metricsWindow = new MetricsWindow();
            _metricsWindow.Closed += (_, _) => _metricsWindow = null;
        }

        if (!_metricsWindow.IsVisible)
        {
            _metricsWindow.Show();
        }

        if (_metricsWindow.WindowState == WindowState.Minimized)
        {
            _metricsWindow.WindowState = WindowState.Normal;
        }

        _metricsWindow.Activate();
    }

    private void RenderSchedule()
    {
        _scheduledBlocks = AppDataStore.GetScheduledBlocks().ToList();

        ScheduleGrid.Children.Clear();
        ScheduleGrid.RowDefinitions.Clear();
        ScheduleGrid.ColumnDefinitions.Clear();
        BlockCanvas.Children.Clear();

        ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimeColumnWidth) });

        for (int column = 0; column < ScheduleDays.Length; column++)
        {
            ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = DayColumnMinWidth });
        }

        ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TimeColumnWidth) });
        ScheduleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });

        for (int slot = 0; slot < 96; slot++)
        {
            ScheduleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_slotHeight) });
        }

        ScheduleHost.Height = HeaderHeight + (96 * _slotHeight);
        BlockCanvas.Height = ScheduleHost.Height;

        for (int dayIndex = 0; dayIndex < DayLabels.Length; dayIndex++)
        {
            TextBlock header = new()
            {
                Text = DayLabels[dayIndex],
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(36, 42, 49)),
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(header, dayIndex + 1);
            Grid.SetRow(header, 0);
            ScheduleGrid.Children.Add(header);
        }

        for (int slot = 0; slot < 96; slot++)
        {
            int minutes = slot * 15;
            for (int dayIndex = 0; dayIndex < ScheduleDays.Length; dayIndex++)
            {
                WpfButton slotButton = CreateSlotButton(ScheduleDays[dayIndex], minutes);
                Grid.SetColumn(slotButton, dayIndex + 1);
                Grid.SetRow(slotButton, slot + 1);
                ScheduleGrid.Children.Add(slotButton);
            }

            AddTimeLabel(minutes, slot + 1, column: 0, horizontalAlignment: WpfHorizontalAlignment.Right, margin: new Thickness(0, 1, 8, 0));
            AddTimeLabel(minutes, slot + 1, column: ScheduleDays.Length + 1, horizontalAlignment: WpfHorizontalAlignment.Left, margin: new Thickness(8, 1, 0, 0));
        }

        ScheduleGrid.UpdateLayout();
        RenderBlockOverlays();
        StatusText.Text = $"{_scheduledBlocks.Count} scheduled blocks active";
    }

    private void AddTimeLabel(int minutes, int row, int column, WpfHorizontalAlignment horizontalAlignment, Thickness margin)
    {
        TextBlock timeLabel = new()
        {
            Text = minutes % 60 == 0 ? TimeHelpers.FormatMinutes(minutes) : string.Empty,
            FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(101, 112, 128)),
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = margin
        };
        Grid.SetColumn(timeLabel, column);
        Grid.SetRow(timeLabel, row);
        ScheduleGrid.Children.Add(timeLabel);
    }

    private static WpfButton CreateSlotButton(DayOfWeek day, int startMinutes)
    {
        WpfButton button = new()
        {
            Background = WpfBrushes.Transparent,
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(223, 228, 236)),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(0),
            Tag = new ScheduleSlot(day, startMinutes),
            Cursor = WpfCursors.Hand
        };
        button.Click += SlotButton_Click;
        return button;
    }

    private static void SlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: ScheduleSlot slot } button &&
            Window.GetWindow(button) is MainWindow mainWindow)
        {
            mainWindow.OpenScheduleBlockWindow(slot.Day, slot.StartMinutes);
        }
    }

    private void OpenScheduleBlockWindow(DayOfWeek day, int startMinutes)
    {
        ScheduleBlockWindow window = new(day, startMinutes, BlockColorOption.All[0].Hex)
        {
            Owner = this
        };

        if (window.ShowDialog() != true || window.ScheduledBlock is null)
        {
            return;
        }

        AppDataStore.AddScheduledBlock(window.ScheduledBlock);
        HandleBlockSaved(window.ScheduledBlock);
        RenderSchedule();
    }

    private void OpenEditScheduleBlockWindow(ScheduledBlock block)
    {
        if (!EnsureBlockCanBeModified(block))
        {
            return;
        }

        ScheduleBlockWindow window = new(CloneScheduledBlock(block))
        {
            Owner = this
        };

        if (window.ShowDialog() != true || window.ScheduledBlock is null)
        {
            return;
        }

        AppDataStore.UpdateScheduledBlock(window.ScheduledBlock);
        HandleBlockSaved(window.ScheduledBlock);
        RenderSchedule();
    }

    private void ScheduleGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderBlockOverlays();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _slotHeight = e.NewValue <= 0 ? DefaultSlotHeight : e.NewValue;
        if (ScheduleGrid is not null)
        {
            RenderSchedule();
        }
    }

    private void RenderBlockOverlays()
    {
        if (ScheduleGrid.ColumnDefinitions.Count == 0 || ScheduleGrid.ActualWidth <= 0)
        {
            return;
        }

        BlockCanvas.Width = ScheduleGrid.ActualWidth;
        BlockCanvas.Children.Clear();

        Dictionary<string, BlockLayout> layouts = BuildBlockLayouts(_scheduledBlocks);
        foreach (ScheduledBlock block in _scheduledBlocks)
        {
            if (!layouts.TryGetValue(block.Id, out BlockLayout? layout))
            {
                continue;
            }

            Border blockPanel = CreateBlockPanel(block, layout);
            PositionBlockPanel(blockPanel, block, layout);
            BlockCanvas.Children.Add(blockPanel);
        }
    }

    private Border CreateBlockPanel(ScheduledBlock block, BlockLayout layout)
    {
        MediaColor color = (MediaColor)WpfColorConverter.ConvertFromString(block.ColorHex);
        Border blockPanel = new()
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(224, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(83, 91, 105)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 3, 6, 3),
            CornerRadius = new CornerRadius(4),
            Cursor = WpfCursors.SizeAll,
            Tag = new BlockElementState(block, layout),
            Child = CreateBlockText(block)
        };

        WpfMenuItem editItem = new()
        {
            Header = "Edit"
        };
        editItem.Click += (_, _) => OpenEditScheduleBlockWindow(block);
        WpfMenuItem deleteItem = new()
        {
            Header = "Delete"
        };
        deleteItem.Click += (_, _) => DeleteScheduleBlock(block);
        blockPanel.ContextMenu = new WpfContextMenu
        {
            Items = { editItem, deleteItem }
        };

        blockPanel.MouseMove += BlockPanel_MouseMove;
        blockPanel.MouseLeftButtonDown += BlockPanel_MouseLeftButtonDown;
        blockPanel.MouseLeftButtonUp += BlockPanel_MouseLeftButtonUp;
        return blockPanel;
    }

    private void DeleteScheduleBlock(ScheduledBlock block)
    {
        if (!EnsureBlockCanBeModified(block))
        {
            return;
        }

        MessageBoxResult result = WpfMessageBox.Show(
            this,
            $"Delete the {block.TimeRangeText} block on {block.Day}?",
            "Delete Schedule Block",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        AppDataStore.DeleteScheduledBlock(block.Id);
        _blockUnlockExpirations.Remove(block.Id);
        RefreshUnlockTimerStatus();
        RenderSchedule();
    }

    private bool EnsureBlockCanBeModified(ScheduledBlock block)
    {
        if (!IsBlockCurrentlyActive(block) || IsBlockUnlocked(block))
        {
            return true;
        }

        string challenge = GenerateUnlockChallenge();
        UnlockBlockWindow unlockWindow = new(challenge)
        {
            Owner = this
        };

        if (unlockWindow.ShowDialog() != true)
        {
            return false;
        }

        GrantBlockEditGrace(block.Id);
        return true;
    }

    private void HandleBlockSaved(ScheduledBlock block)
    {
        if (IsBlockCurrentlyActive(block))
        {
            GrantBlockEditGrace(block.Id);
            WpfMessageBox.Show(
                this,
                "This block covers the current time. You have 1 minute to edit it so it no longer covers the current timeframe before future changes require the unlock procedure again.",
                "Current Time Covered",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else
        {
            _blockUnlockExpirations.Remove(block.Id);
            RefreshUnlockTimerStatus();
        }
    }

    private bool IsBlockUnlocked(ScheduledBlock block)
    {
        if (!_blockUnlockExpirations.TryGetValue(block.Id, out DateTime expiresAt))
        {
            return false;
        }

        if (DateTime.Now >= expiresAt || !IsBlockCurrentlyActive(block))
        {
            _blockUnlockExpirations.Remove(block.Id);
            RefreshUnlockTimerStatus();
            return false;
        }

        return true;
    }

    private void GrantBlockEditGrace(string blockId)
    {
        _blockUnlockExpirations[blockId] = DateTime.Now.AddMinutes(1);
        RefreshUnlockTimerStatus();
    }

    private void RefreshUnlockTimerStatus()
    {
        DateTime now = DateTime.Now;
        Dictionary<string, ScheduledBlock> blocksById = AppDataStore.GetScheduledBlocks()
            .ToDictionary(block => block.Id, StringComparer.OrdinalIgnoreCase);

        foreach (string blockId in _blockUnlockExpirations.Keys.ToList())
        {
            if (_blockUnlockExpirations[blockId] <= now ||
                !blocksById.TryGetValue(blockId, out ScheduledBlock? block) ||
                !IsBlockCurrentlyActive(block))
            {
                _blockUnlockExpirations.Remove(blockId);
            }
        }

        if (_blockUnlockExpirations.Count == 0)
        {
            UnlockTimerBadge.Visibility = Visibility.Collapsed;
            UnlockTimerText.Text = string.Empty;
            return;
        }

        TimeSpan remaining = _blockUnlockExpirations.Values.Min() - now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        UnlockTimerBadge.Visibility = Visibility.Visible;
        UnlockTimerText.Text = $"Edit grace: {remaining.Minutes:D1}:{remaining.Seconds:D2}";
    }

    private static bool IsBlockCurrentlyActive(ScheduledBlock block)
    {
        DateTime now = DateTime.Now;
        int currentMinutes = (now.Hour * 60) + now.Minute;
        return block.Day == now.DayOfWeek &&
               block.StartMinutes <= currentMinutes &&
               currentMinutes < block.EndMinutes;
    }

    private static string GenerateUnlockChallenge()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);

        char[] characters = new char[32];
        for (int index = 0; index < characters.Length; index++)
        {
            characters[index] = alphabet[bytes[index] % alphabet.Length];
        }

        return new string(characters);
    }

    private static TextBlock CreateBlockText(ScheduledBlock block)
    {
        return new TextBlock
        {
            Text = $"{block.TimeRangeText}\n{string.Join(", ", block.TargetProcessNames)}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(24, 29, 36))
        };
    }

    private void PositionBlockPanel(Border blockPanel, ScheduledBlock block, BlockLayout layout)
    {
        int dayColumn = Array.IndexOf(ScheduleDays, block.Day);
        if (dayColumn < 0)
        {
            return;
        }

        int gridColumn = dayColumn + 1;
        double columnLeft = GetColumnLeft(gridColumn);
        double columnWidth = ScheduleGrid.ColumnDefinitions[gridColumn].ActualWidth;
        double laneAreaWidth = Math.Max(48, columnWidth * BlockWidthRatio);
        double laneWidth = Math.Max(36, laneAreaWidth / Math.Max(1, layout.LaneCount));
        double left = columnLeft + (layout.Lane * laneWidth) + 3;
        double top = HeaderHeight + ((block.StartMinutes / 15.0) * _slotHeight) + 2;
        double height = Math.Max(_slotHeight - 4, ((block.EndMinutes - block.StartMinutes) / 15.0 * _slotHeight) - 4);
        double width = Math.Max(32, laneWidth - 6);

        Canvas.SetLeft(blockPanel, left);
        Canvas.SetTop(blockPanel, top);
        blockPanel.Width = width;
        blockPanel.Height = height;
        blockPanel.ToolTip = $"{block.TimeRangeText}\n{string.Join(", ", block.TargetProcessNames)}";

        if (blockPanel.Child is TextBlock textBlock)
        {
            textBlock.Text = $"{block.TimeRangeText}\n{string.Join(", ", block.TargetProcessNames)}";
        }
    }

    private void BlockPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border blockPanel ||
            blockPanel.Tag is not BlockElementState state)
        {
            return;
        }

        WpfPoint positionInBlock = e.GetPosition(blockPanel);
        BlockDragMode mode = GetDragMode(positionInBlock, blockPanel.ActualHeight);
        ScheduledBlock block = state.Block;

        if (!EnsureBlockCanBeModified(block))
        {
            e.Handled = true;
            return;
        }

        _activeDrag = new BlockDragState(
            block,
            blockPanel,
            state.Layout,
            mode,
            e.GetPosition(BlockCanvas),
            block.Day,
            block.StartMinutes,
            block.EndMinutes);

        blockPanel.CaptureMouse();
        e.Handled = true;
    }

    private void BlockPanel_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (sender is not Border blockPanel ||
            blockPanel.Tag is not BlockElementState state)
        {
            return;
        }

        if (_activeDrag is null || !ReferenceEquals(_activeDrag.Element, blockPanel) || e.LeftButton != MouseButtonState.Pressed)
        {
            WpfPoint hoverPoint = e.GetPosition(blockPanel);
            blockPanel.Cursor = GetDragMode(hoverPoint, blockPanel.ActualHeight) == BlockDragMode.Move
                ? WpfCursors.SizeAll
                : WpfCursors.SizeNS;
            return;
        }

        WpfPoint currentPoint = e.GetPosition(BlockCanvas);
        int slotDelta = (int)Math.Round((currentPoint.Y - _activeDrag.StartPointer.Y) / _slotHeight);
        int minuteDelta = slotDelta * 15;
        ScheduledBlock block = _activeDrag.Block;

        switch (_activeDrag.Mode)
        {
            case BlockDragMode.Move:
                int duration = _activeDrag.OriginalEndMinutes - _activeDrag.OriginalStartMinutes;
                int newStart = ClampToSlot(_activeDrag.OriginalStartMinutes + minuteDelta, 0, 24 * 60 - duration);
                block.StartMinutes = newStart;
                block.EndMinutes = newStart + duration;
                block.Day = GetDayFromX(currentPoint.X) ?? _activeDrag.OriginalDay;
                break;

            case BlockDragMode.ResizeTop:
                block.StartMinutes = ClampToSlot(
                    _activeDrag.OriginalStartMinutes + minuteDelta,
                    0,
                    _activeDrag.OriginalEndMinutes - 15);
                block.EndMinutes = _activeDrag.OriginalEndMinutes;
                break;

            case BlockDragMode.ResizeBottom:
                block.StartMinutes = _activeDrag.OriginalStartMinutes;
                block.EndMinutes = ClampToSlot(
                    _activeDrag.OriginalEndMinutes + minuteDelta,
                    _activeDrag.OriginalStartMinutes + 15,
                    24 * 60);
                break;
        }

        PositionBlockPanel(blockPanel, block, state.Layout);
        e.Handled = true;
    }

    private void BlockPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeDrag is null || sender is not Border blockPanel)
        {
            return;
        }

        blockPanel.ReleaseMouseCapture();
        AppDataStore.UpdateScheduledBlock(_activeDrag.Block);
        HandleBlockSaved(_activeDrag.Block);
        _activeDrag = null;
        RenderSchedule();
        e.Handled = true;
    }

    private static BlockDragMode GetDragMode(WpfPoint point, double blockHeight)
    {
        if (point.Y <= ResizeEdgeSize)
        {
            return BlockDragMode.ResizeTop;
        }

        if (blockHeight - point.Y <= ResizeEdgeSize)
        {
            return BlockDragMode.ResizeBottom;
        }

        return BlockDragMode.Move;
    }

    private DayOfWeek? GetDayFromX(double x)
    {
        for (int dayIndex = 0; dayIndex < ScheduleDays.Length; dayIndex++)
        {
            int gridColumn = dayIndex + 1;
            double left = GetColumnLeft(gridColumn);
            double right = left + ScheduleGrid.ColumnDefinitions[gridColumn].ActualWidth;
            if (x >= left && x <= right)
            {
                return ScheduleDays[dayIndex];
            }
        }

        return null;
    }

    private double GetColumnLeft(int columnIndex)
    {
        double left = 0;
        for (int column = 0; column < columnIndex; column++)
        {
            left += ScheduleGrid.ColumnDefinitions[column].ActualWidth;
        }

        return left;
    }

    private static int ClampToSlot(int minutes, int minMinutes, int maxMinutes)
    {
        int snapped = (int)Math.Round(minutes / 15.0) * 15;
        return Math.Clamp(snapped, minMinutes, maxMinutes);
    }

    private static Dictionary<string, BlockLayout> BuildBlockLayouts(IReadOnlyList<ScheduledBlock> blocks)
    {
        Dictionary<string, BlockLayout> layouts = [];

        foreach (IGrouping<DayOfWeek, ScheduledBlock> dayGroup in blocks.GroupBy(block => block.Day))
        {
            List<ScheduledBlock> sortedBlocks = dayGroup
                .OrderBy(block => block.StartMinutes)
                .ThenBy(block => block.EndMinutes)
                .ToList();

            List<ScheduledBlock> currentGroup = [];
            int currentGroupEnd = -1;

            foreach (ScheduledBlock block in sortedBlocks)
            {
                if (currentGroup.Count == 0 || block.StartMinutes < currentGroupEnd)
                {
                    currentGroup.Add(block);
                    currentGroupEnd = Math.Max(currentGroupEnd, block.EndMinutes);
                    continue;
                }

                AddLayoutsForOverlapGroup(currentGroup, layouts);
                currentGroup = [block];
                currentGroupEnd = block.EndMinutes;
            }

            AddLayoutsForOverlapGroup(currentGroup, layouts);
        }

        return layouts;
    }

    private static void AddLayoutsForOverlapGroup(List<ScheduledBlock> group, Dictionary<string, BlockLayout> layouts)
    {
        if (group.Count == 0)
        {
            return;
        }

        List<int> laneEnds = [];
        Dictionary<string, int> assignedLanes = [];

        foreach (ScheduledBlock block in group.OrderBy(block => block.StartMinutes).ThenBy(block => block.EndMinutes))
        {
            int lane = laneEnds.FindIndex(endMinutes => endMinutes <= block.StartMinutes);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(block.EndMinutes);
            }
            else
            {
                laneEnds[lane] = block.EndMinutes;
            }

            assignedLanes[block.Id] = lane;
        }

        int laneCount = Math.Max(1, laneEnds.Count);
        foreach (ScheduledBlock block in group)
        {
            layouts[block.Id] = new BlockLayout(assignedLanes[block.Id], laneCount);
        }
    }

    private static ScheduledBlock CloneScheduledBlock(ScheduledBlock block)
    {
        return new ScheduledBlock
        {
            Id = block.Id,
            Day = block.Day,
            StartMinutes = block.StartMinutes,
            EndMinutes = block.EndMinutes,
            TargetProcessNames = [.. block.TargetProcessNames],
            ColorHex = block.ColorHex,
            CreatedAt = block.CreatedAt
        };
    }

    private async void BlockTimer_Tick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.Now;
        int currentMinutes = (now.Hour * 60) + now.Minute;
        using Process currentProcess = Process.GetCurrentProcess();
        string currentProcessName = currentProcess.ProcessName;

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

                ShowClosureMessage();
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

    private void ShowClosureMessage()
    {
        DateTime now = DateTime.Now;
        if (now < _nextMotivationMessageAt)
        {
            return;
        }

        _nextMotivationMessageAt = now.AddSeconds(7);
        string message = ClosureMessages[Random.Shared.Next(ClosureMessages.Length)];
        MotivationWindow window = new(message);
        window.Show();
    }
}

public sealed record ScheduleSlot(DayOfWeek Day, int StartMinutes);

public sealed record BlockLayout(int Lane, int LaneCount);

public sealed record BlockElementState(ScheduledBlock Block, BlockLayout Layout);

public sealed record BlockDragState(
    ScheduledBlock Block,
    Border Element,
    BlockLayout Layout,
    BlockDragMode Mode,
    WpfPoint StartPointer,
    DayOfWeek OriginalDay,
    int OriginalStartMinutes,
    int OriginalEndMinutes);

public enum BlockDragMode
{
    Move,
    ResizeTop,
    ResizeBottom
}
