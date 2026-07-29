namespace AngryFoot.ApiService.Domain;

public sealed class Profile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string LinkedIn { get; set; } = string.Empty;
    public string GitHub { get; set; } = string.Empty;
    public string ProfessionalSummary { get; set; } = string.Empty;
    public List<WorkHistory> WorkHistory { get; set; } = [];
    public List<Education> Education { get; set; } = [];
    public List<Certification> Certifications { get; set; } = [];
    public DateTime ModifiedDate { get; set; }

    public static Profile CreateEmpty() => new()
    {
        Id = Guid.NewGuid(),
        ModifiedDate = DateTime.UtcNow
    };
}
