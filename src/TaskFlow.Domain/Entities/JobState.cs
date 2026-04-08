namespace TaskFlow.Domain.Entities;

public enum JobState
{
    /// <summary>
    /// Job is ready to be processed immediately.
    /// </summary>
    Pending,
    
    /// <summary>
    /// Job is waiting for a future time to be processed.
    /// </summary>
    Scheduled,
    
    /// <summary>
    /// Job is currently being processed by a worker.
    /// </summary>
    Processing,
    
    /// <summary>
    /// Job finished successfully.
    /// </summary>
    Completed,
    
    /// <summary>
    /// Job failed an execution attempt but is eligible for retry.
    /// </summary>
    Failed,
    
    /// <summary>
    /// Job failed all execution attempts and is parked for manual intervention.
    /// </summary>
    DeadLettered
}
