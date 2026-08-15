using System.Text.Json;
using AngryFoot.ApiService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AngryFoot.ApiService.Data;

public sealed class AngryFootDbContext(DbContextOptions<AngryFootDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DbSet<Bullet> Bullets => Set<Bullet>();
    public DbSet<BulletRevision> BulletRevisions => Set<BulletRevision>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<WorkHistory> WorkHistory => Set<WorkHistory>();
    public DbSet<Education> Education => Set<Education>();
    public DbSet<Certification> Certifications => Set<Certification>();
    public DbSet<GenerationArtifact> GenerationArtifacts => Set<GenerationArtifact>();
    public DbSet<IgnoredBulletDuplicatePair> IgnoredBulletDuplicatePairs => Set<IgnoredBulletDuplicatePair>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var stringListConverter = new ValueConverter<List<string>, string>(
            value => JsonSerializer.Serialize(value ?? new List<string>(), JsonOptions),
            value => JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? new List<string>());

        var guidListConverter = new ValueConverter<List<Guid>, string>(
            value => JsonSerializer.Serialize(value ?? new List<Guid>(), JsonOptions),
            value => JsonSerializer.Deserialize<List<Guid>>(value, JsonOptions) ?? new List<Guid>());

        var stringListComparer = new ValueComparer<List<string>>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (current, item) => HashCode.Combine(current, item.GetHashCode(StringComparison.Ordinal))),
            value => value.ToList());

        var guidListComparer = new ValueComparer<List<Guid>>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (current, item) => HashCode.Combine(current, item.GetHashCode())),
            value => value.ToList());

        modelBuilder.Entity<Bullet>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BulletText).IsRequired();

            var tags = entity.Property(x => x.Tags).HasConversion(stringListConverter);
            tags.Metadata.SetValueComparer(stringListComparer);

            var skills = entity.Property(x => x.Skills).HasConversion(stringListConverter);
            skills.Metadata.SetValueComparer(stringListComparer);

            var technologies = entity.Property(x => x.Technologies).HasConversion(stringListConverter);
            technologies.Metadata.SetValueComparer(stringListComparer);

            var categories = entity.Property(x => x.JobCategories).HasConversion(stringListConverter);
            categories.Metadata.SetValueComparer(stringListComparer);

            var impact = entity.Property(x => x.Impact).HasConversion(stringListConverter);
            impact.Metadata.SetValueComparer(stringListComparer);

            var acknowledged = entity.Property(x => x.AcknowledgedQualitySignals).HasConversion(stringListConverter);
            acknowledged.Metadata.SetValueComparer(stringListComparer);
        });

        modelBuilder.Entity<BulletRevision>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RevisedText).IsRequired();
            entity.Property(x => x.SourceText).IsRequired();

            // Stored as text, unlike EnrichmentState's int: the modes are a user-facing list that is
            // expected to grow, and inserting one in the middle would silently relabel every
            // existing row if the ordinal were the stored value.
            entity.Property(x => x.Mode).HasConversion<string>();

            entity.HasOne(x => x.Bullet)
                .WithMany(x => x.Revisions)
                .HasForeignKey(x => x.BulletId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.BulletId, x.Mode, x.Version }).IsUnique();
        });

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.Email).IsRequired();
            entity.Property(x => x.Phone).IsRequired();
            entity.Property(x => x.LinkedIn).IsRequired();
            entity.Property(x => x.GitHub).IsRequired();
            entity.Property(x => x.ProfessionalSummary).IsRequired();
        });

        modelBuilder.Entity<WorkHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Employer).IsRequired();
            entity.HasOne(x => x.Profile)
                .WithMany(x => x.WorkHistory)
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Education>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Institution).IsRequired();
            entity.HasOne(x => x.Profile)
                .WithMany(x => x.Education)
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Certification>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired();
            entity.HasOne(x => x.Profile)
                .WithMany(x => x.Certifications)
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GenerationArtifact>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.JobDescription).IsRequired();
            entity.Property(x => x.ResumeMarkdown).IsRequired();
            entity.Property(x => x.CoverLetterMarkdown).IsRequired();

            var selectedBulletIds = entity.Property(x => x.SelectedBulletIds).HasConversion(guidListConverter);
            selectedBulletIds.Metadata.SetValueComparer(guidListComparer);

            entity.Property(x => x.JobAnalysisJson).IsRequired();
        });

        modelBuilder.Entity<IgnoredBulletDuplicatePair>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.BulletIdA, x.BulletIdB }).IsUnique();
        });
    }
}
