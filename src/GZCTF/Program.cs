global using GZCTF.Models.Data;
global using GZCTF.Modules.TeamLab.Domain.Runtime;
global using GZCTF.Utils;
global using static GZCTF.Server;
global using AppDbContext = GZCTF.Models.AppDbContext;
global using TaskStatus = GZCTF.Utils.TaskStatus;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using GZCTF.Extensions.Startup;
using GZCTF.Models;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using Serilog;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Log.Logger = LogHelper.GetInitLogger();

Banner();

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_TEMP")))
{
    var requestTempPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, PathHelper.Base, "tmp", "aspnetcore"));
    Directory.CreateDirectory(requestTempPath);
    Environment.SetEnvironmentVariable("ASPNETCORE_TEMP", requestTempPath);
}

var builder = WebApplication.CreateBuilder(args);

await PathHelper.EnsureDirsAsync(builder.Environment);

builder.ConfigureWebHost();
builder.ConfigureDatabase();
builder.ConfigureStorage();
builder.ConfigureCacheAndSignalR();
builder.ConfigureIdentity();
builder.ConfigureTelemetry();

builder.AddServiceConfigurations();
builder.AddCustomServices();
builder.AddWebServices();
builder.AddOpenApiServices();

var app = builder.Build();

Log.Logger = app.GetLogger();

await app.RunPrelaunchWorkAsync();

app.UseMiddlewares();

await app.RunServerAsync();

namespace GZCTF
{
    public class Program
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DesignTimeAppDbContextFactory))]
        static Program()
        {
            using var stream = typeof(Program).Assembly
                .GetManifestResourceStream("GZCTF.Resources.favicon.webp")!;
            DefaultFavicon = new byte[stream.Length];

            stream.ReadExactly(DefaultFavicon);
            DefaultFaviconHash = Convert.ToHexStringLower(SHA256.HashData(DefaultFavicon));
        }

        internal static byte[] DefaultFavicon { get; }
        internal static string DefaultFaviconHash { get; }
    }
}
