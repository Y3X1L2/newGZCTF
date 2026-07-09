using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class AgentPublishConfigurationTests
{
    [Fact]
    public void AgentProject_PublishesSelfContainedSingleFile()
    {
        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "GZCTF.Agent", "GZCTF.Agent.csproj"));

        var project = XDocument.Load(projectPath);
        var properties = project.Root!
            .Elements("PropertyGroup")
            .Elements()
            .ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);
        var selfContained = project.Root!
            .Elements("PropertyGroup")
            .Elements("SelfContained")
            .Single();

        Assert.Equal("true", properties["PublishSingleFile"]);
        Assert.Equal("true", selfContained.Value);
        Assert.Equal("'$(RuntimeIdentifier)' != ''", selfContained.Attribute("Condition")?.Value);
        Assert.Equal("true", properties["IncludeNativeLibrariesForSelfExtract"]);
        Assert.Equal("true", properties["EnableCompressionInSingleFile"]);
    }
}
