using GZCTF.Models;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Integration.Test.Tests.Database;

/// <summary>Seeds the pre-account-retirement schema without borrowing future EF columns.</summary>
internal static class HistoricalUserFixture
{
    public static async Task InsertAsync(AppDbContext context, UserInfo user)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "AspNetUsers"
                ("Id", "Role", "LastSignedInUtc", "LastVisitedUtc", "RegisterTimeUtc", "Bio",
                 "RealName", "StdNumber", "ExerciseVisible", "UserName", "NormalizedUserName",
                 "Email", "NormalizedEmail", "EmailConfirmed", "PhoneNumberConfirmed",
                 "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "IP")
            VALUES
                ({{user.Id}}, {{(int)user.Role}}, {{user.LastSignedInUtc}}, {{user.LastVisitedUtc}},
                 {{user.RegisterTimeUtc}}, {{user.Bio}}, {{user.RealName}}, {{user.StdNumber}},
                 {{user.ExerciseVisible}}, {{user.UserName}}, {{user.NormalizedUserName}},
                 {{user.Email}}, {{user.NormalizedEmail}}, {{user.EmailConfirmed}},
                 {{user.PhoneNumberConfirmed}}, {{user.TwoFactorEnabled}}, {{user.LockoutEnabled}},
                 {{user.AccessFailedCount}}, CAST({{user.IP.ToString()}} AS inet));
            """);
        // Related fixture graphs can reference this existing row without reinserting it with the current model.
        context.Attach(user);
    }
}
