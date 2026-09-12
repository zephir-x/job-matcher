using JobMatcher.Models;

namespace JobMatcher.Interfaces;

// Define a contract for dispatching notifications (e.g., Discord Webhook)
public interface INotifier
{
    // Send a formatted summary of highly recommended job offers
    Task SendDailySummaryAsync(
        IEnumerable<JobOffer> offers, 
        IEnumerable<JobEvaluationResult> results, 
        CancellationToken cancellationToken = default);
}