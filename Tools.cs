namespace NH5Toolkit
{
    /// <summary>
    /// Describes one of the tools hosted by the hub. Each tool is started as its
    /// own standalone executable; the hub stores the path to that executable.
    /// </summary>
    public sealed record ToolInfo(string Key, string Name, string Description)
    {
        /// <summary>
        /// Direct download link for a ready-to-use package of the tool,
        /// either a .zip or a raw .exe. Null means its author hasn't published
        /// a release yet, so the hub only shows the manual "Locate .exe" flow.
        /// GitHub Releases asset links work great here.
        /// </summary>
        public string? DownloadUrl { get; init; }

        /// <summary>
        /// Only needed when a tool lives on GitHub but its download link doesn't
        /// already point at that repo. Usually the hub can read the repo straight
        /// out of a GitHub Releases download URL, in which case leave this null
        /// and release notes are fetched automatically.
        /// </summary>
        public string? GitHubRepo { get; init; }

        /// <summary>
        /// True for sections that live inside the toolkit itself (no separate .exe to
        /// download or launch). Their sidebar entry and home card open a built-in page.
        /// </summary>
        public bool BuiltIn { get; init; }
    }

    public static class Tools
    {
        public static readonly ToolInfo TrackLoader = new(
            Key: "trackLoader",
            Name: "Track Loader",
            Description: "Browse, preview and load custom tracks into NASCAR Heat 5.")
        {
            // TODO: Paste the real download link when the tool is released.
            // GitHub Releases asset URLs work great, e.g.:
            // https://github.com/<user>/<repo>/releases/download/v1.0/NH5TrackLoader.zip
            DownloadUrl = "https://REPLACE-ME.example.com/NH5TrackLoader.zip"
        };

        public static readonly ToolInfo ModManager = new(
            Key: "modManager",
            Name: "Mod Manager",
            Description: "Install, remove and manage mods: file replacements and add-ons, side by side.")
        {
            // Built into the toolkit (ModManagerPage), not a separate download.
            BuiltIn = true
        };

        public static readonly ToolInfo ModStudio = new(
            Key: "modStudio",
            Name: "Mod Studio",
            Description: "Create and edit mods for NASCAR Heat 5.")
        {
            // TODO: Paste the real download link when a build is ready to share.
            DownloadUrl = "https://REPLACE-ME.example.com/NH5ModStudio.zip"
        };

        public static ToolInfo[] All => [TrackLoader, ModManager, ModStudio];

        public static ToolInfo ByKey(string key) => All.First(t => t.Key == key);
    }
}
