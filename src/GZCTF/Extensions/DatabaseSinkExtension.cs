using System.Collections.Concurrent;
using System.Diagnostics;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace GZCTF.Extensions;

public static class DatabaseSinkExtension
{
    extension(LoggerSinkConfiguration loggerConfiguration)
    {
        public LoggerConfiguration Database(IServiceProvider serviceProvider) =>
            loggerConfiguration.Sink(new DatabaseSink(serviceProvider), LogEventLevel.Information);
    }
}

public sealed class DatabaseSink : ILogEventSink, IDisposable
{
    private const int FlushBatchSize = 50;
    private const int MaxFlushBatchSize = 500;
    private const int MaxBufferedLogs = 10_000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentQueue<LogModel> _buffer = [];
    private readonly SemaphoreSlim _flushSignal = new(0, 1);
    private readonly IServiceProvider _serviceProvider;
    private readonly CancellationTokenSource _forceStop = new();
    private readonly Task _writerTask;
    private int _bufferedCount;
    private int _stopping;

    public DatabaseSink(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _writerTask = Task.Run(() => WriteToDatabaseAsync(_forceStop.Token));
    }

    public void Emit(LogEvent logEvent)
    {
        if (Volatile.Read(ref _stopping) != 0)
            return;

        while (Volatile.Read(ref _bufferedCount) >= MaxBufferedLogs &&
               _buffer.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _bufferedCount);
            DatabaseLogSinkMetrics.RecordDropped();
        }

        _buffer.Enqueue(LogModelFactory.FromLogEvent(logEvent));
        var buffered = Interlocked.Increment(ref _bufferedCount);
        DatabaseLogSinkMetrics.SetBuffered(buffered);
        if (buffered >= FlushBatchSize)
            SignalFlush();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
            return;

        SignalFlush();
        if (!_writerTask.Wait(ShutdownTimeout))
        {
            _forceStop.Cancel();
            try
            {
                _writerTask.Wait(ShutdownTimeout);
            }
            catch (AggregateException)
            {
            }
        }

        _forceStop.Dispose();
        _flushSignal.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task WriteToDatabaseAsync(CancellationToken token)
    {
        List<LogModel> pending = [];
        while (!token.IsCancellationRequested)
        {
            if (pending.Count == 0 && Volatile.Read(ref _stopping) != 0 && _buffer.IsEmpty)
                return;

            try
            {
                await _flushSignal.WaitAsync(FlushInterval, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }

            while (pending.Count < MaxFlushBatchSize && _buffer.TryDequeue(out var log))
            {
                pending.Add(log);
                Interlocked.Decrement(ref _bufferedCount);
            }

            DatabaseLogSinkMetrics.SetBuffered(Volatile.Read(ref _bufferedCount) + pending.Count);
            if (pending.Count == 0)
                continue;

            try
            {
                await FlushAsync(pending, token);
                pending.Clear();
                DatabaseLogSinkMetrics.SetBuffered(Volatile.Read(ref _bufferedCount));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                DatabaseLogSinkMetrics.RecordFlushFailure();
                try
                {
                    await Task.Delay(RetryInterval, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task FlushAsync(List<LogModel> pending, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Logs.AddRangeAsync(pending, token);
        await context.SaveChangesAsync(token);
        DatabaseLogSinkMetrics.RecordFlush(pending.Count, Stopwatch.GetElapsedTime(started));
    }

    private void SignalFlush()
    {
        try
        {
            _flushSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }
}

public static class DatabaseLogSinkMetrics
{
    private static long _buffered;
    private static long _dropped;
    private static long _flushFailures;
    private static long _flushed;
    private static long _lastFlushMilliseconds;

    public static long Buffered => Interlocked.Read(ref _buffered);
    public static long Dropped => Interlocked.Read(ref _dropped);
    public static long FlushFailures => Interlocked.Read(ref _flushFailures);
    public static long Flushed => Interlocked.Read(ref _flushed);
    public static long LastFlushMilliseconds => Interlocked.Read(ref _lastFlushMilliseconds);

    internal static void SetBuffered(long value) => Interlocked.Exchange(ref _buffered, Math.Max(0, value));
    internal static void RecordDropped() => Interlocked.Increment(ref _dropped);
    internal static void RecordFlushFailure() => Interlocked.Increment(ref _flushFailures);

    internal static void RecordFlush(int count, TimeSpan elapsed)
    {
        Interlocked.Add(ref _flushed, count);
        Interlocked.Exchange(ref _lastFlushMilliseconds, Math.Max(0, (long)elapsed.TotalMilliseconds));
    }
}
