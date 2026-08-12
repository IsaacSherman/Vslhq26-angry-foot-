using AngryFoot.ApiService.Domain;

namespace AngryFoot.ApiService.Application.Generation;

internal enum EvidenceMatch
{
    /// <summary>
    /// The term must appear as a whole word. The right rule for named technologies and
    /// acronyms: "AWS" is evidence of AWS, but the "aws" inside "laws" is not.
    /// </summary>
    WholeWord,

    /// <summary>
    /// The term must start a word but may be followed by the rest of it, so the deliberately
    /// clipped terms in the benchmark dataset match the words they were clipped from -
    /// "develop" covers "developed" and "development", "analyz" covers "analyzed".
    /// </summary>
    WordStart
}

/// <summary>
/// Decides whether a bullet counts as evidence for a requirement term. Shared by the fit
/// heuristic and the occupation benchmark so the two never disagree about what "covered" means.
/// </summary>
internal static class BulletEvidence
{
    public static bool Supports(Bullet bullet, string term, EvidenceMatch match = EvidenceMatch.WholeWord)
    {
        return ContainsTerm(bullet.BulletText, term, match)
            || bullet.Skills.Any(skill => ContainsTerm(skill, term, match))
            || bullet.Technologies.Any(technology => ContainsTerm(technology, term, match));
    }

    public static bool SupportsAny(Bullet bullet, IReadOnlyList<string> terms, EvidenceMatch match = EvidenceMatch.WholeWord)
    {
        return terms.Any(term => Supports(bullet, term, match));
    }

    private static bool ContainsTerm(string text, string term, EvidenceMatch match)
    {
        if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(text))
        {
            return false;
        }

        var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            if (StartsOnBoundary(text, term, index)
                && (match == EvidenceMatch.WordStart || EndsOnBoundary(text, term, index)))
            {
                return true;
            }

            index = text.IndexOf(term, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// A term opening with punctuation - ".NET" - carries its own boundary, and requiring another
    /// one in front of it would stop it matching "ASP.NET".
    /// </summary>
    private static bool StartsOnBoundary(string text, string term, int index)
    {
        return !IsWordCharacter(term[0]) || index == 0 || !IsWordCharacter(text[index - 1]);
    }

    /// <summary>
    /// Likewise a term closing with punctuation - "C#", "C++" - already ends at a boundary; the
    /// character after it is free to be anything.
    /// </summary>
    private static bool EndsOnBoundary(string text, string term, int index)
    {
        var end = index + term.Length;
        return !IsWordCharacter(term[^1]) || end == text.Length || !IsWordCharacter(text[end]);
    }

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character);
}
