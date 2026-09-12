using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobMatcher.Interfaces;
using JobMatcher.Models;
using Microsoft.Extensions.Logging;

namespace JobMatcher.Clients;

// Implement fetching logic using strongly typed HttpClient
public class SolidJobsScraper(HttpClient httpClient, ILogger<SolidJobsScraper> logger) : IJobScraper
{
    public async Task<IEnumerable<JobOffer>> FetchJobsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fetching junior/trainee job offers from Solid.Jobs...");

        try
        {
            // Execute HTTP GET and deserialize JSON to internal platform-specific models
            var response = await httpClient.GetFromJsonAsync<SolidJobsResponse>("", cancellationToken);

            if (response?.Jobs == null || response.Jobs.Count == 0)
            {
                logger.LogWarning("No job offers retrieved from Solid.Jobs.");
                return [];
            }

            // Map external DTOs into the unified domain record
            var offers = response.Jobs.Select(job => new JobOffer(
                Id: job.JobOfferKey,
                Title: job.Title,
                Company: job.Company,
                Url: job.Url ?? $"https://solid.jobs/offer/{job.JobOfferKey}",
                Technologies: job.Skills?.Select(s => s.Name).ToList() ?? [],
                SalaryRange: job.Salary != null ? $"{job.Salary.From}-{job.Salary.To} {job.Salary.Currency}" : "Undisclosed",
                SourcePlatform: "Solid.Jobs"
            )).ToList();

            logger.LogInformation("Successfully mapped {Count} offers from Solid.Jobs.", offers.Count);
            return offers;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch data from Solid.Jobs.");
            return [];
        }
    }
}

// Internal Data Transfer Objects mapped strictly to Solid.Jobs JSON structure
internal record SolidJobsResponse(
    [property: JsonPropertyName("jobs")] List<SolidJobsOffer> Jobs
);

internal record SolidJobsOffer(
    [property: JsonPropertyName("jobOfferKey")] string JobOfferKey,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("company")] string Company,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("skills")] List<SolidJobsSkill> Skills,
    [property: JsonPropertyName("salary")] SolidJobsSalary? Salary
);

internal record SolidJobsSkill(
    [property: JsonPropertyName("name")] string Name
);

internal record SolidJobsSalary(
    [property: JsonPropertyName("from")] int? From,
    [property: JsonPropertyName("to")] int? To,
    [property: JsonPropertyName("currency")] string Currency
);