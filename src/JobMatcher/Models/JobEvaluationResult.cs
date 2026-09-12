namespace JobMatcher.Models;

// Represent the strictly typed JSON response returned by the AI model
public record JobEvaluationResult(
    string OfferId,
    int MatchScorePercentage,
    List<string> MissingTechnologies,
    List<string> MatchedTechnologies,
    string BriefJustification,
    bool IsHighlyRecommended
);