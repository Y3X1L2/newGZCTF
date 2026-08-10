using System.Collections.Concurrent;
using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services.RemoteAccess;

/// <summary>
/// Owns the cancellation lifetime of TeamLab container terminal sessions on one Agent.
/// A cancellation tombstone prevents a late WebSocket upgrade from reviving a session
/// that the control plane has already ended.
/// </summary>
public sealed class TeamLabTerminalSessionRegistry
{
    private static readonly TimeSpan RevocationRetention = TimeSpan.FromHours(2);
    private readonly ConcurrentDictionary<Guid, TerminalSession> _sessions = new();

    public CancellationToken Attach(Guid sessionId, DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        Prune(now);
        if (expiresAt <= now)
            throw TerminalUnavailable("remote_access.terminal_expired", "The terminal session has expired.");

        var created = new TerminalSession(expiresAt);
        var session = _sessions.GetOrAdd(sessionId, created);
        if (session.Cancellation.IsCancellationRequested)
            throw TerminalUnavailable("remote_access.terminal_ended", "The terminal session has ended.");
        if (session.ExpiresAt <= now)
        {
            _sessions.TryRemove(new KeyValuePair<Guid, TerminalSession>(sessionId, session));
            throw TerminalUnavailable("remote_access.terminal_expired", "The terminal session has expired.");
        }
        if (Interlocked.CompareExchange(ref session.Attached, 1, 0) != 0)
            throw TerminalUnavailable("remote_access.terminal_connected", "The terminal session is already connected.");
        return session.Cancellation.Token;
    }

    public void Detach(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return;
        Interlocked.Exchange(ref session.Attached, 0);
        if (!session.Cancellation.IsCancellationRequested)
            _sessions.TryRemove(new KeyValuePair<Guid, TerminalSession>(sessionId, session));
    }

    public void Cancel(Guid sessionId)
    {
        var now = DateTimeOffset.UtcNow;
        Prune(now);
        var session = _sessions.GetOrAdd(sessionId, _ => new TerminalSession(now.Add(RevocationRetention)));
        session.Cancellation.Cancel();
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (sessionId, session) in _sessions)
        {
            if (session.ExpiresAt <= now && _sessions.TryRemove(new KeyValuePair<Guid, TerminalSession>(sessionId, session)))
                session.Cancellation.Cancel();
        }
    }

    private static AgentOperationException TerminalUnavailable(string code, string message) =>
        new("RemoteAccess", code, message, false);

    private sealed class TerminalSession(DateTimeOffset expiresAt)
    {
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public CancellationTokenSource Cancellation { get; } = new();
        public int Attached;
    }
}
