using System.Diagnostics;

namespace NH5Toolkit
{
    /// <summary>
    /// Starts a tool's standalone executable, with friendly errors for the
    /// "not configured yet" and "path moved/deleted" cases.
    /// </summary>
    public static class Launcher
    {
        public static void TryLaunch(ToolInfo tool)
        {
            var path = ToolkitConfig.GetPath(tool.Key);

            if (path is null)
            {
                MessageBox.Show(
                    $"{tool.Name} hasn't been set up yet.\n\nOpen its page in the hub and click \"Locate .exe\" to point the hub at the standalone app.",
                    tool.Name,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!File.Exists(path))
            {
                MessageBox.Show(
                    $"The configured path no longer exists:\n{path}\n\nUpdate it on the {tool.Name} page.",
                    tool.Name,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not start {tool.Name}:\n\n{ex.Message}",
                    "Launch failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        public static void ShowLocateDialog(ToolInfo tool, IWin32Window owner, Action saved)
        {
            using var dialog = new OpenFileDialog
            {
                Title = $"Locate {tool.Name}",
                Filter = "Executable (*.exe)|*.exe",
                CheckFileExists = true
            };

            var existing = ToolkitConfig.GetPath(tool.Key);
            if (existing is not null)
                dialog.InitialDirectory = Path.GetDirectoryName(existing);

            if (dialog.ShowDialog(owner) != DialogResult.OK)
                return;

            ToolkitConfig.SetPath(tool.Key, dialog.FileName);
            saved();
        }
    }
}
