using GZCTF.GuestSupervisor;
using GZCTF.GuestSupervisor.Enrollment;
using GZCTF.GuestSupervisor.Lifecycle;

var builder = Host.CreateApplicationBuilder(args);
if (OperatingSystem.IsWindows()) builder.Services.AddWindowsService();
else builder.Services.AddSystemd();
var configPath = builder.Configuration["GuestSupervisor:ConfigPath"] ??
                 Environment.GetEnvironmentVariable("GZCTF_GUEST_SUPERVISOR_CONFIG");
var configuration = await GuestSupervisorConfiguration.LoadAsync(configPath, CancellationToken.None);
builder.Services.AddSingleton(configuration);
builder.Services.AddSingleton(new GuestCheckpointStore(configuration.StateRoot));
builder.Services.AddSingleton(new GuestIntentStore(configuration.StateRoot));
builder.Services.AddSingleton(new GuestBootstrapExecutionStore(configuration.StateRoot));
builder.Services.AddSingleton(new GuestSecretStore(configuration.StateRoot));
builder.Services.AddSingleton<GuestLifecycleEngine>();
builder.Services.AddSingleton<GuestEnrollmentClient>();
builder.Services.AddSingleton<IGuestGatewayClient>(services => services.GetRequiredService<GuestEnrollmentClient>());
builder.Services.AddSingleton<GuestBootstrapPackageExecutor>();
builder.Services.AddSingleton<GuestRebootController>();
builder.Services.AddSingleton<GuestNetworkVerifier>();
builder.Services.AddSingleton<GuestRemoteAccessProvisioner>();
builder.Services.AddHostedService<GuestSupervisorWorker>();
await builder.Build().RunAsync();
