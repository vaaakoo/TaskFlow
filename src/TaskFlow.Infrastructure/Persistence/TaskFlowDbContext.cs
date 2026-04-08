using Microsoft.EntityFrameworkCore;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Persistence;

public class TaskFlowDbContext : DbContext
{
    public TaskFlowDbContext(DbContextOptions<TaskFlowDbContext> options) : base(options) { }

    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Job>();

        entity.HasKey(x => x.Id);

        // Map Optimistic Concurrency Token
        // This ensures if two workers call UpdateAsync simultaneously, one throws DbUpdateConcurrencyException
        entity.Property(x => x.Version)
              .IsRowVersion()
              .IsConcurrencyToken();

        // Indexing for Worker Dashboard and Sweep queries
        entity.HasIndex(x => x.State);
        entity.HasIndex(x => x.ScheduledFor);
        
        // EF Core maps properties automatically, even with private setters.
    }
}
