using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Application.Evidence;

/// <param name="Summary">Null when the reviewer offered nothing better than the deterministic summary.</param>
internal sealed record EvidenceReview(
    string? Summary,
    IReadOnlyList<RequirementEvidence> Evidence,
    IReadOnlyList<CoverageDiagnosticDto> Diagnostics);

internal interface IEvidenceReviewer
{
    /// <summary>
    /// Reviews the deterministic evidence against the posting. Null when the reviewer produced
    /// nothing usable, in which case the caller keeps the report it already has.
    /// </summary>
    /// <param name="semantic">
    /// The same embedding matches the baseline was built from, so a bullet the embeddings already
    /// accepted is cited as such rather than being relabelled as the reviewer's own reading.
    /// </param>
    Task<EvidenceReview?> ReviewAsync(
        string jobDescription,
        JobAnalysisDto analysis,
        IReadOnlyList<RequirementEvidence> baseline,
        IReadOnlyList<Bullet> bullets,
        string? professionalSummary,
        SemanticEvidenceIndex? semantic,
        CancellationToken cancellationToken);
}

/// <summary>
/// Asks a model to correct the deterministic engine where word matching gets it wrong - a bullet
/// that demonstrates a requirement without naming it, or a claim the library does not support.
/// </summary>
/// <remarks>
/// The reviewer never returns a score, and its influence is deliberately asymmetric: it may lower a
/// strength freely but raise one only a single step, only when citing a bullet that was actually
/// sent to it. A resume's coverage number has to stay reproducible from the rows beneath it, so an
/// answer this service cannot check against the library is an answer it does not apply.
/// </remarks>
internal sealed class AiEvidenceReviewer(
    IChatClient chatClient,
    ILogger<AiEvidenceReviewer> logger) : IEvidenceReviewer
{
    private const int MaxBulletsSentToAi = 60;
    private const int MaxJobDescriptionLength = 6000;
    private const int MaxCitationsPerRequirement = 3;
    private const int MaxDiagnostics = 5;

    private sealed record CoverageReviewItem(
        string? Requirement,
        string? Strength,
        IReadOnlyList<Guid>? BulletIds,
        string? Reasoning);

    private sealed record UnsupportedClaimItem(
        string? Message,
        IReadOnlyList<Guid>? BulletIds,
        string? Reasoning);

    private sealed record CoverageReviewPayload(
        string? Summary,
        IReadOnlyList<CoverageReviewItem>? Requirements,
        IReadOnlyList<UnsupportedClaimItem>? UnsupportedClaims);

    public async Task<EvidenceReview?> ReviewAsync(
        string jobDescription,
        JobAnalysisDto analysis,
        IReadOnlyList<RequirementEvidence> baseline,
        IReadOnlyList<Bullet> bullets,
        string? professionalSummary,
        SemanticEvidenceIndex? semantic,
        CancellationToken cancellationToken)
    {
        var pool = bullets.Take(MaxBulletsSentToAi).ToDictionary(bullet => bullet.Id);

        try
        {
            var response = await chatClient.GetJsonResponseAsync<CoverageReviewPayload>(
                BuildSystemPrompt(),
                BuildUserPrompt(jobDescription, analysis, baseline, pool.Values, professionalSummary),
                cancellationToken,
                logger);

            if (response.Value is { } payload)
            {
                if (Apply(payload, baseline, pool, semantic) is { } review)
                {
                    return review;
                }

                logger.LogInformation("Evidence review returned nothing applicable; keeping the deterministic report.");
                return null;
            }

            logger.LogWarning(
                "Evidence review response could not be parsed. Keeping the deterministic report. Raw response: {RawResponse}",
                AiJsonUtilities.ForLog(response.RawText));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Evidence review call failed. Keeping the deterministic report.");
        }

        return null;
    }

    private static string BuildSystemPrompt() =>
        "You are reviewing an automated analysis of how well a candidate's achievement bullets evidence a job posting's requirements. " +
        "A deterministic word-matching pass has already linked requirements to bullets; your job is to correct it where matching words got it wrong. " +
        "You are assessing a document's evidence, never the candidate's ability or worth. " +
        "Do NOT return a score - the score is computed from the strengths you return. " +
        "Return strict JSON with fields: " +
        "summary (string, 2-3 sentences on what this resume does and does not evidence for this posting, addressed to the candidate), " +
        "requirements (array of objects, ONLY for requirements whose strength you are changing, each with: " +
        "requirement (string, copied exactly from the list given to you), " +
        "strength (\"Strong\", \"Weak\", or \"Missing\"), " +
        "bulletIds (array of bullet id strings copied exactly from the bullets given to you - required when raising a strength), " +
        "reasoning (string, one or two sentences on why, naming the evidence)), " +
        "unsupportedClaims (array of objects, each with message (string), bulletIds (array of bullet id strings), and reasoning (string), " +
        "for bullets asserting something the rest of the library does not support). " +
        "Strength means: Strong - a bullet demonstrates the requirement and shows what came of it; " +
        "Weak - a bullet mentions it without demonstrating a result; Missing - nothing in the library speaks to it. " +
        "Never cite a bullet id that was not given to you, and never invent requirements.";

    private static string BuildUserPrompt(
        string jobDescription,
        JobAnalysisDto analysis,
        IReadOnlyList<RequirementEvidence> baseline,
        IEnumerable<Bullet> pool,
        string? professionalSummary)
    {
        var currentAssessment = baseline.Select(evidence => new
        {
            requirement = evidence.Requirement.Term,
            kind = evidence.Requirement.Kind.ToString(),
            strength = evidence.Strength.ToString(),
            citedBulletIds = evidence.Citations.Select(citation => citation.Bullet.Id)
        });

        var candidateBullets = pool.Select(bullet => new
        {
            id = bullet.Id,
            text = bullet.BulletText,
            skills = bullet.Skills,
            technologies = bullet.Technologies
        });

        return $"Job description:\n{Truncate(jobDescription, MaxJobDescriptionLength)}\n\n" +
            $"Extracted job requirements: {AiJsonUtilities.ToJson(analysis)}\n\n" +
            $"Candidate profile summary: {professionalSummary ?? "(none provided)"}\n\n" +
            $"Current automated assessment: {AiJsonUtilities.ToJson(currentAssessment)}\n\n" +
            $"Candidate achievement bullets: {AiJsonUtilities.ToJson(candidateBullets)}";
    }

    /// <summary>Null when nothing in the payload survived validation.</summary>
    private EvidenceReview? Apply(
        CoverageReviewPayload payload,
        IReadOnlyList<RequirementEvidence> baseline,
        IReadOnlyDictionary<Guid, Bullet> pool,
        SemanticEvidenceIndex? semantic)
    {
        var adjustmentsByTerm = ReadAdjustments(payload.Requirements, baseline, pool, semantic);
        var diagnostics = ReadUnsupportedClaims(payload.UnsupportedClaims, pool);
        var summary = string.IsNullOrWhiteSpace(payload.Summary) ? null : payload.Summary.Trim();

        if (adjustmentsByTerm.Count == 0 && diagnostics.Count == 0 && summary is null)
        {
            return null;
        }

        var evidence = baseline
            .Select(current => adjustmentsByTerm.GetValueOrDefault(current.Requirement.Term, current))
            .ToArray();

        return new EvidenceReview(summary, evidence, diagnostics);
    }

    private Dictionary<string, RequirementEvidence> ReadAdjustments(
        IReadOnlyList<CoverageReviewItem>? items,
        IReadOnlyList<RequirementEvidence> baseline,
        IReadOnlyDictionary<Guid, Bullet> pool,
        SemanticEvidenceIndex? semantic)
    {
        var adjustments = new Dictionary<string, RequirementEvidence>(StringComparer.OrdinalIgnoreCase);
        if (items is null)
        {
            return adjustments;
        }

        var baselineByTerm = baseline.ToDictionary(
            evidence => evidence.Requirement.Term,
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item.Requirement is null || !baselineByTerm.TryGetValue(item.Requirement, out var current))
            {
                logger.LogWarning(
                    "Evidence review named requirement {Requirement}, which was not extracted from this posting. Skipping it.",
                    AiJsonUtilities.ForLog(item.Requirement));
                continue;
            }

            if (!Enum.TryParse<EvidenceStrengthDto>(item.Strength, ignoreCase: true, out var proposed))
            {
                continue;
            }

            var offered = ReadCitations(item.BulletIds, current.Requirement, pool, semantic);
            var citations = MergeCitations(current, offered);
            var strength = Constrain(proposed, current.Strength, offered);

            if (strength == current.Strength && citations.Count == current.Citations.Count)
            {
                continue;
            }

            var reasoning = string.IsNullOrWhiteSpace(item.Reasoning)
                ? EvidenceNarrative.Reasoning(current.Requirement, strength, citations)
                : item.Reasoning.Trim();

            adjustments[current.Requirement.Term] = current with
            {
                Strength = strength,
                Citations = citations,
                Reasoning = reasoning
            };
        }

        return adjustments;
    }

    /// <summary>
    /// The asymmetric trust rule. A reviewer may always argue the evidence is worse than it looks -
    /// that costs the candidate nothing but an honest report, so it applies unchanged. Arguing the
    /// evidence is better moves the score, so it is bounded three ways.
    /// </summary>
    /// <param name="offered">
    /// The bullets the reviewer <em>cited for this change</em>, not everything now attached to the
    /// requirement. A raise has to stand on the evidence the reviewer produced for it; letting it
    /// borrow a citation the deterministic pass had already found would let a purely semantic
    /// argument reach full credit on the strength of a bullet the reviewer never mentioned.
    /// </param>
    private static EvidenceStrengthDto Constrain(
        EvidenceStrengthDto proposed,
        EvidenceStrengthDto current,
        IReadOnlyList<EvidenceCitation> offered)
    {
        if (proposed <= current)
        {
            return proposed;
        }

        // An upgrade with nothing behind it is an opinion, and the score is not built from those.
        if (offered.Count == 0)
        {
            return current;
        }

        var oneStepUp = (EvidenceStrengthDto)Math.Min((int)proposed, (int)current + 1);

        // Full credit means the resume states the requirement. A reviewer reading a bullet as
        // merely related may say so - the citation is kept and labelled - but it does not pay out.
        return oneStepUp == EvidenceStrengthDto.Strong && !offered.Any(citation => citation.MatchKind == EvidenceMatchKindDto.ExactTerm)
            ? EvidenceStrengthDto.Weak
            : oneStepUp;
    }

    private IReadOnlyList<EvidenceCitation> ReadCitations(
        IReadOnlyList<Guid>? bulletIds,
        Requirement requirement,
        IReadOnlyDictionary<Guid, Bullet> pool,
        SemanticEvidenceIndex? semantic)
    {
        if (bulletIds is null)
        {
            return [];
        }

        var citations = new List<EvidenceCitation>();
        var seen = new HashSet<Guid>();

        foreach (var bulletId in bulletIds)
        {
            if (!pool.TryGetValue(bulletId, out var bullet))
            {
                logger.LogWarning(
                    "Evidence review referenced bullet {BulletId}, which is not in the candidate pool. Skipping it.",
                    bulletId);
                continue;
            }

            if (!seen.Add(bulletId))
            {
                continue;
            }

            // A bullet the deterministic rule or an embedding already accepts keeps its own
            // explanation; anything else is the reviewer reading meaning into text that does not name
            // the requirement, and is labelled as such all the way to the screen.
            citations.Add(EvidenceStrengthRule.Cite(bullet, requirement, semantic?.For(requirement.Term, bulletId))
                ?? new EvidenceCitation(
                    bullet,
                    requirement.Term,
                    EvidenceMatchKindDto.AiIdentified,
                    Confidence: null,
                    EvidenceStrengthDto.Weak,
                    $"The reviewer read this bullet as related to {requirement.Term}, though the bullet does not name it."));
        }

        return citations;
    }

    private static IReadOnlyList<EvidenceCitation> MergeCitations(
        RequirementEvidence current,
        IReadOnlyList<EvidenceCitation> added)
    {
        var known = current.Citations.Select(citation => citation.Bullet.Id).ToHashSet();

        return current.Citations
            .Concat(added.Where(citation => known.Add(citation.Bullet.Id)))
            .Take(MaxCitationsPerRequirement)
            .ToArray();
    }

    private IReadOnlyList<CoverageDiagnosticDto> ReadUnsupportedClaims(
        IReadOnlyList<UnsupportedClaimItem>? items,
        IReadOnlyDictionary<Guid, Bullet> pool)
    {
        if (items is null)
        {
            return [];
        }

        var diagnostics = new List<CoverageDiagnosticDto>();

        foreach (var item in items.Take(MaxDiagnostics))
        {
            if (string.IsNullOrWhiteSpace(item.Message))
            {
                continue;
            }

            var bullets = (item.BulletIds ?? [])
                .Distinct()
                .Where(pool.ContainsKey)
                .Select(bulletId => pool[bulletId])
                .ToArray();

            if (bullets.Length == 0)
            {
                // An unsupported-claim warning the user cannot trace to a bullet is an accusation
                // without a subject, which is exactly the opacity this report exists to remove.
                logger.LogWarning(
                    "Evidence review raised an unsupported claim citing no bullet in the pool. Skipping it: {Message}",
                    AiJsonUtilities.ForLog(item.Message));
                continue;
            }

            diagnostics.Add(new CoverageDiagnosticDto(
                DiagnosticSeverityDto.Warning,
                CoverageDiagnosticCodes.UnsupportedClaim,
                item.Message.Trim(),
                EvidenceMappings.AboutBullets(
                    bullets,
                    string.IsNullOrWhiteSpace(item.Reasoning)
                        ? "The reviewer could not find support for this claim elsewhere in your library."
                        : item.Reasoning.Trim(),
                    ["Either a bullet that substantiates the claim, or narrower wording this bullet can stand behind."]),
                bullets.Select(bullet => bullet.Id).ToArray()));
        }

        return diagnostics;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
