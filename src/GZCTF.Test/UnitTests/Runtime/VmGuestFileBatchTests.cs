using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Text;
using GZCTF.Agent.Services.Vm;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

/// <summary>
/// Guards the batched guest file transfer that replaces per-file QGA round trips.
/// Every additional round trip costs a full virsh process spawn, so the batch must
/// carry file modes itself instead of relying on follow-up chmod calls.
/// </summary>
public class VmGuestFileBatchTests
{
    [Fact]
    public void BuildTarArchive_CarriesEveryFileAndItsModeInOneArchive()
    {
        List<GuestFileEntry> entries =
        [
            new("scripts/install.sh", Encoding.UTF8.GetBytes("#!/bin/sh\necho ready\n"), "0700"),
            new("conf/app.conf", Encoding.UTF8.GetBytes("key=value\n"), "0600")
        ];

        var archive = GuestFileBatch.BuildTarArchive(entries);

        var seen = ReadEntries(archive);
        Assert.Equal(2, seen.Count);
        Assert.Equal("#!/bin/sh\necho ready\n", seen["scripts/install.sh"].Content);
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            seen["scripts/install.sh"].Mode);
        Assert.Equal("key=value\n", seen["conf/app.conf"].Content);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, seen["conf/app.conf"].Mode);
    }

    static Dictionary<string, (string Content, UnixFileMode Mode)> ReadEntries(byte[] archive)
    {
        var result = new Dictionary<string, (string, UnixFileMode)>(StringComparer.Ordinal);
        using var stream = new MemoryStream(archive);
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                continue;
            using var data = new MemoryStream();
            entry.DataStream?.CopyTo(data);
            result[entry.Name] = (Encoding.UTF8.GetString(data.ToArray()), entry.Mode);
        }

        return result;
    }
}
