using System.Diagnostics;

namespace NH5Toolkit
{
    /// <summary>
    /// Landing page: a card per tool with quick-launch, a changelog (hub news
    /// plus each tool's GitHub release notes) and navigation to tool pages.
    /// </summary>
    public class HomePage : UserControl
    {
        private const int CardHeight = 190;

        private readonly Action<string> _navigate;
        private readonly FlowLayoutPanel _changelogList = new();
        private readonly CancellationTokenSource _changelogCancellation = new();

        public HomePage(Action<string> navigate)
        {
            _navigate = navigate;
            Dock = DockStyle.Fill;
            BackColor = Theme.WindowBack;

            // One table controls the whole vertical layout: heading, subtitle,
            // an evenly-split card row, changelog header, changelog list.
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(20, 10, 20, 12),
                BackColor = Theme.WindowBack
            };

            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));         // heading
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));         // subtitle
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, CardHeight)); // cards
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));         // changelog heading
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));         // changelog list

            var heading = new Label
            {
                Text = "Welcome to the NASCAR Heat 5 Toolkit",
                Font = new Font("Segoe UI Semibold", 20f),
                ForeColor = Theme.HeadingText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var subheading = new Label
            {
                Text = "Your one-stop hub for NASCAR Heat 5 modding tools. Click a card to manage it, or hit Launch to open it right away.",
                Font = new Font("Segoe UI", 10f),
                ForeColor = Theme.BodyText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var cardRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 0),
                ColumnCount = Tools.All.Length,
                RowCount = 1
            };

            for (int i = 0; i < Tools.All.Length; i++)
                cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / Tools.All.Length));
            cardRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            foreach (var tool in Tools.All)
                cardRow.Controls.Add(BuildCard(tool));

            var changelogHeading = new Label
            {
                Text = "Changelog",
                Font = new Font("Segoe UI Semibold", 14f),
                ForeColor = Theme.HeadingText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _changelogList.Dock = DockStyle.Fill;
            _changelogList.FlowDirection = FlowDirection.TopDown;
            _changelogList.WrapContents = false;
            _changelogList.AutoScroll = true;
            _changelogList.Margin = new Padding(0);
            _changelogList.BackColor = Theme.WindowBack;

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(subheading, 0, 1);
            root.Controls.Add(cardRow, 0, 2);
            root.Controls.Add(changelogHeading, 0, 3);
            root.Controls.Add(_changelogList, 0, 4);

            // On wide screens the cards would stretch into huge empty panels,
            // so cap their total width but keep the row left-aligned with the
            // rest of the page.
            cardRow.Resize += (s, e) => CapCardRowWidth(cardRow);
            CapCardRowWidth(cardRow);

            Controls.Add(root);

            ShowLocalChangelog();
            _ = LoadRemoteChangelogAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _changelogCancellation.Cancel();
                _changelogCancellation.Dispose();
            }
            base.Dispose(disposing);
        }

        private static void CapCardRowWidth(TableLayoutPanel cardRow)
        {
            int maxTotal = Tools.All.Length * 360;
            int extra = Math.Max(0, cardRow.ClientSize.Width - maxTotal);
            cardRow.Padding = new Padding(0, 0, extra, 0);
        }

        /// <summary>Body of the changelog without GitHub data: just hub news.</summary>
        private void ShowLocalChangelog()
        {
            _changelogList.Controls.Clear();

            foreach (var entry in Changelog.Entries)
                _changelogList.Controls.Add(BuildChangelogEntry(entry));

            if (Tools.All.All(tool => GitHubReleases.RepoFor(tool) is null))
            {
                _changelogList.Controls.Add(NoteLabel(
                    "Each tool's GitHub release notes will appear here automatically once its download link points at a GitHub release."));
            }
        }

        /// <summary>
        /// Fetches GitHub release notes for every tool that has a repo, then
        /// re-renders the changelog with local + remote entries merged.
        /// </summary>
        private async Task LoadRemoteChangelogAsync()
        {
            if (Tools.All.All(tool => GitHubReleases.RepoFor(tool) is null))
                return;   // nothing on GitHub yet; the local list already shows a hint

            var remote = new List<ChangelogEntry>();
            bool failed = false;

            try
            {
                foreach (var tool in Tools.All)
                    remote.AddRange(await GitHubReleases.FetchReleasesAsync(tool, _changelogCancellation.Token));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                failed = true;
            }

            if (_changelogCancellation.IsCancellationRequested || IsDisposed)
                return;

            var combined = remote
                .Concat(Changelog.Entries)
                .OrderByDescending(entry => entry.Date, StringComparer.Ordinal)
                .ToList();

            _changelogList.Controls.Clear();
            foreach (var entry in combined)
                _changelogList.Controls.Add(BuildChangelogEntry(entry));

            if (failed)
                _changelogList.Controls.Add(NoteLabel("Couldn't load release notes from GitHub right now - showing hub news only."));
        }

        private Control BuildChangelogEntry(ChangelogEntry entry)
        {
            var header = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Margin = new Padding(0)
            };

            Control versionLabel;
            if (entry.ReleaseUrl is string releaseUrl)
            {
                var link = new LinkLabel
                {
                    Text = $"{entry.Tool} {entry.Version}",
                    Font = new Font("Segoe UI Semibold", 10.5f),
                    LinkColor = Theme.Accent,
                    LinkBehavior = LinkBehavior.HoverUnderline,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 12, 0)
                };
                link.LinkClicked += (s, e) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = releaseUrl, UseShellExecute = true });
                    }
                    catch
                    {
                        // Opening a browser isn't critical; ignore failures.
                    }
                };
                versionLabel = link;
            }
            else
            {
                versionLabel = new Label
                {
                    Text = $"{entry.Tool} {entry.Version}",
                    Font = new Font("Segoe UI Semibold", 10.5f),
                    ForeColor = Theme.HeadingText,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 12, 0)
                };
            }

            header.Controls.Add(versionLabel);
            header.Controls.Add(new Label
            {
                Text = entry.Date,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Theme.Muted,
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0)
            });

            var entryPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 2, 0, 16)
            };

            entryPanel.Controls.Add(header);
            foreach (string note in entry.Notes)
            {
                entryPanel.Controls.Add(new Label
                {
                    Text = "•  " + note,
                    Font = new Font("Segoe UI", 9.5f),
                    ForeColor = Theme.BodyText,
                    AutoSize = true,
                    Margin = new Padding(0, 3, 0, 0)
                });
            }

            return entryPanel;
        }

        private static Label NoteLabel(string text) => new()
        {
            Text = text,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            ForeColor = Theme.Muted,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };

        private Panel BuildCard(ToolInfo tool)
        {
            // Built-in sections (the Mod Manager) are always ready: "Open" just shows their page.
            bool configured = tool.BuiltIn || ToolkitConfig.GetPath(tool.Key) is not null;
            bool downloadable = !tool.BuiltIn && ToolDownloader.IsAvailable(tool);

            var card = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(10, 4, 10, 4),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.CardBack,
                Cursor = Cursors.Hand,
                Tag = tool
            };

            var title = new Label
            {
                Text = tool.Name,
                Font = new Font("Segoe UI Semibold", 12f),
                ForeColor = Theme.HeadingText,
                AutoSize = true,
                Location = new Point(16, 16),
                BackColor = Color.Transparent
            };

            var status = new Label
            {
                Text = tool.BuiltIn ? "Built into the toolkit"
                    : configured ? "Ready to launch"
                    : downloadable ? "One-click download available" : "Not set up yet",
                Font = new Font("Segoe UI", 9f),
                ForeColor = configured ? Theme.Ok : Theme.Muted,
                AutoSize = true,
                Location = new Point(17, 44),
                BackColor = Color.Transparent
            };

            var description = new Label
            {
                Text = tool.Description,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Theme.BodyText,
                AutoSize = false,
                Location = new Point(17, 70),
                BackColor = Color.Transparent
            };

            var launch = new Button
            {
                Text = tool.BuiltIn ? "Open" : configured ? "Launch" : downloadable ? "Download" : "Launch",
                Size = new Size(96, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Accent,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            launch.FlatAppearance.BorderSize = 0;
            launch.FlatAppearance.MouseOverBackColor = Theme.AccentHover;

            launch.Click += (s, e) =>
            {
                if (tool.BuiltIn)
                {
                    _navigate(tool.Key);
                }
                else if (configured)
                {
                    Launcher.TryLaunch(tool);
                }
                else if (downloadable)
                {
                    var ownerBar = card.FindForm();
                    if (ownerBar is null)
                        return;

                    ToolDownloader.ShowDownloadDialog(tool, ownerBar, () => _navigate(tool.Key));
                }
                else
                {
                    Launcher.TryLaunch(tool);  // triggers the friendly "not set up yet" hint
                }
            };

            // Clicking anywhere on the card (except the action button) opens the tool page.
            EventHandler openPage = (s, e) => _navigate(tool.Key);
            card.Click += openPage;
            title.Click += openPage;
            status.Click += openPage;
            description.Click += openPage;

            // Hover highlight across the whole card surface.
            EventHandler onEnter = (s, e) => card.BackColor = Theme.CardHover;
            EventHandler onLeave = (s, e) => card.BackColor = Theme.CardBack;
            card.MouseEnter += onEnter;
            title.MouseEnter += onEnter;
            status.MouseEnter += onEnter;
            description.MouseEnter += onEnter;
            card.MouseLeave += onLeave;
            title.MouseLeave += onLeave;
            status.MouseLeave += onLeave;
            description.MouseLeave += onLeave;

            card.Resize += (s, e) => PositionCardContent(card, description, launch);
            PositionCardContent(card, description, launch);

            card.Controls.Add(launch);
            card.Controls.Add(description);
            card.Controls.Add(status);
            card.Controls.Add(title);

            return card;
        }

        private static void PositionCardContent(Panel card, Label description, Button launch)
        {
            try
            {
                card.SuspendLayout();
                description.Size = new Size(card.ClientSize.Width - 34, card.ClientSize.Height - 132);
                launch.Location = new Point(
                    card.ClientSize.Width - launch.Width - 14,
                    card.ClientSize.Height - launch.Height - 12);
            }
            finally
            {
                card.ResumeLayout(true);
            }
        }
    }
}
