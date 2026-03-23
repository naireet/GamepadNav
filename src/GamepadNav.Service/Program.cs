using GamepadNav.Service;

var builder = Host.CreateApplicationBuilder(args);

// Run as a Windows Service when not launched from console
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "GamepadNav";
});

builder.Services.AddHostedService<InputEngine>();

var host = builder.Build();
host.Run();
