using System;
using System.Collections.Generic;
using System.IO;
using GZCTF.Agent.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class AgentCapabilityStorageTests
{
    [Fact]
    public void VmStorage_UsesBothConfiguredDirectoriesAndSmallerFreeSpace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-capacity-{Guid.NewGuid():N}");
        var images = Path.Combine(root, "images");
        var runtime = Path.Combine(root, "teamlab");
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(runtime);
        try
        {
            var queried = new List<string>();
            var available = AgentCapabilityService.ReadAvailableVmImageStorage(images, runtime, path =>
            {
                queried.Add(path);
                return path == images ? 180 : 90;
            });

            Assert.Equal(90, available);
            Assert.Equal(new[] { images, runtime }, queried);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void VmStorage_ReturnsZeroWhenEitherDirectoryIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-capacity-{Guid.NewGuid():N}");
        var images = Path.Combine(root, "images");
        var runtime = Path.Combine(root, "teamlab");
        Directory.CreateDirectory(images);
        try
        {
            Assert.Equal(0, AgentCapabilityService.ReadAvailableVmImageStorage(images, runtime, _ => 180));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
