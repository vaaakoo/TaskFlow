using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Processing;
using TaskFlow.Domain.Repositories;

namespace TaskFlow.Worker;

public class JobWorker : BackgroundService
{
    private readonly IJobQueue _jobQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobWorker> _logger;

    public JobWorker(
        IJobQueue jobQueue, 
        IServiceProvider serviceProvider, 
        ILogger<JobWorker> logger)
    {
        _jobQueue = jobQueue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobWorker started continuous execution loop.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Polling Fallback Strategy:
                // We attempt to softly timeout the memory queue read every 15 seconds.
                // If a job was inserted into the DB while the Sweeper was lagging, this ensures we don't block infinitely.
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
                
                Guid jobId = Guid.Empty;
                try
                {
                    jobId = await _jobQueue.DequeueAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Timeout hit (15s passed with no memory items).
                    // We loop aggressively avoiding tight-loops. Sweeper will inherently feed us real work.
                    continue; 
                }

                if (jobId != Guid.Empty)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<IJobProcessor>();
                    
                    // We strictly await to guarantee at-least-once linear processing. 
                    await processor.ProcessAsync(jobId, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Genuine host shutdown requested terminating the service
                break;
            }
            catch (Exception ex)
            {
                // 2. Total Containment:
                // We NEVER let the worker crash! 
                // Delay strategy implemented to prevent a 100% CPU tight-loop if the DB is physically offline.
                _logger.LogError(ex, "Fatal boundary exception inside worker loop. Recovering softly.");
                await Task.Delay(2000, stoppingToken); 
            }
        }
        
        _logger.LogInformation("JobWorker cleanly disposed.");
    }
}
