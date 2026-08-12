using AngryFoot.ApiService.Application.Benchmarks;
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

        generations.MapPost("/analyze", async (
            AnalyzeRequest request,
            IJobAnalyzer analyzer,
            IFitAssessor fitAssessor,
            IOccupationBenchmarkService benchmarkService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.JobDescription))
            {
                return Results.BadRequest(new { error = "jobDescription is required." });
            }

            var job = await analyzer.AnalyzeAsync(request.JobDescription, cancellationToken);
            var fit = await fitAssessor.AssessAsync(request.JobDescription, job, cancellationToken);
            var benchmark = await benchmarkService.BuildAsync(request.JobTitle, job, cancellationToken);
            return Results.Ok(new JobFitAnalysisDto(job, fit, benchmark));
        });

        return apiGroup;
    }
}
