namespace Inquiry.ReleaseTools;

public static class ReleaseNotesExtractor
{
    /// <summary>
    /// Extracts the CHANGELOG section for exactly <paramref name="version"/>. The heading must be
    /// the whole line (<c>## [version]</c>) or continue with a space (<c>## [version] - date</c>);
    /// a longer version that merely starts with the requested one is rejected so a missing section
    /// fails closed instead of publishing another version's notes.
    /// </summary>
    public static string Extract(string changelog, string version)
    {
        var heading = $"## [{version}]";
        var found = false;
        var notes = new List<string>();
        foreach (var line in changelog.ReplaceLineEndings("\n").Split('\n'))
        {
            if (!found)
            {
                found = line == heading || line.StartsWith(heading + " ", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith("## [", StringComparison.Ordinal) || IsLinkReference(line))
            {
                break;
            }

            notes.Add(line);
        }

        if (!found)
        {
            throw new ReleaseVerificationException(
                $"CHANGELOG.md has no '## [{version}]' section. Cut the changelog before tagging.");
        }

        // Trim only surrounding blank lines: content lines keep their exact whitespace so
        // indented Markdown (code blocks, nested lists) survives into the release notes.
        while (notes.Count > 0 && string.IsNullOrWhiteSpace(notes[0]))
        {
            notes.RemoveAt(0);
        }

        while (notes.Count > 0 && string.IsNullOrWhiteSpace(notes[^1]))
        {
            notes.RemoveAt(notes.Count - 1);
        }

        if (notes.Count == 0)
        {
            throw new ReleaseVerificationException(
                $"The '## [{version}]' CHANGELOG.md section is empty. Cut the changelog before tagging.");
        }

        return string.Join('\n', notes) + "\n";
    }

    // Matches the awk exit condition this replaced: a link-reference line such as '[unreleased]: https://...'.
    private static bool IsLinkReference(string line)
    {
        if (!line.StartsWith('['))
        {
            return false;
        }

        var end = line.IndexOf(']', 1);
        return end > 1 && end + 2 < line.Length && line[end + 1] == ':' && line[end + 2] == ' ';
    }
}
