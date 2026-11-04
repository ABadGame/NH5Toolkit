namespace NH5Toolkit
{
    /// <summary>
    /// Downloads a tool's published package (zip or plain exe) into the hub-owned
    /// tools folder and points the hub at the installed executable, so a fresh
    /// user gets a working setup with one click. Installation itself is shared
    /// with the updater via PackageInstaller.
    /// </summary>
    public static class ToolDownloader
    {
        /// <summary>Hub-owned location: %AppData%\NH5Toolkit\Tools\&lt;tool&gt;\</summary>
        public static readonly string ToolsRoot =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NH5Toolkit", "Tools");

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(15)
        };

        public static bool IsAvailable(ToolInfo tool) => !string.IsNullOrWhiteSpace(tool.DownloadUrl);

        public static string InstallFolder(ToolInfo tool) => Path.Combine(ToolsRoot, tool.Key);

        public static void ShowDownloadDialog(ToolInfo tool, IWin32Window owner, Action completed)
        {
            using var dialog = new DownloadDialog(tool);
            dialog.StartPosition = FormStartPosition.CenterParent;
            if (dialog.ShowDialog(owner) == DialogResult.OK)
                completed();
        }

        private sealed class DownloadDialog : Form
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

            public DownloadDialog(ToolInfo tool)
            {
                _tool = tool;

                Text = $"Download {tool.Name}";
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

                _message.Text = $"Connecting to download {tool.Name}...";
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
                    string fileName = Path.GetFileName(new Uri(url).LocalPath);
                    if (string.IsNullOrWhiteSpace(fileName))
                        throw new IOException("The download link does not point to a file.");

                    Directory.CreateDirectory(ToolsRoot);
                    string archivePath = Path.Combine(ToolsRoot, fileName);

                    using var response = await Http.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead,
                        _cancellation.Token);
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;
                    long downloaded = 0;

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
                    await using (var source = await response.Content.ReadAsStreamAsync(_cancellation.Token))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await source.ReadAsync(buffer, _cancellation.Token)) > 0)
                        {
                            await target.WriteAsync(buffer.AsMemory(0, read), _cancellation.Token);
                            downloaded += read;

                            if (totalBytes is { } total)
                            {
                                int percent = (int)(downloaded * 100 / total);
                                SetMessage($"{fileName}  {PercentOrBytes(percent, downloaded, null)}");
                                _bar.Value = Math.Clamp(percent, 0, 100);
                            }
                            else
                            {
                                SetMessage($"{fileName}  {FormatBytes(downloaded)} downloaded...");
                            }
                        }
                    }

                    SetMessage("Installing...");
                    string result = PackageInstaller.Install(_tool, archivePath);
                    SetMessage(result);
                    DialogResult = DialogResult.OK;
                }
                catch (OperationCanceledException)
                {
                    DialogResult = DialogResult.Cancel;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Download failed:\n\n{ex.Message}", _tool.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    DialogResult = DialogResult.Cancel;
                }
            }

            private static string PercentOrBytes(int percent, long done, long? _) => FormatBytes(done) + $"  ({percent}%)";

            private static string FormatBytes(long bytes) =>
                bytes >= 0x100000 ? $"{bytes / 1048576.0:0.0} MB"
                    : bytes >= 0x400 ? $"{bytes / 1024.0:0.0} KB"
                    : $"{bytes} B";
        }
    }
}
