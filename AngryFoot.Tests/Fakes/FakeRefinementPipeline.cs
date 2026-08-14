using AngryFoot.ApiService.Application.Refinement;
using AngryFoot.Contracts;

namespace AngryFoot.Tests.Fakes;

/// <summary>
/// Stands in for the deep-review pass. Defaults to returning nothing, which is what every service
/// test that is not specifically about deep review should see.
/// </summary>
internal sealed class FakeRefinementPipeline(Func<RefinementRequest, RefinementDto?> factory) : IDraftRefinementPipeline
{
    public FakeRefinementPipeline(RefinementDto? result = null)
        : this(_ => result)
    {
    }

    /// <summary>What <see cref="CritiqueAsync"/> hands back; null makes the gate unavailable.</summary>
    public RefinementCritique? Critique { get; init; } = new("Too vague.", "The reviewer's version.");

    public List<RefinementRequest> Requests { get; } = [];
    public List<RefinementRequest> CritiqueRequests { get; } = [];
    public List<RefinementRequest> CompleteRequests { get; } = [];

    public Task<RefinementDto?> RefineAsync(RefinementRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(factory(request));
    }

    public Task<RefinementCritique?> CritiqueAsync(RefinementRequest request, CancellationToken cancellationToken)
    {
        CritiqueRequests.Add(request);
        return Task.FromResult(Critique);
    }

    public Task<RefinementDto?> CompleteAsync(RefinementRequest request, RefinementCritique critique, CancellationToken cancellationToken)
    {
        CompleteRequests.Add(request);
        return Task.FromResult(factory(request));
    }
}

/// <summary>Grounding that returns whatever the test wants, and records what it was asked for.</summary>
internal sealed class FakeRefinementGrounding(string context = "") : IRefinementGrounding
{
    public List<string> Queries { get; } = [];

    public Task<string> BuildContextAsync(string queryText, CancellationToken cancellationToken)
    {
        Queries.Add(queryText);
        return Task.FromResult(context);
    }
}
