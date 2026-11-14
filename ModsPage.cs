using NH5Toolkit.ModCore;

namespace NH5Toolkit
{
    /// <summary>
    /// "Mods" tab: the game's non-stock files grouped into mods (ModGrouper), with what each one
    /// adds or replaces, where the grouping came from, and any conflicts. Read-only for now.
    /// </summary>
    public class ModsPage : UserControl
    {
        private readonly Label _status = new();
        private readonly Button _refresh;
        private readonly ListView _mods = new();
        private readonly ListView _files = new();
        private readonly Label _detailTitle = new();
        private readonly Label _notes = new();
        private readonly SplitContainer _split = new();
        private List<ModGroup> _groups = new();
        private CancellationTokenSource? _cts;

        public ModsPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.WindowBack;

            var top = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.WindowBack };
            top.Controls.Add(new Label
            {
                Text = "Everything in the game folder that isn't stock, grouped into mods. Read-only: nothing is changed.",
                ForeColor = Theme.BodyText,
                AutoSize = true,
                Location = new Point(31, 8)
            });

            _refresh = new Button
            {
                Text = "Refresh",
                Size = new Size(110, 34),
                Location = new Point(28, 42),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Accent,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            _refresh.FlatAppearance.BorderSize = 0;
            _refresh.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            _refresh.Click += async (s, e) => await Reload();
            top.Controls.Add(_refresh);

            _status.Location = new Point(150, 50);
            _status.AutoSize = true;
            _status.ForeColor = Theme.BodyText;
            top.Controls.Add(_status);

            // Upper half: the mods.
            _mods.Dock = DockStyle.Fill;
            _mods.MultiSelect = false;
            _mods.HideSelection = false;
            _mods.Columns.Add("Mod", 360);
            _mods.Columns.Add("Type", 120);
            _mods.Columns.Add("Files", 60, HorizontalAlignment.Right);
            _mods.Columns.Add("Size", 80, HorizontalAlignment.Right);
            _mods.Columns.Add("Found by", 300);
            ListStyle.Apply(_mods, fillColumn: 4);
            _mods.SelectedIndexChanged += (s, e) => ShowDetails();

            // Lower half: the selected mod's notes and files.
            _detailTitle.Dock = DockStyle.Top;
            _detailTitle.Height = 30;
            _detailTitle.Font = new Font("Segoe UI Semibold", 11f);
            _detailTitle.ForeColor = Theme.HeadingText;
            _detailTitle.TextAlign = ContentAlignment.BottomLeft;

            // A wrapping label that grows with its text: no scrollbar (a TextBox's stays white in dark mode).
            _notes.Dock = DockStyle.Top;
            _notes.AutoSize = true;
            _notes.Padding = new Padding(0, 4, 0, 10);
            _notes.ForeColor = Theme.BodyText;
            _notes.BackColor = Theme.WindowBack;
            _split.Panel2.Resize += (s, e) =>
                _notes.MaximumSize = new Size(Math.Max(200, _split.Panel2.ClientSize.Width), 0);

            _files.Dock = DockStyle.Fill;
            _files.Columns.Add("Status", 90);
            _files.Columns.Add("File", 500);
            _files.Columns.Add("Size", 90, HorizontalAlignment.Right);
            ListStyle.Apply(_files, fillColumn: 1);

            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Horizontal;
            _split.BackColor = Theme.WindowBack;
            _split.SplitterWidth = 8;
            _split.Panel1MinSize = 60;
            _split.Panel2MinSize = 60;
            _split.Panel1.BackColor = Theme.WindowBack;
            _split.Panel2.BackColor = Theme.WindowBack;
            _split.Panel1.Controls.Add(_mods);
            _split.Panel2.Controls.Add(_files);
            _split.Panel2.Controls.Add(_notes);
            _split.Panel2.Controls.Add(_detailTitle);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 0, 28, 20), BackColor = Theme.WindowBack };
            host.Controls.Add(_split);

            Controls.Add(host);
            Controls.Add(top);

            // Place the splitter once the split has a real height; a small window used to throw here.
            bool placed = false;
            void PlaceSplitter()
            {
                if (placed) return;
                int min = _split.Panel1MinSize;
                int max = _split.Height - _split.Panel2MinSize - _split.SplitterWidth;
                if (max <= min) return;   // too small yet; try again on the next resize
                _split.SplitterDistance = Math.Clamp(_split.Height * 45 / 100, min, max);
                placed = true;
            }
            _split.Resize += (s, e) => PlaceSplitter();

            Load += async (s, e) =>
            {
                PlaceSplitter();
                await Reload();
            };
            Disposed += (s, e) => _cts?.Cancel();
        }

        private async Task Reload()
        {
            var gameDir = GameScanner.FindGameDir();
            if (gameDir is null)
            {
                _status.Text = "Game folder not found. Set it on the Game Check tab.";
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _refresh.Enabled = false;
            _status.Text = "Scanning the game folder...";

            var progress = new Progress<string>(text => { if (!IsDisposed) _status.Text = text; });
            try
            {
                var groups = await Task.Run(() =>
                {
                    var scan = GameScanner.Scan(gameDir, GameScanner.OldManagerStockDir, null, token);
                    return ModGrouper.Group(scan, progress, token);
                }, token);
                if (IsDisposed) return;

                _groups = groups;
                FillMods();
                int files = groups.Sum(g => g.Files.Count);
                int conflicts = groups.Count(HasConflict);
                _status.Text = $"{groups.Count} mods, {files} files" + (conflicts > 0 ? $"   ·   {conflicts} with a conflict" : "");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed) _status.Text = "Couldn't read the game folder: " + ex.Message;
            }
            finally
            {
                if (!IsDisposed) _refresh.Enabled = true;
            }
        }

        private static bool HasConflict(ModGroup g) => g.Notes.Any(n => n.StartsWith("CONFLICT", StringComparison.Ordinal));

        private void FillMods()
        {
            _mods.BeginUpdate();
            _mods.Items.Clear();
            foreach (var g in _groups)
            {
                var item = new ListViewItem(
                [
                    (HasConflict(g) ? "⚠ " : "") + g.Name,
                    g.Kind,
                    g.Files.Count.ToString(),
                    ListStyle.FormatSize(g.Size),
                    g.Source
                ]) { Tag = g };
                item.ForeColor = g.Kind == "Probably stock" ? Theme.Muted
                               : HasConflict(g) ? Color.Goldenrod
                               : Theme.NavText;
                _mods.Items.Add(item);
            }
            _mods.EndUpdate();
            ListStyle.Fit(_mods, 4);

            if (_mods.Items.Count > 0)
                _mods.Items[0].Selected = true;
            else
                ShowDetails();
        }

        private void ShowDetails()
        {
            var g = _mods.SelectedItems.Count > 0 ? _mods.SelectedItems[0].Tag as ModGroup : null;
            _detailTitle.Text = g?.Name ?? "";
            _notes.Text = g is null ? "" : string.Join(Environment.NewLine, g.Notes.Select(n => "•  " + n));

            _files.BeginUpdate();
            _files.Items.Clear();
            if (g is not null)
                foreach (var f in g.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
                {
                    var item = new ListViewItem([f.State.ToString(), f.Path, ListStyle.FormatSize(f.Size)]);
                    item.ForeColor = f.State == FileState.Replaced ? Theme.Accent
                                   : f.State == FileState.Missing ? Color.IndianRed
                                   : Theme.NavText;
                    _files.Items.Add(item);
                }
            _files.EndUpdate();
            ListStyle.Fit(_files, 1);
        }
    }
}
