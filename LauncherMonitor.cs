using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DNFProcessManager;

public sealed class LauncherMonitor(
    ILogger<LauncherMonitor> logger,
    IOptionsMonitor<ManagerOptions> options) : BackgroundService
{
    private static readonly TimeSpan GameStartTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly Dictionary<LauncherKey, LauncherState> launchers = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wasEnabled = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var current = options.CurrentValue;
                if (!current.CloseLauncherIfGameNotStarted)
                {
                    if (wasEnabled)
                    {
                        logger.LogInformation("DNF launcher timeout monitor disabled by configuration");
                    }

                    wasEnabled = false;
                    launchers.Clear();
                }
                else
                {
                    if (!wasEnabled)
                    {
                        logger.LogInformation("DNF launcher timeout monitor enabled by configuration");
                    }

                    wasEnabled = true;
                    InspectLaunchers(current.ProcessName);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "DNF launcher timeout monitor iteration failed; it will retry");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private void InspectLaunchers(string gameProcessName)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = FindDnfLauncherProcesses();
        var activeKeys = candidates.Select(x => x.Key).ToHashSet();

        foreach (var staleKey in launchers.Keys.Where(x => !activeKeys.Contains(x)).ToArray())
        {
            launchers.Remove(staleKey);
        }

        foreach (var group in candidates.GroupBy(x => x.Key))
        {
            if (!launchers.TryGetValue(group.Key, out var state))
            {
                var startedAt = group.Min(x => x.StartedAt);
                state = new LauncherState(startedAt);
                launchers.Add(group.Key, state);
                logger.LogInformation(
                    "Detected DNF launcher in user session {SessionId}: {Path}",
                    group.Key.SessionId,
                    group.Key.ExecutablePath);
            }

            if (IsProcessRunningInSession(gameProcessName, group.Key.SessionId))
            {
                state.GameWasObserved = true;
            }

            if (state.CloseRequested ||
                state.GameWasObserved ||
                now < state.NextCloseAttemptAt ||
                now - state.StartedAt < GameStartTimeout)
            {
                continue;
            }

            var closeRequested = RequestGracefulClose(
                group.Select(x => x.ProcessId),
                group.Key.SessionId);

            if (closeRequested)
            {
                state.CloseRequested = true;
                logger.LogInformation(
                    "Requested graceful close of DNF launcher in user session {SessionId} because {GameProcessName} did not start within one minute: {Path}",
                    group.Key.SessionId,
                    NormalizeProcessName(gameProcessName),
                    group.Key.ExecutablePath);
            }
            else
            {
                state.NextCloseAttemptAt = now.AddSeconds(10);
                logger.LogWarning(
                    "DNF launcher exceeded the one-minute game-start timeout, but the graceful close request could not be dispatched in user session {SessionId}; it will retry: {Path}",
                    group.Key.SessionId,
                    group.Key.ExecutablePath);
            }
        }
    }

    private static List<LauncherProcess> FindDnfLauncherProcesses()
    {
        var result = new List<LauncherProcess>();
        var identifiedPaths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcessesByName("client"))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId <= 0 || process.HasExited)
                    {
                        continue;
                    }

                    var executablePath = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(executablePath))
                    {
                        continue;
                    }

                    var fullPath = Path.GetFullPath(executablePath);
                    if (!identifiedPaths.TryGetValue(fullPath, out var isDnfLauncher))
                    {
                        isDnfLauncher = IsDnfLauncherExecutable(fullPath);
                        identifiedPaths.Add(fullPath, isDnfLauncher);
                    }

                    if (!isDnfLauncher)
                    {
                        continue;
                    }

                    result.Add(new LauncherProcess(
                        new LauncherKey(fullPath, process.SessionId),
                        process.Id,
                        new DateTimeOffset(process.StartTime.ToUniversalTime())));
                }
                catch
                {
                    // The process may exit or deny inspection while the snapshot is built.
                }
            }
        }

        return result;
    }

    internal static bool IsDnfLauncherExecutable(string executablePath)
    {
        try
        {
            if (!Path.GetFileName(executablePath).Equals("client.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
            if (directory is null ||
                !File.Exists(Path.Combine(directory, "resources", "app.asar")) ||
                !File.Exists(Path.Combine(directory, "uninstall.json")))
            {
                return false;
            }

            var manifestPath = Path.Combine(directory, "filelist.json");
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 1024 * 1024)
            {
                return false;
            }

            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!manifest.RootElement.TryGetProperty("filelist", out var files) ||
                files.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return files.EnumerateArray().Any(IsDnfDownloadEntry);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDnfDownloadEntry(JsonElement entry)
    {
        foreach (var propertyName in new[] { "url", "bkurl", "curl", "bkcurl" })
        {
            if (entry.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) &&
                uri.Host.Equals("dnf.gcloudcdn.qq.com", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool RequestGracefulClose(IEnumerable<int> processIds, int sessionId)
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            var arguments = string.Join(' ', processIds.Select(x => x.ToString()));
            if (arguments.Length == 0)
            {
                return false;
            }

            return PInvoke.StartInteractiveProcess(
                executablePath,
                sessionId,
                logger,
                $"--close-launcher-window {arguments}").HasValue;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessRunningInSession(string configuredName, int sessionId)
    {
        var processes = Process.GetProcessesByName(NormalizeProcessName(configuredName));
        try
        {
            return processes.Any(process =>
            {
                try
                {
                    return process.SessionId == sessionId && !process.HasExited;
                }
                catch
                {
                    return false;
                }
            });
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string NormalizeProcessName(string configuredName) =>
        Path.GetFileNameWithoutExtension(configuredName.Trim());

    private sealed record LauncherKey(string ExecutablePath, int SessionId);
    private sealed record LauncherProcess(LauncherKey Key, int ProcessId, DateTimeOffset StartedAt);

    private sealed class LauncherState(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;
        public DateTimeOffset NextCloseAttemptAt { get; set; }
        public bool GameWasObserved { get; set; }
        public bool CloseRequested { get; set; }
    }
}
