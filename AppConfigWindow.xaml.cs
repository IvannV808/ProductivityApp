using System.Windows;
using System.Windows.Input;

namespace ProductivityApp;

public partial class AppConfigWindow : Window
{
    private Point _dragStartPoint;

    public AppConfigWindow()
    {
        InitializeComponent();
        DatabasePathText.Text = $"Database: {AppDataStore.TargetAppsPath}";
        RefreshLists();
    }

    private void RefreshLists()
    {
        RunningAppsList.ItemsSource = RunningAppInfo.GetRunningApps();
        BlockedAppsList.ItemsSource = AppDataStore.GetTargetApps();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshLists();
    }

    private void MoveToBlockButton_Click(object sender, RoutedEventArgs e)
    {
        AddSelectedRunningApps();
    }

    private void RunningAppsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragStartPoint = e.GetPosition(null);
            return;
        }

        Point currentPosition = e.GetPosition(null);
        if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        List<RunningAppInfo> selectedApps = RunningAppsList.SelectedItems
            .Cast<RunningAppInfo>()
            .ToList();

        if (selectedApps.Count > 0)
        {
            DragDrop.DoDragDrop(RunningAppsList, selectedApps, DragDropEffects.Copy);
        }
    }

    private void BlockedAppsPanel_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(List<RunningAppInfo>))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void BlockedAppsPanel_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(List<RunningAppInfo>)) is not List<RunningAppInfo> apps)
        {
            return;
        }

        AddRunningApps(apps);
    }

    private void AddSelectedRunningApps()
    {
        AddRunningApps(RunningAppsList.SelectedItems.Cast<RunningAppInfo>());
    }

    private void AddRunningApps(IEnumerable<RunningAppInfo> runningApps)
    {
        List<TargetApp> targetApps = runningApps
            .Select(app => new TargetApp
            {
                ProcessName = app.ProcessName,
                DisplayName = app.DisplayText
            })
            .ToList();

        if (targetApps.Count == 0)
        {
            return;
        }

        AppDataStore.AddTargetApps(targetApps);
        BlockedAppsList.ItemsSource = AppDataStore.GetTargetApps();
    }
}
