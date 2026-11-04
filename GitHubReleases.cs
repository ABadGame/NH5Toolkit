using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NH5Toolkit
{
    /// <summary>
    /// Reads each tool's release notes from its GitHub repository so the hub's
    /// changelog updates itself. The repo is normally worked out from the
    /// tool's download URL (any GitHub Releases asset link reveals it);
    /// ToolInfo.GitHubRepo can override when the link lives elsewhere.
    /// </summary>
    internal static class GitHubReleases
    {
        private static readonly HttpClient Http = CreateClient();

        private static readonly Regex RepoFromUrl = new(
            @"^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/releases/download/",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LinkRegex = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex NumberedRegex = new(@"^\d+\.\s*", RegexOptions.Compiled);

        private static readonly Dictionary<string, (DateTimeOffset Fetched, List<ChangelogEntry> Entries)> Cache = new();
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NH5Toolkit");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        /// <summary>The tool's GitHub home as "owner/repo", or null when unknown.</summary>
        public static string? RepoFor(ToolInfo tool)
        {
            if (!string.IsNullOrWhiteSpace(tool.GitHubRepo))
                return tool.GitHubRepo;

            if (!string.IsNullOrWhiteSpace(tool.DownloadUrl))
            {
                var match = RepoFromUrl.Match(tool.DownloadUrl);
                if (match.Success)
                    return $"{match.Groups["owner"].Value}/{match.Groups["repo"].Value}";
            }

            return null;
        }

        /// <summary>The tool's five most recent GitHub releases as changelog entries.</summary>
        public static async Task<List<ChangelogEntry>> FetchReleasesAsync(ToolInfo tool, CancellationToken ct)
        {
            string? repo = RepoFor(tool);
            if (repo is null)
                return [];

            if (Cache.TryGetValue(repo, out var cached) && DateTimeOffset.UtcNow - cached.Fetched < CacheLifetime)
                return cached.Entries;

            using var response = await Http.GetAsync(
                $"https://api.github.com/repos/{repo}/releases?per_page=5",
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if ((int)response.StatusCode == 403)
                throw new IOException("GitHub's API rate limit was reached - try again later.");
            if ((int)response.StatusCode == 404)
                throw new IOException($"The GitHub repo '{repo}' doesn't exist or is private.");

            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var entries = new List<ChangelogEntry>();

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                    continue;

                string tag = release.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() ?? "" : "";
                string title = release.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    ? name.GetString() ?? ""
                    : "";
                string body = release.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind == JsonValueKind.String
                    ? bodyEl.GetString() ?? ""
                    : "";

                DateTimeOffset? published = null;
                if (release.TryGetProperty("published_at", out var publishedEl) && publishedEl.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(publishedEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
                {
                    published = when;
                }

                string[] notes = ParseNotes(body);
                if (notes.Length == 0 && title.Length > 0)
                    notes = [title];

                if (tag.Length == 0 || notes.Length == 0)
                    continue;

                entries.Add(new ChangelogEntry(
                    Date: published?.ToString("yyyy-MM-dd") ?? "undated",
                    Tool: tool.Name,
                    Version: tag,
                    Notes: notes)
                {
                    ReleaseUrl = $"https://github.com/{repo}/releases/tag/{Uri.EscapeDataString(tag)}"
                });
            }

            Cache[repo] = (DateTimeOffset.UtcNow, entries);
            return entries;
        }

        /// <summary>Reduces a release body (markdown) to plain bullet lines.</summary>
        private static string[] ParseNotes(string body)
        {
            var notes = new List<string>();
            foreach (string raw in body.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("!["))   // skip images
                    continue;

                line = line.TrimStart('#').Trim();               // headings
                if (line.StartsWith("- ") || line.StartsWith("* "))
                    line = line[2..].Trim();                     // bullets
                else
                    line = NumberedRegex.Replace(line, "");      // numbered lists

                line = LinkRegex.Replace(line, "$1");            // [text](url) -> text
                line = BoldRegex.Replace(line, "$1");            // **text** -> text
                line = line.Trim();

                if (line.Length == 0)
                    continue;

                notes.Add(line);
                if (notes.Count >= 8)
                    break;
            }

            return [.. notes];
        }
    }
}
