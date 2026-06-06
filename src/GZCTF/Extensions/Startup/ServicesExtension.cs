using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Internal;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Cache;
using GZCTF.Services.Config;

using GZCTF.Services.Concurrency;
using GZCTF.Services.Container;
using GZCTF.Services.CronJob;

using GZCTF.Services.Fleet;
using GZCTF.Services.Mail;
using GZCTF.Services.Token;
using GZCTF.Services.Transfer;
using GZCTF.Services.Vm;
using GZCTF.Storage;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;

namespace GZCTF.Extensions.Startup;

internal static class ServicesExtension
{
    extension(WebApplicationBuilder builder)
    {
        private void AddConfig<TConfig>()
            where TConfig : class
            => builder.Services.Configure<TConfig>(builder.Configuration.GetSection(typeof(TConfig).Name));

        internal void AddServiceConfigurations()
        {
            builder.AddConfig<EmailConfig>();
            builder.AddConfig<AccountPolicy>();
            builder.AddConfig<GlobalConfig>();
            builder.AddConfig<ManagedConfig>();
            builder.AddConfig<ContainerPolicy>();
            builder.AddConfig<ContainerProvider>();
            builder.AddConfig<KvmSettings>();
            builder.AddConfig<GuacamoleSettings>();

            builder.Services.Configure<RegistrySet<RegistryConfig>>(builder.Configuration.GetSection("Registries"));

            var oldConfig = builder.Configuration.GetSection(nameof(RegistryConfig)).Get<RegistryConfig>();
            if (!string.IsNullOrWhiteSpace(oldConfig?.ServerAddress))
                // Add old config to new config set
                builder.Services.Configure<RegistrySet<RegistryConfig>>(set =>
                {
                    if (!set.TryAdd(oldConfig.ServerAddress, oldConfig))
                        set[oldConfig.ServerAddress] = oldConfig;
                });

            var forwardedOptions =
                builder.Configuration.GetSection(nameof(ForwardedOptions)).Get<ForwardedOptions>();
            if (forwardedOptions is null)
                builder.Services.Configure<ForwardedHeadersOptions>(options =>
                {
                    options.ForwardedHeaders =
                        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                });
            else
                builder.Services.Configure<ForwardedHeadersOptions>(forwardedOptions.ToForwardedHeadersOptions);
        }

        internal void AddCustomServices()
        {
            builder.Services.AddCaptchaService(builder.Configuration);
            builder.Services.AddContainerService(builder.Configuration);

            builder.Services.AddScoped<IConfigService, ConfigService>();
            builder.Services.AddScoped<ITokenService, TokenService>();
            builder.Services.AddScoped<ILogRepository, LogRepository>();
            builder.Services.AddScoped<IBlobRepository, BlobRepository>();
            builder.Services.AddScoped<IPostRepository, PostRepository>();
            builder.Services.AddScoped<IGameRepository, GameRepository>();
            builder.Services.AddScoped<ITeamRepository, TeamRepository>();
            builder.Services.AddScoped<IApiTokenRepository, ApiTokenRepository>();
            builder.Services.AddScoped<IContainerRepository, ContainerRepository>();
            builder.Services.AddScoped<IGameEventRepository, GameEventRepository>();
            builder.Services.AddScoped<ICheatInfoRepository, CheatInfoRepository>();
            builder.Services.AddScoped<IGameNoticeRepository, GameNoticeRepository>();
            builder.Services.AddScoped<ISubmissionRepository, SubmissionRepository>();
            builder.Services.AddScoped<IGameInstanceRepository, GameInstanceRepository>();
            builder.Services.AddScoped<IGameChallengeRepository, GameChallengeRepository>();
            builder.Services.AddScoped<IParticipationRepository, ParticipationRepository>();
            builder.Services.AddScoped<IDivisionRepository, DivisionRepository>();

            builder.Services.AddScoped<ExcelHelper>();
            builder.Services.AddScoped<GameExportService>();
            builder.Services.AddScoped<GameImportService>();

            builder.Services.AddChannel<CacheRequest>();
            builder.Services.AddSingleton<CacheHelper>();
            builder.Services.AddSingleton<IMailSender, MailSender>();

            builder.Services.AddSingleton<ImageStorage>();
            builder.Services.AddSingleton<ContainerOrchestrator>();
            builder.Services.AddSingleton<IVirtualMachineProvider, KvmProvider>();
            #pragma warning disable CS0618 // VmManager is obsolete but still needed by teammate's EnvironmentService/GameChallengeRepository
            builder.Services.AddSingleton<VmManager>();
            #pragma warning restore CS0618
            builder.Services.AddScoped<LocalImageImporter>();
            builder.Services.AddScoped<IArchiveExtractor, ArchiveExtractor>();

            builder.Services.AddScoped<GamePhaseService>();
            builder.Services.AddScoped<TheoryExamService>();

            // AWD and scenario services (from teammate's branch)
            builder.Services.AddScoped<EnvironmentService>();
            builder.Services.AddScoped<ScoringService>();
            builder.Services.AddScoped<LeaderboardService>();
            builder.Services.AddScoped<GuacamoleProxy>();
            builder.Services.AddScoped<SSHAccessService>();
            builder.Services.AddScoped<CheckpointVerificationService>();
            builder.Services.AddScoped<FlagChecker>();
            builder.Services.AddScoped<AuditLogService>();

            // Phase 3 fleet services
            builder.Services.AddScoped<INodeRepository, NodeRepository>();
            builder.Services.AddScoped<NodeDeployService>();
            builder.Services.AddScoped<FleetManager>();
            builder.Services.AddScoped<WeightedScheduler>();
            builder.Services.AddSingleton<QueueManager>();
            builder.Services.AddScoped<ImageDistributionService>();
            builder.Services.AddHostedService<FleetHealthCheckService>();
            builder.Services.AddHostedService<QueueProcessingService>();

            builder.Services.AddHttpClient("Agent");
            builder.Services.AddSingleton<AgentClient>();
            builder.Services.AddScoped<FleetVmService>();
            builder.Services.AddScoped<GuacamoleService>();
            builder.Services.AddHostedService<VmReadyService>();

            // Phase 7 security: distributed lock
            if (builder.Configuration.GetValue<string>("RunMode") == "Fleet")
                builder.Services.AddSingleton<IDistributedLockService, RedisDistributedLock>();
            else
                builder.Services.AddSingleton<IDistributedLockService, LocalSemaphoreLock>();

            builder.Services.AddHostedService<CacheMaker>();
            builder.Services.AddHostedService<CronJobService>();
            builder.Services.AddHostedService<LocalNodeRegistrar>();
        }

        internal void AddWebServices()
        {
            builder.Services.AddRouting(options => options.LowercaseUrls = true);
            builder.Services.AddRateLimiter(RateLimiter.ConfigureRateLimiter);
            builder.Services.AddResponseCompression(options =>
            {
                options.Providers.Add<ZStandardCompressionProvider>();
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    [
                        // See others in ResponseCompressionDefaults.MimeTypes
                        MediaTypeNames.Application.Pdf
                    ]
                );
                options.EnableForHttps = true;
            });

            builder.Services.AddControllersWithViews().ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = InvalidModelStateHandler;
            }).AddDataAnnotationsLocalization(options =>
            {
                options.DataAnnotationLocalizerProvider = (_, factory) =>
                    factory.Create(typeof(Resources.Program));
            }).AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ConfigCustomSerializerOptions();
            });
            builder.Services.AddHttpClient("GuacamoleClient", client =>
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            });

            builder.Services.AddResponseCaching();
        }

        internal void AddDevelopmentServices()
        {
            if (!builder.Environment.IsDevelopment())
                return;

            builder.Services.AddOpenApiDocument(settings =>
            {
                settings.DocumentName = "v1";
                settings.Version = "v1";
                settings.Title = "GZCTF Server API";
                settings.Description = "GZCTF Server API Document";
                settings.UseControllerSummaryAsTagDescription = true;
                settings.SchemaSettings.TypeMappers.Add(new OpenApiDateTimeOffsetToUIntMapper());
                settings.SchemaSettings.TypeMappers.Add(new OpenApiIPAddressToStringMapper());
                settings.SchemaSettings.ReflectionService = new GenericsSystemTextJsonReflectionService();
            });
        }
    }
}
