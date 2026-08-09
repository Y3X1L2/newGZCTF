using System.Text.Json;
using GZCTF.GuestControl.Contracts;

namespace GZCTF.GuestSupervisor.Lifecycle;

public sealed class GuestIntentStore(string stateRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path = Path.Combine(stateRoot, "intent.json");

    public async Task SaveAsync(GuestBootstrapIntent intent, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temporary, JsonSerializer.SerializeToUtf8Bytes(intent, JsonOptions), cancellationToken);
            await using (var stream = new FileStream(
                             temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                stream.Flush(flushToDisk: true);
            File.Move(temporary, _path, true);
            GuestCheckpointStore.Restrict(_path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<GuestBootstrapIntent> LoadAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<GuestBootstrapIntent>(
                   stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException("guest_intent_invalid");
    }
}
