using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Vm;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabVmArtifactSafetyTests
{
    [Fact]
    public async Task VerifyBaseImage_RehashesContentEvenWhenLengthAndTimestampAreUnchanged()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "content-a");
            var timestamp = File.GetLastWriteTimeUtc(path);
            var expected = $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("content-a"))).ToLowerInvariant()}";

            Assert.True(await LibvirtTeamLabProvider.HasExpectedBaseImageAsync(
                path, expected, CancellationToken.None));

            await File.WriteAllTextAsync(path, "content-b");
            File.SetLastWriteTimeUtc(path, timestamp);

            Assert.False(await LibvirtTeamLabProvider.HasExpectedBaseImageAsync(
                path, expected, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VmImageDigestLock_SerializesVerificationAndReplacement()
    {
        var resourceLock = new AgentResourceLock();
        var imagePath = Path.Combine(Path.GetTempPath(), "teamlab-vm-artifact-lock.qcow2");
        await using var verification = await resourceLock.AcquireAsync(
            LibvirtTeamLabProvider.BaseImageLockKey(imagePath), CancellationToken.None);

        var replacement = resourceLock.AcquireAsync(
            LibvirtTeamLabProvider.BaseImageLockKey(imagePath), CancellationToken.None).AsTask();

        Assert.False(replacement.IsCompleted);
        await verification.DisposeAsync();
        await using var acquired = await replacement.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
