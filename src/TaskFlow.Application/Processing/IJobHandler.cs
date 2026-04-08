using System.Threading;
using System.Threading.Tasks;

namespace TaskFlow.Application.Processing;

/// <summary>
/// Non-generic handler interface for the JobProcessor to consume cleanly.
/// Internally, implementations handle casting to their specific TPayload.
/// </summary>
public interface IJobHandler
{
    Task ExecuteAsync(object payload, CancellationToken cancellationToken);
}

/// <summary>
/// Generic handler interface that business logic developers actually implement.
/// </summary>
public interface IJobHandler<in TPayload> : IJobHandler where TPayload : class
{
    Task ExecuteAsync(TPayload payload, CancellationToken cancellationToken);

    // Explicit generic-to-object routing
    Task IJobHandler.ExecuteAsync(object payload, CancellationToken cancellationToken)
    {
        return ExecuteAsync((TPayload)payload, cancellationToken);
    }
}
