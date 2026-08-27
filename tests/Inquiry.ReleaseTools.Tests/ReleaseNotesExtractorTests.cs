namespace Inquiry.ReleaseTools.Tests;

public sealed class ReleaseNotesExtractorTests
{
    private const string Changelog = """
        # Changelog

        ## [Unreleased]

        - pending work

        ## [1.0.0-preview.8-hotfix] - 2026-08-20

        - hotfix notes that must never publish as preview.8

        ## [1.0.0-preview.8] - 2026-08-17

        - real notes
        - more real notes

        ## [1.0.0-preview.7] - 2026-08-03

        - old notes

        [unreleased]: https://example.invalid/compare/main
        """;

    [Fact]
    public void Extracts_exactly_the_requested_versions_section()
    {
        var notes = ReleaseNotesExtractor.Extract(Changelog, "1.0.0-preview.8");

        Assert.Equal("- real notes\n- more real notes\n", notes);
    }

    [Fact]
    public void Stops_at_the_link_reference_block()
    {
        var notes = ReleaseNotesExtractor.Extract(Changelog, "1.0.0-preview.7");

        Assert.Equal("- old notes\n", notes);
    }

    [Fact]
    public void Accepts_a_heading_without_a_date_suffix()
    {
        var notes = ReleaseNotesExtractor.Extract("## [1.0.0]\n\n- stable notes\n", "1.0.0");

        Assert.Equal("- stable notes\n", notes);
    }

    [Theory]
    [InlineData("## [1.0.0-preview.8-hotfix] - 2026-08-20")]
    [InlineData("## [1.0.0-preview.8]-hotfix - 2026-08-20")]
    [InlineData("## [1.0.0-preview.8].1 - 2026-08-20")]
    [InlineData("## [1.0.0-preview.80] - 2026-08-20")]
    public void Prefix_collisions_are_rejected_instead_of_publishing_the_wrong_section(string collidingHeading)
    {
        var changelog = $"# Changelog\n\n{collidingHeading}\n\n- wrong-version notes\n";

        var exception = Assert.Throws<ReleaseVerificationException>(
            () => ReleaseNotesExtractor.Extract(changelog, "1.0.0-preview.8"));

        Assert.Contains("has no '## [1.0.0-preview.8]' section", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_section_fails_closed()
    {
        var exception = Assert.Throws<ReleaseVerificationException>(
            () => ReleaseNotesExtractor.Extract(Changelog, "1.0.0-preview.9"));

        Assert.Contains("has no '## [1.0.0-preview.9]' section", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_section_fails_closed()
    {
        const string changelog = "## [1.0.0] - 2026-08-20\n\n## [0.9.0] - 2026-08-01\n\n- old\n";

        var exception = Assert.Throws<ReleaseVerificationException>(
            () => ReleaseNotesExtractor.Extract(changelog, "1.0.0"));

        Assert.Contains("section is empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extract_notes_command_writes_the_notes_file()
    {
        var changelogPath = Path.Combine(Path.GetTempPath(), $"inquiry-changelog-{Guid.NewGuid():N}.md");
        var notesPath = Path.Combine(Path.GetTempPath(), $"inquiry-notes-{Guid.NewGuid():N}.md");
        File.WriteAllText(changelogPath, Changelog);
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            var exitCode = await ReleaseTool.RunAsync(
                ["extract-notes", changelogPath, "1.0.0-preview.8", notesPath], output, error);

            Assert.Equal(0, exitCode);
            Assert.Equal("- real notes\n- more real notes\n", File.ReadAllText(notesPath));
        }
        finally
        {
            File.Delete(changelogPath);
            File.Delete(notesPath);
        }
    }

    [Fact]
    public async Task Extract_notes_command_fails_closed_when_the_section_is_missing()
    {
        var changelogPath = Path.Combine(Path.GetTempPath(), $"inquiry-changelog-{Guid.NewGuid():N}.md");
        var notesPath = Path.Combine(Path.GetTempPath(), $"inquiry-notes-{Guid.NewGuid():N}.md");
        File.WriteAllText(changelogPath, Changelog);
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            var exitCode = await ReleaseTool.RunAsync(
                ["extract-notes", changelogPath, "1.0.0-preview.9", notesPath], output, error);

            Assert.Equal(1, exitCode);
            Assert.StartsWith("release-verification-error: ", error.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(notesPath));
        }
        finally
        {
            File.Delete(changelogPath);
        }
    }
}
