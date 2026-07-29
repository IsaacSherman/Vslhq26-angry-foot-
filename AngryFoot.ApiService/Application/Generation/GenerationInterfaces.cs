using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Generation;

public interface IJobAnalyzer
{
    Task<JobAnalysisDto> AnalyzeAsync(string jobDescription, CancellationToken cancellationToken);
}

public interface IGenerationOrchestrator
{
    Task<GenerationResultDto> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken);
}
