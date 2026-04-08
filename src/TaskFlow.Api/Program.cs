using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api;
using TaskFlow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Ensure Docker connections map down seamlessly to PostgreSQL
var dbConnectionString = builder.Configuration.GetConnectionString("TaskFlowDb") 
    ?? "Host=localhost;Database=taskflowdb;Username=taskflow_user;Password=SecretPassword123!";

builder.Services.AddTaskFlowSystem(dbConnectionString);

var app = builder.Build();

// Auto-run Entity Framework Migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TaskFlowDbContext>();
    // For production resilience, we run strict schema migrations.
    db.Database.Migrate(); 
}

app.MapTaskFlowEndpoints();

app.Run();
