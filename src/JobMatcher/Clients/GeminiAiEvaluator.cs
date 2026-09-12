using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobMatcher.Interfaces;
using JobMatcher.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobMatcher.Clients;

// Implement AI evaluation logic directly calling the Google Gemini REST API
public class GeminiAiEvaluator(HttpClient httpClient, IConfiguration config, ILogger<GeminiAiEvaluator> logger) : IAiEvaluator
{
    public async Task<IEnumerable<JobEvaluationResult>> EvaluateOffersAsync(
        IEnumerable<JobOffer> offers, 
        string candidateProfile, 
        CancellationToken cancellationToken = default)
    {
        var apiKey = config["AI:ApiKey"];
        var model = config["AI:Model"] ?? "gemini-2.5-flash";
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("PLACEHOLDER"))
        {
            logger.LogWarning("Gemini API Key is missing. Skipping AI evaluation.");
            return [];
        }

        // Serialize offers to inject them into the AI prompt (Batch Processing for token efficiency)
        var offersJson = JsonSerializer.Serialize(offers);

        // System Prompt strictly defining the Persona, Task, and expected JSON Output Schema
        var prompt = $@"
            You are an expert IT Tech Lead and Tech Recruiter.
            I will provide you with a Candidate Profile and a JSON array of Job Offers.
            Evaluate EACH job offer against the candidate's profile.
            Calculate a MatchScorePercentage (0-100).
            Identify missing and matched technologies.
            Provide a brief justification for your score.
            Determine if it is Highly Recommended (score >= 70).

            Candidate Profile:
            {candidateProfile}

            Job Offers:
            {offersJson}

            Return ONLY a valid JSON array of objects matching this exact structure, with no markdown formatting:
            [
              {{
                ""OfferId"": ""string"",
                ""MatchScorePercentage"": int,
                ""MissingTechnologies"": [""string""],
                ""MatchedTechnologies"": [""string""],
                ""BriefJustification"": ""string"",
                ""IsHighlyRecommended"": boolean
              }}
            ]";

        // Construct the strict Gemini API payload requesting JSON response
        var requestPayload = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { responseMimeType = "application/json" }
        };

        try
        {
            logger.LogInformation("Sending a batch of {Count} offers to Gemini AI for evaluation...", offers.Count());
            
            var response = await httpClient.PostAsJsonAsync(url, requestPayload, cancellationToken);
            
            // 1. Zdejmujemy EnsureSuccessStatusCode i czytamy błąd z serwera!
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Gemini API returned {StatusCode}. Details: {ErrorBody}", response.StatusCode, errorBody);
                return [];
            }

            var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: cancellationToken);
            var resultText = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(resultText))
            {
                logger.LogWarning("Gemini AI returned an empty response.");
                return [];
            }

            // Clean markdown formatting safely (removing ```json and ``` blocks)
            resultText = resultText.Trim();
            if (resultText.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                resultText = resultText.Substring(7);
            }
            else if (resultText.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                resultText = resultText.Substring(3);
            }

            if (resultText.EndsWith("```"))
            {
                resultText = resultText.Substring(0, resultText.Length - 3);
            }
            
            resultText = resultText.Trim();

            // Deserialize the AI-generated JSON string directly into our Domain Records
            var evaluationResults = JsonSerializer.Deserialize<List<JobEvaluationResult>>(resultText);
            
            logger.LogInformation("Successfully evaluated {Count} offers via AI.", evaluationResults?.Count ?? 0);
            return evaluationResults ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to evaluate offers via Gemini AI.");
            return [];
        }
    }
}

// Internal Data Transfer Objects mapped to the Google Gemini API JSON structure
internal record GeminiResponse(
    [property: JsonPropertyName("candidates")] List<GeminiCandidate>? Candidates
);

internal record GeminiCandidate(
    [property: JsonPropertyName("content")] GeminiContent? Content
);

internal record GeminiContent(
    [property: JsonPropertyName("parts")] List<GeminiPart>? Parts
);

internal record GeminiPart(
    [property: JsonPropertyName("text")] string? Text
);