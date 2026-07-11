using System.Diagnostics;

namespace AutoManagerProcess;

public sealed class ManagerOptions
{
    public const string SectionName = "Manager";

    public string ProcessName { get; set; } = "DNF.exe";

    public int ProcessPollSeconds { get; set; } = 2;

    public int ActionDelaySeconds { get; set; } = 60;

    public bool OptimizeGamePriority { get; set; } = true;

    public string GamePriority { get; set; } = nameof(ProcessPriorityClass.AboveNormal);

    public List<string> LimitList { get; set; } = [];
    public List<string> KillList { get; set; } = [];
    public List<string> AutoStart { get; set; } = [];
    public List<string> AutoStop { get; set; } = [];

    public static bool IsSupportedGamePriority(string? value) =>
        Enum.TryParse<ProcessPriorityClass>(value, ignoreCase: true, out var priority) &&
        priority is ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal;
}
