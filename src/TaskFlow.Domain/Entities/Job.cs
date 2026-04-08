using System;

namespace TaskFlow.Domain.Entities;

/// <summary>
/// Aggregate Root representing a unit of asynchronous work.
/// </summary>
public class Job
{
    public Guid Id { get; private set; }
    public string Type { get; private set; }
    public string Payload { get; private set; }
    public string PayloadType { get; private set; }
    public JobState State { get; private set; }
    
    public int RetryCount { get; private set; }
    public int MaxRetries { get; private set; }
    
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    
    public string? LastError { get; private set; }
    public string? IdempotencyKey { get; private set; }
    
    public DateTimeOffset? LockExpiresAt { get; private set; }
    public string? LockedBy { get; private set; }
    
    public uint Version { get; private set; } // Field for Optimistic Concurrency Control in EF Core

    // Parameterless constructor for EF Core. Needs to be protected/private to enforce invariants.
    protected Job() { }

    public Job(
        string type, 
        string payload, 
        string payloadType, 
        int maxRetries = 3, 
        string? idempotencyKey = null, 
        DateTimeOffset? scheduledFor = null)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Job type cannot be empty.", nameof(type));
        
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Payload cannot be empty.", nameof(payload));
            
        if (string.IsNullOrWhiteSpace(payloadType))
            throw new ArgumentException("Payload Type cannot be empty.", nameof(payloadType));

        Id = Guid.NewGuid();
        Type = type;
        Payload = payload;
        PayloadType = payloadType;
        
        State = scheduledFor.HasValue && scheduledFor > DateTimeOffset.UtcNow 
            ? JobState.Scheduled 
            : JobState.Pending;
            
        RetryCount = 0;
        MaxRetries = maxRetries;
        CreatedAt = DateTimeOffset.UtcNow;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// Claims the job for a worker, putting it into the Processing state.
    /// </summary>
    /// <param name="workerId">Identifier of the worker picking up the job.</param>
    /// <param name="lockTimeout">How long the worker has before the job is considered stranded.</param>
    public void MarkAsProcessing(string workerId, TimeSpan? lockTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker ID must be provided.", nameof(workerId));

        if (State != JobState.Pending && State != JobState.Scheduled)
            throw new InvalidOperationException($"Cannot start processing from state '{State}'. Job must be Pending or Scheduled.");

        if (State == JobState.Scheduled && ScheduledFor > DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Cannot start processing a scheduled job before its scheduled time arrives.");

        State = JobState.Processing;
        LockedBy = workerId;
        LockExpiresAt = DateTimeOffset.UtcNow.Add(lockTimeout ?? TimeSpan.FromMinutes(5));
        
        // Only set this the first time it starts, keeping the original start time across retries.
        if (StartedAt == null)
        {
            StartedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Successfully completes the job.
    /// </summary>
    public void MarkAsCompleted()
    {
        if (State != JobState.Processing)
            throw new InvalidOperationException($"Cannot complete job from state '{State}'. It must be currently Processing.");

        State = JobState.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        LockedBy = null;
        LockExpiresAt = null;
        LastError = null;
    }

    /// <summary>
    /// Fails the current processing attempt. Automatically routes to DeadLettered if max retries are exceeded.
    /// </summary>
    /// <param name="error">The exception details or failure reason.</param>
    public void MarkAsFailed(string error)
    {
        if (State != JobState.Processing)
            throw new InvalidOperationException($"Cannot fail job from state '{State}'. It must be currently Processing.");

        LastError = error;
        LockedBy = null;
        LockExpiresAt = null;

        if (RetryCount >= MaxRetries)
            MoveToDeadLetter();
        else
            State = JobState.Failed;
    }

    /// <summary>
    /// Prepares a failed job for another attempt.
    /// </summary>
    /// <param name="nextScheduledTime">Optional backoff time for the retry.</param>
    public void IncrementRetry(DateTimeOffset? nextScheduledTime = null)
    {
        if (State != JobState.Failed)
            throw new InvalidOperationException($"Cannot retry job from state '{State}'. Only Failed jobs can be retried.");

        if (RetryCount >= MaxRetries)
            throw new InvalidOperationException("Maximum retry count has been reached. Cannot requeue.");

        RetryCount++;
        
        if (nextScheduledTime.HasValue && nextScheduledTime > DateTimeOffset.UtcNow)
        {
            ScheduledFor = nextScheduledTime;
            State = JobState.Scheduled;
        }
        else
        {
            State = JobState.Pending;
        }
    }

    /// <summary>
    /// Internal transition used when retries are exhausted.
    /// </summary>
    private void MoveToDeadLetter()
    {
        State = JobState.DeadLettered;
        CompletedAt = DateTimeOffset.UtcNow; // Or consider a dedicated DeadLetteredAt if tracking completion separate from failure end
    }
}
