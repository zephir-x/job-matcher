using JobMatcher.Interfaces;
using JobMatcher.Models;
using Microsoft.Extensions.Logging;

namespace JobMatcher.Services;

// Orchestrate the end-to-end pipeline: scraping, AI evaluation, and dispatching
public class JobMatcherEngine(
    IEnumerable<IJobScraper> scrapers,
    ILogger<JobMatcherEngine> logger)
{
    public async Task RunPipelineAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting Job Matcher pipeline execution...");

        var allOffers = new List<JobOffer>();

        // Iterate through all registered scrapers polymorphically
        foreach (var scraper in scrapers)
        {
            var offers = await scraper.FetchJobsAsync(cancellationToken);
            allOffers.AddRange(offers);
        }

        logger.LogInformation("Total offers fetched across all platforms: {Count}", allOffers.Count);

        // Display sample records in console to verify live integration
        foreach (var offer in allOffers.Take(5))
        {
            logger.LogInformation("Sample Offer: [{Platform}] {Title} @ {Company} | Salary: {Salary} | Link: {Url}",
                offer.SourcePlatform, offer.Title, offer.Company, offer.SalaryRange, offer.Url);
        }

        // AI evaluation and notifications will be plugged in here in the next steps
    }
}