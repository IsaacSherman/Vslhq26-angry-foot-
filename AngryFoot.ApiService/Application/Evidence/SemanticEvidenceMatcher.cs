using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Domain;

namespace AngryFoot.ApiService.Application.Evidence;

/// <summary>
/// Which bullets an embedding read as evidence for which requirements, and how strongly. Computed
/// before the coverage engine runs and handed to it, so the engine stays pure and synchronous - see
/// <see cref="EvidenceCoverageEngine"/>, which is also called once per candidate bullet from the
/// generation explanation and could not afford to await anything.
/// </summary>
public sealed class SemanticEvidenceIndex(IReadOnlyDictionary<(string Term, Guid BulletId), double> confidence)
{
    public static SemanticEvidenceIndex Empty { get; } = new(new Dictionary<(string, Guid), double>());

    public bool IsEmpty => confidence.Count == 0;

    public double? For(string term, Guid bulletId)
        => confidence.TryGetValue((term, bulletId), out var score) ? score : null;
}

internal interface ISemanticEvidenceMatcher
{
    Task<SemanticEvidenceIndex> MatchAsync(
        IReadOnlyList<Requirement> requirements,
        IReadOnlyList<Bullet> bullets,
        CancellationToken cancellationToken);
}

internal sealed class SemanticEvidenceMatcher(ITextEmbedder embedder) : ISemanticEvidenceMatcher
{
    /// <summary>
    /// How close a requirement and a bullet must sit before the bullet is offered as evidence.
    /// <para>
    /// Its own number rather than a reuse of an existing one, because the distribution is its own: a
    /// short requirement phrase against a tag-enriched bullet vector is neither the bullet-against-
    /// bullet comparison behind <c>BulletDuplicateDetector</c>'s 0.80 nor the whole-job-description
    /// query behind <c>GenerationOrchestrator</c>'s 0.35.
    /// </para>
    /// <para>
    /// Measured against <c>text-embedding-3-small</c> over a real 30-bullet library of embedded and
    /// industrial-automation work. Requirements from unrelated professions - pediatric nursing,
    /// equine dentistry, underwater welding - produced no score above 0.45 at all, so the floor is
    /// not where the risk lies. The risk is adjacent work the library does <em>not</em> contain:
    /// </para>
    /// <list type="table">
    /// <item><description>evidenced: industrial automation 0.551, embedded device integration 0.548,
    /// hardware test systems 0.541, task automation 0.523, hardware validation 0.514</description></item>
    /// <item><description>absent: React front-end development 0.519, Microsoft Azure 0.469,
    /// machine learning training 0.467</description></item>
    /// </list>
    /// <para>
    /// Those two bands <em>overlap</em>: a technology the library has never touched outscored work it
    /// genuinely evidences. No threshold separates them, so this one is set above the false-positive
    /// band rather than below the true-positive one. That costs real paraphrases - task automation and
    /// hardware validation are missed at this cut - and the trade is deliberate: a requirement wrongly
    /// counted inflates the score and tells the user their resume says something it does not, which is
    /// the failure this whole feature exists to prevent. A requirement wrongly missed only leaves a row
    /// reading "not stated in these words", which the report already says out loud.
    /// </para>
    /// <para>
    /// One library on one deployment, so treat it as a starting point rather than a constant of
    /// nature. <c>SemanticEvidenceThreshold_KeepsRelatedWorkAboveUnrelatedTechnologies</c> in
    /// <c>RealAiSmokeTests</c> re-measures it.
    /// </para>
    /// </summary>
    public const double MinimumConfidence = 0.53;

    public async Task<SemanticEvidenceIndex> MatchAsync(
        IReadOnlyList<Requirement> requirements,
        IReadOnlyList<Bullet> bullets,
        CancellationToken cancellationToken)
    {
        if (!embedder.IsAvailable || requirements.Count == 0 || bullets.Count == 0)
        {
            return SemanticEvidenceIndex.Empty;
        }

        var requirementTexts = requirements.Select(QueryTextFor).ToArray();
        var bulletTexts = bullets.Select(BulletEmbeddingText.For).ToArray();

        var vectors = await embedder.EmbedAsync([.. requirementTexts, .. bulletTexts], cancellationToken);
        if (vectors is null)
        {
            return SemanticEvidenceIndex.Empty;
        }

        var confidence = new Dictionary<(string, Guid), double>();
        for (var r = 0; r < requirements.Count; r++)
        {
            for (var b = 0; b < bullets.Count; b++)
            {
                var similarity = VectorMath.CosineSimilarity(vectors[r], vectors[requirements.Count + b]);
                if (similarity >= MinimumConfidence)
                {
                    confidence[(requirements[r].Term, bullets[b].Id)] = similarity;
                }
            }
        }

        return confidence.Count == 0 ? SemanticEvidenceIndex.Empty : new SemanticEvidenceIndex(confidence);
    }

    /// <summary>
    /// A bare "Azure" embeds as the place, not as the requirement; naming what the string is puts it
    /// in the same register as the bullet it is being compared against.
    /// </summary>
    public static string QueryTextFor(Requirement requirement) => $"Job requirement: {requirement.Term}";
}
