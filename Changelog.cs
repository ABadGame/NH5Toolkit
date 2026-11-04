namespace NH5Toolkit
{
    /// <summary>One dated line of news: which tool changed and what happened.</summary>
    public sealed record ChangelogEntry(string Date, string Tool, string Version, string[] Notes)
    {
        /// <summary>Link to the release page on GitHub, set when the entry came from there.</summary>
        public string? ReleaseUrl { get; init; }
    }

    /// <summary>
    /// Hand-authored hub news. Per-tool release notes are fetched automatically
    /// from each tool's GitHub releases (see GitHubReleases) and merged with
    /// these on the home page, newest first.
    /// </summary>
    public static class Changelog
    {
        public static readonly ChangelogEntry[] Entries =
        [
            new(
                Date: "2026-11-04",
                Tool: "Hub",
                Version: "1.0.0",
                Notes:
                [
                    "NASCAR Heat 5 Toolkit launched: launch, download and update all of your modding tools from one place.",
                    "Added tool downloads, update checking, a changelog and dark mode."
                ]),
        ];
    }
}
