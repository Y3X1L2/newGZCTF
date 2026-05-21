using System;
using Xunit;
using GZCTF.Models.Data;

namespace GZCTF.Test.UnitTests.Docker;

public class ImageTemplateTests
{
    [Fact]
    public void ImageTemplate_CanStoreDockerRegistryUrl()
    {
        var template = new ImageTemplate
        {
            Name = "test-image",
            ImageType = ImageType.Docker,
            RegistryUrl = "docker.io/library/nginx:latest",
            OSType = OSType.Linux,
            Status = ImageStatus.Ready,
        };
        Assert.Equal(ImageType.Docker, template.ImageType);
        Assert.Equal("docker.io/library/nginx:latest", template.RegistryUrl);
    }

    [Fact]
    public void ImageTemplate_CanStoreQcow2Path()
    {
        var template = new ImageTemplate
        {
            Name = "windows-server",
            ImageType = ImageType.Qcow2,
            LocalFilePath = "./images/abc/disk.qcow2",
            OriginalArchiveName = "win2012.zip",
            OSType = OSType.Windows,
            Status = ImageStatus.Ready,
        };
        Assert.Equal(ImageType.Qcow2, template.ImageType);
        Assert.Equal("./images/abc/disk.qcow2", template.LocalFilePath);
    }
}
