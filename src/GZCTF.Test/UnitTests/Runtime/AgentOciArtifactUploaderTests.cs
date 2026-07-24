using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.Vm;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class AgentOciArtifactUploaderTests
{
    [Fact]
    public async Task TryResolveAsync_RecoversStableArtifactIdentityAcrossOperations()
    {
        var digest = "sha256:" + new string('a', 64);
        var manifest = $$"""
            {
              "schemaVersion": 2,
              "artifactType": "application/vnd.gzctf.vm-prepared.qcow2",
              "layers": [{"mediaType":"application/vnd.gzctf.vm-template.qcow2","digest":"{{digest}}","size":4096}],
              "annotations": {
                "org.gzctf.vm-build.operation":"operation-1",
                "org.gzctf.vm-build.source-digest":"source-1",
                "org.gzctf.vm-build.recipe-digest":"recipe-1"
              }
            }
            """;
        var handler = new RegistryHandler(manifest, digest, 4096);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, false));
        var uploader = new AgentOciArtifactUploader(factory.Object);
        var target = new AgentOciRegistryTarget("10.24.0.28:5000", "gzctf/vm", "build-1");

        var recovered = await uploader.TryResolveAsync(
            target,
            new Dictionary<string, string>
            {
                ["org.gzctf.vm-build.source-digest"] = "source-1",
                ["org.gzctf.vm-build.recipe-digest"] = "recipe-1"
            },
            CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(digest, recovered.LayerDigest);
        Assert.Equal(4096, recovered.Size);
        var recoveredByAnotherOperation = await uploader.TryResolveAsync(
            target,
            new Dictionary<string, string>
            {
                ["org.gzctf.vm-build.source-digest"] = "source-1",
                ["org.gzctf.vm-build.recipe-digest"] = "recipe-1"
            },
            CancellationToken.None);
        Assert.NotNull(recoveredByAnotherOperation);

        var conflict = await Assert.ThrowsAsync<AgentOperationException>(() => uploader.TryResolveAsync(
            target,
            new Dictionary<string, string>
            {
                ["org.gzctf.vm-build.source-digest"] = "source-1",
                ["org.gzctf.vm-build.recipe-digest"] = "recipe-2"
            },
            CancellationToken.None));
        Assert.Equal("oci_registry_identity_conflict", conflict.Code);
    }

    private sealed class RegistryHandler(string manifest, string digest, long size) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(manifest, Encoding.UTF8,
                        "application/vnd.oci.image.manifest.v1+json")
                };
                response.Headers.TryAddWithoutValidation("Docker-Content-Digest", "sha256:" + new string('b', 64));
                return Task.FromResult(response);
            }
            if (request.Method == HttpMethod.Head && request.RequestUri!.AbsolutePath.EndsWith(digest,
                    StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([])
                };
                response.Content.Headers.ContentLength = size;
                return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
