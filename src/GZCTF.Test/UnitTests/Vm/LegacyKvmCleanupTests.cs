using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
    public void Dispose() => Directory.Delete(root, true);

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

    [Fact]
    public async Task ActiveUuidMismatchRetainsDomainAndAllFiles()
    {
        var provider = Provider();
        provider.Domains.Add(provider.OtherUuid, Domain(Name, provider.OtherUuid, Disk));
        Assert.False((await provider.DestroyAsync(Name, Guid.NewGuid().ToString(), default)).Success);
        Assert.True(File.Exists(Disk));
        Assert.DoesNotContain(provider.Commands, command => command.Contains(" destroy ", StringComparison.Ordinal));
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
        public readonly List<string> Commands = [];
        public readonly string OtherUuid = Guid.NewGuid().ToString();
        public bool InventoryFailure;
        public bool ChangedQuarantineIdentity;
        public string? ForeignBacking;
        public string? InactiveDomain;
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
                // Stable fixture file identity survives an atomic rename, as a real stat inode does.
                var identity = path == disk || path == quarantine ? "disk" : path == xml ? "xml" : path == baseFile ? "base" : path;
                if (path == quarantine && ChangedQuarantineIdentity) identity = "replacement";
                return Result(0, "fixture:" + identity + (arguments.Contains("%s", StringComparison.Ordinal) ? ":size:mtime" : ""));
            }
            if (command == "qemu-img")
            {
                var backing = path == disk || path == quarantine ? baseFile : Path.GetFileName(path) == "other.qcow2" ? ForeignBacking : null;
                return Result(0, JsonSerializer.Serialize(new Dictionary<string, object?>
                { ["filename"] = path, ["format"] = "qcow2", ["backing-filename"] = backing }));
            }
            throw new InvalidOperationException("Unexpected fixture command.");
        }
        static Task<CommandResult> Result(int code, string output, string error = "") => Task.FromResult(new CommandResult(code, output, error));
    }
}
