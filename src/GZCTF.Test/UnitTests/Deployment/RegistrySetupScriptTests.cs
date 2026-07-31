using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Modules.Content.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Deployment;

public sealed class RegistrySetupScriptTests
{
    [Fact]
    public void RegistrySetupScript_VerifiesManifestDeletion()
    {
        var script = ReadRepositoryFile("docs", "registry-server", "setup-gzctf-image-registry.sh");

        Assert.Contains("verify_registry_delete", script, StringComparison.Ordinal);
        Assert.Contains("Docker-Content-Digest", script, StringComparison.Ordinal);
        Assert.Contains("-X DELETE", script, StringComparison.Ordinal);
        Assert.Contains("Registry manifest delete verification failed", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerSetupScript_MergesDaemonJsonAndVerifiesPrivateRegistryPull()
    {
        var script = ReadRepositoryFile("docs", "node-deployment", "setup-gzctf-worker-node.sh");

        Assert.Contains("json.loads", script, StringComparison.Ordinal);
        Assert.Contains("cmp -s", script, StringComparison.Ordinal);
        Assert.Contains("mktemp", script, StringComparison.Ordinal);
        Assert.Contains("verify_private_registry_pull", script, StringComparison.Ordinal);
        Assert.Contains("docker pull", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseActivationScript_PreservesAtomicReleaseLayoutAndStopsWritersBeforeMigration()
    {
        var script = ReadRepositoryFile("scripts", "deployment", "activate-gzctf-release.sh");

        Assert.Contains("test -L \"$current\"", script, StringComparison.Ordinal);
        Assert.Contains("release_root=\"$root/releases/$release_id\"", script, StringComparison.Ordinal);
        Assert.Contains("mv -Tf \"$next_link\" \"$current\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("mv \"$current\" \"$previous\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("mv \"$current/files\"", script, StringComparison.Ordinal);

        var stop = script.IndexOf("systemctl stop gzctf.service", StringComparison.Ordinal);
        var migrate = script.IndexOf("./efbundle --no-color", StringComparison.Ordinal);
        Assert.True(stop >= 0 && migrate > stop, "The application must stop before migrations run.");
    }

    [Fact]
    public async Task OciArtifactDelete_MethodNotAllowedRemainsFailure()
    {
        var handler = new RecordingHandler(request => request.Method switch
        {
            { Method: "HEAD" } => ManifestHeadResponse(),
            { Method: "DELETE" } => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>())).Returns(client);
        var registry = new OciArtifactRegistryClient(
            factory.Object,
            NullLogger<OciArtifactRegistryClient>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.DeleteAsync(
            new OciArtifactReference("registry.internal:5000", "ctf/test", "latest", new string('a', 64), 1),
            CancellationToken.None));

        Assert.Contains("405", exception.Message, StringComparison.Ordinal);
        Assert.Equal([HttpMethod.Head, HttpMethod.Delete], handler.Methods);
    }

    private static HttpResponseMessage ManifestHeadResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Docker-Content-Digest", $"sha256:{new string('b', 64)}");
        response.Content = new ByteArrayContent([]);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.oci.image.manifest.v1+json");
        return response;
    }

    private static string ReadRepositoryFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. parts]));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "docs")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            return Task.FromResult(respond(request));
        }
    }
}
