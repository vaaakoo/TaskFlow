using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Repositories;

namespace TaskFlow.Application.Processing;

public class JobProcessor : IJobProcessor
{
    private readonly IJobRepository _jobRepository;
    private readonly IJobQueue _jobQueue;
    private readonly IHandlerResolver _handlerResolver;
    private readonly ILogger<JobProcessor> _logger;

    public JobProcessor(
        IJobRepository jobRepository,
        IJobQueue jobQueue,
        IHandlerResolver handlerResolver,
        ILogger<JobProcessor> logger)
    {
        _jobRepository = jobRepository;
        _jobQueue = jobQueue;
        _handlerResolver = handlerResolver;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        try 
        {
            var job = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
            if (job == null) return;

            // 1. Immutable Safety Guards
            if (job.State == JobState.Completed || job.State == JobState.DeadLettered)
            {
                _logger.LogInformation("Idempotency guard triggered: Job {JobId} (State: {State}). Skipping.", jobId, job.State);
                return;
            }

            if (job.State == JobState.Processing)
            {
                _logger.LogDebug("Job {JobId} is strictly locked. Skipping redundant parallel execution attempt.", jobId);
                return;
            }

            // 2. Concurrency Lock
            try
            {
                // The worker lock naturally expires avoiding infinite blackholing.
                job.MarkAsProcessing(Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(5));
                await _jobRepository.UpdateAsync(job, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Optimistic Concurrency blocked redundant lock for Job {JobId}. Error: {Message}", jobId, ex.Message);
                return; 
            }

            _logger.LogInformation("Job {JobId} started processing natively.", jobId);

            // 3. Delegation & Isolated Deserialization
            IJobHandler handler;
            object deserializedPayload;
            try
            {
                var targetType = _handlerResolver.ResolvePayloadType(job.PayloadType);
                deserializedPayload = JsonSerializer.Deserialize(job.Payload, targetType) 
                    ?? throw new InvalidOperationException("Payload deserialization resulted in null allocation.");
                handler = _handlerResolver.ResolveHandler(job.PayloadType);
            }
            catch (Exception ex)
            {
                await HandleFailureLocallyAsync(job, ex, cancellationToken);
                return; // Must strictly return to prevent further execution!
            }

            // 4. Action Execution
            await handler.ExecuteAsync(deserializedPayload, cancellationToken);

            job.MarkAsCompleted();
            await _jobRepository.UpdateAsync(job, cancellationToken);
            
            _logger.LogInformation("Job {JobId} completed execution sequentially.", jobId);
        }
        catch (Exception ex)
        {
             // Utter containment check enforcing worker survival rules
             _logger.LogError(ex, "Job {JobId} triggered critical unbounded runtime failure.", jobId);
        }
    }

    private async Task HandleFailureLocallyAsync(Job job, Exception ex, CancellationToken cancellationToken)
    {
        _logger.LogError(ex, "Execution failed sequentially for Job {JobId}.", job.Id);
        job.MarkAsFailed(ex.ToString());

        if (job.State != JobState.DeadLettered)
        {
            var nextRetryTime = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, job.RetryCount + 1));
            job.IncrementRetry(nextRetryTime);
            
            await _jobRepository.UpdateAsync(job, cancellationToken);
            _logger.LogInformation("Job {JobId} failed. Native exponential retry scheduled at {NextRetryTime}.", job.Id, nextRetryTime);
        }
        else
        {
            await _jobRepository.UpdateAsync(job, cancellationToken);
            _logger.LogCritical("Job {JobId} met dead-lettered threshold boundaries.", job.Id);
        }
    }
}
