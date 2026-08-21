using AngryFoot.ApiService.Domain;

namespace AngryFoot.ApiService.Application.Retrieval;

/// <summary>
/// How a bullet is written out for embedding. Shared so that a vector stored in the index and a
/// vector computed in memory describe the same bullet the same way - a similarity threshold measured
/// against one of them would otherwise not hold for the other.
/// </summary>
internal static class BulletEmbeddingText
{
    public static string For(Bullet bullet)
    {
        var parts = new List<string> { bullet.BulletText };

        if (bullet.Skills.Count > 0)
        {
            parts.Add("Skills: " + string.Join(", ", bullet.Skills));
        }

        if (bullet.Technologies.Count > 0)
        {
            parts.Add("Technologies: " + string.Join(", ", bullet.Technologies));
        }

        if (bullet.JobCategories.Count > 0)
        {
            parts.Add("Job categories: " + string.Join(", ", bullet.JobCategories));
        }

        return string.Join(". ", parts);
    }
}
