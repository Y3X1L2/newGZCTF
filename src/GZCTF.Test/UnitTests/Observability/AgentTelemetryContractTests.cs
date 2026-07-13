using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Middlewares;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using AgentProtocolError = GZCTF.Agent.Models.AgentErrorResponse;
using MainProtocolError = GZCTF.Modules.Audit.Contracts.AgentErrorResponse;

namespace GZCTF.Test.UnitTests.Observability;

public sealed class AgentTelemetryContractTests
{
    [Theory]
    [InlineData("POST", "/api/containers/create", "container.create")]
    [InlineData("DELETE", "/api/containers/0123456789abcdef", "container.destroy")]
    [InlineData("POST", "/api/containers/runtime-42/fabric/routes", "fabric.route.apply")]
    [InlineData("POST", "/api/vms/team-99/ip", "vm.ip.read")]
    [InlineData("POST", "/api/images/download-vm", "image.vm.download")]
    [InlineData("GET", "/api/teamlab/capture/42/91/download", "teamlab.capture.id.id.download")]
    public void OperationName_IsStableAndDoesNotExposeResourceIdentifiers(
        string method,
        string path,
        string expected)
    {
        var operation = AgentOperationName.Resolve(new HttpMethod(method), path);

        Assert.Equal(expected, operation);
        Assert.DoesNotContain("0123456789abcdef", operation);
        Assert.DoesNotContain("runtime-42", operation);
        Assert.DoesNotContain("team-99", operation);
    }

    [Fact]
    public async Task Handler_PropagatesAmbientCorrelationAndPreservesTypedErrorHeaders()
    {
        var correlation = new OperationalCorrelation();
        var expectedCorrelation = Guid.CreateVersion7();
        var inner = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.Add(AgentTelemetryHandler.ErrorCategoryHeaderName, "NodeUnavailable");
            response.Headers.Add(AgentTelemetryHandler.ErrorCodeHeaderName, "node.offline");
            response.Headers.Add(AgentTelemetryHandler.RetryableHeaderName, "true");
            return response;
        });
        var handler = new AgentTelemetryHandler(correlation) { InnerHandler = inner };
        using var client = new HttpClient(handler);
        using var scope = correlation.Begin(expectedCorrelation);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://worker:5000/api/vms/create");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(expectedCorrelation.ToString(),
            inner.Request!.Headers.GetValues(OperationalCorrelation.HeaderName).Single());
        Assert.Equal("node.offline",
            response.Headers.GetValues(AgentTelemetryHandler.ErrorCodeHeaderName).Single());
    }

    [Fact]
    public async Task AgentMiddleware_ReturnsSafeTypedErrorWithCallerCorrelation()
    {
        var correlationId = Guid.CreateVersion7();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/vms/create";
        context.Request.Headers[GZCTF.Agent.Models.AgentProtocolHeaders.CorrelationId] = correlationId.ToString();
        context.Response.Body = new MemoryStream();
        var middleware = new AgentCorrelationErrorMiddleware(
            _ => throw new InvalidOperationException("secret process output"),
            NullLogger<AgentCorrelationErrorMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var payload = await JsonSerializer.DeserializeAsync<AgentProtocolError>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("Kvm", payload.Category);
        Assert.Equal("kvm.operation_failed", payload.Code);
        Assert.Equal(correlationId.ToString(), payload.CorrelationId);
        Assert.DoesNotContain("secret process output", payload.Message);
        Assert.Equal("Kvm",
            context.Response.Headers[GZCTF.Agent.Models.AgentProtocolHeaders.ErrorCategory].ToString());
    }

    [Fact]
    public void MainClassifier_UsesAgentCategoryCodeAndRetryability()
    {
        var nodeId = Guid.CreateVersion7();
        var response = new MainProtocolError(
            "ImageTransfer",
            "image.digest_mismatch",
            "Image digest verification failed.",
            false,
            "image.vm.download",
            Guid.CreateVersion7().ToString());

        var error = OperationalErrorClassifier.FromAgentResponse(
            response,
            StatusCodes.Status500InternalServerError,
            "image.vm.download",
            "Node worker-1 failed to prepare the VM image.",
            nodeId);

        Assert.Equal(OperationalErrorCategory.ImageTransfer, error.Category);
        Assert.Equal("image.digest_mismatch", error.Code);
        Assert.False(error.Retryable);
        Assert.Equal(nodeId, error.WorkerNodeId);
        Assert.Contains("worker-1", error.Message);
    }

    [Fact]
    public void CorrelationScope_RestoresPreviousAmbientValue()
    {
        var correlation = new OperationalCorrelation();
        var outer = Guid.CreateVersion7();
        var inner = Guid.CreateVersion7();

        using (correlation.Begin(outer))
        {
            Assert.Equal(outer, correlation.Current);
            using (correlation.Begin(inner))
                Assert.Equal(inner, correlation.Current);
            Assert.Equal(outer, correlation.Current);
        }

        Assert.Null(correlation.Current);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(responseFactory(request));
        }
    }
}
