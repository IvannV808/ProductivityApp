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

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProductivityApp");

    public static string TargetAppsPath { get; } = Path.Combine(DataDirectory, "target_block_apps.db");

    public static string ProductivityDataPath { get; } = Path.Combine(DataDirectory, "productivity_metrics.db");

    private static string LegacyTargetAppsPath { get; } = Path.Combine(AppContext.BaseDirectory, "target_block_apps.db");

    private static string LegacyProductivityDataPath { get; } = Path.Combine(AppContext.BaseDirectory, "productivity_metrics.db");

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
                if (string.IsNullOrWhiteSpace(app.ProcessName) ||
                    !AppBlockRules.IsAllowedBlockingCandidate(app) ||
                    !existing.Add(app.ProcessName))
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

    public static void RemoveTargetApps(IEnumerable<string> processNames)
    {
        lock (SyncLock)
        {
            HashSet<string> processNameSet = processNames
                .Where(processName => !string.IsNullOrWhiteSpace(processName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (processNameSet.Count == 0)
            {
                return;
            }

            TargetAppsDatabase database = LoadTargetAppsDatabase();
            database.Apps.RemoveAll(app => processNameSet.Contains(app.ProcessName));
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

    public static void UpdateScheduledBlock(ScheduledBlock block)
    {
        lock (SyncLock)
        {
            ProductivityDatabase database = LoadProductivityDatabase();
            int index = database.ScheduledBlocks.FindIndex(existing =>
                string.Equals(existing.Id, block.Id, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                database.ScheduledBlocks[index] = block;
                Save(ProductivityDataPath, database);
            }
        }
    }

    public static void DeleteScheduledBlock(string blockId)
    {
        lock (SyncLock)
        {
            ProductivityDatabase database = LoadProductivityDatabase();
            database.ScheduledBlocks.RemoveAll(block =>
                string.Equals(block.Id, blockId, StringComparison.OrdinalIgnoreCase));
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

    public static IReadOnlyList<AppUsageSession> GetUsageSessions()
    {
        lock (SyncLock)
        {
            return LoadProductivityDatabase().UsageSessions
                .OrderByDescending(entry => entry.StartAt)
                .ToList();
        }
    }

    public static void ClearUsageSessions()
    {
        lock (SyncLock)
        {
            ProductivityDatabase database = LoadProductivityDatabase();
            database.UsageSessions.Clear();
            Save(ProductivityDataPath, database);
        }
    }

    public static void UpsertUsageSession(AppUsageSession session)
    {
        lock (SyncLock)
        {
            ProductivityDatabase database = LoadProductivityDatabase();
            int index = database.UsageSessions.FindIndex(existing =>
                string.Equals(existing.Id, session.Id, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                database.UsageSessions[index] = session;
            }
            else
            {
                database.UsageSessions.Add(session);
            }

            Save(ProductivityDataPath, database);
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
            Directory.CreateDirectory(DataDirectory);
            MigrateLegacyDatabase(LegacyTargetAppsPath, TargetAppsPath);
            MigrateLegacyDatabase(LegacyProductivityDataPath, ProductivityDataPath);

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

    private static void MigrateLegacyDatabase(string legacyPath, string newPath)
    {
        if (File.Exists(newPath) || !File.Exists(legacyPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(newPath) ?? DataDirectory);
        File.Copy(legacyPath, newPath);
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
