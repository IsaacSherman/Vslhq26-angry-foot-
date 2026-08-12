using System.Text.Json;

namespace AngryFoot.ApiService.Application.Benchmarks;

/// <summary>
/// One requirement an occupational dataset associates with an occupation.
/// <paramref name="EvidenceTerms"/> holds the bullet-text keywords that count as supporting
/// evidence for it - see the dataset's own notes for why those are ours and not O*NET's.
/// </summary>
internal sealed record BenchmarkItem(
    string Name,
    string Kind,
    int Importance,
    IReadOnlyList<string> EvidenceTerms);

internal sealed record BenchmarkOccupation(
    string SocCode,
    string Title,
    IReadOnlyList<string> AlternateTitles,
    IReadOnlyList<BenchmarkItem> Items);

internal sealed record OccupationBenchmarkData(
    string SourceVersion,
    string Attribution,
    IReadOnlyList<BenchmarkOccupation> Occupations)
{
    public static OccupationBenchmarkData Empty { get; } =
        new(string.Empty, string.Empty, []);

    public bool IsAvailable => Occupations.Count > 0;
}

internal interface IOccupationBenchmarkDataset
{
    OccupationBenchmarkData Data { get; }
}

/// <summary>
/// Loads the bundled aggregate occupational dataset once and holds it for the process
/// lifetime. A missing or unreadable file yields <see cref="OccupationBenchmarkData.Empty"/>
/// rather than throwing: the benchmark is supplementary and must never stop the app - or a
/// job analysis - from working.
/// </summary>
internal sealed class OccupationBenchmarkDataset : IOccupationBenchmarkDataset
{
    internal const string DataFileName = "onet-occupations.json";

    private readonly Lazy<OccupationBenchmarkData> data;

    public OccupationBenchmarkDataset(IHostEnvironment environment, ILogger<OccupationBenchmarkDataset> logger)
    {
        var path = Path.Combine(environment.ContentRootPath, "Application", "Benchmarks", "Data", DataFileName);
        data = new Lazy<OccupationBenchmarkData>(() => Load(path, logger), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public OccupationBenchmarkData Data => data.Value;

    internal static OccupationBenchmarkData Load(string path, ILogger logger)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var payload = JsonSerializer.Deserialize<DatasetPayload>(
                stream, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            if (payload?.Occupations is null || payload.Occupations.Count == 0)
            {
                logger.LogWarning("Occupation benchmark dataset at {Path} contained no occupations.", path);
                return OccupationBenchmarkData.Empty;
            }

            var evidence = payload.EvidenceTerms ?? new Dictionary<string, List<string>>();
            var occupations = payload.Occupations
                .Where(o => !string.IsNullOrWhiteSpace(o.SocCode) && o.Items is { Count: > 0 })
                .Select(o => new BenchmarkOccupation(
                    o.SocCode!,
                    o.Title ?? o.SocCode!,
                    o.AlternateTitles ?? [],
                    o.Items!
                        .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                        .Select(i => new BenchmarkItem(
                            i.Name!,
                            i.Kind ?? "Skill",
                            i.Importance,
                            // Falling back to the descriptor name keeps an item usable even if
                            // the shared term map has no entry for it.
                            evidence.TryGetValue(i.Name!, out var terms) && terms.Count > 0 ? terms : [i.Name!]))
                        .ToArray()))
                .ToArray();

            logger.LogInformation(
                "Loaded occupation benchmark dataset {Version} with {Count} occupations.",
                payload.SourceVersion, occupations.Length);

            return new OccupationBenchmarkData(
                payload.SourceVersion ?? string.Empty,
                payload.Attribution ?? string.Empty,
                occupations);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not load occupation benchmark dataset from {Path}. Benchmarking is disabled.", path);
            return OccupationBenchmarkData.Empty;
        }
    }

    private sealed class DatasetPayload
    {
        public string? SourceVersion { get; set; }
        public string? Attribution { get; set; }
        public Dictionary<string, List<string>>? EvidenceTerms { get; set; }
        public List<OccupationPayload>? Occupations { get; set; }
    }

    private sealed class OccupationPayload
    {
        public string? SocCode { get; set; }
        public string? Title { get; set; }
        public List<string>? AlternateTitles { get; set; }
        public List<ItemPayload>? Items { get; set; }
    }

    private sealed class ItemPayload
    {
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public int Importance { get; set; }
    }
}
