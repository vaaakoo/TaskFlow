using System;
using System.Threading;
using System.Threading.Tasks;

namespace TaskFlow.Domain.Repositories;

public interface IJobQueue
{
    Task EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<Guid> DequeueAsync(CancellationToken cancellationToken = default);
}
