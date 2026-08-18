namespace AngryFoot.ApiService.Application.Review;

/// <summary>
/// The summary the review returns when no AI wrote one. Kept here rather than inline for the reason
/// <c>EvidenceNarrative</c> exists: every sentence the product says about someone's resume without a
/// model behind it lives in one file, where its tone can be read in one sitting.
/// </summary>
internal static class ResumeReviewNarrative
{
    public static string Summary(int bulletCount, int findingCount)
    {
        if (bulletCount == 0)
        {
            return "No achievement bullets could be read out of this document, so there was nothing to review "
                + "line by line. The notes below are about the document itself.";
        }

        var bullets = $"{bulletCount} bullet{(bulletCount == 1 ? "" : "s")}";

        // Deliberately not "your resume is good": nothing here measured quality, only the presence
        // of things that can be checked, and saying otherwise would claim an assessment not made.
        return findingCount == 0
            ? $"Read {bullets}. None of the checks below found anything, which means no rule was broken - "
                + "not that the writing is finished."
            : $"Read {bullets} and found {findingCount} thing{(findingCount == 1 ? "" : "s")} worth a look. "
                + "Each one names the bullet it is about and what would settle it.";
    }
}
