using System.Diagnostics;
using System.Security.Cryptography;

namespace NH5Toolkit
{
    /// <summary>
    /// Checks a tool's published package for changes and installs the newer
    /// version: bundled setup programs are launched for the user, portable
    /// packages have their files replaced in place. Fingerprinting uses the
    /// server's ETag / Last-Modified / size when provided, falling back to a
    /// SHA-256 comparison of the downloaded package.
    /// </summary>
    internal static class ToolUpdater
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(15)
        };

        /// <summary>A tool can be update-checked only when installed and a package URL exists.</summary>
        public static bool CanCheck(ToolInfo tool) =>
            ToolDownloader.IsAvailable(tool) && ToolkitConfig.GetPath(tool.Key) is not null;

        public static void ShowUpdateDialog(ToolInfo tool, IWin32Window owner, Action completed)
        {
            using var dialog = new UpdateDialog(tool);
            dialog.StartPosition = FormStartPosition.CenterParent;
            if (dialog.ShowDialog(owner) == DialogResult.OK)
                completed();
        }

        /// <summary>Update fingerprints live in the same config, keyed apart from exe paths.</summary>
        private static string SignatureKey(ToolInfo tool) => $"update-signature:{tool.Key}";

        private static void SaveSignature(ToolInfo tool, string signature) =>
            ToolkitConfig.SetPath(SignatureKey(tool), signature);

        private static string? StoredSignature(ToolInfo tool) =>
            ToolkitConfig.GetPath(SignatureKey(tool));

        /// <summary>
        /// Asks the server for a cheap change fingerprint via a HEAD request.
        /// Returns null when the server provides nothing usable, in which case
        /// the caller must download the package and compare SHA-256 hashes.
        /// </summary>
        private static async Task<string?> GetRemoteSignatureAsync(string url, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new IOException($"The update link responded with {(int)response.StatusCode} {response.StatusCode}.");

            string etag = response.Headers.ETag?.Tag ?? "";
            string lastModified = response.Content.Headers.LastModified?.ToString("O") ?? "";
            long length = response.Content.Headers.ContentLength ?? -1;

            var parts = new List<string>();
            if (etag.Length > 0)
                parts.Add("etag:" + etag);
            if (lastModified.Length > 0)
                parts.Add("modified:" + lastModified);
            if (length >= 0)
                parts.Add("size:" + length);

            return parts.Count > 0 ? string.Join("|", parts) : null;
        }

        /// <summary>
        /// Blocks until the configured executable isn't running (updating the
        /// files while the app is open fails), giving the user chances to close
        /// it. Returns false when the user decides to skip the update.
        /// </summary>
        private static bool EnsureNotRunning(ToolInfo tool)
        {
            while (true)
            {
                string exePath = ToolkitConfig.GetPath(tool.Key) ?? "";
                string processName = string.IsNullOrEmpty(exePath)
                    ? ""
                    : Path.GetFileNameWithoutExtension(exePath);

                if (string.IsNullOrEmpty(processName) || Process.GetProcessesByName(processName).Length == 0)
                    return true;

                var choice = MessageBox.Show(
                    $"{tool.Name} is currently running and its files can't be replaced.\n\nClose it, then click Retry to continue the update.",
                    tool.Name,
                    MessageBoxButtons.RetryCancel,
                    MessageBoxIcon.Warning);

                if (choice != DialogResult.Retry)
                    return false;
            }
        }

        private static string ComputeSha256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private sealed class UpdateDialog : Form
        {
            private readonly ToolInfo _tool;
            private readonly Label _message = new()
            {
                AutoSize = false,
                Size = new Size(400, 24),
                Location = new Point(16, 16),
                TextAlign = ContentAlignment.MiddleLeft
            };

            private readonly ProgressBar _bar = new()
            {
                Size = new Size(400, 22),
                Location = new Point(16, 46)
            };

            private readonly Button _cancelButton = new()
            {
                Text = "Cancel",
                Size = new Size(80, 28),
                Location = new Point(336, 86)
            };

            private readonly CancellationTokenSource _cancellation = new();

            public UpdateDialog(ToolInfo tool)
            {
                _tool = tool;

                Text = $"Update {tool.Name}";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterParent;
                ClientSize = new Size(432, 126);
                Font = new Font("Segoe UI", 9.75f);

                _cancelButton.Click += (s, e) =>
                {
                    _cancelButton.Enabled = false;
                    _message.Text = "Cancelling...";
                    _cancellation.Cancel();
                };

                Controls.Add(_bar);
                Controls.Add(_message);
                Controls.Add(_cancelButton);

                _message.Text = "Checking for updates...";
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _ = RunAsync();
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                _cancellation.Cancel();
                base.OnFormClosing(e);
            }

            private void SetMessage(string text) => _message.Text = text;

            private async Task RunAsync()
            {
                try
                {
                    string url = _tool.DownloadUrl!;
                    string? remote = await GetRemoteSignatureAsync(url, _cancellation.Token);
                    string stored = StoredSignature(_tool) ?? "";

                    string packagePath;
                    string localSignature;

                    if (remote is null)
                    {
                        // Server gave no fingerprint: must download and hash-compare.
                        SetMessage($"Downloading {_tool.Name} to compare versions...");
                        packagePath = await DownloadAsync(_cancellation.Token);
                        localSignature = "sha256:" + ComputeSha256(packagePath);

                        if (stored == localSignature)
                        {
                            FinishUpToDate();
                            return;
                        }
                    }
                    else
                    {
                        if (stored == remote)
                        {
                            FinishUpToDate();
                            return;
                        }

                        SetMessage($"Downloading update for {_tool.Name}...");
                        packagePath = await DownloadAsync(_cancellation.Token);
                        localSignature = remote;
                    }

                    SetMessage("Installing...");
                    if (!EnsureNotRunning(_tool))
                    {
                        DialogResult = DialogResult.Cancel;
                        return;
                    }

                    PackageInstaller.Install(_tool, packagePath);
                    SaveSignature(_tool, localSignature);
                    DialogResult = DialogResult.OK;
                }
                catch (OperationCanceledException)
                {
                    DialogResult = DialogResult.Cancel;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Update failed:\n\n{ex.Message}", _tool.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    DialogResult = DialogResult.Cancel;
                }
            }

            private void FinishUpToDate()
            {
                MessageBox.Show(this,
                    $"{_tool.Name} is already up to date.",
                    _tool.Name,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                DialogResult = DialogResult.Cancel;
            }

            private async Task<string> DownloadAsync(CancellationToken ct)
            {
                string url = _tool.DownloadUrl!;
                string fileName = Path.GetFileName(new Uri(url).LocalPath);
                if (string.IsNullOrWhiteSpace(fileName))
                    throw new IOException("The update link does not point to a file.");

                Directory.CreateDirectory(ToolDownloader.ToolsRoot);
                string archivePath = Path.Combine(ToolDownloader.ToolsRoot, fileName);

                using var response = await Http.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;

                if (totalBytes is not null)
                {
                    _bar.Style = ProgressBarStyle.Continuous;
                    _bar.Maximum = 100;
                }
                else
                {
                    _bar.Style = ProgressBarStyle.Marquee;
                }

                await using (var target = File.Create(archivePath))
                await using (var source = await response.Content.ReadAsStreamAsync(ct))
                {
                    var buffer = new byte[81920];
                    long downloaded = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                        downloaded += read;

                        if (totalBytes is { } total)
                        {
                            int percent = (int)(downloaded * 100 / total);
                            SetMessage($"{fileName}  {FormatBytes(downloaded)}  ({percent}%)");
                            _bar.Value = Math.Clamp(percent, 0, 100);
                        }
                        else
                        {
                            SetMessage($"{fileName}  {FormatBytes(downloaded)} downloaded...");
                        }
                    }
                }

                return archivePath;
            }

            private static string FormatBytes(long bytes) =>
                bytes >= 0x100000 ? $"{bytes / 1048576.0:0.0} MB"
                    : bytes >= 0x400 ? $"{bytes / 1024.0:0.0} KB"
                    : $"{bytes} B";
        }
    }
}
