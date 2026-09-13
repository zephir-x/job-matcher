using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JobMatcher.Interfaces;
using JobMatcher.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobMatcher.Clients;

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

        // Join results with offers and group by platform
        var groupedData = results
            .Join(offers, r => r.OfferId, o => o.Id, (Result, Offer) => new { Result, Offer })
            .GroupBy(x => x.Offer.SourcePlatform)
            .ToList();

        var embeds = new List<object>();
        var sb = new StringBuilder();
        sb.AppendLine("# Full Job Matcher Evaluation Report");
        sb.AppendLine($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\n");

        foreach (var group in groupedData)
        {
            var platformName = group.Key;
            var sortedGroup = group.OrderByDescending(x => x.Result.MatchScorePercentage).ToList();

			// Build Markdown Section
            sb.AppendLine($"## Source: {platformName}");
            foreach (var item in sortedGroup)
            {
                sb.AppendLine($"### [{item.Result.MatchScorePercentage}%] {item.Offer.Title} @ {item.Offer.Company}");
                sb.AppendLine($"- Salary: {item.Offer.SalaryRange ?? "Undisclosed"}");
                sb.AppendLine($"- Missing Tech: {(item.Result.MissingTechnologies.Count > 0 ? string.Join(", ", item.Result.MissingTechnologies) : "None")}");
                sb.AppendLine($"- AI Verdict: {item.Result.BriefJustification}");
                sb.AppendLine($"- URL: {item.Offer.Url}");
                sb.AppendLine();
            }

			// Build Discord Embed Section
            var topMatches = sortedGroup
                .Where(x => x.Result.IsHighlyRecommended || x.Result.MatchScorePercentage >= 70)
                .Take(3) // Top 3 per platform to keep the message clean
                .ToList();

            if (topMatches.Count > 0)
            {
                var fields = topMatches.Select(x => new 
                {
                    name = $"[{x.Result.MatchScorePercentage}%] {x.Offer.Title.ToUpper()}",
                    
                    value = $"🏢 **COMPANY:** {x.Offer.Company}\n" +
                            $"💰 **SALARY:** `{x.Offer.SalaryRange ?? "Undisclosed"}`\n" +
                            $"🚀 **GAP:** {(x.Result.MissingTechnologies.Count > 0 ? $"`{string.Join("`, `", x.Result.MissingTechnologies)}`" : "*None! Perfect Match.*")}\n" +
                            $"💡 **VERDICT:**\n> {x.Result.BriefJustification}\n\n" +
                            $"🔗 **[CLICK HERE TO APPLY]({x.Offer.Url})**\n\u200B",
                    
                    inline = false
                }).ToArray();

                embeds.Add(new 
                {
                    title = $"📊 PLATFORM: {platformName.ToUpper()}",
                    color = 5814783, // Discord aesthetic color (Green/Blue)
                    fields = fields
                });
            }
        }

        if (embeds.Count == 0)
        {
            logger.LogInformation("No highly recommended jobs today to send to Discord.");
            return;
        }

        logger.LogInformation("Preparing to send consolidated report to Discord...");

        // Dispatch Payload
        var fileBytes = Encoding.UTF8.GetBytes(sb.ToString());
        using var multipartContent = new MultipartFormDataContent();

        var jsonPayload = JsonSerializer.Serialize(new
        {
            content = "🚀 **Daily Job Matcher Report is ready!** Here are the best fits for your profile segmented by platform. See the attached markdown file for the full list of evaluated offers:",
            embeds = embeds
        });

        multipartContent.Add(new StringContent(jsonPayload, Encoding.UTF8, "application/json"), "payload_json");

        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        multipartContent.Add(fileContent, "files[0]", "FullEvaluationReport.md");

        try
        {
            var response = await httpClient.PostAsync(webhookUrl, multipartContent, cancellationToken);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("Successfully sent daily summary and report file to Discord.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send notification to Discord.");
        }
    }
}