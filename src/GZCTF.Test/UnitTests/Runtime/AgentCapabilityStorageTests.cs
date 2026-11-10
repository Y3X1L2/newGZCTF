using System;
using System.Collections.Generic;
using System.IO;
using GZCTF.Agent.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class AgentCapabilityStorageTests
{

    [Fact]
    public void DockerStorage_UsesDaemonDirectoryAndFailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-docker-capacity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string? queried = null;
            Assert.Equal(70, AgentCapabilityService.ReadAvailableStorage(root, path =>
            {
                queried = path;
                return 70;
            }));
            Assert.Equal(root, queried);
            Assert.True(AgentCapabilityService.ReadAvailableStorage(root) > 0);
            Assert.Equal(0, AgentCapabilityService.ReadAvailableStorage(null));
            Assert.Equal(0, AgentCapabilityService.ReadAvailableStorage(Path.Combine(root, "missing")));
            Assert.Equal(0, AgentCapabilityService.ReadAvailableStorage(root,
                _ => throw new IOException("The data-root is unavailable.")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
