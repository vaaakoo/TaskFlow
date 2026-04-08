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

            // 1. Safety Guards
            if (job.State == JobState.Completed || job.State == JobState.DeadLettered)
            {
                _logger.LogInformation("Job {JobId} skipped. Idempotency guard triggered (Status: {State}).", jobId, job.State);
                return;
            }

            if (job.State == JobState.Processing)
            {
                _logger.LogInformation("Job {JobId} skipped. Already processing/locked.", jobId);
                return;
            }

            if (job.State == JobState.Scheduled && job.ScheduledFor > DateTimeOffset.UtcNow)
            {
                _logger.LogDebug("Job {JobId} skipped. Scheduled in the future at {ScheduledFor}.", jobId, job.ScheduledFor);
                return; 
            }

            // 2. Lock Acquisition
            try
            {
                job.MarkAsProcessing(Guid.NewGuid().ToString("N"));
                await _jobRepository.UpdateAsync(job, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogDebug("Concurrency conflict acquiring lock for Job {JobId}. Handled cleanly.", jobId);
                return; 
            }

            _logger.LogInformation("Job {JobId} started processing.", jobId);

            // 3. Delegation & Deserialization
            IJobHandler handler;
            object deserializedPayload;
            try
            {
                var targetType = _handlerResolver.ResolvePayloadType(job.PayloadType);
                deserializedPayload = JsonSerializer.Deserialize(job.Payload, targetType) 
                    ?? throw new InvalidOperationException("Payload deserialized to null.");
                handler = _handlerResolver.ResolveHandler(job.PayloadType);
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(job, ex, cancellationToken);
                return;
            }

            // 4. Execution Loop
            await handler.ExecuteAsync(deserializedPayload, cancellationToken);

            job.MarkAsCompleted();
            await _jobRepository.UpdateAsync(job, cancellationToken);
            _logger.LogInformation("Job {JobId} completed successfully.", jobId);
        }
        catch (Exception ex)
        {
             // Last resort containment. Never throw to calling Host.
             _logger.LogError(ex, "Unexpected hard failure inside ProcessAsync for Job {JobId}.", jobId);
        }
    }

    private async Task HandleFailureAsync(Job job, Exception ex, CancellationToken cancellationToken)
    {
        _logger.LogError(ex, "Job {JobId} failed during handler execution.", job.Id);
        job.MarkAsFailed(ex.ToString());

        if (job.State != JobState.DeadLettered)
        {
            var nextRetryTime = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, job.RetryCount + 1));

            job.IncrementRetry(nextRetryTime);
            await _jobRepository.UpdateAsync(job, cancellationToken);
            
            _logger.LogInformation("Retry {RetryCount} scheduled for Job {JobId} at {NextRetryTime}.", job.RetryCount, job.Id, nextRetryTime);
        }
        else
        {
            await _jobRepository.UpdateAsync(job, cancellationToken);
            _logger.LogCritical("Job {JobId} exceeded max retries and is DEAD-LETTERED.", job.Id);
        }
    }
}
