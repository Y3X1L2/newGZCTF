using System.Collections.Concurrent;
using System.Text.Json;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.RuntimeSignals;

public sealed class AgentRuntimeSignalJournal(IOptions<AgentTeamLabConfig> options)
{
    private const long MaxJournalBytes = 1024 * 1024;
    private readonly string _root = Path.Combine(options.Value.RuntimeStateRoot, "runtime-signals");
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AgentRuntimeSignalModel> AppendAsync(
        AgentRuntimeSignalDraft draft,
        CancellationToken cancellationToken)
    {
        Validate(draft);
        var gate = _locks.GetOrAdd(draft.OperationId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_root);
            var path = JournalPath(draft.OperationId);
            var length = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (length >= MaxJournalBytes)
                throw new InvalidOperationException("The runtime signal journal reached its size limit.");
            var sequence = await ReadLastSequenceAsync(path, cancellationToken) + 1;
            var signal = new AgentRuntimeSignalModel(
                draft.OperationId,
                draft.RuntimeId,
                draft.Generation,
                draft.ResourceKind.Trim(),
                draft.ResourceId.Trim(),
                sequence,
                draft.Stage,
                draft.Outcome,
                DateTimeOffset.UtcNow,
                NullIfWhiteSpace(draft.ErrorCode),
                draft.Retryable,
                draft.Facts);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(signal, JsonOptions);
            await using var stream = new FileStream(
                path, FileMode.Append, FileAccess.Write, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return signal;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentRuntimeSignalModel>> ReadPendingAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var acknowledged = await ReadAcknowledgedAsync(operationId, cancellationToken);
        return (await ReadAllAsync(operationId, cancellationToken))
            .Where(signal => signal.Sequence > acknowledged)
            .ToArray();
    }

    public async Task<IReadOnlyList<AgentRuntimeSignalModel>> ReadAllAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(operationId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = JournalPath(operationId);
            if (!File.Exists(path)) return [];
            var result = new List<AgentRuntimeSignalModel>();
            await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var signal = JsonSerializer.Deserialize<AgentRuntimeSignalModel>(line, JsonOptions)
                             ?? throw new InvalidDataException("The runtime signal journal is corrupt.");
                result.Add(signal);
            }
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AcknowledgeAsync(
        Guid operationId,
        long sequence,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        var current = await ReadAcknowledgedAsync(operationId, cancellationToken);
        if (sequence <= current) return;
        var target = AckPath(operationId);
        var temporary = $"{target}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, sequence.ToString(), cancellationToken);
        File.Move(temporary, target, true);
    }

    public async Task DeleteAsync(Guid operationId)
    {
        var gate = _locks.GetOrAdd(operationId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            DeleteIfExists(JournalPath(operationId));
            DeleteIfExists(AckPath(operationId));
        }
        finally
        {
            gate.Release();
        }
        _locks.TryRemove(new KeyValuePair<Guid, SemaphoreSlim>(operationId, gate));
    }

    public async Task<int> DeleteAcknowledgedGenerationAsync(
        int runtimeId,
        int generation,
        CancellationToken cancellationToken)
    {
        var deleted = 0;
        foreach (var operationId in ListOperations())
        {
            var history = await ReadAllAsync(operationId, cancellationToken);
            if (history.Count == 0 || history[0].RuntimeId != runtimeId ||
                history[0].Generation != generation)
                continue;
            if ((await ReadPendingAsync(operationId, cancellationToken)).Count != 0)
                continue;
            await DeleteAsync(operationId);
            deleted++;
        }
        return deleted;
    }

    public IReadOnlyList<Guid> ListOperations()
    {
        if (!Directory.Exists(_root)) return [];
        return Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Select(value => Guid.TryParseExact(value, "N", out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Order()
            .ToArray();
    }

    private static async Task<long> ReadLastSequenceAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return 0;
        string? last = null;
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
            if (!string.IsNullOrWhiteSpace(line)) last = line;
        return last is null
            ? 0
            : JsonSerializer.Deserialize<AgentRuntimeSignalModel>(last, JsonOptions)?.Sequence ??
              throw new InvalidDataException("The runtime signal journal tail is corrupt.");
    }

    private async Task<long> ReadAcknowledgedAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var path = AckPath(operationId);
        if (!File.Exists(path)) return 0;
        var value = await File.ReadAllTextAsync(path, cancellationToken);
        return long.TryParse(value.Trim(), out var sequence) ? sequence : 0;
    }

    private static void Validate(AgentRuntimeSignalDraft draft)
    {
        if (draft.OperationId == Guid.Empty || draft.RuntimeId <= 0 || draft.Generation <= 0 ||
            string.IsNullOrWhiteSpace(draft.ResourceKind) || draft.ResourceKind.Length > 64 ||
            string.IsNullOrWhiteSpace(draft.ResourceId) || draft.ResourceId.Length > 256 ||
            draft.ErrorCode?.Length > 128 || draft.Facts is { Count: > 32 })
            throw new ArgumentException("The runtime signal exceeds its bounds.", nameof(draft));
        if (draft.Facts is null) return;
        foreach (var (key, value) in draft.Facts)
            if (string.IsNullOrWhiteSpace(key) || key.Length > 64 || value.Length > 256)
                throw new ArgumentException("The runtime signal facts exceed their bounds.", nameof(draft));
    }

    private string JournalPath(Guid operationId) => Path.Combine(_root, $"{operationId:N}.jsonl");
    private string AckPath(Guid operationId) => Path.Combine(_root, $"{operationId:N}.ack");
    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
