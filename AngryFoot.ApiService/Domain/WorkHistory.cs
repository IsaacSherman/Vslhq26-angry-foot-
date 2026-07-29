namespace AngryFoot.ApiService.Domain;

public sealed class WorkHistory
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public Profile Profile { get; set; } = default!;
    public string Employer { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Location { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public int SortOrder { get; set; }
}
