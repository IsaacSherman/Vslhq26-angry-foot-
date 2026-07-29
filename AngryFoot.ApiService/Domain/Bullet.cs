namespace AngryFoot.ApiService.Domain;

public sealed class Bullet
{
    public Guid Id { get; set; }
    public string BulletText { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> Skills { get; set; } = [];
    public List<string> Technologies { get; set; } = [];
    public List<string> JobCategories { get; set; } = [];
    public List<string> Impact { get; set; } = [];
    public EnrichmentState EnrichmentState { get; set; } = EnrichmentState.Pending;
    public string? SourceEmployer { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
}
