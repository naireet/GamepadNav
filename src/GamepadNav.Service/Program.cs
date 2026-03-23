using GamepadNav.Service;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "GamepadNav";
});

// When running as a Windows Service (LocalSystem), use the slim login screen engine.
// When running interactively (user session), use the full InputEngine.
if (Environment.UserInteractive)
    builder.Services.AddHostedService<InputEngine>();
else
    builder.Services.AddHostedService<LoginScreenEngine>();

var host = builder.Build();
host.Run();
