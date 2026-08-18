using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Application.Review;

/// <param name="BulletSuggestions">Advice keyed by the bullet's position in what the model was shown.</param>
internal sealed record ResumeReview(
    string? Summary,
    IReadOnlyList<CoverageDiagnosticDto> SpotChecks,
    IReadOnlyDictionary<int, IReadOnlyList<string>> BulletSuggestions);

internal interface IResumeReviewer
{
    Task<ResumeReview?> ReviewAsync(
        IReadOnlyList<Bullet> bullets,
        IReadOnlyList<CoverageDiagnosticDto> deterministicFindings,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads the resume after the deterministic checks have run, and adds what a rule cannot: whether
/// the bullets read as one story, whether a claim is vague, what a particular bullet is missing.
/// </summary>
/// <remarks>
/// The deterministic findings are the floor. This can add to them and explain them; it cannot remove
/// one, and anything it returns that does not point at a bullet it was actually shown is dropped. A
/// note about a bullet that does not exist is not a note, and from the outside the user cannot tell
/// it apart from one that does.
/// </remarks>
internal sealed class AiResumeReviewer(IChatClient chatClient, ILogger<AiResumeReviewer> logger) : IResumeReviewer
{
    private const int MaxBulletsSentToAi = 60;
    private const int MaxSpotChecks = 5;
    private const int MaxSuggestionsPerBullet = 3;

    private sealed record BulletNote(int? Bullet, IReadOnlyList<string>? Suggestions);

    private sealed record SpotCheck(string? Message, string? Reasoning, IReadOnlyList<int>? Bullets);

    private sealed record ReviewPayload(
        string? Summary,
        IReadOnlyList<SpotCheck>? SpotChecks,
        IReadOnlyList<BulletNote>? BulletNotes);

    private const string SystemPrompt = """
        You are reviewing the text of a resume for the person who wrote it.

        Write about the document, never about the person: what the bullets say and do not say, not
        what the candidate is or is not capable of. State findings plainly; do not encourage,
        reassure, or soften. Do not invent metrics, employers, or technologies the resume does not
        contain, and do not propose replacement wording - you cannot know the facts that would fill
        it in, and inventing them is the one failure the reader cannot catch.

        Return JSON with:
        - summary: two or three sentences on how the document reads as a whole.
        - spotChecks: notes about the resume overall. Every one must cite the bullets it is about in
          its bullets array. A note that cites nothing will be discarded. Do not name an index in the
          message itself - the reader is shown the bullet you cited, not its number, so "Bullet 0
          omits the timeframe" reads to them as a reference to nothing.
        - bulletNotes: for the bullets that would gain the most, the specific thing that would
          strengthen each, referenced by index.

        Do not repeat a finding already listed as known. Add only what those miss.
        """;

    public async Task<ResumeReview?> ReviewAsync(
        IReadOnlyList<Bullet> bullets,
        IReadOnlyList<CoverageDiagnosticDto> deterministicFindings,
        CancellationToken cancellationToken)
    {
        if (bullets.Count == 0)
        {
            return null;
        }

        var pool = bullets.Take(MaxBulletsSentToAi).ToArray();
        var userPrompt = AiJsonUtilities.ToJson(new
        {
            Bullets = pool.Select((bullet, index) => new
            {
                Index = index,
                Text = bullet.BulletText,
                Employer = bullet.SourceEmployer
            }),
            KnownFindings = deterministicFindings.Select(finding => finding.Message)
        });

        try
        {
            var response = await chatClient.GetJsonResponseAsync<ReviewPayload>(
                SystemPrompt, userPrompt, cancellationToken, logger);

            if (response.Value is not { } payload)
            {
                logger.LogWarning(
                    "Resume review response could not be parsed as JSON. Keeping the deterministic report. Raw response: {RawResponse}",
                    AiJsonUtilities.ForLog(response.RawText));
                return null;
            }

            var spotChecks = ReadSpotChecks(payload.SpotChecks, pool);
            var suggestions = ReadBulletNotes(payload.BulletNotes, pool);
            var summary = string.IsNullOrWhiteSpace(payload.Summary) ? null : payload.Summary.Trim();

            return summary is null && spotChecks.Count == 0 && suggestions.Count == 0
                ? null
                : new ResumeReview(summary, spotChecks, suggestions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Resume review call failed. Keeping the deterministic report.");
            return null;
        }
    }

    private IReadOnlyList<CoverageDiagnosticDto> ReadSpotChecks(
        IReadOnlyList<SpotCheck>? items,
        IReadOnlyList<Bullet> pool)
    {
        if (items is null)
        {
            return [];
        }

        List<CoverageDiagnosticDto> spotChecks = [];

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Message))
            {
                continue;
            }

            var cited = ResolveBullets(item.Bullets, pool);
            if (cited.Count == 0)
            {
                // A note about the resume that points at nothing in it cannot be checked, and the
                // user has no way to tell an observation from an invention.
                logger.LogWarning(
                    "Resume review raised a spot-check citing no bullet it was shown; discarding it. Message: {Message}",
                    AiJsonUtilities.ForLog(item.Message));
                continue;
            }

            spotChecks.Add(new CoverageDiagnosticDto(
                DiagnosticSeverityDto.Suggestion,
                CoverageDiagnosticCodes.WeakEvidence,
                item.Message.Trim(),
                EvidenceMappings.AboutBullets(
                    cited,
                    string.IsNullOrWhiteSpace(item.Reasoning) ? "Raised by the AI review." : item.Reasoning.Trim()),
                [.. cited.Select(bullet => bullet.Id)]));

            if (spotChecks.Count == MaxSpotChecks)
            {
                break;
            }
        }

        return spotChecks;
    }

    private IReadOnlyDictionary<int, IReadOnlyList<string>> ReadBulletNotes(
        IReadOnlyList<BulletNote>? notes,
        IReadOnlyList<Bullet> pool)
    {
        Dictionary<int, IReadOnlyList<string>> suggestions = [];
        if (notes is null)
        {
            return suggestions;
        }

        foreach (var note in notes)
        {
            if (note.Bullet is not { } index || index < 0 || index >= pool.Count)
            {
                logger.LogWarning(
                    "Resume review returned a note for bullet index {Index}, which it was not shown; discarding it.",
                    note.Bullet);
                continue;
            }

            var lines = (note.Suggestions ?? [])
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxSuggestionsPerBullet)
                .ToArray();

            if (lines.Length > 0)
            {
                suggestions[index] = lines;
            }
        }

        return suggestions;
    }

    private static IReadOnlyList<Bullet> ResolveBullets(IReadOnlyList<int>? indexes, IReadOnlyList<Bullet> pool)
        => indexes is null
            ? []
            : [.. indexes.Where(index => index >= 0 && index < pool.Count).Distinct().Select(index => pool[index])];
}
