using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

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

            var connection = new NpgsqlConnectionStringBuilder(
                builder.Configuration.GetConnectionString("Database"));
            if (!connection.ContainsKey("Connection Idle Lifetime"))
                connection.ConnectionIdleLifetime = 60;
            var connectionString = connection.ConnectionString;

            builder.Services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseNpgsql(connectionString,
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
                    options.UseNpgsql(connectionString);
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
