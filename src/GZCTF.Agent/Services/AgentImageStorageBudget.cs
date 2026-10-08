using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services;

/// <summary>
/// Accounts only for active image writers' future bytes. Already allocated files are included
/// in filesystem free-space facts; runtime growth must be reserved by the main scheduler.
/// Leases are scoped to the actual writer, never to an HTTP caller waiting for a shared pull.
/// </summary>
public sealed class AgentImageStorageBudget
{
    readonly object _sync = new();
    readonly Dictionary<string, long> _reserved = new(StringComparer.Ordinal);
    readonly Func<string, AgentStorageSnapshot> _probe;
    readonly long _safetyMargin;
    public long DockerPullBudgetBytes { get; }
    public int DockerPullTimeoutSeconds { get; }

    public AgentImageStorageBudget(IOptions<AgentConfig> options)
        : this(options.Value.ImageStorage, AgentStorageProbe.Read) { }

    internal AgentImageStorageBudget(AgentImageStorageConfig config, Func<string, AgentStorageSnapshot> probe)
    {
        if (config.SafetyMarginBytes <= 0 || config.DockerPullBudgetBytes <= 0 ||
            config.DockerPullTimeoutSeconds is <= 0 or > 7200)
            throw new ArgumentOutOfRangeException(nameof(config),
                "Image storage budgets must be positive and Docker pull duration must be between 1 and 7200 seconds.");
        _safetyMargin = config.SafetyMarginBytes;
        DockerPullBudgetBytes = config.DockerPullBudgetBytes;
        DockerPullTimeoutSeconds = config.DockerPullTimeoutSeconds;
        _probe = probe;
    }

    public Lease Reserve(string path, long additionalBytes, string phase)
    {
        if (additionalBytes < 0) throw new ArgumentOutOfRangeException(nameof(additionalBytes));
        lock (_sync)
        {
            var facts = Read(path, phase);
            var reserved = _reserved.GetValueOrDefault(facts.FileSystemId);
            var usable = Math.Max(0, facts.AvailableBytes - _safetyMargin);
            if (additionalBytes > Math.Max(0, usable - reserved))
                throw Insufficient(phase, additionalBytes, facts.AvailableBytes, reserved);
            _reserved[facts.FileSystemId] = checked(reserved + additionalBytes);
            return new Lease(this, path, facts.FileSystemId, additionalBytes, phase);
        }
    }

    AgentStorageSnapshot Read(string path, string phase)
    {
        try { return _probe(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            throw new AgentOperationException("Storage", "image.storage_unavailable",
                $"Image storage cannot be measured during {phase}.", true,
                StatusCodes.Status503ServiceUnavailable, exception);
        }
    }

    AgentOperationException Insufficient(string phase, long required, long available, long reserved) =>
        new("Storage", "image.storage_capacity_insufficient",
            $"Image storage capacity is insufficient during {phase}: requiredAdditionalBytes={required}, " +
            $"availableBytes={available}, committedBytes={reserved}, safetyMarginBytes={_safetyMargin}.",
            true, StatusCodes.Status507InsufficientStorage);

    public sealed class Lease : IDisposable
    {
        readonly AgentImageStorageBudget _owner;
        readonly string _path;
        readonly string _fileSystemId;
        readonly string _phase;
        long _remaining;
        bool _disposed;

        internal Lease(AgentImageStorageBudget owner, string path, string fileSystemId, long bytes, string phase)
        { _owner = owner; _path = path; _fileSystemId = fileSystemId; _remaining = bytes; _phase = phase; }

        public void CheckBeforeWrite(int bytes)
        {
            lock (_owner._sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (bytes < 0 || bytes > _remaining)
                    throw new AgentOperationException("ImageTransfer", "image.size_mismatch",
                        "Image payload exceeds the admitted transfer size.", false);
                CheckCapacity();
            }
        }

        // Docker's daemon owns the writes. Keep its full conservative allowance until the
        // pull returns; do not guess that external changes in free space belong to this pull.
        public void CheckCapacity()
        {
            lock (_owner._sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var facts = _owner.Read(_path, _phase);
                if (facts.FileSystemId != _fileSystemId)
                    throw new AgentOperationException("Storage", "image.storage_changed",
                        "The image storage filesystem changed during the transfer.", true);
                var reserved = _owner._reserved.GetValueOrDefault(_fileSystemId);
                if (facts.AvailableBytes < _owner._safetyMargin ||
                    reserved > facts.AvailableBytes - _owner._safetyMargin)
                    throw _owner.Insufficient(_phase, _remaining, facts.AvailableBytes, reserved);
            }
        }

        public void RecordWritten(int bytes)
        {
            lock (_owner._sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (bytes < 0 || bytes > _remaining) throw new ArgumentOutOfRangeException(nameof(bytes));
                _remaining -= bytes;
                _owner._reserved[_fileSystemId] -= bytes;
            }
        }

        public void CompleteWrites()
        {
            lock (_owner._sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _owner._reserved[_fileSystemId] = _owner._reserved.GetValueOrDefault(_fileSystemId) - _remaining;
                _remaining = 0;
            }
        }

        public void Dispose()
        {
            lock (_owner._sync)
            {
                if (_disposed) return;
                _disposed = true;
                _owner._reserved[_fileSystemId] = _owner._reserved.GetValueOrDefault(_fileSystemId) - _remaining;
                if (_owner._reserved[_fileSystemId] == 0) _owner._reserved.Remove(_fileSystemId);
                _remaining = 0;
            }
        }
    }
}
