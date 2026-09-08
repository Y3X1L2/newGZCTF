using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.TeamLab.Contracts;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class TeamLabSftpFileTests
{
    [Fact]
    public async Task RealSftpSupportsBoundedFilesAndRejectsChangedHostIdentity()
    {
        var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var imageDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sftp");
        var imageVersion = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(imageDirectory, "Dockerfile"))))[..12];
        // Cache this credential-free test dependency; containers and runtime-generated keys remain disposable.
        var image = new ImageFromDockerfileBuilder().WithName("gzctf-qa-sftp:" + imageVersion)
            .WithDockerfileDirectory(imageDirectory).WithImageBuildPolicy(PullPolicy.Missing)
            .WithCleanUp(false).WithDeleteIfExists(false).Build();
        await image.CreateAsync(startup.Token);
        await using var server = new ContainerBuilder(image)
            .WithEnvironment("QA_PASSWORD", credential)
            .WithPortBinding(2222, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server listening"))
            .Build();
        await server.StartAsync(startup.Token);
        var request = new TeamLabVmFileRequest("sftp-protocol-fixture", 1, Guid.NewGuid(), "127.0.0.1",
            server.GetMappedPublicPort(2222), "qa", credential, "probe", "/home/qa");
        var probe = await SftpAssetFileStore.ExecuteAsync(server.Hostname, request, default);
        Assert.StartsWith("SHA256:", probe.HostKeySha256);
        request = request with { HostKeySha256 = probe.HostKeySha256 };
        var path = "/home/qa/中文.txt";
        var bytes = Encoding.UTF8.GetBytes("真实 SFTP 文件内容");
        await SftpAssetFileStore.ExecuteAsync(server.Hostname, request with { Operation = "upload", Path = path, Content = bytes }, default);
        var download = await SftpAssetFileStore.ExecuteAsync(server.Hostname, request with { Operation = "download", Path = path }, default);
        Assert.Equal(bytes, download.Content);
        await Assert.ThrowsAsync<AgentOperationException>(() => SftpAssetFileStore.ExecuteAsync(server.Hostname,
            request with { Operation = "upload", Path = path, Content = bytes }, default));
        var replaced = Encoding.UTF8.GetBytes("updated");
        await SftpAssetFileStore.ExecuteAsync(server.Hostname, request with { Operation = "upload", Path = path, Content = replaced, Overwrite = true }, default);
        Assert.Equal(replaced, (await SftpAssetFileStore.ExecuteAsync(server.Hostname, request with { Operation = "download", Path = path }, default)).Content);
        await Assert.ThrowsAsync<AgentOperationException>(() => SftpAssetFileStore.ExecuteAsync(server.Hostname,
            request with { Operation = "list", HostKeySha256 = "SHA256:" + new string('x', 43) }, default));
        await SftpAssetFileStore.ExecuteAsync(server.Hostname, request with { Operation = "delete", Path = path }, default);
        var staging = await server.ExecAsync(["su", "qa", "-s", "/bin/sh", "-c",
            "mkdir -p /home/qa/.gzctf-upload-staging && touch -t 202601010000 /home/qa/.gzctf-upload-staging/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa && touch /home/qa/.gzctf-upload-staging/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"]);
        Assert.Equal(0, staging.ExitCode);
        var listing = await SftpAssetFileStore.ExecuteAsync(server.Hostname, request with { Operation = "list" }, default);
        Assert.DoesNotContain(listing.Entries!, item => item.Name == "中文.txt" || item.Name.StartsWith(".teamlab-upload-", StringComparison.Ordinal));
        Assert.DoesNotContain(listing.Entries!, item => item.Name == TeamLabFileLimits.StagingDirectory);
        Assert.Equal(0, (await server.ExecAsync(["test", "!", "-e", "/home/qa/.gzctf-upload-staging/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"])).ExitCode);
        Assert.Equal(0, (await server.ExecAsync(["test", "-f", "/home/qa/.gzctf-upload-staging/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"])).ExitCode);
        Assert.DoesNotContain(credential, request.ToString());
    }
}
