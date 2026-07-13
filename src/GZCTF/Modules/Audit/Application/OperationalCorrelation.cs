namespace GZCTF.Modules.Audit.Application;

public sealed class OperationalCorrelation
{
    public const string HeaderName = "X-GZCTF-Correlation-Id";

    private static readonly AsyncLocal<State?> Ambient = new();

    public Guid? Current => Ambient.Value?.CorrelationId;

    public Guid Ensure()
    {
        var correlationId = Current ?? Guid.CreateVersion7();
        Ambient.Value = new State(correlationId);
        return correlationId;
    }

    public void Promote(Guid correlationId)
    {
        if (correlationId == Guid.Empty)
            throw new ArgumentException("Correlation id cannot be empty.", nameof(correlationId));
        Ambient.Value = new State(correlationId);
    }

    public IDisposable Begin(Guid? correlationId = null)
    {
        var previous = Ambient.Value;
        Ambient.Value = new State(correlationId is { } value && value != Guid.Empty
            ? value
            : Guid.CreateVersion7());
        return new Scope(previous);
    }

    private sealed record State(Guid CorrelationId);

    private sealed class Scope(State? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            Ambient.Value = previous;
            _disposed = true;
        }
    }
}
