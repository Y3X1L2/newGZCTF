using System;
using GZCTF.Agent.Services.Vm;
using Xunit;

namespace GZCTF.Test.UnitTests.Vm;

/// <summary>
/// A cached VM template is the backing file of every overlay created from it, and that link lives
/// only in qcow2 metadata. Misreading it means deleting a file that leaves other games' VMs
/// permanently unbootable, so parsing must fail closed rather than report "no reference".
/// </summary>
public class VmImageBackingChainTests
{
    [Fact]
    public void PrefersTheResolvedBackingPath()
    {
        // full-backing-filename is already resolved against the overlay's directory.
        const string json = """
        {
          "filename": "/var/lib/gzctf/images/tl42-dc01.qcow2",
          "backing-filename": "7.qcow2",
          "full-backing-filename": "/var/lib/gzctf/images/7.qcow2"
        }
        """;

        Assert.Equal("/var/lib/gzctf/images/7.qcow2", VmImageBackingChainInspector.ParseBackingFile(json));
    }

    [Fact]
    public void FallsBackToTheRelativeBackingPath()
    {
        const string json = """
        {"filename": "/var/lib/gzctf/images/tl42-dc01.qcow2", "backing-filename": "7.qcow2"}
        """;

        Assert.Equal("7.qcow2", VmImageBackingChainInspector.ParseBackingFile(json));
    }

    [Fact]
    public void ReportsNoBackingFileForAStandaloneImage()
    {
        const string json = """{"filename": "/var/lib/gzctf/images/7.qcow2", "format": "qcow2"}""";

        Assert.Null(VmImageBackingChainInspector.ParseBackingFile(json));
    }

    [Fact]
    public void TreatsAnEmptyBackingFileAsAbsent()
    {
        const string json = """{"filename": "/var/lib/gzctf/images/7.qcow2", "backing-filename": ""}""";

        Assert.Null(VmImageBackingChainInspector.ParseBackingFile(json));
    }

    [Fact]
    public void UnreadableOutputRaisesRatherThanReportingNoReference()
    {
        // Returning null here would let an irreversible delete proceed on unknown information.
        Assert.Throws<InvalidOperationException>(
            () => VmImageBackingChainInspector.ParseBackingFile("not json"));
    }
}
