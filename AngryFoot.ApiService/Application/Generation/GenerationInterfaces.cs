using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Generation;

public interface IJobAnalyzer
{
    Task<JobAnalysisDto> AnalyzeAsync(string jobDescription, CancellationToken cancellationToken);
}

public interface IGenerationOrchestrator
{
    Task<GenerationResultDto> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// A resume built from the whole bullet library with no posting to aim at. Produces no cover
    /// letter and no coverage report: both are statements about a posting, and there is none.
    /// </summary>
    Task<GenerationResultDto> GenerateGenericAsync(GenericGenerationRequest request, CancellationToken cancellationToken);
}
