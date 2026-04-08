using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Repositories;

namespace TaskFlow.Infrastructure.Persistence;

public class JobRepository : IJobRepository
{
    private readonly TaskFlowDbContext _context;

    public JobRepository(TaskFlowDbContext context)
    {
        _context = context;
    }

    public async Task<Job?> GetByIdAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs.FindAsync(new object[] { jobId }, cancellationToken);
    }

    public async Task UpdateAsync(Job job, CancellationToken cancellationToken = default)
    {
        var entry = _context.Entry(job);
        
        if (entry.State == EntityState.Detached)
        {
            _context.Jobs.Add(job);
        }
        else
        {
            _context.Jobs.Update(job);
        }

        // On concurrent modification, EF Core evaluates the 'Version' field and throws DbUpdateConcurrencyException
        await _context.SaveChangesAsync(cancellationToken);
    }
}
