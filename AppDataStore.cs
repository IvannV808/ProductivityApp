using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ProductivityApp;

public static class AppDataStore
{
    private const int PasswordHashIterations = 210_000;
    private const int PasswordSaltLength = 16;
    private const int PasswordHashLength = 32;

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
                .Select(NormalizeTargetApp)
                .Where(AppBlockRules.IsAllowedBlockingCandidate)
                .GroupBy(app => app.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase).First())
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
                TargetApp normalizedApp = NormalizeTargetApp(app);
                if (string.IsNullOrWhiteSpace(normalizedApp.ProcessName) ||
                    !AppBlockRules.IsAllowedBlockingCandidate(normalizedApp) ||
                    !existing.Add(normalizedApp.ProcessName))
                {
                    continue;
                }

                database.Apps.Add(new TargetApp
                {
                    ProcessName = normalizedApp.ProcessName,
                    DisplayName = string.IsNullOrWhiteSpace(normalizedApp.DisplayName) ? normalizedApp.ProcessName : normalizedApp.DisplayName,
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

    public static bool HasMasterPassword()
    {
        lock (SyncLock)
        {
            return LoadProductivityDatabase().MasterPassword is not null;
        }
    }

    public static void SetMasterPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(PasswordSaltLength);
        byte[] hash = HashPassword(password, salt, PasswordHashIterations);

        lock (SyncLock)
        {
            ProductivityDatabase database = LoadProductivityDatabase();
            database.MasterPassword = new MasterPasswordCredential
            {
                Salt = Convert.ToBase64String(salt),
                Hash = Convert.ToBase64String(hash),
                Iterations = PasswordHashIterations,
                UpdatedAt = DateTime.Now
            };
            Save(ProductivityDataPath, database);
        }
    }

    public static void ClearMasterPassword()
    {
        lock (SyncLock)
        {
            ProductivityDatabase database = LoadProductivityDatabase();
            database.MasterPassword = null;
            Save(ProductivityDataPath, database);
        }
    }

    public static bool ValidateMasterPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        lock (SyncLock)
        {
            MasterPasswordCredential? credential = LoadProductivityDatabase().MasterPassword;
            if (credential is null ||
                string.IsNullOrWhiteSpace(credential.Salt) ||
                string.IsNullOrWhiteSpace(credential.Hash) ||
                credential.Iterations <= 0)
            {
                return false;
            }

            try
            {
                byte[] salt = Convert.FromBase64String(credential.Salt);
                byte[] expectedHash = Convert.FromBase64String(credential.Hash);
                byte[] actualHash = HashPassword(password, salt, credential.Iterations);
                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                return false;
            }
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
            else
            {
                NormalizeTargetAppsDatabase();
            }

            if (!File.Exists(ProductivityDataPath))
            {
                Save(ProductivityDataPath, new ProductivityDatabase());
            }
            else
            {
                NormalizeProductivityDatabase();
            }
        }
    }

    private static void NormalizeTargetAppsDatabase()
    {
        TargetAppsDatabase database = LoadTargetAppsDatabase();
        List<TargetApp> normalizedApps = database.Apps
            .Select(NormalizeTargetApp)
            .Where(AppBlockRules.IsAllowedBlockingCandidate)
            .GroupBy(app => app.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase).First())
            .ToList();

        if (!TargetAppsEqual(database.Apps, normalizedApps))
        {
            database.Apps = normalizedApps;
            Save(TargetAppsPath, database);
        }
    }

    private static void NormalizeProductivityDatabase()
    {
        ProductivityDatabase database = LoadProductivityDatabase();
        bool changed = false;

        foreach (ScheduledBlock block in database.ScheduledBlocks)
        {
            List<string> normalizedProcesses = block.TargetProcessNames
                .Where(processName => !string.IsNullOrWhiteSpace(processName))
                .Select(AppBlockRules.NormalizeTargetProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!block.TargetProcessNames.SequenceEqual(normalizedProcesses, StringComparer.OrdinalIgnoreCase))
            {
                block.TargetProcessNames = normalizedProcesses;
                changed = true;
            }
        }

        if (changed)
        {
            Save(ProductivityDataPath, database);
        }
    }

    private static TargetApp NormalizeTargetApp(TargetApp app)
    {
        string processName = AppBlockRules.NormalizeTargetProcessName(app.ProcessName);
        string displayName = RunningAppNameResolver.GetFriendlyDisplayName(processName, app.DisplayName);

        return new TargetApp
        {
            ProcessName = processName,
            DisplayName = displayName,
            AddedAt = app.AddedAt
        };
    }

    private static bool TargetAppsEqual(IReadOnlyList<TargetApp> first, IReadOnlyList<TargetApp> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (int index = 0; index < first.Count; index++)
        {
            if (!string.Equals(first[index].ProcessName, second[index].ProcessName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(first[index].DisplayName, second[index].DisplayName, StringComparison.Ordinal) ||
                first[index].AddedAt != second[index].AddedAt)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] HashPassword(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            PasswordHashLength);

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
