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
        _logger.LogInformation("JobSweeper bounds initialized. Polling executing every 10 seconds.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sweeper loop failed to map database. Soft recovery active.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task SweepJobsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskFlowDbContext>();
        var now = DateTimeOffset.UtcNow;

        // --- 1. CRASH RECOVERY (Stale Locks) ---
        // Context: If a machine pulls the plug mid-execution, its lock persists forever.
        // We find jobs stuck in "Processing" where the LockExpiresAt is strictly in the past.
        var stuckJobs = await db.Jobs
            .Where(j => j.State == JobState.Processing && j.LockExpiresAt < now)
            .ToListAsync(ct);

        foreach (var job in stuckJobs)
        {
            _logger.LogWarning("Crash Recovery! Job {JobId} is stale (Locked processing expired). Resetting.", job.Id);
            
            // Mark it failed, but immediately trigger IncrementRetry mapping to bypass DeadLetter bounds if allowed
            job.MarkAsFailed("Worker processing lock organically expired. Assumed offline.");
            if (job.State != JobState.DeadLettered)
            {
                job.IncrementRetry(null); // Force it to Pending naturally through the Domain
                await _jobQueue.EnqueueAsync(job.Id, ct);
            }
        }

        // --- 2. SCHEDULED EXECUTION ---
        var scheduledJods = await db.Jobs
            .Where(j => j.State == JobState.Scheduled && j.ScheduledFor <= now)
            .Select(j => j.Id)
            .ToListAsync(ct);

        foreach (var id in scheduledJods)
        {
            _logger.LogInformation("Sweeper pulling natively Scheduled Job {JobId} to active channel.", id);
            await _jobQueue.EnqueueAsync(id, ct);
        }

        if (stuckJobs.Any()) 
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
