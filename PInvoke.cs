using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DNFProcessManager;

public static class PInvoke
{
    private const uint MaximumAllowed = 0x02000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(
        out IntPtr environment,
        IntPtr token,
        [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [DllImport("userenv.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUserW(
        IntPtr token,
        string applicationName,
        string? commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        ProcessAccessFlags processAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(
        IntPtr process,
        ProcessInformationClass informationClass,
        ref ProcessPowerThrottlingState information,
        uint informationSize);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationProcess(
        IntPtr process,
        int informationClass,
        ref IoPriorityHintInformation information,
        int informationLength);

    public static int? StartInteractiveProcess(
        string applicationPath,
        int sessionId,
        ILogger logger)
    {
        if (!WTSQueryUserToken((uint)sessionId, out var userToken))
        {
            logger.LogError(
                new Win32Exception(Marshal.GetLastWin32Error()),
                "Could not obtain the user token for session {SessionId}",
                sessionId);
            return null;
        }

        var primaryToken = IntPtr.Zero;
        var environment = IntPtr.Zero;
        try
        {
            if (!DuplicateTokenEx(
                    userToken,
                    MaximumAllowed,
                    IntPtr.Zero,
                    SecurityImpersonation,
                    TokenPrimary,
                    out primaryToken))
            {
                logger.LogError(
                    new Win32Exception(Marshal.GetLastWin32Error()),
                    "Could not create a primary user token for session {SessionId}",
                    sessionId);
                return null;
            }

            var creationFlags = 0u;
            if (CreateEnvironmentBlock(out environment, primaryToken, inherit: false))
            {
                creationFlags |= CreateUnicodeEnvironment;
            }
            else
            {
                logger.LogWarning(
                    new Win32Exception(Marshal.GetLastWin32Error()),
                    "Could not load the user environment for session {SessionId}; starting with the service environment",
                    sessionId);
            }

            var startupInfo = new StartupInfo
            {
                Size = (uint)Marshal.SizeOf<StartupInfo>(),
                Desktop = @"winsta0\default"
            };

            if (!CreateProcessAsUserW(
                    primaryToken,
                    applicationPath,
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles: false,
                    creationFlags,
                    environment,
                    Path.GetDirectoryName(applicationPath),
                    ref startupInfo,
                    out var processInformation))
            {
                logger.LogError(
                    new Win32Exception(Marshal.GetLastWin32Error()),
                    "Could not start {ApplicationPath} in user session {SessionId}",
                    applicationPath,
                    sessionId);
                return null;
            }

            try
            {
                return checked((int)processInformation.ProcessId);
            }
            finally
            {
                CloseHandle(processInformation.Thread);
                CloseHandle(processInformation.Process);
            }
        }
        finally
        {
            if (environment != IntPtr.Zero)
            {
                DestroyEnvironmentBlock(environment);
            }

            if (primaryToken != IntPtr.Zero)
            {
                CloseHandle(primaryToken);
            }

            CloseHandle(userToken);
        }
    }

    public static bool SetIoPriority(int processId, ILogger logger)
    {
        var process = OpenProcess(ProcessAccessFlags.SetInformation, inheritHandle: false, processId);
        if (process == IntPtr.Zero)
        {
            logger.LogWarning(
                new Win32Exception(Marshal.GetLastWin32Error()),
                "Could not open process {ProcessId} to set its I/O priority",
                processId);
            return false;
        }

        try
        {
            var information = new IoPriorityHintInformation
            {
                Priority = IoPriorityHint.VeryLow
            };
            var status = NtSetInformationProcess(
                process,
                informationClass: 0x21,
                ref information,
                Marshal.SizeOf(information));

            if (status != 0)
            {
                logger.LogWarning(
                    "Could not set very-low I/O priority for process {ProcessId}; NTSTATUS 0x{Status:X8}",
                    processId,
                    status);
                return false;
            }

            logger.LogInformation("Set very-low I/O priority for process {ProcessId}", processId);
            return true;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public static bool SetEfficiencyMode(int processId, ILogger logger)
    {
        var process = OpenProcess(ProcessAccessFlags.SetInformation, inheritHandle: false, processId);
        if (process == IntPtr.Zero)
        {
            logger.LogWarning(
                new Win32Exception(Marshal.GetLastWin32Error()),
                "Could not open process {ProcessId} to enable efficiency mode",
                processId);
            return false;
        }

        try
        {
            var state = new ProcessPowerThrottlingState
            {
                Version = 1,
                ControlMask = 0x1,
                StateMask = 0x1
            };

            if (!SetProcessInformation(
                    process,
                    ProcessInformationClass.ProcessPowerThrottling,
                    ref state,
                    (uint)Marshal.SizeOf(state)))
            {
                logger.LogWarning(
                    new Win32Exception(Marshal.GetLastWin32Error()),
                    "Could not enable efficiency mode for process {ProcessId}",
                    processId);
                return false;
            }

            logger.LogInformation("Enabled efficiency mode for process {ProcessId}", processId);
            return true;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Length;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    private enum IoPriorityHint
    {
        VeryLow = 0,
        Low = 1,
        Normal = 2,
        High = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoPriorityHintInformation
    {
        public IoPriorityHint Priority;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessPowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    private enum ProcessInformationClass
    {
        ProcessPowerThrottling = 4
    }

    [Flags]
    private enum ProcessAccessFlags : uint
    {
        SetInformation = 0x0200
    }
}
