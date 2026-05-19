using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ProductivityApp;

public partial class MetricsWindow : Window
{
    private static readonly Color[] SeriesColors =
    [
        Color.FromRgb(37, 99, 235),
        Color.FromRgb(22, 163, 74),
        Color.FromRgb(217, 119, 6),
        Color.FromRgb(147, 51, 234),
        Color.FromRgb(220, 38, 38),
        Color.FromRgb(8, 145, 178)
    ];

    private readonly List<SelectableMetricApp> _metricApps = [];
    private readonly DispatcherTimer _refreshTimer;
    private List<UsageChartSeries> _chartSeries = [];
    private List<UsageBucket> _chartBuckets = [];

    public MetricsWindow()
    {
        InitializeComponent();
        ToDatePicker.SelectedDate = DateTime.Today;
        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-6);
        RenderMetrics();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += (_, _) => RenderMetrics();
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RenderMetrics();
    }

    private void Filter_Changed(object sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RenderUsageMetrics();
    }

    private void MetricAppCheckBox_Click(object sender, RoutedEventArgs e)
    {
        RenderUsageMetrics();
    }

    private void UsageChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawUsageChart();
    }

    private void RenderMetrics()
    {
        RefreshMetricApps();
        RenderUsageMetrics();
        RenderClosureMetrics();
    }

    private void RefreshMetricApps()
    {
        HashSet<string> selectedApps = _metricApps
            .Where(app => app.IsSelected)
            .Select(app => app.ProcessName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<AppUsageSession> sessions = AppDataStore.GetUsageSessions();
        IReadOnlyList<TargetApp> targetApps = AppDataStore.GetTargetApps();

        _metricApps.Clear();
        _metricApps.AddRange(targetApps
            .Select(targetApp =>
            {
                AppUsageSession? recentSession = sessions
                    .Where(session => string.Equals(session.ProcessName, targetApp.ProcessName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(session => session.EndAt)
                    .FirstOrDefault();

                return new SelectableMetricApp(
                    targetApp.ProcessName,
                    recentSession?.DisplayName ?? targetApp.DisplayName,
                    selectedApps.Count == 0 || selectedApps.Contains(targetApp.ProcessName));
            })
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase));

        AppsList.ItemsSource = null;
        AppsList.ItemsSource = _metricApps;
    }

    private void RenderUsageMetrics()
    {
        DateOnly fromDate = DateOnly.FromDateTime(FromDatePicker.SelectedDate ?? DateTime.Today.AddDays(-6));
        DateOnly toDate = DateOnly.FromDateTime(ToDatePicker.SelectedDate ?? DateTime.Today);
        if (fromDate > toDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        bool weekly = IsWeeklyMode();
        List<AppUsageSession> sessions = AppDataStore.GetUsageSessions()
            .Where(session => session.Date >= fromDate && session.Date <= toDate && session.DurationSeconds > 0)
            .ToList();

        HashSet<string> selectedProcesses = _metricApps
            .Where(app => app.IsSelected)
            .Select(app => app.ProcessName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<AppUsageSession> selectedSessions = sessions
            .Where(session => selectedProcesses.Contains(session.ProcessName))
            .ToList();

        _chartBuckets = BuildBuckets(fromDate, toDate, weekly);
        Dictionary<(string ProcessName, DateOnly BucketStart), int> totals = selectedSessions
            .GroupBy(session => (session.ProcessName, BucketStart: GetBucketStart(session.Date, weekly)))
            .ToDictionary(group => group.Key, group => group.Sum(session => session.DurationSeconds));

        _chartSeries = _metricApps
            .Where(app => app.IsSelected)
            .Select((app, index) => new UsageChartSeries(
                app.DisplayName,
                app.ProcessName,
                SeriesColors[index % SeriesColors.Length],
                _chartBuckets.Select(bucket => totals.TryGetValue((app.ProcessName, bucket.StartDate), out int seconds) ? seconds : 0).ToList()))
            .ToList();

        UsageGrid.ItemsSource = _chartSeries
            .SelectMany(series => _chartBuckets.Select((bucket, index) => new UsageTotalRow(
                bucket.Label,
                series.DisplayName,
                FormatDuration(series.Values[index]),
                series.Values[index])))
            .Where(row => row.Seconds > 0)
            .OrderBy(row => row.Period)
            .ThenBy(row => row.DisplayName)
            .ToList();

        DrawUsageChart();
    }

    private void DrawUsageChart()
    {
        UsageChartCanvas.Children.Clear();
        LegendPanel.Children.Clear();

        double width = UsageChartCanvas.ActualWidth;
        double height = UsageChartCanvas.ActualHeight;
        if (width <= 0 || height <= 0 || _chartBuckets.Count == 0 || _chartSeries.Count == 0)
        {
            EmptyChartText.Visibility = Visibility.Visible;
            return;
        }

        int maxSeconds = _chartSeries.SelectMany(series => series.Values).DefaultIfEmpty(0).Max();
        if (maxSeconds <= 0)
        {
            EmptyChartText.Visibility = Visibility.Visible;
            return;
        }

        EmptyChartText.Visibility = Visibility.Collapsed;
        const double leftPadding = 56;
        const double rightPadding = 20;
        const double topPadding = 20;
        const double bottomPadding = 44;
        double plotWidth = Math.Max(1, width - leftPadding - rightPadding);
        double plotHeight = Math.Max(1, height - topPadding - bottomPadding);

        DrawAxis(leftPadding, topPadding, plotWidth, plotHeight, maxSeconds);

        for (int seriesIndex = 0; seriesIndex < _chartSeries.Count; seriesIndex++)
        {
            UsageChartSeries series = _chartSeries[seriesIndex];
            SolidColorBrush brush = new(series.Color);
            Polyline line = new()
            {
                Stroke = brush,
                StrokeThickness = 2.5
            };

            for (int index = 0; index < _chartBuckets.Count; index++)
            {
                double x = leftPadding + (_chartBuckets.Count == 1 ? plotWidth / 2 : index * (plotWidth / (_chartBuckets.Count - 1)));
                double y = topPadding + plotHeight - ((series.Values[index] / (double)maxSeconds) * plotHeight);
                line.Points.Add(new Point(x, y));

                Ellipse point = new()
                {
                    Width = 7,
                    Height = 7,
                    Fill = brush,
                    ToolTip = $"{series.DisplayName}\n{_chartBuckets[index].Label}: {FormatDuration(series.Values[index])}"
                };
                Canvas.SetLeft(point, x - 3.5);
                Canvas.SetTop(point, y - 3.5);
                UsageChartCanvas.Children.Add(point);
            }

            UsageChartCanvas.Children.Add(line);
            AddLegendItem(series.DisplayName, series.Color);
        }

        for (int index = 0; index < _chartBuckets.Count; index++)
        {
            double x = leftPadding + (_chartBuckets.Count == 1 ? plotWidth / 2 : index * (plotWidth / (_chartBuckets.Count - 1)));
            TextBlock label = new()
            {
                Text = _chartBuckets[index].ShortLabel,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(101, 112, 128))
            };
            Canvas.SetLeft(label, x - 24);
            Canvas.SetTop(label, topPadding + plotHeight + 10);
            UsageChartCanvas.Children.Add(label);
        }
    }

    private void DrawAxis(double left, double top, double width, double height, int maxSeconds)
    {
        SolidColorBrush axisBrush = new(Color.FromRgb(188, 197, 208));
        UsageChartCanvas.Children.Add(new Line { X1 = left, X2 = left, Y1 = top, Y2 = top + height, Stroke = axisBrush, StrokeThickness = 1 });
        UsageChartCanvas.Children.Add(new Line { X1 = left, X2 = left + width, Y1 = top + height, Y2 = top + height, Stroke = axisBrush, StrokeThickness = 1 });

        for (int tick = 0; tick <= 4; tick++)
        {
            double y = top + height - (tick / 4.0 * height);
            int seconds = (int)(tick / 4.0 * maxSeconds);
            UsageChartCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + width,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(232, 236, 242)),
                StrokeThickness = 1
            });

            TextBlock label = new()
            {
                Text = FormatDuration(seconds),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(101, 112, 128))
            };
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, y - 8);
            UsageChartCanvas.Children.Add(label);
        }
    }

    private void AddLegendItem(string name, Color color)
    {
        StackPanel item = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 18, 0)
        };
        item.Children.Add(new Border
        {
            Width = 12,
            Height = 12,
            Background = new SolidColorBrush(color),
            Margin = new Thickness(0, 0, 6, 0)
        });
        item.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = new SolidColorBrush(Color.FromRgb(36, 42, 49))
        });
        LegendPanel.Children.Add(item);
    }

    private void RenderClosureMetrics()
    {
        ClosureMetricsPanel.Children.Clear();

        IReadOnlyList<AppClosureEvent> events = AppDataStore.GetClosureEvents();
        if (events.Count == 0)
        {
            ClosureMetricsPanel.Children.Add(new TextBlock
            {
                Text = "No apps have been closed by a scheduled block yet.",
                Foreground = new SolidColorBrush(Color.FromRgb(101, 112, 128)),
                FontSize = 15,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return;
        }

        var dayGroups = events
            .GroupBy(entry => entry.Date)
            .OrderByDescending(group => group.Key);

        foreach (var dayGroup in dayGroups)
        {
            ClosureMetricsPanel.Children.Add(new TextBlock
            {
                Text = $"{dayGroup.Key:dddd, MMM d, yyyy}",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 42, 49)),
                Margin = new Thickness(0, 8, 0, 8)
            });

            DataGrid grid = new()
            {
                AutoGenerateColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                CanUserAddRows = false,
                IsReadOnly = true,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Margin = new Thickness(0, 0, 0, 16),
                ItemsSource = dayGroup
                    .GroupBy(entry => entry.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new ClosureMetricsRow(
                        group.First().DisplayName,
                        group.Key,
                        group.Count(),
                        group.Max(entry => entry.ClosedAt).ToString("h:mm tt")))
                    .OrderByDescending(row => row.ClosedCount)
                    .ThenBy(row => row.DisplayName)
                    .ToList()
            };

            grid.Columns.Add(new DataGridTextColumn { Header = "App", Binding = new Binding("DisplayName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Process", Binding = new Binding("ProcessName"), Width = new DataGridLength(160) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Closed", Binding = new Binding("ClosedCount"), Width = new DataGridLength(90) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Last closed", Binding = new Binding("LastClosed"), Width = new DataGridLength(120) });

            ClosureMetricsPanel.Children.Add(grid);
        }
    }

    private static List<UsageBucket> BuildBuckets(DateOnly fromDate, DateOnly toDate, bool weekly)
    {
        List<UsageBucket> buckets = [];
        DateOnly current = weekly ? GetBucketStart(fromDate, weekly: true) : fromDate;
        while (current <= toDate)
        {
            DateOnly end = weekly ? current.AddDays(6) : current;
            buckets.Add(new UsageBucket(
                current,
                weekly ? $"{current:MMM d} - {end:MMM d}" : $"{current:ddd, MMM d}",
                weekly ? $"{current:MMM d}" : $"{current:MMM d}"));
            current = weekly ? current.AddDays(7) : current.AddDays(1);
        }

        return buckets;
    }

    private static DateOnly GetBucketStart(DateOnly date, bool weekly)
    {
        if (!weekly)
        {
            return date;
        }

        int offset = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-offset);
    }

    private bool IsWeeklyMode()
    {
        return GroupModeCombo.SelectedItem is ComboBoxItem item &&
               string.Equals(item.Content?.ToString(), "Weekly", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds < 60)
        {
            return $"{seconds}s";
        }

        TimeSpan duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{duration.Minutes}m";
    }
}

public sealed class SelectableMetricApp : INotifyPropertyChanged
{
    private bool _isSelected;

    public SelectableMetricApp(string processName, string displayName, bool isSelected)
    {
        ProcessName = processName;
        DisplayName = displayName;
        _isSelected = isSelected;
    }

    public string ProcessName { get; }
    public string DisplayName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record UsageBucket(DateOnly StartDate, string Label, string ShortLabel);

public sealed record UsageChartSeries(string DisplayName, string ProcessName, Color Color, List<int> Values);

public sealed record UsageTotalRow(string Period, string DisplayName, string TimeSpent, int Seconds);

public sealed record ClosureMetricsRow(string DisplayName, string ProcessName, int ClosedCount, string LastClosed);
