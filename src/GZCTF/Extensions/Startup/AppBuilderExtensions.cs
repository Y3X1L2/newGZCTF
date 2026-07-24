using Serilog;
using StackExchange.Redis;
using Microsoft.Extensions.Caching.Hybrid;
using GZCTF.Infrastructure.Cache;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;

namespace GZCTF.Extensions.Startup;

internal static class AppBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        internal void ConfigureWebHost()
        {
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.ConfigCustomSerializerOptions();
            });

            builder.Services.AddLocalization(options => options.ResourcesPath = "Resources")
                .Configure<RequestLocalizationOptions>(options =>
                {
                    options
                        .AddSupportedCultures(SupportedCultures)
                        .AddSupportedUICultures(SupportedCultures);

                    options.ApplyCurrentCultureToResponseHeaders = true;
                });

            builder.WebHost.ConfigureKestrel(options =>
            {
                var kestrelSection = builder.Configuration.GetSection("Kestrel");
                options.Configure(kestrelSection);
                kestrelSection.Bind(options);
            }).UseKestrel(options =>
            {
                options.ListenAnyIP(builder.Configuration.GetValue("ServerPort", ServerPort));
                options.ListenAnyIP(builder.Configuration.GetValue("MetricPort", MetricPort));
            });

            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Logging.AddSerilog(dispose: true);
            builder.Host.UseSerilog(dispose: true);
            builder.Configuration.AddEnvironmentVariables("GZCTF_");

            builder.Services.AddServiceDiscovery();
            builder.Services.ConfigureHttpClientDefaults(http =>
            {
                http.AddStandardResilienceHandler();
                http.AddServiceDiscovery();
            });
        }

        internal void ConfigureCacheAndSignalR()
        {
            var signalrBuilder = builder.Services.AddSignalR().AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.ConfigCustomSerializerOptions();
            });

            var section = builder.Configuration.GetSection(RedisRuntimeOptions.SectionName);
            var connectionString = section["ConnectionString"] ??
                                   builder.Configuration.GetConnectionString("RedisCache");
            var configuredMode = section.GetValue<RedisRuntimeMode?>(nameof(RedisRuntimeOptions.Mode));
            var mode = configuredMode ?? (string.IsNullOrWhiteSpace(connectionString)
                ? RedisRuntimeMode.Disabled
                : string.Equals(builder.Configuration["RunMode"], "Fleet", StringComparison.OrdinalIgnoreCase)
                    ? RedisRuntimeMode.Distributed
                    : RedisRuntimeMode.SingleInstance);
            var keyPrefix = section[nameof(RedisRuntimeOptions.KeyPrefix)] ?? "gzctf";
            var frameworkKeyspace = new RedisKeyspace(keyPrefix);

            builder.Services.AddOptions<RedisRuntimeOptions>()
                .Bind(section)
                .PostConfigure(options =>
                {
                    options.Mode = mode;
                    options.ConnectionString ??= connectionString;
                })
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<RedisRuntimeOptions>, RedisRuntimeOptionsValidator>();
            builder.Services.AddSingleton<RedisTelemetry>();
            builder.Services.AddSingleton<RedisRuntimeState>();
            builder.Services.AddSingleton<RedisKeyspace>();
            builder.Services.AddSingleton<IRedisConnectionProvider, RedisConnectionProvider>();

            if (mode == RedisRuntimeMode.Disabled || string.IsNullOrWhiteSpace(connectionString))
            {
                builder.Services.AddDistributedMemoryCache();
            }
            else
            {
                builder.Services.AddStackExchangeRedisCache(_ => { });
                builder.Services.AddOptions<RedisCacheOptions>()
                    .Configure<IRedisConnectionProvider>((options, provider) =>
                    {
                        options.InstanceName = frameworkKeyspace.CreateFrameworkPrefix(
                            RedisKeyPurpose.Cache, "hybrid");
                        options.ConnectionMultiplexerFactory = async () =>
                            await provider.GetAsync() ??
                            throw new InvalidOperationException("Redis connection is not configured.");
                    });

                signalrBuilder.AddStackExchangeRedis(options =>
                {
                    options.Configuration.ChannelPrefix = new RedisChannel(
                        frameworkKeyspace.CreateFrameworkPrefix(RedisKeyPurpose.Backplane, "signalr"),
                        RedisChannel.PatternMode.Literal);
                });
                builder.Services.AddOptions<RedisOptions>()
                    .Configure<IRedisConnectionProvider>((options, provider) =>
                    {
                        options.ConnectionFactory = async _ =>
                            await provider.GetAsync() ??
                            throw new InvalidOperationException("Redis connection is not configured.");
                    });
            }

            builder.Services.AddMemoryCache();
            builder.Services.AddHybridCache(options =>
            {
                options.MaximumPayloadBytes = 8 * 1024 * 1024;
                options.MaximumKeyLength = 512;
            })
                .AddSerializer(new ScoreboardHybridCacheSerializer())
                .AddSerializer(new PostListHybridCacheSerializer());
        }
    }
}
