using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskFlow.Api; // Reusing the identical DI namespace mapping

var builder = Host.CreateApplicationBuilder(args);

var dbConnectionString = builder.Configuration.GetConnectionString("TaskFlowDb") 
    ?? "Host=localhost;Database=taskflowdb;Username=taskflow_user;Password=SecretPassword123!";

// Reuse identically clean Database and Processor wirings
builder.Services.AddTaskFlowSystem(dbConnectionString);

// Hook Sweeper and Worker specifically for this headless Background Process
builder.Services.AddTaskFlowWorker();

var host = builder.Build();
host.Run();
