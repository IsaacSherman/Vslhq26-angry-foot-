using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class ResumeMarkdownServiceTests
{
    private static readonly JobAnalysisDto EmptyAnalysis = new([], [], [], [], [], null, null);

    private static RewrittenBullet Bullet(string text, string? employer = null)
        => new(new Bullet { Id = Guid.NewGuid(), BulletText = text, SourceEmployer = employer }, text);

    [Fact]
    public void BuildResume_GroupsBulletsUnderMatchingEmployer_CaseInsensitively()
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            Name = "Ada Lovelace",
            WorkHistory = [new WorkHistory { Id = Guid.NewGuid(), Employer = "Acme Corp", SortOrder = 0 }]
        };
        var sut = new ResumeMarkdownService();

        var result = sut.BuildResume(profile, EmptyAnalysis, [Bullet("Shipped the widget line.", "acme corp")]);

        result.Should().Contain("### Acme Corp");
        result.Should().Contain("- Shipped the widget line.");
        result.Should().NotContain("Experience details pending", "the employer has a mapped bullet");
        result.Should().NotContain("### Selected Experience", "every bullet was mapped to an employer");
    }

    [Fact]
    public void BuildResume_PutsUnmappedBulletsUnderSelectedExperience()
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            Name = "Ada Lovelace",
            WorkHistory = [new WorkHistory { Id = Guid.NewGuid(), Employer = "Acme Corp", SortOrder = 0 }]
        };
        var sut = new ResumeMarkdownService();

        var result = sut.BuildResume(profile, EmptyAnalysis, [Bullet("Freelance win.", employer: null)]);

        result.Should().Contain("### Acme Corp", "the role still belongs on the timeline");
        result.Should().NotContain("Experience details pending",
            "selection weights recent work, so older roles routinely have no bullets and a placeholder "
            + "under each of them reads as an unfinished document");
        result.Should().Contain("### Selected Experience");
        result.Should().Contain("- Freelance win.");
    }

    [Fact]
    public void BuildResume_WithEmptyProfile_UsesPlaceholders()
    {
        var sut = new ResumeMarkdownService();

        var result = sut.BuildResume(new Profile { Id = Guid.NewGuid() }, EmptyAnalysis, []);

        result.Should().Contain("# Candidate Name");
        result.Should().Contain("Contact details pending.");
        result.Should().Contain("Professional summary to be provided.");
        result.Should().Contain("Skills pending.");
        result.Should().Contain("- Education details pending.");
        result.Should().Contain("- Certifications pending.");
    }

    [Fact]
    public void BuildResume_JoinsContactDetails_SkippingBlanks()
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            Name = "Ada",
            Email = "ada@example.com",
            Phone = "  ",
            GitHub = "github.com/ada"
        };
        var sut = new ResumeMarkdownService();

        var result = sut.BuildResume(profile, EmptyAnalysis, []);

        result.Should().Contain("ada@example.com | github.com/ada");
    }

    [Fact]
    public void BuildResume_SkillsLine_DeduplicatesAndCapsAtSixteen()
    {
        var manySkills = Enumerable.Range(1, 30).Select(i => $"Skill{i}").ToArray();
        var analysis = new JobAnalysisDto(manySkills, [], ["skill1"], [], [], null, null);
        var bullet = new RewrittenBullet(
            new Bullet { Id = Guid.NewGuid(), BulletText = "x", Skills = ["Skill1"] },
            "x");
        var sut = new ResumeMarkdownService();

        var result = sut.BuildResume(new Profile { Id = Guid.NewGuid(), Name = "A" }, analysis, [bullet]);

        var skillsLine = result.Split(Environment.NewLine).First(l => l.StartsWith("Skill1,"));
        skillsLine.Split(',').Should().HaveCount(16, "the skills line caps at 16 case-insensitively deduplicated entries");
    }

    [Fact]
    public void BuildResume_RendersEducationAndCertifications_WithOptionalParts()
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            Name = "Ada",
            Education =
            [
                new Education { Id = Guid.NewGuid(), Institution = "State U", Credential = "BS", Field = "CS", GraduationDate = "2016", SortOrder = 0 },
                new Education { Id = Guid.NewGuid(), Institution = "Night School", SortOrder = 1 }
            ],
            Certifications =
            [
                new Certification { Id = Guid.NewGuid(), Name = "AZ-204", Issuer = "Microsoft", IssueDate = "2025", SortOrder = 0 }
            ]
        };
        var sut = new ResumeMarkdownService();

        var result = sut.BuildResume(profile, EmptyAnalysis, []);

        result.Should().Contain("- State U, BS (CS) - 2016");
        result.Should().Contain("- Night School");
        result.Should().Contain("- AZ-204 (Microsoft) - 2025");
    }

    private static Profile ProfileWith(WorkHistory work)
        => new() { Id = Guid.NewGuid(), Name = "Ada Lovelace", WorkHistory = [work] };

    [Fact]
    public void BuildResume_PrintsTheRoleAndItsDatesUnderTheEmployer()
    {
        var profile = ProfileWith(new WorkHistory
        {
            Id = Guid.NewGuid(),
            Employer = "Acme Corp",
            Title = "Senior Engineer",
            StartDate = "Jan 2020",
            EndDate = "Mar 2024",
            SortOrder = 0
        });
        var sut = new ResumeMarkdownService();

        var result = sut.BuildResume(profile, EmptyAnalysis, [Bullet("Shipped the widget line.", "Acme Corp")]);

        result.Should().Contain("### Acme Corp");
        result.Should().Contain("*Senior Engineer | Jan 2020 - Mar 2024*");
    }

    [Fact]
    public void BuildResume_ReadsAMissingEndDateAsTheCurrentRole()
    {
        var profile = ProfileWith(new WorkHistory
        {
            Id = Guid.NewGuid(),
            Employer = "Acme Corp",
            StartDate = "Jan 2020",
            SortOrder = 0
        });
        var sut = new ResumeMarkdownService();

        var result = sut.BuildResume(profile, EmptyAnalysis, []);

        result.Should().Contain("*Jan 2020 - Present*");
    }

    [Fact]
    public void BuildResume_WithNoTitleOrDates_PrintsNoRoleLine()
    {
        var profile = ProfileWith(new WorkHistory { Id = Guid.NewGuid(), Employer = "Acme Corp", SortOrder = 0 });
        var sut = new ResumeMarkdownService();

        var result = sut.BuildResume(profile, EmptyAnalysis, [Bullet("Shipped the widget line.", "Acme Corp")]);

        result.Should().Contain("### Acme Corp");
        result.Should().NotContain("*", "an employer with neither a title nor dates gets no subtitle");
    }
}
