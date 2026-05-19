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

    public string DisplayText => DisplayName;

    public static IReadOnlyList<RunningAppInfo> GetRunningApps()
    {
        int currentProcessId = Environment.ProcessId;
        using Process currentProcess = Process.GetCurrentProcess();
        string currentProcessName = currentProcess.ProcessName;
        string appAssemblyName = typeof(RunningAppInfo).Assembly.GetName().Name ?? "ProductivityApp";

        return Process.GetProcesses()
            .Where(process => process.Id != currentProcessId)
            .Select(TryCreate)
            .Where(app => app is not null)
            .Cast<RunningAppInfo>()
            .Where(app => !string.Equals(app.ProcessName, currentProcessName, StringComparison.OrdinalIgnoreCase))
            .Where(app => !string.Equals(app.ProcessName, appAssemblyName, StringComparison.OrdinalIgnoreCase))
            .Where(app => !string.Equals(app.DisplayName, "Productivity App", StringComparison.OrdinalIgnoreCase))
            .Where(app => !string.Equals(app.DisplayName, "ProductivityApp", StringComparison.OrdinalIgnoreCase))
            .Where(AppBlockRules.IsAllowedBlockingCandidate)
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
                DisplayName = RunningAppNameResolver.GetFriendlyDisplayName(process, processName)
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

public static class RunningAppNameResolver
{
    private static readonly IReadOnlyDictionary<string, string> KnownDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome",
        ["msedge"] = "Microsoft Edge",
        ["firefox"] = "Firefox",
        ["notepad"] = "Notepad",
        ["mspaint"] = "Paint",
        ["discord"] = "Discord",
        ["spotify"] = "Spotify",
        ["steam"] = "Steam",
        ["teams"] = "Microsoft Teams",
        ["devenv"] = "Microsoft Visual Studio",
        ["Code"] = "Visual Studio Code",
        ["WinStore.App"] = "Microsoft Store",
        ["TextInputHost"] = "Windows Input Experience",
        ["steamwebhelper"] = "Steam",
        ["PhoneExperienceHost"] = "Phone Link"
    };

    public static string GetFriendlyDisplayName(Process process, string processName)
    {
        if (KnownDisplayNames.TryGetValue(processName, out string? knownDisplayName))
        {
            return knownDisplayName;
        }

        try
        {
            FileVersionInfo? versionInfo = process.MainModule?.FileVersionInfo;
            if (versionInfo is not null)
            {
                string? displayName = FirstUsefulName(versionInfo.ProductName, versionInfo.FileDescription);
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return CleanDisplayName(displayName);
                }
            }
        }
        catch
        {
        }

        return CleanDisplayName(processName);
    }

    private static string? FirstUsefulName(params string?[] names)
    {
        return names.FirstOrDefault(name =>
            !string.IsNullOrWhiteSpace(name) &&
            !name.Contains(".exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "Electron", StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanDisplayName(string name)
    {
        string cleaned = name.Trim();
        return cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? cleaned[..^4]
            : cleaned;
    }
}

public static class AppBlockRules
{
    private static readonly string[] BlockedDisplayNames =
    [
        "Microsoft Windows Operating System",
        "Windows Operating System"
    ];

    public static bool IsAllowedBlockingCandidate(RunningAppInfo app) =>
        IsAllowedBlockingCandidate(app.ProcessName, app.DisplayName);

    public static bool IsAllowedBlockingCandidate(TargetApp app) =>
        IsAllowedBlockingCandidate(app.ProcessName, app.DisplayName);

    public static bool IsAllowedBlockingCandidate(string processName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        return !BlockedDisplayNames.Any(blocked =>
            string.Equals(displayName, blocked, StringComparison.OrdinalIgnoreCase));
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

public sealed class BlockColorOption
{
    public string Name { get; init; } = string.Empty;
    public string Hex { get; init; } = string.Empty;

    public string DisplayText => Name;

    public static IReadOnlyList<BlockColorOption> All { get; } =
    [
        new() { Name = "Blue", Hex = "#8EC5FF" },
        new() { Name = "Green", Hex = "#A7D8A0" },
        new() { Name = "Orange", Hex = "#F6C177" },
        new() { Name = "Purple", Hex = "#D9B8FF" },
        new() { Name = "Red", Hex = "#FFAAA5" },
        new() { Name = "Teal", Hex = "#85DCCF" },
        new() { Name = "Yellow", Hex = "#F5E27A" },
        new() { Name = "Indigo", Hex = "#B4C6FF" }
    ];
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

public sealed class AppUsageSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }

    public int DurationSeconds => Math.Max(0, (int)(EndAt - StartAt).TotalSeconds);
}

public sealed class TargetAppsDatabase
{
    public List<TargetApp> Apps { get; set; } = [];
}

public sealed class ProductivityDatabase
{
    public List<ScheduledBlock> ScheduledBlocks { get; set; } = [];
    public List<AppClosureEvent> ClosureEvents { get; set; } = [];
    public List<AppUsageSession> UsageSessions { get; set; } = [];
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
