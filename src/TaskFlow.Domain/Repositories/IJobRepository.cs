using System;
using System.Threading;
using System.Threading.Tasks;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Domain.Repositories;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Job job, CancellationToken cancellationToken = default);
}
