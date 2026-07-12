using System.Reflection;
using Cronos;
using GZCTF.Infrastructure.Concurrency;

namespace GZCTF.Services.CronJob;

public delegate Task CronJob(
    AsyncServiceScope scope,
    ILogger<CronJobService> logger,
    CancellationToken cancellationToken);

public record CronJobEntry(CronJob Job, CronExpression Expression);

public class CronJobService(IDistributedLeaseProvider leases, IServiceScopeFactory provider,
    ILogger<CronJobService> logger)
    : IHostedService, IDisposable
{
    private readonly Dictionary<string, CronJobEntry> _jobs = [];
    private bool _disposed;
    private IDistributedLease? _leaderLease;
    private Timer? _timer;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task StartAsync(CancellationToken token)
    {
        if (await TryHoldLock())
            LaunchCronJob();
        else
            LaunchWatchDog();
    }

    public Task StopAsync(CancellationToken token)
    {
        StopCronJob();
        return DropLock();
    }

    ~CronJobService()
    {
        Dispose();
    }

    /// <summary>
    /// Add a job to the cron job service
    /// </summary>
    public bool AddJob(CronJob job)
    {
        lock (_jobs)
        {
            var (name, entry) = job.ToEntry();
            if (!_jobs.TryAdd(name, entry))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Remove a job from the cron job service
    /// </summary>
    /// <param name="job"></param>
    public bool RemoveJob(string job)
    {
        lock (_jobs)
        {
            if (!_jobs.Remove(job))
                return false;
        }

        return true;
    }

    private void LaunchCronJob()
    {
        var methods = typeof(RuntimeCronJobs).GetMethods(BindingFlags.Static | BindingFlags.Public);
        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<CronJobAttribute>();
            if (attr is null)
                continue;

            AddJob(method.CreateDelegate<CronJob>());
        }

        _timer = new Timer(_ => Task.Run(Execute),
            null, TimeSpan.FromSeconds(60 - DateTime.UtcNow.Second), TimeSpan.FromMinutes(1));

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.CronJob_Started)],
            TaskStatus.Success, LogLevel.Debug);
    }

    private void StopCronJob()
    {
        _timer?.Change(Timeout.Infinite, 0);
        lock (_jobs)
        {
            _jobs.Clear();
        }

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.CronJob_Stopped)], TaskStatus.Exit,
            LogLevel.Debug);
    }

    private async Task<bool> TryHoldLock()
    {
        if (_leaderLease is { LeaseLost.IsCancellationRequested: false })
            return true;
        try
        {
            _leaderLease = await leases.AcquireAsync("cron-job-leader", TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMinutes(2));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async Task DropLock()
    {
        if (_leaderLease is null)
            return;
        await _leaderLease.DisposeAsync();
        _leaderLease = null;
    }

    private void LaunchWatchDog()
    {
        var delay = Random.Shared.Next(30, 120);

        _timer = new Timer(async void (_) =>
        {
            try
            {
                if (!await TryHoldLock())
                    return;

                _timer?.Change(Timeout.Infinite, 0);
                LaunchCronJob();
            }
            catch (Exception e)
            {
                logger.SystemLog(StaticLocalizer[nameof(Resources.Program.CronJob_ExecuteFailed),
                        "WatchDog", e.Message],
                    TaskStatus.Failed, LogLevel.Warning);
            }
        }, null, TimeSpan.FromSeconds(delay), TimeSpan.FromMinutes(5));

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.CronJob_LaunchedWatchDog)],
            TaskStatus.Pending, LogLevel.Debug);
    }

    private async Task Execute()
    {
        var now = DateTime.UtcNow;
        var last = now - TimeSpan.FromSeconds(30);
        List<Task> handles = [];

        if (_leaderLease is null || _leaderLease.LeaseLost.IsCancellationRequested)
        {
            StopCronJob();
            await DropLock();
            LaunchWatchDog();
            return;
        }
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(_leaderLease.LeaseLost);

        lock (_jobs)
        {
            foreach (var (job, entry) in _jobs)
            {
                if (entry.Expression.GetNextOccurrence(last) is not { } next ||
                    Math.Abs((next - now).TotalSeconds) > 30D)
                    continue;

                handles.Add(Task.Run(async () =>
                {
                    await using var scope = provider.CreateAsyncScope();

                    try
                    {
                        await entry.Job(scope, logger, leaseCancellation.Token);
                    }
                    catch (OperationCanceledException) when (leaseCancellation.IsCancellationRequested)
                    {
                        logger.LogWarning("Cron job {CronJob} stopped because the leader lease was lost.", job);
                    }
                    catch (Exception e)
                    {
                        logger.SystemLog(
                            StaticLocalizer[nameof(Resources.Program.CronJob_ExecuteFailed), job, e.Message],
                            TaskStatus.Failed, LogLevel.Warning);
                    }
                }));
            }
        }

        await Task.WhenAll(handles);
    }
}
