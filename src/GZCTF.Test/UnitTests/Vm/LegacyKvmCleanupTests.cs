using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Services.Vm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.Vm;

public sealed class LegacyKvmCleanupTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "legacy-vm-" + Guid.NewGuid().ToString("N"));
    readonly string externalRoot = Path.Combine(Path.GetTempPath(), "legacy-vm-dependent-" + Guid.NewGuid().ToString("N"));
    const string Name = "vm_c40_u019e5fa3-34ba-7697-8949-cfa343cde908";
    string Disk => Path.Combine(root, Name + ".qcow2");
    string Xml => Path.Combine(root, Name + ".xml");
    string Base => Path.Combine(root, "1.qcow2");
    string Quarantine => Path.Combine(root, ".legacy-vm-" + Name + ".pending-delete");
    public LegacyKvmCleanupTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Base, "canonical base retained");
        File.WriteAllText(Disk, "owned instance changes");
        File.WriteAllText(Xml, Domain(Name, null, Disk));
    }
    public void Dispose()
    {
        Directory.Delete(root, true);
        if (Directory.Exists(externalRoot)) Directory.Delete(externalRoot, true);
    }

    [Fact]
    public async Task MissingDomain_ReclaimsOnlyItsProvenOverlayAndXmlAndIsIdempotent()
    {
        var provider = Provider();
        Assert.True((await provider.DestroyAsync(Name, default)).Success);
        Assert.False(File.Exists(Disk)); Assert.False(File.Exists(Xml)); Assert.False(File.Exists(Quarantine));
        Assert.True(File.Exists(Base));
        Assert.True((await provider.DestroyAsync(Name, default)).Success);
        Assert.DoesNotContain(provider.Commands, command => command.Contains(" destroy ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingDomainCannotHideOtherVmUseOrBackingDependents(bool backingDependent)
    {
        var provider = Provider();
        if (backingDependent)
        {
            File.WriteAllText(Path.Combine(root, "other.qcow2"), "other instance");
            provider.ForeignBacking = Disk;
        }
        else provider.Domains.Add(provider.OtherUuid, Domain("other-vm", provider.OtherUuid, Disk));
        var result = await provider.DestroyAsync(Name, default);
        Assert.False(result.Success);
        Assert.Contains(backingDependent ? "backs onto" : "Another VM", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.True(File.Exists(Disk)); Assert.True(File.Exists(Xml)); Assert.True(File.Exists(Base));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DomainDiskOutsideImageRootWithBackingDependencyRetainsTheLegacyOverlay(bool inactive, bool nestedRelative)
    {
        Directory.CreateDirectory(externalRoot);
        var dependent = Path.Combine(externalRoot, "current.qcow2");
        File.WriteAllText(dependent, "another runtime disk");
        var provider = Provider();
        var chain = nestedRelative
            ? new object[]
            {
                new Dictionary<string, object?> { ["filename"] = dependent, ["backing-filename"] = "intermediate/middle.qcow2" },
                new Dictionary<string, object?> { ["filename"] = Path.Combine(externalRoot, "intermediate", "middle.qcow2"),
                    ["backing-filename"] = Path.GetRelativePath(Path.Combine(externalRoot, "intermediate"), Disk) }
            }
            : new object[] { new Dictionary<string, object?> { ["filename"] = dependent, ["backing-filename"] = Disk } };
        provider.BackingChains[dependent] = JsonSerializer.Serialize(chain);
        provider.Domains.Add(provider.OtherUuid, Domain("other-vm", provider.OtherUuid, inactive ? Base : dependent));
        if (inactive) provider.InactiveDomain = Domain("other-vm", provider.OtherUuid, dependent);
        var diskBytes = File.ReadAllBytes(Disk); var xmlBytes = File.ReadAllBytes(Xml);

        var result = await provider.DestroyAsync(Name, default);

        Assert.False(result.Success);
        Assert.Contains("backs onto", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal(diskBytes, File.ReadAllBytes(Disk)); Assert.Equal(xmlBytes, File.ReadAllBytes(Xml));
        Assert.Equal("another runtime disk", File.ReadAllText(dependent)); Assert.True(File.Exists(Base));
        Assert.DoesNotContain(provider.Commands, command => command.Contains(" destroy ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ActiveUuidMismatchRetainsDomainAndAllFiles()
    {
        var provider = Provider();
        var expected = Guid.NewGuid().ToString();
        File.WriteAllText(Xml, Domain(Name, expected, Disk));
        provider.Domains.Add(provider.OtherUuid, Domain(Name, provider.OtherUuid, Disk));
        Assert.False((await provider.DestroyAsync(Name, expected, default)).Success);
        Assert.True(File.Exists(Disk));
        Assert.DoesNotContain(provider.Commands, command => command.Contains(" destroy ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task MissingDomainWithUnprovenCapturedUuidRetainsAllBytes(bool quarantined, bool capturedUuid)
    {
        File.WriteAllText(Xml, Domain(Name, capturedUuid ? Guid.NewGuid().ToString() : null, Disk));
        if (quarantined) File.Move(Disk, Quarantine);
        var diskBytes = File.ReadAllBytes(quarantined ? Quarantine : Disk);
        var xmlBytes = File.ReadAllBytes(Xml);
        var provider = Provider();

        var result = await provider.DestroyAsync(Name, Guid.NewGuid().ToString(), default);

        Assert.False(result.Success);
        Assert.Contains("native identity differs", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal(diskBytes, File.ReadAllBytes(quarantined ? Quarantine : Disk));
        Assert.Equal(xmlBytes, File.ReadAllBytes(Xml));
        Assert.True(File.Exists(Base));
        Assert.DoesNotContain(provider.Commands, command => command.StartsWith("virsh", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingDomainWithMatchingCapturedUuidStillReclaimsItsOwnFiles()
    {
        var uuid = Guid.NewGuid().ToString();
        File.WriteAllText(Xml, Domain(Name, uuid, Disk));

        Assert.True((await Provider().DestroyAsync(Name, uuid, default)).Success);

        Assert.False(File.Exists(Disk)); Assert.False(File.Exists(Xml)); Assert.True(File.Exists(Base));
    }

    [Theory]
    [InlineData("4")]
    [InlineData("")]
    [InlineData("invalid")]
    public async Task MissingDomainWithAgentSidecarAndLegacyXmlRetainsTheNewOverlay(string generation)
    {
        var sidecar = Path.Combine(root, Name + ".generation");
        File.WriteAllText(sidecar, generation);
        File.WriteAllText(Disk, "new managed Agent overlay with stale legacy ownership XML");
        var xmlBytes = File.ReadAllBytes(Xml);
        var provider = Provider();

        var result = await provider.DestroyAsync(Name, default);

        Assert.False(result.Success);
        Assert.Contains("Agent identity-aware cleanup", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal("new managed Agent overlay with stale legacy ownership XML", File.ReadAllText(Disk));
        Assert.Equal(xmlBytes, File.ReadAllBytes(Xml));
        Assert.Equal(generation, File.ReadAllText(sidecar));
        Assert.True(File.Exists(Base)); Assert.False(File.Exists(Quarantine));
        Assert.Empty(provider.Commands);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("cloud-init")]
    [InlineData("runtime-injection")]
    [InlineData("vm-runtime")]
    public async Task AgentRuntimeDirectoryProtectsAnOrphanBeforeTheSidecarIsWritten(string marker)
    {
        var path = marker == "generation" ? Path.Combine(root, Name + ".generation") : Path.Combine(root, marker, Name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "owned-metadata"), "Agent runtime identity");
        var diskBytes = File.ReadAllBytes(Disk);
        var xmlBytes = File.ReadAllBytes(Xml);
        var provider = Provider();

        var result = await provider.DestroyAsync(Name, default);

        Assert.False(result.Success);
        Assert.Contains("Agent identity-aware cleanup", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal(diskBytes, File.ReadAllBytes(Disk)); Assert.Equal(xmlBytes, File.ReadAllBytes(Xml));
        Assert.True(File.Exists(Path.Combine(path, "owned-metadata"))); Assert.True(File.Exists(Base));
        Assert.Empty(provider.Commands);
    }

    [Theory]
    [InlineData(false, "description")]
    [InlineData(false, "metadata")]
    [InlineData(true, "description")]
    [InlineData(true, "metadata")]
    public async Task ManagedXmlRequiresAgentCleanupEvenWithMatchingNativeUuid(bool liveDomain, string marker)
    {
        var provider = Provider();
        var legacy = Domain(Name, provider.OtherUuid, Disk);
        File.WriteAllText(Xml, legacy);
        var managed = XDocument.Parse(legacy);
        managed.Root!.Add(new XElement(marker, marker == "description" ? "gzctf-generation=4" : new XElement("runtime", "owned")));
        if (liveDomain) provider.Domains.Add(provider.OtherUuid, managed.ToString());
        else File.WriteAllText(Xml, managed.ToString());
        var xmlBytes = File.ReadAllBytes(Xml);

        var result = await provider.DestroyAsync(Name, provider.OtherUuid, default);

        Assert.False(result.Success);
        Assert.Contains("Agent cleanup path", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal(xmlBytes, File.ReadAllBytes(Xml)); Assert.True(File.Exists(Disk)); Assert.True(File.Exists(Base));
        Assert.Equal(liveDomain ? 1 : 0, provider.Domains.Count);
        Assert.DoesNotContain(provider.Commands, command => command.Contains(" destroy ", StringComparison.Ordinal) ||
            command.Contains(" undefine ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentSidecarAppearingDuringCleanupRestoresTheQuarantinedDisk()
    {
        var provider = Provider();
        provider.AgentSidecarDuringCleanup = true;

        var result = await provider.DestroyAsync(Name, default);

        Assert.False(result.Success);
        Assert.Contains("Agent identity-aware cleanup", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal("owned instance changes", File.ReadAllText(Disk));
        Assert.True(File.Exists(Xml)); Assert.True(File.Exists(Base)); Assert.False(File.Exists(Quarantine));
        Assert.Equal("4", File.ReadAllText(Path.Combine(root, Name + ".generation")));
    }

    [Fact]
    public async Task CapturedXmlOutsideInstanceDirectoryIsRejected()
    {
        File.WriteAllText(Xml, Domain(Name, null, Path.Combine(root, "..", "outside.qcow2")));
        var provider = Provider();
        Assert.False((await provider.DestroyAsync(Name, default)).Success);
        Assert.True(File.Exists(Disk)); Assert.True(File.Exists(Base));
    }

    [Fact]
    public async Task InventoryFailureRetainsOriginalBytes()
    {
        var provider = Provider(); provider.InventoryFailure = true;
        Assert.False((await provider.DestroyAsync(Name, default)).Success);
        Assert.True(File.Exists(Disk)); Assert.True(File.Exists(Xml));
    }

    [Fact]
    public async Task ChangedInodeAfterRenameRetainsAndRestoresTheFile()
    {
        var provider = Provider(); provider.ChangedQuarantineIdentity = true;
        Assert.False((await provider.DestroyAsync(Name, default)).Success);
        Assert.True(File.Exists(Disk)); Assert.True(File.Exists(Xml)); Assert.False(File.Exists(Quarantine));
    }

    [Fact]
    public async Task ExactPinnedNativeUuidIsDestroyedAndInstanceFilesAreReclaimed()
    {
        var provider = Provider();
        File.WriteAllText(Xml, Domain(Name, provider.OtherUuid, Disk));
        provider.Domains.Add(provider.OtherUuid, Domain(Name, provider.OtherUuid, Disk));
        Assert.True((await provider.DestroyAsync(Name, provider.OtherUuid, default)).Success);
        Assert.Empty(provider.Domains);
        Assert.Contains(provider.Commands, command => command.Contains(" destroy " + provider.OtherUuid, StringComparison.Ordinal));
        Assert.True(File.Exists(Base)); Assert.False(File.Exists(Disk)); Assert.False(File.Exists(Xml));
    }

    [Fact]
    public async Task InactiveForeignDefinitionStillProtectsItsInstanceDisk()
    {
        var provider = Provider();
        provider.Domains.Add(provider.OtherUuid, Domain("other-vm", provider.OtherUuid, Base));
        provider.InactiveDomain = Domain("other-vm", provider.OtherUuid, Disk);
        Assert.False((await provider.DestroyAsync(Name, default)).Success);
        Assert.True(File.Exists(Disk)); Assert.True(File.Exists(Xml));
    }

    [Fact]
    public async Task OriginalXmlAndQuarantinedDiskResumeWithoutTouchingCanonicalBase()
    {
        File.Move(Disk, Quarantine);
        Assert.True((await Provider().DestroyAsync(Name, default)).Success);
        Assert.False(File.Exists(Quarantine)); Assert.False(File.Exists(Xml)); Assert.True(File.Exists(Base));
    }

    [Fact]
    public async Task BothOriginalAndQuarantineAreAmbiguousAndRemainIntact()
    {
        File.WriteAllText(Quarantine, "another file");
        Assert.False((await Provider().DestroyAsync(Name, default)).Success);
        Assert.True(File.Exists(Disk)); Assert.True(File.Exists(Quarantine)); Assert.True(File.Exists(Xml));
    }

    ProofCommands Provider() => new(root, Disk, Xml, Base, Quarantine);
    static string Domain(string name, string? uuid, string disk) =>
        $"<domain><name>{name}</name>{(uuid is null ? "" : "<uuid>" + uuid + "</uuid>")}<devices><disk device='disk'><source file='{System.Security.SecurityElement.Escape(disk)}'/></disk></devices></domain>";

    sealed class ProofCommands(string root, string disk, string xml, string baseFile, string quarantine)
        : KvmProvider(Options.Create(new KvmSettings { ImageStoragePath = root }), NullLogger<KvmProvider>.Instance)
    {
        public readonly Dictionary<string, string> Domains = [];
        public readonly Dictionary<string, string> BackingChains = [];
        public readonly List<string> Commands = [];
        public readonly string OtherUuid = Guid.NewGuid().ToString();
        public bool InventoryFailure;
        public bool ChangedQuarantineIdentity;
        public bool AgentSidecarDuringCleanup;
        public string? ForeignBacking;
        public string? InactiveDomain;
        internal override string AgentRuntimeDirectory(string vmName) => Path.Combine(root, "vm-runtime", vmName);
        internal override Task<CommandResult> RunCommandAsync(string command, string arguments)
        {
            Commands.Add(command + " " + arguments);
            if (command == "virsh")
            {
                if (InventoryFailure) return Result(1, "", "inventory unavailable");
                if (arguments.Contains("list --all --uuid", StringComparison.Ordinal)) return Result(0, string.Join('\n', Domains.Keys));
                var uuid = Regex.Match(arguments, @"[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}").Value;
                if (arguments.Contains("dumpxml", StringComparison.Ordinal))
                    return Result(0, arguments.Contains("--inactive", StringComparison.Ordinal) && InactiveDomain is not null ? InactiveDomain : Domains[uuid]);
                if (arguments.Contains("undefine", StringComparison.Ordinal)) { Domains.Remove(uuid); return Result(0, ""); }
                if (arguments.Contains("domstate", StringComparison.Ordinal)) return Result(0, "running");
                return Result(0, "");
            }
            var path = Regex.Matches(arguments, "\"([^\"]*)\"")[^1].Groups[1].Value;
            if (command == "stat")
            {
                if (path == quarantine && AgentSidecarDuringCleanup)
                    File.WriteAllText(Path.Combine(root, Name + ".generation"), "4");
                // Stable fixture file identity survives an atomic rename, as a real stat inode does.
                var identity = path == disk || path == quarantine ? "disk" : path == xml ? "xml" : path == baseFile ? "base" : path;
                if (path == quarantine && ChangedQuarantineIdentity) identity = "replacement";
                return Result(0, "fixture:" + identity + (arguments.Contains("%s", StringComparison.Ordinal) ? ":size:mtime" : ""));
            }
            if (command == "qemu-img")
            {
                if (BackingChains.TryGetValue(path, out var chain)) return Result(0, chain);
                var backing = path == disk || path == quarantine ? baseFile : Path.GetFileName(path) == "other.qcow2" ? ForeignBacking : null;
                return Result(0, JsonSerializer.Serialize(new Dictionary<string, object?>
                { ["filename"] = path, ["format"] = "qcow2", ["backing-filename"] = backing }));
            }
            throw new InvalidOperationException("Unexpected fixture command.");
        }
        static Task<CommandResult> Result(int code, string output, string error = "") => Task.FromResult(new CommandResult(code, output, error));
    }
}
