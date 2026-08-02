using System.Diagnostics;

namespace DNFProcessManager;

public static class LauncherCloseHelper
{
    public static bool TryHandle(string[] arguments, out int exitCode)
    {
        exitCode = 1;
        if (arguments.Length < 2 ||
            !arguments[0].Equals("--close-launcher-window", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var closeRequested = false;
        foreach (var value in arguments.Skip(1))
        {
            if (!int.TryParse(value, out var processId) || processId <= 0)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                closeRequested |= !process.HasExited && process.CloseMainWindow();
            }
            catch
            {
                // The launcher process may have exited before the helper inspected it.
            }
        }

        exitCode = closeRequested ? 0 : 1;
        return true;
    }
}
