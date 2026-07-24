using GZCTF.Agent.Models;
using GZCTF.Agent.Services.Vm;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/vms/scenario-artifacts")]
public sealed class VmScenarioArtifactController(VmScenarioArtifactService scenarios) : ControllerBase
{
    [HttpPost]
    public Task<CommitVmScenarioResponse> Commit(
        CommitVmScenarioRequest request,
        CancellationToken cancellationToken) => scenarios.CommitAsync(request, cancellationToken);
}
