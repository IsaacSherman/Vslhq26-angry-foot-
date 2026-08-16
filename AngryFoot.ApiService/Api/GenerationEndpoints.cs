using AngryFoot.ApiService.Application.Benchmarks;
using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Application.Generation;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Api;

public static class GenerationEndpoints
{
    private sealed record AnalyzeRequest(string JobDescription, string? JobTitle = null);

    public static RouteGroupBuilder MapGenerationEndpoints(this RouteGroupBuilder apiGroup)
    {
        var generations = apiGroup.MapGroup("/generations");

        generations.MapPost("/", async (GenerationRequest request, IGenerationOrchestrator orchestrator, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.JobDescription))
            {
                return Results.BadRequest(new { error = "jobDescription is required." });
            }

            var result = await orchestrator.GenerateAsync(request, cancellationToken);
            return Results.Ok(result);
        });

        // A resume for a recruiter with no posting in hand. Deliberately its own route rather than
        // a nullable jobDescription on the one above: half the tailored request's fields mean
        // nothing here, and a shape whose validity depends on a flag is a shape nobody can read.
        generations.MapPost("/generic", async (
            GenericGenerationRequest request,
            IGenerationOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.IsDefined(request.Audience))
            {
                return Results.BadRequest(new
                {
                    error = $"audience must be one of: {string.Join(", ", Enum.GetNames<ResumeAudienceDto>())}."
                });
            }

            var result = await orchestrator.GenerateGenericAsync(request, cancellationToken);
            return Results.Ok(result);
        });

        generations.MapPost("/analyze", async (
            AnalyzeRequest request,
            IJobAnalyzer analyzer,
            IEvidenceCoverageAnalyzer coverageAnalyzer,
            IOccupationBenchmarkService benchmarkService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.JobDescription))
            {
                return Results.BadRequest(new { error = "jobDescription is required." });
            }

            var job = await analyzer.AnalyzeAsync(request.JobDescription, cancellationToken);
            var coverage = await coverageAnalyzer.AnalyzeLibraryAsync(request.JobDescription, job, cancellationToken);
            var benchmark = await benchmarkService.BuildAsync(request.JobTitle, job, cancellationToken);
            return Results.Ok(new JobEvidenceAnalysisDto(job, coverage, benchmark));
        });

        return apiGroup;
    }
}
