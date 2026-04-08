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
    public static IServiceCollection AddTaskFlowSystem(this IServiceCollection services, string dbConnectionString)
    {
        services.AddDbContext<TaskFlowDbContext>(options => 
            options.UseSqlServer(dbConnectionString));
            
        services.AddScoped<IJobRepository, JobRepository>();

        services.AddSingleton<IJobQueue, InMemoryJobQueue>();

        services.AddScoped<IJobProcessor, JobProcessor>();

        services.AddHostedService<JobWorker>();
        
        // Critical: Background Sweeper registered to clean stuck/scheduled jobs.
        services.AddHostedService<JobSweeper>(); 

        return services;
    }
}
