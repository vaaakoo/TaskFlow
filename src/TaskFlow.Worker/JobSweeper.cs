using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Repositories;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Worker;

public class JobSweeper : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IJobQueue _jobQueue;
    private readonly ILogger<JobSweeper> _logger;

    public JobSweeper(
        IServiceProvider serviceProvider, 
        IJobQueue jobQueue,
        ILogger<JobSweeper> logger)
    {
        _serviceProvider = serviceProvider;
        _jobQueue = jobQueue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobSweeper started. Polling every 10 seconds.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in JobSweeper loop.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task SweepJobsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskFlowDbContext>();
        var now = DateTimeOffset.UtcNow;

        // 1. Recover Stuck Processing Jobs (Crash Recovery)
        var stuckJobs = await db.Jobs
            .Where(j => j.State == JobState.Processing && j.LockExpiresAt < now)
            .ToListAsync(ct);

        foreach (var job in stuckJobs)
        {
            _logger.LogWarning("Recovering crashed Job {JobId}. Resetting state from stale Processing lock.", job.Id);
            
            // We leverage the Domain constraint creatively. Mark it failed due to the worker crash, and requeue it.
            job.MarkAsFailed("Worker processing lock expired. Assumed offline or OOM crash.");
            
            if (job.State != JobState.DeadLettered)
            {
                job.IncrementRetry(null); // Return it natively back to Pending
                _jobQueue.EnqueueAsync(job.Id, ct).ConfigureAwait(false);
            }
        }

        // 2. Poll Database for Jobs that are due for execution right now
        var scheduledJobIds = await db.Jobs
            .Where(j => j.State == JobState.Scheduled && j.ScheduledFor <= now)
            .Select(j => j.Id)
            .ToListAsync(ct);

        foreach (var id in scheduledJobIds)
        {
            _logger.LogInformation("Enqueuing natively Scheduled Job {JobId} for immediate processing.", id);
            await _jobQueue.EnqueueAsync(id, ct);
        }

        if (stuckJobs.Any()) // Only stuck jobs mutate the context state here
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
