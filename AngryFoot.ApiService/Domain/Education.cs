namespace AngryFoot.ApiService.Domain;

public sealed class Education
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public Profile Profile { get; set; } = default!;
    public string Institution { get; set; } = string.Empty;
    public string? Credential { get; set; }
    public string? Field { get; set; }
    public string? GraduationDate { get; set; }
    public int SortOrder { get; set; }
}
