using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;

namespace GZCTF.Extensions.Startup;

internal static class DatabaseExtension
{
    extension(WebApplicationBuilder builder)
    {
        internal void ConfigureDatabase()
        {
            if (!builder.Configuration.GetSection("ConnectionStrings").GetSection("Database").Exists())
                ExitWithFatalMessage(
                    StaticLocalizer[nameof(Resources.Program.Database_NoConnectionString)]);

            builder.Services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseNpgsql(builder.Configuration.GetConnectionString("Database"),
                        o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

                    options.ConfigureWarnings(w =>
                        w.Ignore(RelationalEventId.PendingModelChangesWarning));

                    if (!builder.Environment.IsDevelopment())
                        return;

                    options.EnableSensitiveDataLogging();
                    options.EnableDetailedErrors();
                }
            );

            try
            {
                builder.Configuration.AddEntityConfiguration(options =>
                {
                    options.UseNpgsql(builder.Configuration.GetConnectionString("Database"));
                    options.ConfigureWarnings(w =>
                        w.Ignore(RelationalEventId.PendingModelChangesWarning));
                });
            }
            catch (Exception e)
            {
                if (builder.Configuration.GetSection("ConnectionStrings").GetSection("Database").Exists())
                    Log.Logger.Error(StaticLocalizer[
                        nameof(Resources.Program.Database_CurrentConnectionString),
                        builder.Configuration.GetConnectionString("Database") ?? "null"]);
                ExitWithFatalMessage(
                    StaticLocalizer[nameof(Resources.Program.Database_ConnectionFailed), e.Message]);
            }
        }
    }
}
