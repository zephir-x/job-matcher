using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobMatcher.Interfaces;
using JobMatcher.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobMatcher.Clients;

// Implement fetching logic injecting IConfiguration to safely read absolute URLs
public class SolidJobsScraper(HttpClient httpClient, IConfiguration config, ILogger<SolidJobsScraper> logger) : IJobScraper
{
    public async Task<IEnumerable<JobOffer>> FetchJobsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fetching job offers from Solid.Jobs...");

        try
        {
            var url = config["JobSources:SolidJobsUrl"];

            // 1. Execute GET request manually to intercept error bodies
            var response = await httpClient.GetAsync(url, cancellationToken);

            // 2. Safely read the error response if status code is not 2xx
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Solid.Jobs API returned {StatusCode}. Details: {ErrorBody}", response.StatusCode, errorBody);
                return [];
            }

            // 3. Deserialize successful response
            var data = await response.Content.ReadFromJsonAsync<SolidJobsResponse>(cancellationToken: cancellationToken);

            if (data?.Jobs == null || data.Jobs.Count == 0)
            {
                logger.LogWarning("No job offers retrieved from Solid.Jobs.");
                return [];
            }

            // 4. Map external DTOs into the unified domain record
            var offers = data.Jobs.Select(job => new JobOffer(
                Id: job.JobOfferKey,
                Title: job.Title,
                Company: job.Company,
                Url: job.Url ?? $"https://solid.jobs/offer/{job.JobOfferKey}",
                Technologies: job.Skills?.Select(s => s.Name).ToList() ?? [],
                SalaryRange: job.Salary != null ? $"{job.Salary.From:0.##}-{job.Salary.To:0.##} {job.Salary.Currency}" : "Undisclosed",
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
    [property: JsonPropertyName("from")] decimal? From,
    [property: JsonPropertyName("to")] decimal? To,
    [property: JsonPropertyName("currency")] string Currency
);