using System.Reflection;
using GZCTF.Infrastructure.Api;
using GZCTF.Hubs;
using GZCTF.Modules.Identity.Infrastructure;
using GZCTF.Modules.Audit.Infrastructure;
using Scalar.AspNetCore;
using Serilog;

namespace GZCTF.Extensions.Startup;

internal static class AppExtensions
{
    private static readonly StaticFileOptions DefaultStaticFileOptions = new()
    {
        OnPrepareResponse = ctx =>
        {
            var headers = ctx.Context.Response.GetTypedHeaders();
            if (Path.GetExtension(ctx.File.Name).Equals(".html", StringComparison.OrdinalIgnoreCase))
            {
                headers.CacheControl = new()
                {
                    NoCache = true,
                    NoStore = true,
                    MustRevalidate = true,
                    MaxAge = TimeSpan.Zero
                };
                return;
            }

            headers.CacheControl = new()
            {
                Public = true,
                MaxAge = TimeSpan.FromDays(7)
            };
        }
    };

    private static readonly WebSocketOptions DefaultWebSocketOptions =
        new() { KeepAliveInterval = TimeSpan.FromMinutes(30) };

    extension(WebApplication app)
    {
        internal async Task RunServerAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Server>>();

            try
            {
                var version = typeof(Server).Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;
                logger.SystemLog(version ?? "YINYU CTF平台", TaskStatus.Pending, LogLevel.Debug);
                await app.RunAsync();
            }
            catch (Exception exception)
            {
                logger.LogErrorMessage(exception, StaticLocalizer[nameof(Resources.Program.Server_Failed)]);
                throw;
            }
            finally
            {
                logger.SystemLog(StaticLocalizer[nameof(Resources.Program.Server_Exited)], TaskStatus.Exit,
                    LogLevel.Debug);

                await Log.CloseAndFlushAsync();
            }
        }

        internal void UseMiddlewares()
        {
            app.UseRequestLocalization();

            app.UseResponseCaching();
            app.UseResponseCompression();

            app.UseCustomFavicon();
            app.UseStaticFiles(DefaultStaticFileOptions);

            app.UseForwardedHeaders();

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/error/500");
                app.UseHsts();
            }

            app.MapOpenApiDocumentation();

            app.UseMiddleware<ExternalApiRequestAuditMiddleware>();
            app.UseMiddleware<ExternalApiExceptionHandler>();

            app.UseRouting();

            if (app.Configuration.GetValue<bool>("DisableRateLimit") is not true)
                app.UseRateLimiter();

            app.UseAuthentication();
            app.UseMiddleware<ApiTokenRateLimitMiddleware>();
            app.UseAuthorization();

            if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("RequestLogging"))
                app.UseRequestLogging();

            app.UseWebSockets(DefaultWebSocketOptions);
            app.UseTelemetry();

            app.MapHealthCheck();
            app.MapControllers();

            app.MapHub<UserHub>("/hub/user");
            app.MapHub<MonitorHub>("/hub/monitor");
            app.MapHub<AdminHub>("/hub/admin");
            app.UseIndexAsync();
        }

        internal void MapOpenApiDocumentation()
        {
            app.UseOpenApi(options =>
            {
                options.PostProcess += (document, _) => document.Servers.Clear();
                options.Path = "/openapi/{documentName}.json";
            });
            app.MapScalarApiReference("/api-docs", options =>
            {
                options
                    .WithTitle("YINYU 平台开放 API 文档")
                    .WithOpenApiRoutePattern("/openapi/{documentName}.json")
                    .WithDynamicBaseServerUrl(true)
                    .AddDocument(
                        "open-v1",
                        "YINYU 平台开放 API v1",
                        "/openapi/open-v1.json",
                        true)
                    .AddHeaderContent(
                        """
                        <section class="gz-api-guide" aria-label="开放 API 中文导航">
                          <div class="gz-api-guide__content">
                            <div class="gz-api-guide__title">
                              <strong>YINYU 平台开放 API v1</strong>
                              <span>面向内容流水线、组网自动化和受控系统集成。</span>
                            </div>
                            <div class="gz-api-guide__items">
                              <span><b>认证</b>：点击“Authentication”，填写 Bearer Token。</span>
                              <span><b>写操作</b>：按接口要求提供稳定的 <code>Idempotency-Key</code>。</span>
                              <span><b>接口分类</b>：题目与镜像、Bootstrap Profile、TeamLab 拓扑、运行环境、流量与抓包。</span>
                              <span><b>组网导航</b>：<code>TeamLab - Topologies</code>、<code>TeamLab - Runtimes</code>、<code>TeamLab - Traffic and Captures</code>。</span>
                              <span><b>唯一契约</b>：本页面只读取 <code>/openapi/open-v1.json</code>，不维护第二份接口定义。</span>
                            </div>
                          </div>
                        </section>
                        """)
                    .WithCustomCss(
                        """
                        .gz-api-guide {
                          box-sizing: border-box;
                          border-bottom: 1px solid var(--scalar-border-color);
                          background: var(--scalar-background-1);
                          color: var(--scalar-color-1);
                          font-family: var(--scalar-font);
                          padding: 12px 20px;
                        }
                        .gz-api-guide__content {
                          display: grid;
                          gap: 8px;
                          margin: 0 auto;
                          max-width: 1600px;
                        }
                        .gz-api-guide__title {
                          align-items: baseline;
                          display: flex;
                          flex-wrap: wrap;
                          gap: 6px 14px;
                        }
                        .gz-api-guide__title strong {
                          font-size: 15px;
                        }
                        .gz-api-guide__items {
                          display: flex;
                          flex-wrap: wrap;
                          gap: 5px 18px;
                        }
                        .gz-api-guide span {
                          color: var(--scalar-color-2);
                          font-size: 13px;
                          line-height: 1.5;
                        }
                        .gz-api-guide b {
                          color: var(--scalar-color-1);
                          font-weight: 600;
                        }
                        .gz-api-guide code {
                          color: var(--scalar-color-1);
                          font-family: var(--scalar-font-code);
                        }
                        """)
                    .WithJsonDocumentDownload()
                    .EnablePersistentAuthentication()
                    .ShowOperationId();
            });
        }
    }
}
