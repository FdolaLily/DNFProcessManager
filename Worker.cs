using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace DNFProcessManager;

public sealed class Worker(
    ILogger<Worker> logger,
    IOptionsMonitor<ManagerOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Process manager started");

        while (!stoppingToken.IsCancellationRequested)
        {
            DetectedGame? detectedGame = null;
            try
            {
                detectedGame = await WaitForGameStartAsync(stoppingToken);
                await MonitorGameLifecycleAsync(detectedGame, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Process manager iteration failed; it will retry");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            finally
            {
                detectedGame?.Process.Dispose();
            }
        }
    }

    private async Task<DetectedGame> WaitForGameStartAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var current = options.CurrentValue;
            var normalizedName = NormalizeProcessName(current.ProcessName);
            var process = FindGameProcess(normalizedName);
            if (process is not null)
            {
                return new DetectedGame(process, normalizedName, process.Id, process.SessionId);
            }

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(1, current.ProcessPollSeconds)),
                cancellationToken);
        }
    }

    private async Task MonitorGameLifecycleAsync(
        DetectedGame game,
        CancellationToken serviceCancellationToken)
    {
        var startupOptions = options.CurrentValue;
        logger.LogInformation(
            "Detected {ProcessName} ({ProcessId}) startup in user session {SessionId}",
            game.ProcessName,
            game.ProcessId,
            game.SessionId);

        using var lifecycleCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(serviceCancellationToken);
        var actionTask = RunGameStartActionsAsync(
            startupOptions,
            game.SessionId,
            lifecycleCancellation.Token);
        var priorityTask = MaintainGamePriorityAsync(
            game.Process,
            game.ProcessName,
            game.ProcessId,
            lifecycleCancellation.Token);

        var gameExited = false;
        try
        {
            await WaitForGameExitAsync(game, serviceCancellationToken);
            gameExited = true;
            logger.LogInformation(
                "Detected {ProcessName} ({ProcessId}) exit from user session {SessionId}",
                game.ProcessName,
                game.ProcessId,
                game.SessionId);
        }
        finally
        {
            lifecycleCancellation.Cancel();
            await Task.WhenAll(actionTask, priorityTask);
        }

        if (gameExited && !IsProcessRunningInSession(game.ProcessName, game.SessionId))
        {
            StopConfiguredApplications(options.CurrentValue.AutoStop, game.SessionId);
        }
    }

    private async Task WaitForGameExitAsync(
        DetectedGame game,
        CancellationToken cancellationToken)
    {
        try
        {
            await game.Process.WaitForExitAsync(cancellationToken);
        }
        catch (Win32Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not wait on {ProcessName} ({ProcessId}) directly; falling back to exit polling",
                game.ProcessName,
                game.ProcessId);
            await WaitForGameExitByPollingAsync(game, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            await WaitForGameExitByPollingAsync(game, cancellationToken);
        }
    }

    private async Task WaitForGameExitByPollingAsync(
        DetectedGame game,
        CancellationToken cancellationToken)
    {
        while (IsProcessRunningInSession(
                   game.ProcessName,
                   game.SessionId,
                   game.ProcessId))
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private async Task RunGameStartActionsAsync(
        ManagerOptions current,
        int gameSessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await StartConfiguredApplicationsAsync(
                current.AutoStart,
                gameSessionId,
                cancellationToken);

            if (current.ActionDelaySeconds > 0)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(current.ActionDelaySeconds),
                    cancellationToken);
            }

            KillConfiguredProcesses(current.KillList);
            LimitConfiguredProcesses(current.LimitList);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Cancelled delayed game-start actions because the game or service stopped");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Game-start actions failed");
        }
    }

    private async Task MaintainGamePriorityAsync(
        Process gameProcess,
        string processName,
        int processId,
        CancellationToken cancellationToken)
    {
        var successLogged = false;
        var failureLogged = false;

        try
        {
            var intervalSeconds = Math.Max(1, options.CurrentValue.ProcessPollSeconds);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

            while (!cancellationToken.IsCancellationRequested)
            {
                var current = options.CurrentValue;
                if (current.OptimizeGamePriority &&
                    Enum.TryParse<ProcessPriorityClass>(
                        current.GamePriority,
                        ignoreCase: true,
                        out var targetPriority) &&
                    targetPriority is ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal)
                {
                    try
                    {
                        var previousPriority = gameProcess.PriorityClass;
                        if (previousPriority != targetPriority)
                        {
                            gameProcess.PriorityClass = targetPriority;
                            gameProcess.Refresh();
                            if (gameProcess.PriorityClass != targetPriority)
                            {
                                throw new InvalidDataException(
                                    $"The process reported priority {gameProcess.PriorityClass} after the change.");
                            }
                        }

                        failureLogged = false;
                        if (!successLogged)
                        {
                            successLogged = true;
                            logger.LogInformation(
                                "Game priority is {Priority} for {ProcessName} ({ProcessId}); previous priority was {PreviousPriority}",
                                targetPriority,
                                processName,
                                processId,
                                previousPriority);
                        }
                        else if (previousPriority != targetPriority)
                        {
                            logger.LogDebug(
                                "Restored game priority {Priority} for {ProcessName} ({ProcessId})",
                                targetPriority,
                                processName,
                                processId);
                        }
                    }
                    catch (InvalidOperationException) when (!cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        if (!failureLogged)
                        {
                            failureLogged = true;
                            logger.LogWarning(
                                exception,
                                "Could not set game priority for {ProcessName} ({ProcessId}); the game will continue normally",
                                processName,
                                processId);
                        }
                    }
                }

                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the game or service stops.
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Game priority monitoring stopped for {ProcessName} ({ProcessId}); game exit monitoring remains active",
                processName,
                processId);
        }
    }

    private async Task StartConfiguredApplicationsAsync(
        IEnumerable<string> applicationPaths,
        int gameSessionId,
        CancellationToken cancellationToken)
    {
        foreach (var configuredPath in applicationPaths.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string applicationPath;
            try
            {
                var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
                applicationPath = Path.IsPathFullyQualified(expandedPath)
                    ? Path.GetFullPath(expandedPath)
                    : Path.GetFullPath(expandedPath, AppContext.BaseDirectory);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Invalid AutoStart path: {Path}", configuredPath);
                continue;
            }

            if (!File.Exists(applicationPath))
            {
                logger.LogWarning("AutoStart file does not exist: {Path}", applicationPath);
                continue;
            }

            var processName = Path.GetFileNameWithoutExtension(applicationPath);
            if (IsProcessRunningInSession(processName, gameSessionId))
            {
                logger.LogDebug(
                    "AutoStart process is already running in session {SessionId}: {ProcessName}",
                    gameSessionId,
                    processName);
                continue;
            }

            try
            {
                var startedProcessId = PInvoke.StartInteractiveProcess(
                    applicationPath,
                    gameSessionId,
                    logger);
                if (!startedProcessId.HasValue)
                {
                    continue;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                if (IsProcessRunningInSession(processName, gameSessionId))
                {
                    logger.LogInformation(
                        "Started interactive application {Path} as process {ProcessId} in user session {SessionId}",
                        applicationPath,
                        startedProcessId.Value,
                        gameSessionId);
                }
                else
                {
                    logger.LogWarning(
                        "Interactive application {Path} was created as process {ProcessId} in session {SessionId}, but exited immediately",
                        applicationPath,
                        startedProcessId.Value,
                        gameSessionId);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Failed to start interactive application: {Path}", applicationPath);
            }
        }
    }

    private void KillConfiguredProcesses(IEnumerable<string> processNames)
    {
        foreach (var configuredName in processNames.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var processName = NormalizeProcessName(configuredName);
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var processId = process.Id;
                        process.Kill(entireProcessTree: false);
                        if (process.WaitForExit(milliseconds: 5000))
                        {
                            logger.LogInformation(
                                "Stopped process {ProcessName} ({ProcessId})",
                                processName,
                                processId);
                        }
                        else
                        {
                            logger.LogWarning(
                                "Process {ProcessName} ({ProcessId}) did not exit within 5 seconds",
                                processName,
                                processId);
                        }
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(
                            exception,
                            "Could not stop process {ProcessName} ({ProcessId})",
                            processName,
                            process.Id);
                    }
                }
            }
        }
    }

    private void LimitConfiguredProcesses(IEnumerable<string> processNames)
    {
        foreach (var configuredName in processNames.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var processName = NormalizeProcessName(configuredName);
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        SetLastAllowedCpuAffinity(process);
                        PInvoke.SetIoPriority(process.Id, logger);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(
                            exception,
                            "Could not limit process {ProcessName} ({ProcessId})",
                            processName,
                            process.Id);
                    }
                }
            }
        }
    }

    private void StopConfiguredApplications(
        IEnumerable<string> processNames,
        int gameSessionId)
    {
        foreach (var configuredName in processNames.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var processName = NormalizeProcessName(configuredName);
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != gameSessionId)
                        {
                            continue;
                        }

                        var processId = process.Id;
                        process.Kill(entireProcessTree: false);
                        if (process.WaitForExit(milliseconds: 5000))
                        {
                            logger.LogInformation(
                                "Stopped auto-start application {ProcessName} ({ProcessId}) after the game exited",
                                processName,
                                processId);
                        }
                        else
                        {
                            logger.LogWarning(
                                "Auto-start application {ProcessName} ({ProcessId}) did not exit within 5 seconds",
                                processName,
                                processId);
                        }
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(
                            exception,
                            "Could not stop auto-start application {ProcessName} ({ProcessId}) after the game exited",
                            processName,
                            process.Id);
                    }
                }
            }
        }
    }

    private static void SetLastAllowedCpuAffinity(Process process)
    {
        var affinity = unchecked((ulong)process.ProcessorAffinity.ToInt64());
        if (affinity == 0)
        {
            return;
        }

        var highestBit = 63 - System.Numerics.BitOperations.LeadingZeroCount(affinity);
        process.ProcessorAffinity = new IntPtr(unchecked((long)(1UL << highestBit)));
    }

    private static Process? FindGameProcess(string normalizedName)
    {
        var processes = Process.GetProcessesByName(normalizedName);
        Process? selected = null;

        foreach (var process in processes)
        {
            if (selected is not null)
            {
                process.Dispose();
                continue;
            }

            try
            {
                if (process.SessionId > 0 && !process.HasExited)
                {
                    selected = process;
                }
                else
                {
                    process.Dispose();
                }
            }
            catch
            {
                process.Dispose();
            }
        }

        return selected;
    }

    private static bool IsProcessRunningInSession(
        string configuredName,
        int sessionId,
        int? requiredProcessId = null)
    {
        var processes = Process.GetProcessesByName(NormalizeProcessName(configuredName));
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId == sessionId &&
                        (!requiredProcessId.HasValue || process.Id == requiredProcessId.Value))
                    {
                        return true;
                    }
                }
                catch
                {
                    // The process may exit while it is being inspected.
                }
            }

            return false;
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

    private sealed record DetectedGame(
        Process Process,
        string ProcessName,
        int ProcessId,
        int SessionId);
}
