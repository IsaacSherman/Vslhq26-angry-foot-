using AngryFoot.Contracts;

namespace AngryFoot.Web.Models;

/// <summary>
/// Two-way-bindable view model for one resume-import candidate. Mirrors
/// <see cref="CandidateBulletDto"/> because Blazor's @bind can't target record properties, and
/// carries the review state (selected, per-warning ignore) that only exists client-side until the
/// import is confirmed.
/// </summary>
public sealed class EditableCandidateBullet
{
    public int Index { get; set; }
    public bool Selected { get; set; } = true;
    public string BulletText { get; set; } = string.Empty;
    public string? SourceEmployer { get; set; }
    public List<EditableDuplicateWarning> Duplicates { get; set; } = [];

    public static EditableCandidateBullet FromDto(CandidateBulletDto dto)
    {
        return new EditableCandidateBullet
        {
            Index = dto.Index,
            BulletText = dto.BulletText,
            SourceEmployer = dto.SuggestedEmployer,
            Duplicates = dto.Duplicates.Select(EditableDuplicateWarning.FromDto).ToList()
        };
    }

    public ImportBulletItem ToItem()
    {
        return new ImportBulletItem(
            Index,
            BulletText.Trim(),
            string.IsNullOrWhiteSpace(SourceEmployer) ? null : SourceEmployer.Trim(),
            Duplicates
                .Where(x => x.Ignored)
                .Select(x => new IgnoredDuplicateDecision(x.ExistingBulletId, x.CandidateIndex, x.Similarity))
                .ToArray());
    }
}

public sealed class EditableDuplicateWarning
{
    public DuplicateWarningKindDto Kind { get; set; }
    public Guid? ExistingBulletId { get; set; }
    public int? CandidateIndex { get; set; }
    public string MatchedText { get; set; } = string.Empty;
    public double Similarity { get; set; }

    /// <summary>The user declared the two bullets distinct; the pair is recorded so it stops warning.</summary>
    public bool Ignored { get; set; }

    /// <summary>The user acknowledged the overlap and is importing anyway, without recording a pair.</summary>
    public bool Accepted { get; set; }

    public static EditableDuplicateWarning FromDto(DuplicateWarningDto dto)
    {
        return new EditableDuplicateWarning
        {
            Kind = dto.Kind,
            ExistingBulletId = dto.ExistingBulletId,
            CandidateIndex = dto.CandidateIndex,
            MatchedText = dto.MatchedText,
            Similarity = dto.Similarity
        };
    }
}
