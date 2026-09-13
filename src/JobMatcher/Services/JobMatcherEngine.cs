using JobMatcher.Interfaces;
using JobMatcher.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobMatcher.Services;

// Orchestrates the main job matching pipeline: fetches data sequentially from multiple sources, evaluates it via AI, and dispatches notifications
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

        // Injects the candidate profile required by the AI evaluator context
        var profilePath = Path.Combine(AppContext.BaseDirectory, "profile.json");
        var candidateProfile = await File.ReadAllTextAsync(profilePath, cancellationToken);

        // Executes batch processing sequentially to prevent LLM context window overflow and API rate limits
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
            
            // Pauses execution between scraper modules to comply with external API rate limits
            await Task.Delay(2000, cancellationToken);
        }

        if (allResults.Count == 0)
        {
            logger.LogInformation("No offers met the criteria across any platforms today.");
            return;
        }

        // Dispatches the consolidated report to the configured notification channel
        await notifier.SendDailySummaryAsync(allOffers, allResults, cancellationToken);
    }

    // Filters raw job offers by keywords, deduplicates them, prioritizes specific roles, and enforces safe batch limits
    private List<JobOffer> FilterRelevantOffers(IEnumerable<JobOffer> allOffers)
    {
        var targetKeywords = config["TargetKeywords"]?
            .Split(',')
            .Select(k => k.Trim())
            .ToArray() ?? [".NET", "C#"];

        var targetRoles = config["TargetRoles"]?
            .Split(',')
            .Select(k => k.Trim())
            .ToArray() ?? ["Junior", "Intern", "Staż"];

        return allOffers
            .Where(o => targetKeywords.Any(keyword => o.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(o => $"{o.Title.Trim().ToLower()}-{o.Company.Trim().ToLower()}")
            .OrderByDescending(o => targetRoles.Any(role => o.Title.Contains(role, StringComparison.OrdinalIgnoreCase)))
            .Take(50) 
            .ToList();
    }
}