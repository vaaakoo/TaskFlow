using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Processing;
using TaskFlow.Domain.Repositories;
using TaskFlow.Infrastructure.Messaging;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Worker;

namespace TaskFlow.Api;

public static class DependencyInjection
{
    // Bootstrapped by both API and Worker exactly mapping the Core services down to EF Core
    public static IServiceCollection AddTaskFlowSystem(this IServiceCollection services, string dbConnectionString)
    {
        services.AddDbContext<TaskFlowDbContext>(options => 
            options.UseNpgsql(dbConnectionString));
            
        services.AddScoped<IJobRepository, JobRepository>();
        
        // This Memory Channel queues data natively within whoever registered it
        services.AddSingleton<IJobQueue, InMemoryJobQueue>();
        
        services.AddScoped<IJobProcessor, JobProcessor>();
        
        // Provide the fallback generic resolver
        services.AddSingleton<IHandlerResolver, DefaultHandlerResolver>();

        return services;
    }

    // Configured exclusively on the Worker process
    public static IServiceCollection AddTaskFlowWorker(this IServiceCollection services)
    {
        services.AddHostedService<JobWorker>();
        
        // Critical: Background Sweeper registered to clean stuck DB jobs and feed the InMemory Queue above.
        services.AddHostedService<JobSweeper>(); 
        
        return services;
    }
}

// Fallback MVP execution handler
public class DefaultHandlerResolver : IHandlerResolver
{
    public Type ResolvePayloadType(string payloadType) => Type.GetType(payloadType) ?? typeof(object);
    
    public IJobHandler ResolveHandler(string payloadType) => new NullHandler();
}

public class NullHandler : IJobHandler
{
    public Task ExecuteAsync(object payload, CancellationToken cancellationToken) => Task.CompletedTask;
}
