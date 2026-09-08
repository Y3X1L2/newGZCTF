using System.Text.Json;
using System;
using System.Threading.Tasks;
using GZCTF.Controllers;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public class DeploymentCancellationResponseTests
{
    [Theory]
    [InlineData(DeploymentQueueTicketStatus.Running, "ticket_running")]
    [InlineData(DeploymentQueueTicketStatus.Succeeded, "ticket_not_cancellable")]
    [InlineData(DeploymentQueueTicketStatus.Failed, "ticket_not_cancellable")]
    public async Task NonCancelledTicketCannotBeReportedAsSuccess(DeploymentQueueTicketStatus status, string code)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var ticket = new DeploymentQueueTicket { Status = status };
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var queue = new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance);
        var controller = new DeploymentQueueController(context, queue, null!, NullLogger<DeploymentQueueController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var response = Assert.IsType<ConflictObjectResult>(await controller.Cancel(ticket.Id));
        Assert.Equal(409, response.StatusCode);
        var body = JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(status, (await context.DeploymentQueueTickets.AsNoTracking().SingleAsync()).Status);
    }
}
