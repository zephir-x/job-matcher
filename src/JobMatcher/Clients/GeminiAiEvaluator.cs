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

        // System Prompt strictly defining the Persona, Task, Dealbreakers and expected JSON Schema (you can customize it to your preferences!)
        var prompt = $@"
            You are a strict, highly pragmatic IT Tech Recruiter evaluating job offers for a specific candidate.
            I will provide a Candidate Profile (JSON) and an array of Job Offers (JSON).
            
            CRITICAL RULES (DEALBREAKERS):
            1. SENIORITY (STRICT): The candidate is actively seeking Internships (Staż/Praktyki), Junior positions, IT Support, Helpdesk, or Technical Administration roles. 
               If the job title contains 'Senior', 'Lead', 'Expert', 'Architect', 'Mid', 'Regular', 'Specjalista', or requires 2+ years of commercial experience, the MatchScorePercentage MUST be heavily penalized (maximum 20%).
            2. ROLE SCOPE: The candidate is open to Software Development (.NET/React), Cloud (Azure/Terraform), and broad IT operations (Support, Networking, Documentation). Reward offers that mention 'Student', 'Graduate', 'Trainee', 'Helpdesk', or 'Intern'.
            3. LOCATION: The candidate is based in Kraków, Poland. If a job is strictly on-site (Office) in a city other than Kraków, the score MUST be below 20%. Fully Remote or Hybrid roles in Kraków are perfect.
            4. SALARY HEURISTIC: In Poland, salaries above 10,000 PLN generally indicate Regular/Mid expectations. Use this as a strong hint to downgrade the score unless the title explicitly says 'Junior' or 'Intern'.

            SCORING RUBRIC:
            - Base score heavily depends on matching the entry-level/internship requirement.
            - Evaluate technology stack overlap or IT support/networking relevance based on the profile.
            - Immediately apply the penalties from the CRITICAL RULES above.
            - Provide a brief, brutally honest justification.
            - Set 'IsHighlyRecommended' to true ONLY if the score is >= 75 AND it is a genuine entry-level/intern/support role matching the location.

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
            
            // Remove EnsureSuccessStatusCode and read the error from the server
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
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var evaluationResults = JsonSerializer.Deserialize<List<JobEvaluationResult>>(resultText, jsonOptions);
            
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