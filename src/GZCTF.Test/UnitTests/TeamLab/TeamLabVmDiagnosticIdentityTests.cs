using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.TeamLab.Contracts;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabVmDiagnosticIdentityTests
{
    [Fact]
    public async Task OversizedOutputIsRejected()
    {
        using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 512 * 1024 + 1))));
        await Assert.ThrowsAsync<AgentOperationException>(() => KvmService.ReadDiagnosticTextAsync(reader, default));
    }

    [Fact]
    public async Task NormalOutputIsPreserved()
    {
        using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("running\n")));
        Assert.Equal("running\n", await KvmService.ReadDiagnosticTextAsync(reader, default));
    }

    private static readonly Guid NativeId = Guid.Parse("52af8621-ab23-4440-b589-22949d872127");
    private static TeamLabVmDiagnosticsRequest Request => new("teamlab-vm", 3, NativeId);

    [Fact]
    public void ExactBoundIdentityIsAccepted() => KvmService.RequireDiagnosticIdentity(
        $"<domain><name>teamlab-vm</name><uuid>{NativeId}</uuid><description>gzctf-generation=3 gzctf-execution-plan=v2</description></domain>", Request);

    [Theory]
    [InlineData("other-vm", 3)]
    [InlineData("teamlab-vm", 30)]
    public void ForeignNameOrGenerationIsRejected(string name, int generation) =>
        Assert.Throws<AgentOperationException>(() => KvmService.RequireDiagnosticIdentity(
            $"<domain><name>{name}</name><uuid>{NativeId}</uuid><description>gzctf-generation={generation}</description></domain>", Request));

    [Fact]
    public void ForeignUuidIsRejected() => Assert.Throws<AgentOperationException>(() => KvmService.RequireDiagnosticIdentity(
        $"<domain><name>teamlab-vm</name><uuid>{Guid.NewGuid()}</uuid><description>gzctf-generation=3</description></domain>", Request));

    [Fact]
    public void DtdIsRejected() => Assert.Throws<System.Xml.XmlException>(() => KvmService.RequireDiagnosticIdentity(
        "<!DOCTYPE domain [<!ENTITY secret SYSTEM 'file:///invalid'>]><domain><name>&secret;</name></domain>", Request));
}
