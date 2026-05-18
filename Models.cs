using System.Diagnostics;

namespace ProductivityApp;

public sealed class TargetApp
{
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.Now;
}

public sealed class RunningAppInfo
{
    public string ProcessName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    public string DisplayText => string.Equals(ProcessName, DisplayName, StringComparison.OrdinalIgnoreCase)
        ? ProcessName
        : $"{DisplayName} ({ProcessName})";

    public static IReadOnlyList<RunningAppInfo> GetRunningApps()
    {
        int currentProcessId = Environment.ProcessId;
        using Process currentProcess = Process.GetCurrentProcess();
        string currentProcessName = currentProcess.ProcessName;

        return Process.GetProcesses()
            .Where(process => process.Id != currentProcessId)
            .Select(TryCreate)
            .Where(app => app is not null)
            .Cast<RunningAppInfo>()
            .Where(app => !string.Equals(app.ProcessName, currentProcessName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(app => app.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(app => app.DisplayName).First())
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RunningAppInfo? TryCreate(Process process)
    {
        try
        {
            string processName = process.ProcessName;
            string title = process.MainWindowTitle;

            if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            return new RunningAppInfo
            {
                ProcessName = processName,
                DisplayName = title
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            process.Dispose();
        }
    }
}

public sealed class ScheduledBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DayOfWeek Day { get; set; }
    public int StartMinutes { get; set; }
    public int EndMinutes { get; set; }
    public List<string> TargetProcessNames { get; set; } = [];
    public string ColorHex { get; set; } = "#7BA7FF";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string TimeRangeText => $"{TimeHelpers.FormatMinutes(StartMinutes)} - {TimeHelpers.FormatMinutes(EndMinutes)}";
}

public sealed class AppClosureEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ScheduleBlockId { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DayOfWeek Day { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public DateTime DetectedAt { get; set; }
    public DateTime ClosedAt { get; set; }
}

public sealed class TargetAppsDatabase
{
    public List<TargetApp> Apps { get; set; } = [];
}

public sealed class ProductivityDatabase
{
    public List<ScheduledBlock> ScheduledBlocks { get; set; } = [];
    public List<AppClosureEvent> ClosureEvents { get; set; } = [];
}

public static class TimeHelpers
{
    public static string FormatMinutes(int minutes)
    {
        minutes = Math.Clamp(minutes, 0, 24 * 60);
        if (minutes == 24 * 60)
        {
            return "12:00 AM";
        }

        TimeOnly time = new(minutes / 60, minutes % 60);
        return time.ToString("h:mm tt");
    }

    public static bool TryParseTime(string value, out int minutes)
    {
        minutes = 0;

        if (!TimeOnly.TryParse(value, out TimeOnly time))
        {
            return false;
        }

        minutes = (time.Hour * 60) + time.Minute;
        return minutes >= 0 && minutes <= 24 * 60;
    }
}
