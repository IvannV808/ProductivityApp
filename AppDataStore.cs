using System.IO;
using System.Text.Json;

namespace ProductivityApp;

public static class AppDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly object SyncLock = new();

    public static string TargetAppsPath { get; } = Path.Combine(AppContext.BaseDirectory, "target_block_apps.db");

    public static string ProductivityDataPath { get; } = Path.Combine(AppContext.BaseDirectory, "productivity_metrics.db");

    public static IReadOnlyList<TargetApp> GetTargetApps()
    {
        lock (SyncLock)
        {
            return LoadTargetAppsDatabase().Apps
                .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public static void AddTargetApps(IEnumerable<TargetApp> apps)
    {
        lock (SyncLock)
        {
            TargetAppsDatabase database = LoadTargetAppsDatabase();
            HashSet<string> existing = database.Apps
                .Select(app => app.ProcessName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (TargetApp app in apps)
            {
                if (string.IsNullOrWhiteSpace(app.ProcessName) || !existing.Add(app.ProcessName))
                {
                    continue;
                }

                database.Apps.Add(new TargetApp
                {
                    ProcessName = app.ProcessName,
                    DisplayName = string.IsNullOrWhiteSpace(app.DisplayName) ? app.ProcessName : app.DisplayName,
                    AddedAt = DateTime.Now
                });
            }

            Save(TargetAppsPath, database);
        }
    }

    public static IReadOnlyList<ScheduledBlock> GetScheduledBlocks()
    {
        lock (SyncLock)
        {
            return LoadProductivityDatabase().ScheduledBlocks
                .OrderBy(block => block.Day)
                .ThenBy(block => block.StartMinutes)
                .ToList();
        }
    }

    public static void AddScheduledBlock(ScheduledBlock block)
    {
        lock (SyncLock)
        {
            ProductivityDatabase database = LoadProductivityDatabase();
            database.ScheduledBlocks.Add(block);
            Save(ProductivityDataPath, database);
        }
    }

    public static IReadOnlyList<AppClosureEvent> GetClosureEvents()
    {
        lock (SyncLock)
        {
            return LoadProductivityDatabase().ClosureEvents
                .OrderByDescending(entry => entry.ClosedAt)
                .ToList();
        }
    }

    public static void RecordClosure(AppClosureEvent closureEvent)
    {
        lock (SyncLock)
        {
            ProductivityDatabase database = LoadProductivityDatabase();
            database.ClosureEvents.Add(closureEvent);
            Save(ProductivityDataPath, database);
        }
    }

    public static void EnsureDatabasesExist()
    {
        lock (SyncLock)
        {
            if (!File.Exists(TargetAppsPath))
            {
                Save(TargetAppsPath, new TargetAppsDatabase());
            }

            if (!File.Exists(ProductivityDataPath))
            {
                Save(ProductivityDataPath, new ProductivityDatabase());
            }
        }
    }

    private static TargetAppsDatabase LoadTargetAppsDatabase() =>
        Load(TargetAppsPath, new TargetAppsDatabase());

    private static ProductivityDatabase LoadProductivityDatabase() =>
        Load(ProductivityDataPath, new ProductivityDatabase());

    private static T Load<T>(string path, T fallback)
    {
        if (!File.Exists(path))
        {
            Save(path, fallback);
            return fallback;
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }
}
