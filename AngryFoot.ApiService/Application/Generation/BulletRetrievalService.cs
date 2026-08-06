using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Generation;

/// <summary>
/// Turns a job description + its analysis into a semantic query against
/// <see cref="IBulletVectorStore"/>. Kept separate from <see cref="BulletRankingService"/> so the
/// existing deterministic keyword ranking stays untouched as the fallback path.
/// </summary>
internal sealed class BulletRetrievalService(IBulletVectorStore vectorStore)
{
    public bool IsAvailable => vectorStore.IsAvailable;

    public Task<IReadOnlyList<BulletSimilarityMatch>> SearchAsync(
        string jobDescription, JobAnalysisDto analysis, int topK, CancellationToken cancellationToken)
        => vectorStore.SearchAsync(BuildQueryText(jobDescription, analysis), topK, cancellationToken);

    private static string BuildQueryText(string jobDescription, JobAnalysisDto analysis)
    {
        var parts = new List<string> { jobDescription };

        if (analysis.RequiredSkills.Count > 0)
        {
            parts.Add("Required skills: " + string.Join(", ", analysis.RequiredSkills));
        }

        if (analysis.PreferredSkills.Count > 0)
        {
            parts.Add("Preferred skills: " + string.Join(", ", analysis.PreferredSkills));
        }

        if (analysis.Technologies.Count > 0)
        {
            parts.Add("Technologies: " + string.Join(", ", analysis.Technologies));
        }

        if (analysis.Keywords.Count > 0)
        {
            parts.Add("Keywords: " + string.Join(", ", analysis.Keywords));
        }

        return string.Join(". ", parts);
    }
}
