using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Repositories;

namespace TaskFlow.Api;

public static class JobEndpoints
{
    public static void MapTaskFlowEndpoints(this WebApplication app)
    {
        // Add Basic System Status
        app.MapGet("/health", () => Results.Ok(new 
        { 
            Status = "Healthy", 
            System = "TaskFlow",
            Timestamp = DateTimeOffset.UtcNow 
        }));

        app.MapPost("/jobs", async (
            [FromBody] CreateJobRequest request, 
            [FromServices] IJobRepository repo, 
            [FromServices] IJobQueue queue, 
            CancellationToken ct) => 
        {
            var job = new Job(request.Type, request.Payload, request.PayloadType);
            await repo.UpdateAsync(job, ct); 

            // Defer to sweeper if job naturally created as Scheduled. Only pipe Pending directly.
            if (job.State == JobState.Pending)
            {
                await queue.EnqueueAsync(job.Id, ct); 
            }

            return Results.Accepted($"/jobs/{job.Id}", new { job.Id, job.State });
        });

        app.MapGet("/jobs/{id:guid}", async (
            Guid id, 
            [FromServices] IJobRepository repo, 
            CancellationToken ct) => 
        {
            var job = await repo.GetByIdAsync(id, ct);
            if (job == null) return Results.NotFound(new { Message = "Job not found." });

            return Results.Ok(new 
            { 
                job.Id, 
                StateName = job.State.ToString()
            });
        });
    }
}

public record CreateJobRequest(string Type, string Payload, string PayloadType);
