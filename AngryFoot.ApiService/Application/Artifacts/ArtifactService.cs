using AngryFoot.ApiService.Data;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Artifacts;

public interface IArtifactService
{
    Task<IReadOnlyList<ArtifactSummaryDto>> GetSummariesAsync(CancellationToken cancellationToken);
    Task<GenerationArtifactDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class ArtifactService(AngryFootDbContext dbContext) : IArtifactService
{
    public async Task<IReadOnlyList<ArtifactSummaryDto>> GetSummariesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.GenerationArtifacts
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => x.ToSummaryDto())
            .ToListAsync(cancellationToken);
    }

    public async Task<GenerationArtifactDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var artifact = await dbContext.GenerationArtifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return artifact?.ToDto();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await dbContext.GenerationArtifacts
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }
}
