using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ProductivityApp;

public partial class MetricsWindow : Window
{
    public MetricsWindow()
    {
        InitializeComponent();
        RenderMetrics();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RenderMetrics();
    }

    private void RenderMetrics()
    {
        MetricsPanel.Children.Clear();

        IReadOnlyList<AppClosureEvent> events = AppDataStore.GetClosureEvents();
        if (events.Count == 0)
        {
            MetricsPanel.Children.Add(new TextBlock
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
            TextBlock header = new()
            {
                Text = $"{dayGroup.Key:dddd, MMM d, yyyy}",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 42, 49)),
                Margin = new Thickness(0, 8, 0, 8)
            };
            MetricsPanel.Children.Add(header);

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
                    .Select(group => new MetricsRow(
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

            MetricsPanel.Children.Add(grid);
        }
    }
}

public sealed record MetricsRow(string DisplayName, string ProcessName, int ClosedCount, string LastClosed);
