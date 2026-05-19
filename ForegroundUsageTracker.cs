using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ProductivityApp;

public sealed class ForegroundUsageTracker
{
    private readonly string _currentProcessName;
    private AppUsageSession? _currentSession;

    public ForegroundUsageTracker()
    {
        using Process currentProcess = Process.GetCurrentProcess();
        _currentProcessName = currentProcess.ProcessName;
    }

    public void Sample()
    {
        ForegroundApp? foregroundApp = GetForegroundApp();
        DateTime now = DateTime.Now;

        if (foregroundApp is null || IsProductivityApp(foregroundApp))
        {
            FinishCurrentSession(now);
            return;
        }

        TargetApp? trackedTarget = AppDataStore.GetTargetApps()
            .FirstOrDefault(app => string.Equals(app.ProcessName, foregroundApp.ProcessName, StringComparison.OrdinalIgnoreCase));

        if (trackedTarget is null)
        {
            FinishCurrentSession(now);
            return;
        }

        DateOnly today = DateOnly.FromDateTime(now);
        if (_currentSession is null ||
            _currentSession.Date != today ||
            !string.Equals(_currentSession.ProcessName, foregroundApp.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            FinishCurrentSession(now);
            _currentSession = new AppUsageSession
            {
                ProcessName = foregroundApp.ProcessName,
                DisplayName = trackedTarget.DisplayName,
                Date = today,
                StartAt = now,
                EndAt = now
            };
        }

        _currentSession.EndAt = now;
        AppDataStore.UpsertUsageSession(_currentSession);
    }

    public void FinishCurrentSession(DateTime? endAt = null)
    {
        if (_currentSession is null)
        {
            return;
        }

        _currentSession.EndAt = endAt ?? DateTime.Now;
        AppDataStore.UpsertUsageSession(_currentSession);
        _currentSession = null;
    }

    private bool IsProductivityApp(ForegroundApp app)
    {
        return string.Equals(app.ProcessName, _currentProcessName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(app.ProcessName, "ProductivityApp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(app.DisplayName, "Productivity App", StringComparison.OrdinalIgnoreCase);
    }

    private static ForegroundApp? GetForegroundApp()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(foregroundWindow, out uint processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            string processName = process.ProcessName;
            string displayName = GetFriendlyDisplayName(process, processName, foregroundWindow);

            if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(displayName))
            {
                return null;
            }

            return new ForegroundApp(processName, displayName);
        }
        catch
        {
            return null;
        }
    }

    private static string GetFriendlyDisplayName(Process process, string processName, IntPtr foregroundWindow)
    {
        string titleFallback = GetWindowTitle(foregroundWindow);
        string friendlyName = RunningAppNameResolver.GetFriendlyDisplayName(process, processName);
        return string.IsNullOrWhiteSpace(friendlyName) ? titleFallback : friendlyName;
    }

    private static string GetWindowTitle(IntPtr windowHandle)
    {
        int length = GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new(length + 1);
        _ = GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}

public sealed record ForegroundApp(string ProcessName, string DisplayName);
