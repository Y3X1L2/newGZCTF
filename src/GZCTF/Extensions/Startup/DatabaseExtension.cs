using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
                ExitWithFatalMessage(
                    StaticLocalizer[nameof(Resources.Program.Database_ConnectionFailed), FailureCategory(e)]);
            }
        }
    }

    // Provider exception messages can contain connection strings or supplied configuration values.
    internal static string FailureCategory(Exception exception) => exception.GetType().Name;
}
