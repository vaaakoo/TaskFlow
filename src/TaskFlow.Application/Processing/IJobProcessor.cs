using System;
using System.Threading;
using System.Threading.Tasks;

namespace TaskFlow.Application.Processing;

public interface IJobProcessor
{
    Task ProcessAsync(Guid jobId, CancellationToken cancellationToken = default);
}
