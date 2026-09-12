namespace JobMatcher.Models;

// Represent a unified job offer structure parsed from various external job boards
public record JobOffer(
    string Id,
    string Title,
    string Company,
    string Url,
    List<string> Technologies,
    string? SalaryRange,
    string SourcePlatform
);