using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Generation;

/// <summary>
/// Accounts for every candidate bullet the generator considered: what happened to it, where it
/// ended up, and which of the posting's requirements it carries.
/// <para>
/// Pure and deterministic. The generator's choices are already made by the time this runs, so
/// explaining them needs no AI call and cannot disagree with the resume it describes.
/// </para>
/// </summary>
internal static class GenerationExplanationService
{
    public static GenerationExplanationDto Explain(
        JobAnalysisDto analysis,
        IReadOnlyList<RankedBullet> ranked,
        IReadOnlyList<RewrittenBullet> final)
    {
        var requirements = RequirementSet.From(analysis);
        var finalPositions = final
            .Select((rewritten, index) => (rewritten.Bullet.Id, Position: index + 1))
            .ToDictionary(x => x.Id, x => x.Position);
        var finalTexts = final.ToDictionary(x => x.Bullet.Id, x => x.Text);

        // What the resume as a whole still fails to evidence, so an omitted bullet that would have
        // helped can be called out as a cost rather than listed as a neutral fact.
        var unmet = EvidenceCoverageEngine
            .Evaluate(requirements, final.Select(x => x.Bullet).ToArray())
            .Where(x => x.Strength != EvidenceStrengthDto.Strong)
            .Select(x => x.Requirement.Term)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var decisions = ranked
            .Select((candidate, index) => Describe(
                candidate,
                rankerPosition: index + 1,
                finalPositions.TryGetValue(candidate.Bullet.Id, out var position) ? position : null,
                finalTexts.GetValueOrDefault(candidate.Bullet.Id),
                requirements,
                unmet))
            .OrderBy(x => x.ResumePosition ?? int.MaxValue)
            .ThenBy(x => x.RankerPosition)
            .ToArray();

        return new GenerationExplanationDto(Summarize(decisions, final.Count), decisions);
    }

    /// <summary>
    /// The same account for a resume built without a posting. Nothing here can be said in terms of
    /// requirements - there are none - so it reports what the ranker was actually weighing: how the
    /// bullet is written, what ground it covered that nothing else did, and, for the ones left off,
    /// what it repeated.
    /// </summary>
    public static GenerationExplanationDto ExplainGeneric(
        IReadOnlyList<RankedBullet> ranked,
        IReadOnlyList<RewrittenBullet> final,
        ResumeAudienceDto audience)
    {
        var finalPositions = final
            .Select((rewritten, index) => (rewritten.Bullet.Id, Position: index + 1))
            .ToDictionary(x => x.Id, x => x.Position);
        var finalTexts = final.ToDictionary(x => x.Bullet.Id, x => x.Text);

        var decisions = ranked
            .Select((candidate, index) => DescribeGeneric(
                candidate,
                rankerPosition: index + 1,
                finalPositions.TryGetValue(candidate.Bullet.Id, out var position) ? position : null,
                finalTexts.GetValueOrDefault(candidate.Bullet.Id)))
            .OrderBy(x => x.ResumePosition ?? int.MaxValue)
            .ThenBy(x => x.RankerPosition)
            .ToArray();

        return new GenerationExplanationDto(SummarizeGeneric(decisions, final, audience), decisions);
    }

    private static BulletDecisionDto DescribeGeneric(
        RankedBullet candidate,
        int rankerPosition,
        int? resumePosition,
        string? finalText)
    {
        var bullet = candidate.Bullet;
        var kind = ClassifyKind(bullet, rankerPosition, resumePosition, finalText);
        var reasons = candidate.Reasons ?? [];
        var credits = reasons.Where(x => x.Kind is RankingReasonKind.Quality or RankingReasonKind.Breadth).ToArray();
        var costs = reasons.Where(x => x.Kind is RankingReasonKind.Overlap or RankingReasonKind.Concentration).ToArray();

        return new BulletDecisionDto(
            bullet.Id,
            bullet.BulletText,
            finalText,
            kind,
            rankerPosition,
            resumePosition,
            new EvidenceRationaleDto(
                Requirement: null,
                SupportingEvidence: [new EvidenceCitationDto(
                    bullet.Id,
                    bullet.BulletText,
                    MatchedTerm: string.Empty,
                    IsExactTermMatch: true,
                    Because: credits.Length == 0
                        ? "The ranker recorded nothing in this bullet's favour."
                        : string.Join(" ", credits.Select(x => x.Text)))],
                // Only for the bullets left off: what a kept bullet repeats is a note about the
                // resume's shape, not a reason it is missing anything.
                MissingEvidence: kind.HasFlag(BulletDecisionKindDto.Omitted)
                    ? costs.Select(x => x.Text).ToArray()
                    : [],
                Reasoning: GenericReasoning(kind, rankerPosition, resumePosition, costs)));
    }

    private static string GenericReasoning(
        BulletDecisionKindDto kind,
        int rankerPosition,
        int? resumePosition,
        IReadOnlyList<RankingReason> costs)
    {
        if (kind.HasFlag(BulletDecisionKindDto.Omitted))
        {
            return costs.Count == 0
                ? $"Ranked {rankerPosition} of the candidates, which was below the cut for this resume."
                : $"Ranked {rankerPosition} of the candidates, held down by what it repeats of the bullets above it.";
        }

        var parts = new List<string>
        {
            kind.HasFlag(BulletDecisionKindDto.Reordered)
                ? $"The ranker placed it {rankerPosition}; deep review moved it to {resumePosition}."
                : $"Ranked {rankerPosition} and kept at that position."
        };

        parts.Add(kind.HasFlag(BulletDecisionKindDto.Revised)
            ? "Its wording was tailored to this audience; your own is shown above it."
            : "It appears in your own words.");

        if (costs.Count > 0)
        {
            parts.Add($"Kept despite one reservation: {string.Join(" ", costs.Select(x => x.Text))}");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Counts the breadth the resume actually achieved rather than restating the goal, because a
    /// library with one technology in it will produce a narrow resume however the ranker is
    /// weighted, and saying so is more use than a claim of diversity.
    /// </summary>
    private static string SummarizeGeneric(
        IReadOnlyList<BulletDecisionDto> decisions,
        IReadOnlyList<RewrittenBullet> final,
        ResumeAudienceDto audience)
    {
        // Technologies only. Enrichment writes skills as phrases - "charting/data visualization
        // optimization" - which are near-unique per bullet, so counting them reports a number in
        // the dozens for eight bullets and says nothing about breadth.
        var technologies = final
            .SelectMany(x => x.Bullet.Technologies)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var employers = final
            .Select(x => x.Bullet.SourceEmployer)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var parts = new List<string>
        {
            $"No posting: {final.Count} of the {decisions.Count} bullets the ranker surfaced made this resume, "
                + $"chosen for how they are written and for covering as much ground as your library allows"
        };

        if (technologies > 0)
        {
            parts.Add($"{technologies} distinct technolog{(technologies == 1 ? "y" : "ies")}");
        }

        if (employers > 0)
        {
            parts.Add($"{employers} employer{(employers == 1 ? string.Empty : "s")}");
        }

        var revised = decisions.Count(x => x.Kind.HasFlag(BulletDecisionKindDto.Revised));
        if (revised > 0)
        {
            parts.Add($"{revised} reworded for {AudienceLabel(audience)}");
        }

        return string.Join(", ", parts) + ".";
    }

    private static string AudienceLabel(ResumeAudienceDto audience) => audience switch
    {
        ResumeAudienceDto.Recruiter => "a recruiter",
        ResumeAudienceDto.HiringManager => "a hiring manager",
        ResumeAudienceDto.TechnicalLeader => "a technical leader",
        ResumeAudienceDto.Executive => "an executive",
        _ => "a general audience"
    };

    private static BulletDecisionDto Describe(
        RankedBullet candidate,
        int rankerPosition,
        int? resumePosition,
        string? finalText,
        IReadOnlyList<Requirement> requirements,
        HashSet<string> unmet)
    {
        var bullet = candidate.Bullet;
        var evidenced = EvidenceCoverageEngine.Evaluate(requirements, [bullet])
            .Where(x => x.Strength != EvidenceStrengthDto.Missing)
            .ToArray();

        var kind = ClassifyKind(bullet, rankerPosition, resumePosition, finalText);

        return new BulletDecisionDto(
            bullet.Id,
            bullet.BulletText,
            finalText,
            kind,
            rankerPosition,
            resumePosition,
            new EvidenceRationaleDto(
                Requirement: null,
                SupportingEvidence: [bullet.ToDecisionCitation(evidenced)],
                MissingEvidence: MissingEvidence(kind, evidenced, unmet),
                Reasoning: Reasoning(kind, rankerPosition, resumePosition, evidenced)));
    }

    private static BulletDecisionKindDto ClassifyKind(
        Bullet bullet,
        int rankerPosition,
        int? resumePosition,
        string? finalText)
    {
        if (resumePosition is null)
        {
            return BulletDecisionKindDto.Omitted;
        }

        var kind = BulletDecisionKindDto.Selected;

        if (resumePosition != rankerPosition)
        {
            kind |= BulletDecisionKindDto.Reordered;
        }

        if (!string.Equals(finalText, bullet.BulletText, StringComparison.Ordinal))
        {
            kind |= BulletDecisionKindDto.Revised;
        }

        return kind;
    }

    /// <summary>
    /// One sentence per thing that happened, assembled from the flags, so a bullet that was both
    /// moved and reworded reads as both rather than as whichever came first in a switch.
    /// </summary>
    private static string Reasoning(
        BulletDecisionKindDto kind,
        int rankerPosition,
        int? resumePosition,
        IReadOnlyList<RequirementEvidence> evidenced)
    {
        var carries = evidenced.Count == 0
            ? "It evidences none of the requirements extracted from this posting."
            : $"It evidences {Describe(evidenced)}.";

        if (kind.HasFlag(BulletDecisionKindDto.Omitted))
        {
            return evidenced.Count == 0
                ? $"Ranked {rankerPosition} of the candidates and left off. {carries}"
                : $"Ranked {rankerPosition} of the candidates, which was below the cut for this resume. {carries}";
        }

        var parts = new List<string>
        {
            kind.HasFlag(BulletDecisionKindDto.Reordered)
                ? $"The ranker placed it {rankerPosition}; deep review moved it to {resumePosition}, so a reader reaches it sooner or later than the ranker intended."
                : $"Ranked {rankerPosition} and kept at that position."
        };

        parts.Add(kind.HasFlag(BulletDecisionKindDto.Revised)
            ? "Its wording was tailored to this posting; your own is shown above it."
            : "It appears in your own words.");

        parts.Add(carries);

        return string.Join(" ", parts);
    }

    private static IReadOnlyList<string> MissingEvidence(
        BulletDecisionKindDto kind,
        IReadOnlyList<RequirementEvidence> evidenced,
        HashSet<string> unmet)
    {
        if (!kind.HasFlag(BulletDecisionKindDto.Omitted))
        {
            return [];
        }

        // An omitted bullet that speaks to something the resume does not evidence is the one worth
        // a second look, so say which requirement leaving it out costs.
        var wouldHaveHelped = evidenced
            .Select(x => x.Requirement.Term)
            .Where(unmet.Contains)
            .ToArray();

        return wouldHaveHelped.Length == 0
            ? []
            : [$"This resume does not fully evidence {string.Join(", ", wouldHaveHelped)}, which this bullet speaks to. Raising Max Bullets, or strengthening this one, would bring it in."];
    }

    private static string Describe(IReadOnlyList<RequirementEvidence> evidenced)
    {
        var terms = evidenced.Select(x => x.Requirement.Term).ToArray();

        return terms.Length switch
        {
            1 => terms[0],
            2 => $"{terms[0]} and {terms[1]}",
            _ => $"{string.Join(", ", terms.Take(3))} and {terms.Length - 3} more"
        };
    }

    private static string Summarize(IReadOnlyList<BulletDecisionDto> decisions, int resumeCount)
    {
        var omitted = decisions.Count(x => x.Kind.HasFlag(BulletDecisionKindDto.Omitted));
        var revised = decisions.Count(x => x.Kind.HasFlag(BulletDecisionKindDto.Revised));
        var reordered = decisions.Count(x => x.Kind.HasFlag(BulletDecisionKindDto.Reordered));

        var parts = new List<string>
        {
            $"{resumeCount} of the {decisions.Count} bullets the ranker surfaced made this resume"
        };

        if (revised > 0)
        {
            parts.Add($"{revised} reworded for this posting");
        }

        if (reordered > 0)
        {
            parts.Add($"{reordered} moved from the ranker's order");
        }

        if (omitted > 0)
        {
            parts.Add($"{omitted} left off");
        }

        return string.Join(", ", parts) + ".";
    }
}

file static class DecisionCitationExtensions
{
    /// <summary>
    /// The bullet itself, cited as the subject of the decision rather than as proof of a
    /// requirement - so the panel that renders every other rationale renders this one too.
    /// </summary>
    public static EvidenceCitationDto ToDecisionCitation(
        this Bullet bullet,
        IReadOnlyList<RequirementEvidence> evidenced)
    {
        return new EvidenceCitationDto(
            bullet.Id,
            bullet.BulletText,
            MatchedTerm: string.Empty,
            IsExactTermMatch: true,
            Because: evidenced.Count == 0
                ? "This bullet matches nothing the posting asks for."
                : $"This bullet carries {evidenced.Count} of the posting's requirements.");
    }
}
