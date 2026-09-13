using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobMatcher.Interfaces;
using JobMatcher.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobMatcher.Clients;

// Implementation of IJobScraper for NoFluffJobs API using sequential POST requests for keyword-based filtering
public class NoFluffJobsScraper(HttpClient httpClient, IConfiguration config, ILogger<NoFluffJobsScraper> logger) : IJobScraper
{
    public async Task<IEnumerable<JobOffer>> FetchJobsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fetching job offers from NoFluffJobs...");

        try
        {
            var url = config["JobSources:NoFluffJobsUrl"];
            
            var targetKeywords = config["TargetKeywords"]?
                .Split(',')
                .Select(k => k.Trim())
                .ToArray() ?? [".NET", "React"];

            var allOffers = new List<JobOffer>();

            // Iterates through target keywords to perform individual POST requests, simulating an OR search condition
            foreach (var keyword in targetKeywords)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(new 
                    { 
                        criteriaSearch = new { keyword = new[] { keyword } }, 
                        page = 1 
                    })
                };
                
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                var response = await httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<NoFluffJobsResponse>(cancellationToken: cancellationToken);

                    if (data?.Postings != null && data.Postings.Count > 0)
                    {
                        var mappedOffers = data.Postings.Select(job => new JobOffer(
                            Id: job.Id ?? Guid.NewGuid().ToString(),
                            Title: job.Title ?? "Unknown Title",
                            Company: job.Name ?? "Unknown Company",
                            Url: $"https://nofluffjobs.com/pl/job/{job.Url}",
                            Technologies: job.Technology != null ? [job.Technology] : [],
                            SalaryRange: ExtractSalary(job.Salary),
                            SourcePlatform: "NoFluffJobs"
                        ));

                        allOffers.AddRange(mappedOffers);
                    }
                }
                else
                {
                    logger.LogWarning("Failed to fetch jobs for keyword '{Keyword}'. Status: {StatusCode}", keyword, response.StatusCode);
                }
            }

            if (allOffers.Count == 0)
            {
                logger.LogWarning("No job offers retrieved from NoFluffJobs across all keywords.");
                return [];
            }

            // Deduplicates aggregated results to prevent overlapping offers from multiple keyword queries
            var uniqueOffers = allOffers.DistinctBy(o => o.Id).ToList();

            logger.LogInformation("Successfully mapped {Count} unique offers from NoFluffJobs.", uniqueOffers.Count);
            return uniqueOffers;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch data from NoFluffJobs.");
            return [];
        }
    }

    private static string ExtractSalary(NoFluffJobsSalary? salary)
    {
        if (salary == null || salary.From == null || salary.To == null) return "Undisclosed";
        return $"{salary.From:0.##}-{salary.To:0.##} {salary.Currency ?? "PLN"}";
    }
}

// Internal Data Transfer Objects mapped to NoFluffJobs JSON structure
internal record NoFluffJobsResponse(
    [property: JsonPropertyName("postings")] List<NoFluffJobsPosting>? Postings
);

internal record NoFluffJobsPosting(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("technology")] string? Technology,
    [property: JsonPropertyName("salary")] NoFluffJobsSalary? Salary
);

internal record NoFluffJobsSalary(
    [property: JsonPropertyName("from")] decimal? From,
    [property: JsonPropertyName("to")] decimal? To,
    [property: JsonPropertyName("currency")] string? Currency
);