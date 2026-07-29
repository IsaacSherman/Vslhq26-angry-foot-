namespace AngryFoot.ApiService.Domain;

public sealed class Certification
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public Profile Profile { get; set; } = default!;
    public string Name { get; set; } = string.Empty;
    public string? Issuer { get; set; }
    public string? IssueDate { get; set; }
    public int SortOrder { get; set; }
}
