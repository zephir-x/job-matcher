using JobMatcher.Interfaces;
using JobMatcher.Models;
using Microsoft.Extensions.Logging;

namespace JobMatcher.Services;

// Orchestrate the end-to-end pipeline: scraping, AI evaluation, and dispatching
public class JobMatcherEngine(
    IEnumerable<IJobScraper> scrapers,
    IAiEvaluator aiEvaluator,
    INotifier notifier,
    ILogger<JobMatcherEngine> logger)
{
    public async Task RunPipelineAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting Job Matcher pipeline execution...");

        var allOffers = new List<JobOffer>();

        // 1. Ingestion
        foreach (var scraper in scrapers)
        {
            var offers = await scraper.FetchJobsAsync(cancellationToken);
            allOffers.AddRange(offers);
        }

        logger.LogInformation("Total offers fetched across all platforms: {Count}", allOffers.Count);

        // 2. Pre-Filtering (Extracted to private method for Clean Code)
        var targetOffers = FilterRelevantOffers(allOffers);

        if (targetOffers.Count == 0)
        {
            logger.LogInformation("No junior .NET/React offers found on the market today.");
            return;
        }

        // 3. Context Injection
        var profilePath = Path.Combine(AppContext.BaseDirectory, "profile.json");
        var candidateProfile = await File.ReadAllTextAsync(profilePath, cancellationToken);

        // 4. AI Evaluation (Gemini)
        var evaluationResults = await aiEvaluator.EvaluateOffersAsync(targetOffers, candidateProfile, cancellationToken);

        foreach (var result in evaluationResults.OrderByDescending(r => r.MatchScorePercentage))
        {
            logger.LogInformation(
                "Match: {Score}% | Recommended: {IsRecommended} | Missing: {Missing}",
                result.MatchScorePercentage, 
                result.IsHighlyRecommended,
                string.Join(", ", result.MissingTechnologies));
        }

        // 5. Output (Dispatch to Discord)
        await notifier.SendDailySummaryAsync(targetOffers, evaluationResults, cancellationToken);
    }

    private static List<JobOffer> FilterRelevantOffers(IEnumerable<JobOffer> allOffers)
    {
        return allOffers
            .Where(o => 
                o.Title.Contains("Junior", StringComparison.OrdinalIgnoreCase) || 
                o.Title.Contains("Staż", StringComparison.OrdinalIgnoreCase) || 
                o.Title.Contains("Intern", StringComparison.OrdinalIgnoreCase) || 
                o.Title.Contains("Trainee", StringComparison.OrdinalIgnoreCase))
            .Where(o => 
                o.Title.Contains(".NET", StringComparison.OrdinalIgnoreCase) || 
                o.Title.Contains("C#", StringComparison.OrdinalIgnoreCase) || 
                o.Title.Contains("React", StringComparison.OrdinalIgnoreCase) ||
                o.Title.Contains("Fullstack", StringComparison.OrdinalIgnoreCase))
            .Take(15)
            .ToList();
    }
}