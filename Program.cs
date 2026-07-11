using AutoManagerProcess;
using Serilog;


var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables(prefix: "AMP_");

builder.Services.AddSerilog(c =>
{
    c.WriteTo
        .File(
            Path.Combine(AppContext.BaseDirectory, "logs", "auto-manager.log"),
            rollingInterval: RollingInterval.Infinite,
            fileSizeLimitBytes: 2 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            retainedFileCountLimit: 1,
            shared: true)
        .ReadFrom.Configuration(builder.Configuration);
});



builder.Services.AddWindowsService(o =>
{
    o.ServiceName = "Auto Manager Process";
});

builder.Services
    .AddOptions<ManagerOptions>()
    .Bind(builder.Configuration.GetSection(ManagerOptions.SectionName))
    .Validate(x => !string.IsNullOrWhiteSpace(x.ProcessName), "Manager:ProcessName is required")
    .Validate(x => x.ProcessPollSeconds is >= 1 and <= 60, "Manager:ProcessPollSeconds must be between 1 and 60")
    .Validate(x => x.ActionDelaySeconds is >= 0 and <= 3600, "Manager:ActionDelaySeconds must be between 0 and 3600")
    .Validate(x => ManagerOptions.IsSupportedGamePriority(x.GamePriority), "Manager:GamePriority must be Normal or AboveNormal")
    .ValidateOnStart();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
