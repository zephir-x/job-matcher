using JobMatcher.Interfaces;
using JobMatcher.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobMatcher.Services;

public class JobMatcherEngine(
    IEnumerable<IJobScraper> scrapers,
    IAiEvaluator aiEvaluator,
    INotifier notifier,
    IConfiguration config,
    ILogger<JobMatcherEngine> logger)
{
    public async Task RunPipelineAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting Job Matcher pipeline execution...");

        var allOffers = new List<JobOffer>();
        var allResults = new List<JobEvaluationResult>();

        // 1. Context Injection (Read once)
        var profilePath = Path.Combine(AppContext.BaseDirectory, "profile.json");
        var candidateProfile = await File.ReadAllTextAsync(profilePath, cancellationToken);

        // 2. Sequential Batch Processing per Platform
        foreach (var scraper in scrapers)
        {
            var scraperName = scraper.GetType().Name;
            logger.LogInformation("--- Processing platform via {ScraperName} ---", scraperName);

            var offers = await scraper.FetchJobsAsync(cancellationToken);
            var targetOffers = FilterRelevantOffers(offers);

            if (targetOffers.Count == 0)
            {
                logger.LogInformation("No matching target offers found from {ScraperName}.", scraperName);
                continue;
            }

            var evaluationResults = await aiEvaluator.EvaluateOffersAsync(targetOffers, candidateProfile, cancellationToken);

            allOffers.AddRange(targetOffers);
            allResults.AddRange(evaluationResults);
            
            // Anti-Rate-Limit delay to protect Gemini API quota
            await Task.Delay(2000, cancellationToken);
        }

        if (allResults.Count == 0)
        {
            logger.LogInformation("No offers met the criteria across any platforms today.");
            return;
        }

        // 3. Output (Dispatch consolidated report to Discord)
        await notifier.SendDailySummaryAsync(allOffers, allResults, cancellationToken);
    }

    private List<JobOffer> FilterRelevantOffers(IEnumerable<JobOffer> allOffers)
    {
        var targetKeywords = config["TargetKeywords"]?
            .Split(',')
            .Select(k => k.Trim())
            .ToArray() ?? [".NET", "C#"];

        return allOffers
            .Where(o => targetKeywords.Any(keyword => o.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Take(40)
            .ToList();
    }
}