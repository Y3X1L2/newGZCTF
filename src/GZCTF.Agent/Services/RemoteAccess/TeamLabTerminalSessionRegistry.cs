using System.Collections.Concurrent;
using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services.RemoteAccess;

/// <summary>
/// Owns the cancellation lifetime of TeamLab container terminal sessions on one Agent.
/// A cancellation tombstone prevents a late WebSocket upgrade from reviving a session
/// that the control plane has already ended.
/// </summary>
public sealed class TeamLabTerminalSessionRegistry(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private static readonly TimeSpan RevocationRetention = TimeSpan.FromHours(2);
    private readonly ConcurrentDictionary<Guid, TerminalSession> _sessions = new();

    public Guid[] ActiveSessionIds() => _sessions.Where(item => item.Value.Attached != 0 &&
        !item.Value.Cancellation.IsCancellationRequested && item.Value.ExpiresAt > _clock.GetUtcNow())
        .Select(item => item.Key).ToArray();

    public CancellationToken Attach(Guid sessionId, DateTimeOffset expiresAt)
    {
        var now = _clock.GetUtcNow();
        Prune(now);
        if (expiresAt <= now)
            throw TerminalUnavailable("remote_access.terminal_expired", "The terminal session has expired.");

        var created = new TerminalSession(expiresAt);
        var session = _sessions.GetOrAdd(sessionId, created);
        if (!ReferenceEquals(created, session))
            created.Cancellation.Dispose();
        lock (session)
        {
            if (session.Cancellation.IsCancellationRequested)
                throw TerminalUnavailable("remote_access.terminal_ended", "The terminal session has ended.");
            if (session.ExpiresAt <= now)
            {
                throw TerminalUnavailable("remote_access.terminal_expired", "The terminal session has expired.");
            }
            if (Interlocked.CompareExchange(ref session.Attached, 1, 0) != 0)
                throw TerminalUnavailable("remote_access.terminal_connected", "The terminal session is already connected.");
            return session.Cancellation.Token;
        }
    }

    public void RegisterCleanup(Guid sessionId, Func<CancellationToken, Task> cleanup)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            lock (session)
                session.Cleanup = cleanup;
    }

    public async Task CancelAndWaitAsync(Guid sessionId)
    {
        Cancel(sessionId);
        if (!_sessions.TryGetValue(sessionId, out var session))
            return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        if (Volatile.Read(ref session.Attached) != 0)
            await session.Completion.Task.WaitAsync(timeout.Token);
        await session.CleanupGate.WaitAsync(timeout.Token);
        try
        {
            if (session.Completion.Task.IsCompletedSuccessfully && session.Completion.Task.Result is not null &&
                !session.CleanupSucceeded)
            {
                if (session.Cleanup is null)
                    throw TerminalUnavailable("remote_access.cleanup_pending", "Terminal cleanup is incomplete.");
                await session.Cleanup(timeout.Token);
                lock (session)
                    session.CleanupSucceeded = true;
            }
        }
        finally { session.CleanupGate.Release(); }
    }

    public void Detach(Guid sessionId, Exception? failure = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return;
        lock (session)
        {
            session.Cancellation.Cancel();
            // Execution registers cleanup before creating a process. Earlier failures
            // (validation or WebSocket upgrade) have no process resource to reclaim.
            session.Completion.TrySetResult(session.Cleanup is null ? null : failure);
            Interlocked.Exchange(ref session.Attached, 0);
        }
    }

    public void Cancel(Guid sessionId)
    {
        var now = _clock.GetUtcNow();
        Prune(now);
        var session = _sessions.GetOrAdd(sessionId, _ => new TerminalSession(now.Add(RevocationRetention)));
        lock (session)
            session.Cancellation.Cancel();
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (sessionId, session) in _sessions)
        {
            lock (session)
            {
                if (session.ExpiresAt > now)
                    continue;
                session.Cancellation.Cancel();
                // Expiration revokes access; it must not discard unfinished process cleanup.
                var cleanupPending = session.Completion.Task.IsCompletedSuccessfully &&
                    session.Completion.Task.Result is not null && !session.CleanupSucceeded;
                if (session.Attached == 0 && !cleanupPending && session.ExpiresAt.Add(RevocationRetention) <= now)
                    _sessions.TryRemove(new KeyValuePair<Guid, TerminalSession>(sessionId, session));
            }
        }
    }

    private static AgentOperationException TerminalUnavailable(string code, string message) =>
        new("RemoteAccess", code, message, false);

    private sealed class TerminalSession(DateTimeOffset expiresAt)
    {
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public CancellationTokenSource Cancellation { get; } = new();
        public int Attached;
        public TaskCompletionSource<Exception?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<CancellationToken, Task>? Cleanup;
        public SemaphoreSlim CleanupGate { get; } = new(1, 1);
        public bool CleanupSucceeded;
    }
}
