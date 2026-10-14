using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Middlewares;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Observability;

public sealed class AdminMutationAuditTests
{
    [Theory]
    [InlineData(400, OperationalEventOutcome.Failed)]
    [InlineData(403, OperationalEventOutcome.Failed)]
    [InlineData(409, OperationalEventOutcome.Failed)]
    [InlineData(500, OperationalEventOutcome.Failed)]
    [InlineData(202, OperationalEventOutcome.Pending)]
    [InlineData(200, OperationalEventOutcome.Succeeded)]
    public async Task Mutation_UsesDeclaredHttpResultBeforeItExecutes(int status, OperationalEventOutcome outcome)
    {
        var draft = await ExecuteAsync(new ObjectResult(new { message = "result" }) { StatusCode = status });
        Assert.Equal(outcome, draft.Outcome);
        Assert.Equal(status, draft.Detail!["httpStatus"]);
        Assert.Equal(outcome == OperationalEventOutcome.Failed, draft.ErrorCategory is not null);
        if (status == 202) Assert.EndsWith("accepted.", draft.Message);
    }

    [Fact]
    public async Task Mutation_StatusCodeResultAndExceptionRemainFailures()
    {
        Assert.Equal(OperationalEventOutcome.Failed, (await ExecuteAsync(new StatusCodeResult(500))).Outcome);
        Assert.Equal(OperationalEventOutcome.Failed,
            (await ExecuteAsync(null, new InvalidOperationException("test failure"))).Outcome);
        Assert.Equal(OperationalEventOutcome.Failed, (await ExecuteAsync(new ForbidResult())).Outcome);
        Assert.Equal(403, (await ExecuteAsync(new ForbidResult())).Detail!["httpStatus"]);
        Assert.Equal(401, (await ExecuteAsync(new ChallengeResult())).Detail!["httpStatus"]);
    }

    [Fact]
    public async Task Mutation_UserGuidCannotBecomeNodeId()
    {
        var id = Guid.NewGuid();
        var user = await ExecuteAsync(new OkResult(), controller: "Admin", routeId: id);
        Assert.Null(user.WorkerNodeId);
        var node = await ExecuteAsync(new OkResult(), controller: "Nodes", routeId: id);
        Assert.Equal(id, node.WorkerNodeId);
        Assert.Equal("worker-node", node.ResourceType);
    }

    private static async Task<OperationalEventDraft> ExecuteAsync(IActionResult? result,
        Exception? exception = null, string controller = "Admin", Guid? routeId = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "PUT";
        http.Request.Path = "/api/admin/test";
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Name, "admin")
        }, "test"));
        var routes = new RouteData();
        routes.Values["id"] = routeId;
        var action = new ActionContext(http, routes, new ControllerActionDescriptor
        {
            ControllerName = controller, ActionName = "Update"
        }, new ModelStateDictionary());
        var filters = new List<IFilterMetadata> { new RequireAdminAttribute() };
        var executing = new ActionExecutingContext(action, filters, new Dictionary<string, object?>(), new object());
        OperationalEventDraft? recorded = null;
        var writer = new Mock<IOperationalEventWriter>();
        writer.Setup(value => value.AppendAndSaveAsync(It.IsAny<OperationalEventDraft>(), It.IsAny<CancellationToken>()))
            .Callback<OperationalEventDraft, CancellationToken>((draft, _) => recorded = draft)
            .ReturnsAsync(new OperationalEvent());
        var filter = new AdminMutationAuditFilter(new OperationalCorrelation(), writer.Object,
            NullLogger<AdminMutationAuditFilter>.Instance);
        await filter.OnActionExecutionAsync(executing, () => Task.FromResult(new ActionExecutedContext(action,
            filters, new object()) { Result = result, Exception = exception }));
        return Assert.IsType<OperationalEventDraft>(recorded);
    }
}
