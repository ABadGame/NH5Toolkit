using System.Diagnostics;
using System.IO.Compression;

namespace NH5Toolkit
{
    /// <summary>
    /// Shared packaging logic used by both first-time installs (ToolDownloader)
    /// and updates (ToolUpdater): handles zip packages and bare executables,
    /// detects bundled setup programs, and replaces portable files.
    /// </summary>
    internal static class PackageInstaller
    {
        /// <summary>
        /// Installs a downloaded package for the tool. Returns a message the caller
        /// can show the user. When the package contains a setup program it is only
        /// launched (install location becomes the setup's decision); otherwise the
        /// hub's tools folder is replaced and the config re-pointed automatically.
        /// </summary>
        public static string Install(ToolInfo tool, string packagePath)
        {
            string folder = ToolDownloader.InstallFolder(tool);

            if (Path.GetExtension(packagePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(packagePath);

                if (LooksLikeInstaller(fileName))
                {
                    Process.Start(new ProcessStartInfo { FileName = packagePath, UseShellExecute = true });
                    return $"{fileName} is an installer and is running now. Finish its steps - then use \"Locate .exe\" on the {tool.Name} page if the app's location changed.";
                }

                // A portable single-file app: drop it in and point the hub at it.
                ReplaceFolder(folder, tool);
                string target = Path.Combine(folder, fileName);
                File.Copy(packagePath, target, overwrite: true);
                ToolkitConfig.SetPath(tool.Key, target);
                return "Installed! Ready to launch.";
            }

            // Zip package: extract to a temp dir first so a bundled installer can be
            // detected before any installed files are touched.
            string extractDir = Path.Combine(Path.GetTempPath(), $"nh5_pkg_{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(packagePath, extractDir);

                string? installer = FindInstaller(extractDir);
                if (installer is not null)
                {
                    Process.Start(new ProcessStartInfo { FileName = installer, UseShellExecute = true });
                    return "The package contained a setup program - it's running now. When it finishes, use \"Locate .exe\" on the tool's hub page if the app's location changed.";
                }

                ReplaceFolder(folder, tool);

                int copied = 0;
                foreach (string file in Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories))
                {
                    string destination = Path.Combine(folder, Path.GetRelativePath(extractDir, file));
                    string? destinationDir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destinationDir))
                        Directory.CreateDirectory(destinationDir);

                    File.Copy(file, destination, overwrite: true);
                    copied++;
                }

                string executable = PickExecutable(tool, folder)
                    ?? throw new IOException("The package was unpacked, but no .exe was found inside it.");

                ToolkitConfig.SetPath(tool.Key, executable);
                return copied == 0
                    ? "Nothing to update - the package was empty."
                    : "Files replaced. Ready to launch.";
            }
            finally
            {
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
            }
        }

        private static bool LooksLikeInstaller(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            return stem.Contains("setup", StringComparison.OrdinalIgnoreCase)
                || stem.Contains("install", StringComparison.OrdinalIgnoreCase)
                || stem.Contains("update", StringComparison.OrdinalIgnoreCase);
        }

        private static string? FindInstaller(string directory)
        {
            return Directory
                .EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault(file => LooksLikeInstaller(Path.GetFileNameWithoutExtension(file)));
        }

        /// <summary>Prefers an exe matching the tool's name, then top-level exes.</summary>
        private static string? PickExecutable(ToolInfo tool, string folder)
        {
            var candidates = Directory
                .EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories)
                .ToList();
            if (candidates.Count == 0)
                return null;

            string preferred = tool.Name.Replace(" ", "", StringComparison.Ordinal);
            return candidates.FirstOrDefault(exe =>
                       Path.GetFileNameWithoutExtension(exe)
                           .Replace(" ", "", StringComparison.Ordinal)
                           .Equals(preferred, StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault(exe =>
                       Path.GetDirectoryName(exe)!.Equals(folder, StringComparison.OrdinalIgnoreCase))
                ?? candidates.First();
        }

        /// <summary>
        /// Clears the install folder so old files don't linger, with a friendly
        /// error when the running tool keeps the folder locked.
        /// </summary>
        private static void ReplaceFolder(string folder, ToolInfo tool)
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                return;
            }

            try
            {
                Directory.Delete(folder, recursive: true);
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"{tool.Name} is still using its folder. Close the tool and try again.\n\n({ex.Message})");
            }
        }
    }
}
