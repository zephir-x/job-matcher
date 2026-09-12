using System.Net.Http.Json;
using JobMatcher.Interfaces;
using JobMatcher.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobMatcher.Clients;

// Implement Discord Webhook integration to dispatch daily matching reports
public class DiscordNotifier(HttpClient httpClient, IConfiguration config, ILogger<DiscordNotifier> logger) : INotifier
{
    public async Task SendDailySummaryAsync(
        IEnumerable<JobOffer> offers, 
        IEnumerable<JobEvaluationResult> results, 
        CancellationToken cancellationToken = default)
    {
        var webhookUrl = config["Notifications:DiscordWebhookUrl"];

        if (string.IsNullOrWhiteSpace(webhookUrl) || webhookUrl.Contains("PLACEHOLDER"))
        {
            logger.LogWarning("Discord Webhook URL is missing. Skipping notifications.");
            return;
        }

        // Filter only offers that AI deemed a good fit (Highly Recommended or >= 70%)
        var recommendedResults = results
            .Where(r => r.IsHighlyRecommended || r.MatchScorePercentage >= 70)
            .OrderByDescending(r => r.MatchScorePercentage)
            .Take(5)
            .ToList();

        if (recommendedResults.Count == 0)
        {
            logger.LogInformation("No highly recommended jobs today to send to Discord.");
            return;
        }

        logger.LogInformation("Preparing to send {Count} job matches to Discord...", recommendedResults.Count);

        // Construct Discord Rich Embeds for beautiful UI presentation
        var embeds = recommendedResults.Select(result =>
        {
            var offer = offers.FirstOrDefault(o => o.Id == result.OfferId) 
                        ?? offers.First(); // Fallback in case AI slightly hallucinated the ID

            return new
            {
                title = $"[{result.MatchScorePercentage}%] {offer.Title}",
                url = offer.Url,
                color = 5814783, // Discord aesthetic color (Green/Blue)
                fields = new[]
                {
                    new { name = "🏢 Company", value = offer.Company, inline = true },
                    new { name = "💰 Salary", value = offer.SalaryRange ?? "Undisclosed", inline = true },
                    new { 
                        name = "🚀 Missing Tech (Gap)", 
                        value = result.MissingTechnologies.Count != 0 ? string.Join(", ", result.MissingTechnologies) : "None! Perfect Match.", 
                        inline = false 
                    },
                    new { name = "💡 AI Verdict", value = result.BriefJustification, inline = false }
                },
                footer = new { text = $"Source: {offer.SourcePlatform} | Job Matcher AI Agent" }
            };
        }).ToList();

        var payload = new
        {
            content = "🚀 **Daily Job Matcher Report is ready!** Here are the best fits for your .NET/React profile:",
            embeds = embeds
        };

        try
        {
            // Dispatch the payload via HTTP POST to Discord
            var response = await httpClient.PostAsJsonAsync(webhookUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            logger.LogInformation("Successfully sent daily summary to Discord.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send notification to Discord.");
        }
    }
}