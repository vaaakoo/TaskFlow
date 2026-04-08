using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TaskFlow.Domain.Repositories;

namespace TaskFlow.Infrastructure.Messaging;

public class InMemoryJobQueue : IJobQueue
{
    // Unbounded Channel acts as an incredibly lightweight in-memory thread loop
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public async Task EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(jobId, cancellationToken);
    }

    public async Task<Guid> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }
}
