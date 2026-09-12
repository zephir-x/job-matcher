using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Configure generic host to enable dependency injection, logging, and configuration
using var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        // Load configuration from standard files and environment variables (essential for CI/CD secrets)
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        // Register typed HTTP clients to fetch data from external job boards
        // services.AddHttpClient<IJobPlatformClient, SolidJobsClient>(...);
        
        // Register core application services
        // services.AddTransient<JobMatcherEngine>();
    })
    .Build();

// Retrieve logger to monitor application lifecycle
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Job Matcher Agent started at {Time}", DateTimeOffset.Now);

try
{
    // Resolve main engine and execute the processing pipeline
    // var engine = host.Services.GetRequiredService<JobMatcherEngine>();
    // await engine.RunPipelineAsync();
    
    logger.LogInformation("Job matching pipeline completed successfully.");
}
catch (Exception ex)
{
    logger.LogCritical(ex, "A fatal error occurred during pipeline execution.");
    Environment.ExitCode = 1; // Explicitly fail the GitHub Action workflow on crash
}

logger.LogInformation("Shutting down the agent.");