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
                var jobId = await _jobQueue.DequeueAsync(stoppingToken);

                // Scope ensures clean EF Core DbContext per execution run without memory leaks
                using var scope = _serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IJobProcessor>();

                await processor.ProcessAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // Host is shutting down
            }
            catch (Exception ex)
            {
                // CRITICAL SAFETY GUARD: Exception inside queue consumer does not bubble up.
                _logger.LogError(ex, "Fatal outer boundary exception inside worker loop. Avoiding crash.");
                await Task.Delay(1000, stoppingToken); 
            }
        }
        
        _logger.LogInformation("JobWorker stopped cleanly.");
    }
}
