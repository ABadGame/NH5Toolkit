namespace NH5Toolkit
{
    /// <summary>
    /// Per-tool page: manages the standalone-executable path and launches the app.
    /// </summary>
    public class ToolPage : UserControl
    {
        private readonly ToolInfo _tool;
        private readonly Action<string> _navigate;
        private readonly Label _statusDot = new();
        private readonly Label _pathValue = new();
        private readonly Button _downloadButton;
        private readonly Button _checkButton;

        public ToolPage(ToolInfo tool, Action<string> navigate)
        {
            _tool = tool;
            _navigate = navigate;
            Dock = DockStyle.Fill;
            BackColor = Theme.WindowBack;

            var back = new LinkLabel
            {
                Text = "Back to home",
                AutoSize = true,
                Location = new Point(28, 18),
                LinkColor = Theme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            back.LinkClicked += (s, e) => _navigate("home");

            var title = new Label
            {
                Text = tool.Name,
                Font = new Font("Segoe UI Semibold", 18f),
                ForeColor = Theme.HeadingText,
                AutoSize = true,
                Location = new Point(28, 44)
            };

            var description = new Label
            {
                Text = tool.Description,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Theme.BodyText,
                AutoSize = false,
                Size = new Size(560, 24),
                Location = new Point(31, 78)
            };

            var launch = MakeButton($"Launch {tool.Name}", Theme.Accent, Color.White, 176, 38, new Point(28, 118));
            launch.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            launch.Click += (s, e) => Launcher.TryLaunch(_tool);

            var locate = MakeButton("Locate .exe", Theme.WindowBack, Theme.Accent, 110, 38, new Point(210, 118));
            locate.FlatAppearance.BorderColor = Theme.CardBorder;
            locate.FlatAppearance.BorderSize = 1;
            locate.Click += (s, e) => Launcher.ShowLocateDialog(_tool, this, RefreshStatus);

            // Shown only when the tool's author has published a package URL.
            _downloadButton = MakeButton("Download", Theme.WindowBack, Theme.Accent, 110, 38, new Point(326, 118));
            _downloadButton.FlatAppearance.BorderColor = Theme.CardBorder;
            _downloadButton.FlatAppearance.BorderSize = 1;
            _downloadButton.Click += (s, e) => ToolDownloader.ShowDownloadDialog(_tool, this, RefreshStatus);

            // Shown only when the tool is already installed.
            _checkButton = MakeButton("Check for Update", Theme.WindowBack, Theme.Accent, 136, 38, new Point(326, 118));
            _checkButton.FlatAppearance.BorderColor = Theme.CardBorder;
            _checkButton.FlatAppearance.BorderSize = 1;
            _checkButton.Click += (s, e) => ToolUpdater.ShowUpdateDialog(_tool, this, RefreshStatus);

            _statusDot.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _statusDot.AutoSize = true;
            _statusDot.Location = new Point(31, 170);
            _statusDot.BackColor = Color.Transparent;

            _pathValue.Font = new Font("Segoe UI", 9.5f);
            _pathValue.ForeColor = Theme.BodyText;
            _pathValue.AutoSize = false;
            _pathValue.Size = new Size(600, 24);
            _pathValue.Location = new Point(31, 192);
            _pathValue.BackColor = Color.Transparent;

            var tip = new LinkLabel
            {
                Text = "Where is my configuration saved?",
                AutoSize = true,
                Location = new Point(28, 310),
                LinkColor = Theme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            tip.LinkClicked += (s, e) => MessageBox.Show(
                "Each tool's .exe path is stored in:\n%APPDATA%\\NH5Toolkit\\config.json",
                "Configuration location",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            RefreshStatus();

            Controls.Add(tip);
            Controls.Add(_pathValue);
            Controls.Add(_statusDot);
            Controls.Add(_downloadButton);
            Controls.Add(_checkButton);
            Controls.Add(locate);
            Controls.Add(launch);
            Controls.Add(description);
            Controls.Add(title);
            Controls.Add(back);
        }

        private static Button MakeButton(string text, Color back, Color fore, int width, int height, Point location)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(width, height),
                Location = location,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private void RefreshStatus()
        {
            var path = ToolkitConfig.GetPath(_tool.Key);
            bool configured = path is not null;

            _statusDot.Text = configured ? "Configured" : "Not set up yet";
            _statusDot.ForeColor = configured ? Theme.Ok : Theme.Muted;
            _pathValue.Text = configured
                ? path
                : "No path set - click \"Locate .exe\" to point the hub at the standalone app.";

            _downloadButton.Visible = !configured && ToolDownloader.IsAvailable(_tool);
            _checkButton.Visible = configured && ToolUpdater.CanCheck(_tool);

            // Sit the optional buttons side by side with no gap when one is hidden.
            int optionalX = 326;
            if (_downloadButton.Visible)
            {
                _downloadButton.Location = new Point(optionalX, 118);
                optionalX += _downloadButton.Width + 8;
            }
            _checkButton.Location = new Point(optionalX, 118);
        }
    }
}
