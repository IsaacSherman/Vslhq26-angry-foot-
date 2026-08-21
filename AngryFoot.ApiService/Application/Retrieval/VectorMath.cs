namespace AngryFoot.ApiService.Application.Retrieval;

internal static class VectorMath
{
    /// <summary>
    /// Zero for vectors of differing length rather than an exception: a dimension mismatch means a
    /// deployment changed under a stored vector, which should cost a comparison, not a request.
    /// </summary>
    public static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            return 0;
        }

        double dot = 0, leftMagnitude = 0, rightMagnitude = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * (double)right[i];
            leftMagnitude += left[i] * (double)left[i];
            rightMagnitude += right[i] * (double)right[i];
        }

        var denominator = Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude);
        return denominator == 0 ? 0 : dot / denominator;
    }
}
