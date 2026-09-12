using JobMatcher.Models;

namespace JobMatcher.Interfaces;

// Define a contract for evaluating job offers against a candidate's profile
public interface IAiEvaluator
{
    // Evaluate a batch of job offers and return structured analysis results
    Task<IEnumerable<JobEvaluationResult>> EvaluateOffersAsync(
        IEnumerable<JobOffer> offers, 
        string candidateProfile, 
        CancellationToken cancellationToken = default);
}