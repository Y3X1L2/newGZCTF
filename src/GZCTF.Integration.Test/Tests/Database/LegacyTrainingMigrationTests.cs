using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public class LegacyTrainingMigrationTests
{
    private const string PhaseZeroMigration = "20260710100000_RemoveLegacyIrScenarioTraining";

    [Fact]
    public void PhaseZeroContractMigration_IsRegistered()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=phase_zero_contract;Username=postgres;Password=postgres")
            .Options;

        using var context = new AppDbContext(options);

        Assert.Contains(PhaseZeroMigration, context.Database.GetMigrations());
    }
}
