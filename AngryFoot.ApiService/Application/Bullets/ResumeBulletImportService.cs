using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Bullets;

public interface IResumeBulletImportService
{
    Task<ResumeImportPreviewResponse> PreviewAsync(string resumeText, CancellationToken cancellationToken);

    Task<ResumeImportResultDto> ConfirmAsync(ConfirmResumeImportRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Turns pasted resume text into reviewable candidate bullets. <see cref="PreviewAsync"/> never
/// writes to the database, and <see cref="ConfirmAsync"/> creates bullets only through
/// <see cref="IBulletService.CreateAsync"/> so imported bullets get the same tagging, enrichment,
/// and vector indexing as hand-typed ones.
/// </summary>
public sealed class ResumeBulletImportService(
    AngryFootDbContext dbContext,
    IBulletService bulletService,
    IBulletDuplicateDetector duplicateDetector) : IResumeBulletImportService
{
    public async Task<ResumeImportPreviewResponse> PreviewAsync(string resumeText, CancellationToken cancellationToken)
    {
        var parsed = ResumeBulletParser.Parse(resumeText);
        if (parsed.Count == 0)
        {
            return new ResumeImportPreviewResponse([], DuplicateDetectionModeDto.Semantic, null);
        }

        var subjects = parsed.Select((candidate, index) => new DuplicateSubject(index, null, candidate.Text)).ToArray();
        var scan = await duplicateDetector.DetectAsync(subjects, cancellationToken);

        var candidates = parsed
            .Select((candidate, index) => new CandidateBulletDto(
                index,
                candidate.Text,
                candidate.SuggestedEmployer,
                scan.WarningsByIndex.TryGetValue(index, out var warnings) ? warnings : []))
            .ToArray();

        return new ResumeImportPreviewResponse(candidates, scan.Mode, scan.Message);
    }

    public async Task<ResumeImportResultDto> ConfirmAsync(ConfirmResumeImportRequest request, CancellationToken cancellationToken)
    {
        var items = request.Bullets
            .Where(x => !string.IsNullOrWhiteSpace(x.BulletText))
            .ToArray();

        var created = new List<BulletDto>(items.Length);
        var createdIdsByIndex = new Dictionary<int, Guid>(items.Length);

        foreach (var item in items)
        {
            var bullet = await bulletService.CreateAsync(
                new CreateBulletRequest(item.BulletText, item.SourceEmployer), cancellationToken);

            created.Add(bullet);
            createdIdsByIndex[item.Index] = bullet.Id;
        }

        // Ignore decisions can only be persisted now: during review a candidate has no bullet id,
        // so a pair referencing it isn't expressible until the candidate has been accepted.
        var ignoredCount = await PersistIgnoredPairsAsync(items, createdIdsByIndex, cancellationToken);

        return new ResumeImportResultDto(created, ignoredCount);
    }

    /// <summary>
    /// Whether the bullet being imported is the one the duplicate warnings were raised against. An
    /// omitted <see cref="ImportBulletItem.ReviewedBulletText"/> is treated as unreviewed, so a
    /// caller has to state what was reviewed before an ignored pair is recorded on its behalf.
    /// </summary>
    private static bool WasReviewedAsImported(ImportBulletItem item)
    {
        return !string.IsNullOrWhiteSpace(item.ReviewedBulletText)
            && string.Equals(item.ReviewedBulletText.Trim(), item.BulletText.Trim(), StringComparison.Ordinal);
    }

    private async Task<int> PersistIgnoredPairsAsync(
        IReadOnlyList<ImportBulletItem> items,
        IReadOnlyDictionary<int, Guid> createdIdsByIndex,
        CancellationToken cancellationToken)
    {
        var wanted = new Dictionary<(Guid A, Guid B), double>();

        foreach (var item in items)
        {
            // The warnings were raised against the previewed wording. If the user edited the bullet
            // afterwards, those decisions were never made about the text being imported, and
            // recording them would suppress warnings nobody actually reviewed.
            if (!WasReviewedAsImported(item))
            {
                continue;
            }

            var bulletId = createdIdsByIndex[item.Index];

            foreach (var decision in item.IgnoredDuplicates)
            {
                Guid otherId;
                if (decision.ExistingBulletId is { } existingId)
                {
                    otherId = existingId;
                }
                else if (decision.CandidateIndex is { } candidateIndex
                    && createdIdsByIndex.TryGetValue(candidateIndex, out var importedId))
                {
                    // Skipped when the other candidate wasn't imported: there is no pair to ignore.
                    otherId = importedId;
                }
                else
                {
                    continue;
                }

                if (otherId == bulletId)
                {
                    continue;
                }

                var pair = BulletDuplicatePair.Canonical(bulletId, otherId);
                wanted[pair] = Math.Max(wanted.GetValueOrDefault(pair), decision.Similarity);
            }
        }

        if (wanted.Count == 0)
        {
            return 0;
        }

        var existingPairs = await dbContext.IgnoredBulletDuplicatePairs
            .AsNoTracking()
            .Select(x => new { x.BulletIdA, x.BulletIdB })
            .ToListAsync(cancellationToken);
        var alreadyIgnored = existingPairs.Select(x => (x.BulletIdA, x.BulletIdB)).ToHashSet();

        var now = DateTime.UtcNow;
        var toAdd = wanted
            .Where(entry => !alreadyIgnored.Contains(entry.Key))
            .Select(entry => new IgnoredBulletDuplicatePair
            {
                Id = Guid.NewGuid(),
                BulletIdA = entry.Key.A,
                BulletIdB = entry.Key.B,
                Similarity = entry.Value,
                CreatedDate = now,
                Note = "Dismissed during resume import."
            })
            .ToArray();

        if (toAdd.Length == 0)
        {
            return 0;
        }

        dbContext.IgnoredBulletDuplicatePairs.AddRange(toAdd);
        await dbContext.SaveChangesAsync(cancellationToken);

        return toAdd.Length;
    }
}
