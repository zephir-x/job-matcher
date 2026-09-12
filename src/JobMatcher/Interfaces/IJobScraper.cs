using JobMatcher.Models;

namespace JobMatcher.Interfaces;

// Define a contract for fetching job offers from specific platforms
public interface IJobScraper
{
    // Retrieve a list of active job offers matching predefined criteria
    Task<IEnumerable<JobOffer>> FetchJobsAsync(CancellationToken cancellationToken = default);
}