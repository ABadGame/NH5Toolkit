using System.IO.Compression;
using NH5Toolkit.ModCore;

namespace NH5Toolkit
{
    /// <summary>
    /// "My Mods": the mods the toolkit manages. 2026-11-16 simpler layout (user: "still looks kinda complicated to use. the loader part
    /// is perfect"): one list with an On box per mod like Loader Modules, one "Add mod" button (zip or folder; drag-and-drop still
    /// works), an Apply bar that only shows when something is waiting, order arrows only when two switched-on mods change the same file,
    /// and Details / Move / Remove on a row's right-click menu. The engine (Importer, Deployer, InstallerCapture) is unchanged:
    /// nothing in the game changes until Apply, and Apply still shows exactly what it will do first.
    /// </summary>
    public class MyModsPage : UserControl
    {
        private readonly ListView _list = new();
        private readonly Label _status = new();
        private readonly Label _pending = new();
        private readonly Label _orderHint = new();
        private readonly Panel _applyBar = new();
        private readonly FlowLayoutPanel _orderButtons = new();
        private readonly List<Button> _busyButtons = new();
        private readonly Button _apply;
        private readonly ContextMenuStrip _rowMenu = new();
        private ModLibrary _library = new();
        private ProfileStore? _profiles;
        private readonly ComboBox _profileBox = new();
        private readonly CheckBox _multiplayer = new();
        private readonly Label _gameLabel = new();
        private readonly Label _deployedLabel = new();
        private bool _filling;
        private StockIndex? _stock;
        private string? _gameDir;
        private bool _busy;

        public MyModsPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.WindowBack;
            AllowDrop = true;
            DragEnter += (s, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += async (s, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                    await AddMods(paths);
            };

            var top = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Theme.WindowBack };

            // 2026-11-16 (user: "same functionality as the old one just with a new UI. profiles, deploy, etc."):
            // game folder + Refresh, and the old manager's profile bar (+ New, rename / copy / delete / export / import, Enable Multiplayer)
            var gameRow = new FlowLayoutPanel
            {
                Location = new Point(26, 6), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.WindowBack
            };
            gameRow.Controls.Add(new Label { Text = "Game folder", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(3, 9, 6, 0) });
            _gameLabel.AutoSize = false;
            _gameLabel.Height = 22;
            _gameLabel.AutoEllipsis = true;   // a long path shortens with ... instead of pushing the buttons off screen
            _gameLabel.ForeColor = Theme.NavText;
            _gameLabel.Margin = new Padding(0, 9, 10, 0);
            gameRow.Controls.Add(_gameLabel);
            gameRow.Controls.Add(Secondary("Change...", 84, () => { PickGameDir(); return Task.CompletedTask; }));
            gameRow.Controls.Add(Secondary("Refresh", 76, Reload));
            top.Controls.Add(gameRow);

            var profileRow = new FlowLayoutPanel
            {
                Location = new Point(26, 46), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.WindowBack
            };
            profileRow.Controls.Add(new Label { Text = "Profile", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(3, 9, 6, 0) });
            _profileBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _profileBox.Width = 200;
            _profileBox.Margin = new Padding(0, 5, 6, 0);
            _profileBox.FlatStyle = FlatStyle.Flat;
            _profileBox.BackColor = Theme.CardBack;
            _profileBox.ForeColor = Theme.NavText;
            _profileBox.DrawMode = DrawMode.OwnerDrawFixed;   // Windows paints a DropDownList white in dark mode otherwise
            _profileBox.ItemHeight = 20;
            _profileBox.DrawItem += (s, e) =>
            {
                bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using var back = new SolidBrush(hot ? Theme.NavActiveBack : Theme.CardBack);
                e.Graphics.FillRectangle(back, e.Bounds);
                if (e.Index >= 0)
                    TextRenderer.DrawText(e.Graphics, _profileBox.Items[e.Index]?.ToString(), _profileBox.Font, e.Bounds,
                                          hot ? Theme.NavActiveText : Theme.NavText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            _profileBox.SelectedIndexChanged += async (s, e) => await SelectProfile();
            profileRow.Controls.Add(_profileBox);
            profileRow.Controls.Add(Secondary("+ New", 70, () => { NewProfile(); return Task.CompletedTask; }));
            var more = Secondary("Profile ▾", 90, () => Task.CompletedTask);
            var moreMenu = new ContextMenuStrip();
            moreMenu.Items.Add("Rename...", null, (s, e) => RenameProfile());
            moreMenu.Items.Add("Copy...", null, (s, e) => CopyProfile());
            moreMenu.Items.Add("Delete...", null, (s, e) => DeleteProfile());
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add("Export...", null, async (s, e) => await ExportProfile());
            moreMenu.Items.Add("Import...", null, async (s, e) => await ImportProfile());
            moreMenu.Items.Add("Import from old Mod Manager...", null, async (s, e) => await ImportOldManager());
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add("Choose game save folder...", null, (s, e) => ChooseSaveFolder());
            more.Click += (s, e) => { if (!_busy) moreMenu.Show(more, new Point(0, more.Height)); };
            profileRow.Controls.Add(more);
            _multiplayer.Text = "Enable Multiplayer";
            _multiplayer.AutoSize = true;
            _multiplayer.ForeColor = Theme.NavText;
            _multiplayer.Margin = new Padding(16, 8, 0, 0);
            _multiplayer.CheckedChanged += async (s, e) => await MultiplayerChanged();
            profileRow.Controls.Add(_multiplayer);
            _deployedLabel.AutoSize = true;
            _deployedLabel.ForeColor = Theme.Muted;
            _deployedLabel.Margin = new Padding(16, 9, 0, 0);
            profileRow.Controls.Add(_deployedLabel);
            top.Controls.Add(profileRow);
            var intro = new Label
            {
                Text = "Click the On box to switch a mod on or off for this profile, then Deploy. To add a mod, use Add mod or drop a .zip " +
                       "or folder here. Right-click a mod for details.",
                ForeColor = Theme.BodyText,
                AutoSize = true,
                Location = new Point(31, 88)
            };
            top.Controls.Add(intro);

            // 2026-11-16 fix (user screenshot): GrowAndShrink, or the row kept its default 200x100 size and hid the status line
            var row = new FlowLayoutPanel
            {
                Location = new Point(26, 38), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.WindowBack
            };
            var add = Secondary("Add mod...", 110, () => Task.CompletedTask);
            var addMenu = new ContextMenuStrip();
            addMenu.Items.Add("A .zip file...", null, async (s, e) => await PickZips());
            addMenu.Items.Add("A folder...", null, async (s, e) => await PickFolder());
            add.Click += (s, e) => { if (!_busy) addMenu.Show(add, new Point(0, add.Height)); };
            row.Controls.Add(add);

            // order buttons: only while two switched-on mods change the same file
            _orderButtons.AutoSize = true;
            _orderButtons.WrapContents = false;
            _orderButtons.BackColor = Theme.WindowBack;
            _orderButtons.Margin = new Padding(14, 0, 0, 0);
            _orderButtons.Controls.Add(Secondary("▲ Higher", 86, () => { MoveSelected(-1); return Task.CompletedTask; }));
            _orderButtons.Controls.Add(Secondary("▼ Lower", 86, () => { MoveSelected(+1); return Task.CompletedTask; }));
            _orderButtons.Visible = false;
            row.Controls.Add(_orderButtons);
            top.Controls.Add(row);

            _status.Location = new Point(31, 80);
            _status.MaximumSize = new Size(2000, 0);
            _status.AutoSize = true;
            _status.ForeColor = Theme.BodyText;
            top.Controls.Add(_status);

            _orderHint.AutoSize = true;
            _orderHint.ForeColor = Color.Goldenrod;
            _orderHint.Margin = new Padding(12, 9, 0, 0);
            _orderHint.Visible = false;
            row.Controls.Add(_orderHint);

            // lay the top area out from the window width: the intro wraps, the rows follow it (no fixed 900 px line)
            void LayoutTop()
            {
                intro.MaximumSize = new Size(Math.Max(200, top.Width - 62), 0);
                _gameLabel.Width = Math.Max(120, top.Width - 62 - 90 - 84 - 76 - 24);
                profileRow.Top = gameRow.Bottom + 6;
                intro.Top = profileRow.Bottom + 10;
                row.Top = intro.Bottom + 8;
                _status.Top = row.Bottom + 6;
                top.Height = _status.Bottom + 8;
            }
            top.Resize += (s, e) => LayoutTop();
            row.SizeChanged += (s, e) => LayoutTop();
            gameRow.SizeChanged += (s, e) => LayoutTop();
            profileRow.SizeChanged += (s, e) => LayoutTop();
            HandleCreated += (s, e) => LayoutTop();

            _list.Dock = DockStyle.Fill;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.Columns.Add("On", 44);
            _list.Columns.Add("Mod", 300);
            _list.Columns.Add("What it does", 400);
            ListStyle.Apply(_list, fillColumn: 2);
            _list.MouseClick += (s, e) =>
            {
                var hit = _list.HitTest(e.Location);
                if (hit.Item is null) return;
                if (e.Button == MouseButtons.Right)
                {
                    hit.Item.Selected = true;
                    UpdateRowMenu();
                    _rowMenu.Show(_list, e.Location);
                }
                else if (hit.SubItem is not null && hit.Item.SubItems.IndexOf(hit.SubItem) == 0)
                    ToggleSelected();
            };
            _list.DoubleClick += (s, e) => ToggleSelected();
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space) { ToggleSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
            };

            _rowMenu.Items.Add("Files and details...", null, (s, e) => ShowDetailsDialog());
            _rowMenu.Items.Add("Switch on / off", null, (s, e) => ToggleSelected());
            _rowMenu.Items.Add(new ToolStripSeparator());
            _rowMenu.Items.Add("Move higher (wins shared files)", null, (s, e) => MoveSelected(-1));
            _rowMenu.Items.Add("Move lower", null, (s, e) => MoveSelected(+1));
            _rowMenu.Items.Add(new ToolStripSeparator());
            _rowMenu.Items.Add("Remove from the list...", null, (s, e) => RemoveSelected());

            // Apply bar: only shown while the game doesn't match the list
            _applyBar.Dock = DockStyle.Bottom;
            _applyBar.Height = 56;
            _applyBar.BackColor = Theme.SidebarBack;
            _applyBar.Visible = false;
            _pending.AutoSize = true;
            _pending.ForeColor = Theme.HeadingText;
            _pending.Font = new Font("Segoe UI Semibold", 10f);
            _pending.Location = new Point(20, 18);
            _applyBar.Controls.Add(_pending);
            _apply = Primary("Deploy", 120, async () => await ApplyChanges());
            _applyBar.Controls.Add(_apply);
            _applyBar.Resize += (s, e) => _apply.Location = new Point(_applyBar.Width - _apply.Width - 20, 11);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 0, 28, 12), BackColor = Theme.WindowBack };
            host.Controls.Add(_list);
            Controls.Add(host);
            Controls.Add(_applyBar);
            Controls.Add(top);

            Load += async (s, e) => await Reload();
        }

        // ---------- buttons ----------

        private Button Primary(string text, int width, Func<Task> click)
        {
            var b = new Button
            {
                Text = text, Size = new Size(width, 34), Margin = new Padding(2, 0, 6, 0),
                FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White,
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            b.Click += async (s, e) => { if (!_busy) await click(); };
            _busyButtons.Add(b);
            return b;
        }

        private Button Secondary(string text, int width, Func<Task> click)
        {
            var b = new Button
            {
                Text = text, Size = new Size(width, 34), Margin = new Padding(2, 0, 2, 0),
                FlatStyle = FlatStyle.Flat, BackColor = Theme.WindowBack, ForeColor = Theme.Accent,
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Theme.CardBorder;
            b.FlatAppearance.BorderSize = 1;
            b.Click += async (s, e) => { if (!_busy) await click(); };
            _busyButtons.Add(b);
            return b;
        }

        private void SetBusy(bool busy, string? status = null)
        {
            _busy = busy;
            foreach (var b in _busyButtons) b.Enabled = !busy;
            if (status is not null) _status.Text = status;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        // ---------- loading ----------

        private async Task Reload()
        {
            _gameDir = GameScanner.FindGameDir();
            _gameLabel.Text = _gameDir ?? "not found";
            try
            {
                _library = ModLibrary.Load();
                _profiles = ProfileStore.Load(_library);
                ProfileStore.ApplyToLibrary(_profiles.Current, _library);
                FillProfiles();
            }
            catch (Exception ex)
            {
                SetBusy(true, ex.Message);   // stay locked: never work over a library we couldn't read
                return;
            }
            if (_gameDir is null)
            {
                SetBusy(true, "Game folder not found. Click Change... and pick the folder with NASCARHeat5.exe.");
                return;
            }

            SetBusy(true, "Reading the game folder...");
            string game = _gameDir;
            _stock = await Task.Run(() => StockIndex.Build(game));
            foreach (var m in _library.Mods)
                m.Size = m.Files.Values.Distinct().Sum(sha => FileStore.Has(sha) ? new FileInfo(FileStore.PathFor(sha)).Length : 0);
            SetBusy(false, "");
            Fill();
            await RefreshPending();
        }

        /// <summary>Switched-on mods that ship a file another switched-on mod also ships, with different contents.</summary>
        private Dictionary<ModPackage, HashSet<string>> Overlaps()
        {
            var byPath = new Dictionary<string, List<(ModPackage Mod, string Sha)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in _library.Mods.Where(m => m.Enabled))
                foreach (var (path, sha) in m.Files)
                {
                    if (_profiles?.Current.IsExcluded(m.Id, path) == true) continue;
                    if (!byPath.TryGetValue(path, out var l)) byPath[path] = l = new();
                    l.Add((m, sha));
                }
            var result = new Dictionary<ModPackage, HashSet<string>>();
            foreach (var l in byPath.Values)
            {
                if (l.Count < 2 || l.Select(x => x.Sha).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2) continue;
                foreach (var (mod, _) in l)
                {
                    if (!result.TryGetValue(mod, out var others)) result[mod] = others = new();
                    foreach (var (other, _) in l) if (other != mod) others.Add(other.Name);
                }
            }
            return result;
        }

        private string Describe(ModPackage m, Dictionary<ModPackage, HashSet<string>> overlaps)
        {
            string what = m.IsInstaller
                ? (m.Captured ? "Installer mod (has run)" : "Installer mod: runs its own setup when you Apply")
                : _stock is null ? "" : m.KindFor(_stock) switch
                {
                    "Add-on" => "Adds new files",
                    "Replacement" => "Replaces game files",
                    _ => "Adds and replaces game files"
                };
            what += $"  ·  {ListStyle.FormatSize(m.Size)}";
            int left = _profiles is null || m.IsInstaller ? 0 : m.Files.Keys.Count(k => _profiles.Current.IsExcluded(m.Id, k));
            if (left > 0) what += $"  ·  {m.Files.Count - left} of {m.Files.Count} files in this profile";
            if (overlaps.TryGetValue(m, out var others))
                what += $"  ·  ⚠ changes the same files as {string.Join(", ", others)}";
            return what;
        }

        private void Fill(ModPackage? select = null)
        {
            select ??= Selected();
            var overlaps = Overlaps();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var m in _library.Mods.OrderBy(m => m.Priority))
            {
                var item = new ListViewItem(
                [
                    m.Enabled ? "  ☑" : "  ☐",
                    m.Name + (m.Version.Length > 0 ? "  " + m.Version : ""),
                    Describe(m, overlaps)
                ]) { Tag = m };
                item.ForeColor = m.Enabled ? Theme.NavText : Theme.Muted;
                if (m.Enabled && overlaps.ContainsKey(m)) item.ForeColor = Color.Goldenrod;
                _list.Items.Add(item);
                if (m == select) item.Selected = true;
            }
            _list.EndUpdate();
            ListStyle.Fit(_list, 2);

            bool order = overlaps.Count > 0;
            _orderButtons.Visible = order;
            _orderHint.Visible = order;
            _orderHint.Text = order ? "Two switched-on mods change the same files: the higher one in the list wins." : "";

            int on = _library.Mods.Count(m => m.Enabled);
            if (!_busy)
                _status.Text = _library.Mods.Count == 0
                    ? "No mods yet. Click Add mod, or drop a .zip or folder here."
                    : $"{_library.Mods.Count} mods, {on} on";
            if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
        }

        private ModPackage? Selected() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ModPackage : null;

        private void UpdateRowMenu()
        {
            var m = Selected();
            _rowMenu.Items[1].Text = m?.Enabled == true ? "Switch off" : "Switch on";
            foreach (ToolStripItem i in _rowMenu.Items) i.Enabled = !_busy && m is not null;
        }

        /// <summary>
        /// Where the mod came from, its notes and every file. 2026-11-16: each file has a tick for the selected profile (untick =
        /// left out of this profile only, like the old manager's per-file ticks); a search box and tick / untick for what's shown.
        /// </summary>
        private void ShowDetailsDialog()
        {
            if (Selected() is not { } m) return;
            var prof = _profiles?.Current;
            bool editable = prof is not null && !m.IsInstaller;
            using var f = new Form
            {
                Text = m.Name, Size = new Size(860, 600), StartPosition = FormStartPosition.CenterParent,
                BackColor = Theme.WindowBack, ForeColor = Theme.NavText, MinimizeBox = false, ShowIcon = false, ShowInTaskbar = false
            };
            var notes = new List<string>
            {
                $"From {m.Source}  ·  added {m.Imported:yyyy-MM-dd}  ·  came as {m.Format}",
                $"{m.Files.Count} files, {ListStyle.FormatSize(m.Size)}" + (m.IsInstaller ? "  ·  installer mod" : "")
            };
            if (editable)
                notes.Add($"Ticked files are used by the profile \"{prof!.Name}\". Untick a file to leave it out of this profile only " +
                          "(other profiles and the mod keep it). Click Deploy afterwards.");
            else if (m.IsInstaller)
                notes.Add("An installer mod's files go in together, so they can't be ticked one by one.");
            if (m.IsInstaller && !m.Captured)
                notes.Add("Not run yet. Switch it on and Deploy: its installer opens, and the files it changes show up here.");
            notes.AddRange(m.Notes);
            var head = new Label
            {
                Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 12, 16, 8), ForeColor = Theme.BodyText,
                MaximumSize = new Size(820, 0), Text = string.Join(Environment.NewLine, notes.Select(n => "•  " + n))
            };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 4, 0, 0), WrapContents = false, BackColor = Theme.WindowBack };
            var search = new TextBox { Width = 260, BackColor = Theme.CardBack, ForeColor = Theme.NavText, PlaceholderText = "Search files...", Margin = new Padding(4, 6, 8, 0) };
            bar.Controls.Add(search);
            var counter = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(8, 9, 0, 0) };

            var files = new ListView { Dock = DockStyle.Fill, MultiSelect = true, CheckBoxes = editable };
            files.Columns.Add("File in the game folder", 600);
            files.Columns.Add("Size", 90, HorizontalAlignment.Right);
            ListStyle.Apply(files, fillColumn: 0);
            if (editable)
            {
                // ListStyle draws rows by hand: draw the tick box too
                files.DrawSubItem += (s, e) =>
                {
                    if (e.ColumnIndex != 0 || e.Item is null) return;
                    var box = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + (e.Bounds.Height - 13) / 2, 13, 13);
                    CheckBoxRenderer.DrawCheckBox(e.Graphics, box.Location,
                        e.Item.Checked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);
                };
            }
            var all = m.Files.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToList();
            bool filling = false;
            void Count() => counter.Text = editable
                ? $"{all.Count(p => !prof!.IsExcluded(m.Id, p.Key))} of {all.Count} files in \"{prof!.Name}\""
                : $"{all.Count} files";
            void FillFiles()
            {
                filling = true;
                files.BeginUpdate();
                files.Items.Clear();
                string q = search.Text.Trim();
                foreach (var (path, sha) in all)
                {
                    if (q.Length > 0 && path.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    long size = FileStore.Has(sha) ? new FileInfo(FileStore.PathFor(sha)).Length : 0;
                    var item = new ListViewItem([(editable ? "      " : "") + path, ListStyle.FormatSize(size)]) { Tag = path };
                    item.ForeColor = _stock?.IsStockPath(path) == true ? Theme.Accent : Theme.NavText;   // blue = replaces a stock file
                    if (editable)
                    {
                        item.Checked = !prof!.IsExcluded(m.Id, path);
                        if (!item.Checked) item.ForeColor = Theme.Muted;
                    }
                    files.Items.Add(item);
                }
                files.EndUpdate();
                filling = false;
                Count();
            }
            bool changed = false;
            if (editable)
            {
                void SetShown(bool on)
                {
                    foreach (ListViewItem it in files.Items) prof!.SetExcluded(m.Id, (string)it.Tag!, !on);
                    changed = true;
                    FillFiles();
                }
                bar.Controls.Add(Secondary("Tick shown", 96, () => { SetShown(true); return Task.CompletedTask; }));
                bar.Controls.Add(Secondary("Untick shown", 106, () => { SetShown(false); return Task.CompletedTask; }));
                files.ItemChecked += (s, e) =>
                {
                    if (filling || e.Item.Tag is not string path) return;
                    prof!.SetExcluded(m.Id, path, !e.Item.Checked);
                    e.Item.ForeColor = !e.Item.Checked ? Theme.Muted : _stock?.IsStockPath(path) == true ? Theme.Accent : Theme.NavText;
                    changed = true;
                    Count();
                };
                files.KeyDown += (s, e) =>
                {
                    if (e.KeyCode != Keys.Space || files.SelectedItems.Count < 2) return;   // Space on several rows: flip them together
                    bool on = !files.SelectedItems[0].Checked;
                    foreach (ListViewItem it in files.SelectedItems) it.Checked = on;
                    e.Handled = true;
                };
            }
            bar.Controls.Add(counter);
            search.TextChanged += (s, e) => FillFiles();
            FillFiles();

            var foot = new Label
            {
                Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(16, 4, 0, 0), ForeColor = Theme.Muted,
                Text = "Blue = replaces a game file (the original is saved first). White = a new file." + (editable ? " Grey = left out of this profile." : "")
            };
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 0), BackColor = Theme.WindowBack };
            pad.Controls.Add(files);
            f.Controls.Add(pad);
            f.Controls.Add(foot);
            f.Controls.Add(bar);
            f.Controls.Add(head);
            f.ShowDialog(this);

            if (changed && _profiles is not null)
            {
                _profiles.Save();
                Fill(m);
                _ = RefreshPending();
            }
        }

        /// <summary>Shows the Apply bar while the game doesn't match the list yet.</summary>
        private async Task RefreshPending()
        {
            if (_gameDir is null || _stock is null) return;
            string game = _gameDir;
            var lib = _profiles is null ? _library : ProfileStore.Filtered(_library, _profiles.Current);   // the profile's file ticks
            var stock = _stock;
            try
            {
                bool mp = _profiles?.Current.Multiplayer == true;
                var plan = await Task.Run(() => MakePlan(game, lib, Ledger.Load(game), stock, mp));
                int changes = plan.Changes.Count();
                int installers = plan.InstallersToRun.Count;
                string profile = _profiles?.Current.Name ?? "";
                bool otherProfile = _profiles is not null && !string.Equals(_profiles.Deployed, profile, StringComparison.OrdinalIgnoreCase);
                _applyBar.Visible = changes > 0 || installers > 0 || otherProfile;
                _pending.Text = changes > 0 || installers > 0
                    ? $"\"{profile}\" isn't in the game yet: " + (changes > 0 ? $"{changes} file change{(changes == 1 ? "" : "s")}" : "") +
                      (changes > 0 && installers > 0 ? " and " : "") + (installers > 0 ? $"{installers} installer{(installers == 1 ? "" : "s")} to run" : "") +
                      ". Click Deploy."
                    : otherProfile ? $"\"{profile}\" matches the game files. Click Deploy to make it the active profile." : "";
            }
            catch (Exception ex)
            {
                _applyBar.Visible = true;
                _pending.Text = ex.Message;
            }
        }

        // ---------- actions ----------

        private async Task PickZips()
        {
            using var d = new OpenFileDialog { Title = "Add mod", Filter = "Mod archive (*.zip)|*.zip", Multiselect = true };
            if (d.ShowDialog(this) == DialogResult.OK) await AddMods(d.FileNames);
        }

        private async Task PickFolder()
        {
            using var d = new FolderBrowserDialog { Description = "Pick the mod's folder" };
            if (d.ShowDialog(this) == DialogResult.OK) await AddMods([d.SelectedPath]);
        }

        private async Task AddMods(string[] paths)
        {
            if (_busy || _stock is null) return;
            var messages = new List<string>();
            ModPackage? last = null;
            foreach (var path in paths)
            {
                SetBusy(true, $"Adding {Path.GetFileName(path.TrimEnd('\\'))}... (big mods take a minute: every file is copied and checked)");
                var lib = _library;
                var stock = _stock;
                var result = await Task.Run(() => Importer.Import(path, lib, stock, CancellationToken.None));
                if (result.Mod is null)
                {
                    messages.Add($"{Path.GetFileName(path.TrimEnd('\\'))}: {result.Error}");
                    continue;
                }
                var mod = result.Mod;
                mod.Size = mod.Files.Values.Distinct().Sum(sha => new FileInfo(FileStore.PathFor(sha)).Length);
                if (result.Skipped.Count > 0)
                    mod.Notes.Add($"Left out of the download: {string.Join("; ", result.Skipped.Take(8))}" +
                                  (result.Skipped.Count > 8 ? $" and {result.Skipped.Count - 8} more" : "") + ".");
                _library.Add(mod);   // added switched off: nothing happens to the game until it's turned on and applied
                last = mod;
            }
            _library.Save();
            SetBusy(false);
            Fill(last);
            await RefreshPending();
            if (messages.Count > 0)
                MessageBox.Show(this, string.Join("\n\n", messages), "Some mods weren't added", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (last is not null)
                _status.Text = $"Added \"{last.Name}\". It's off: tick its On box, then Deploy.";
        }

        private async void ToggleSelected()
        {
            if (_busy || Selected() is not { } m) return;
            m.Enabled = !m.Enabled;
            _library.Save();
            ProfileChanged();
            Fill(m);
            await RefreshPending();
        }

        private async void MoveSelected(int delta)
        {
            if (_busy || Selected() is not { } m) return;
            _library.Move(m, delta);
            _library.Save();
            ProfileChanged();
            Fill(m);
            await RefreshPending();
        }

        private void RemoveSelected()
        {
            if (_busy || Selected() is not { } m || _gameDir is null) return;
            bool installed;
            try { installed = Ledger.Load(_gameDir).Files.Values.Any(e => e.Owner == m.Id); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Remove"); return; }
            if (installed)
            {
                MessageBox.Show(this, $"\"{m.Name}\" is still in the game. Switch it off and Apply first, then remove it from the list.",
                                "Remove", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, $"Remove \"{m.Name}\" from the list? The game isn't changed. You can add it again later.",
                                "Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _library.Mods.Remove(m);
            _library.Save();
            _profiles?.ForgetMod(m.Id);
            _profiles?.Save();
            if (m.InstallerDir is not null && m.InstallerDir.StartsWith(ToolkitConfig.Dir, StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(m.InstallerDir, true); } catch { /* only the manager's own copy */ }
            Fill();
        }

        private async Task ApplyChanges()
        {
            if (_gameDir is null || _stock is null) return;
            string game = _gameDir;
            var lib = _library;
            var stock = _stock;
            if (_profiles is null) return;
            lib = ProfileStore.Filtered(_library, _profiles.Current);   // the profile's file ticks
            string profileName = _profiles.Current.Name;
            bool mp = _profiles.Current.Multiplayer;
            SetBusy(true, "Checking what needs to change...");
            try
            {
                var ledger = await Task.Run(() => Ledger.Load(game));
                // an installer that runs in this deploy may edit Assembly-CSharp.dll: then the multiplayer patch goes on in a 2nd pass
                var first = await Task.Run(() => Deployer.Plan(game, lib, ledger, stock, CancellationToken.None));
                bool patchNow = mp && first.InstallersToRun.Count == 0;
                var plan = patchNow ? await Task.Run(() => MakePlan(game, lib, ledger, stock, true)) : first;

                if (plan.Problems.Count > 0)
                {
                    MessageBox.Show(this, "Nothing was changed:\n\n• " + string.Join("\n• ", plan.Problems.Take(15)) +
                                          (plan.Problems.Count > 15 ? $"\n• ...and {plan.Problems.Count - 15} more" : ""),
                                    "Can't apply yet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    SetBusy(false, "Not applied.");
                    return;
                }

                var changes = plan.Changes.ToList();
                if (changes.Count == 0 && plan.InstallersToRun.Count == 0
                    && !plan.Steps.Any(s => s.Kind == StepKind.Keep && !ledger.Files.ContainsKey(s.Path)))
                {
                    // no file changes, but the save still switches (two profiles can have the same mods)
                    if (System.Diagnostics.Process.GetProcessesByName("NASCARHeat5").Length > 0)
                        throw new IOException("Close NASCAR Heat 5 before switching profiles (its save is switched too).");
                    await Task.Run(() => SaveSlots.Manager().Deploy(profileName, () => { }));
                    _profiles.Deployed = profileName;
                    _profiles.Save();
                    FillProfiles();
                    SetBusy(false, $"The game already matches \"{profileName}\". It's now the active profile, with its own save.");
                    await RefreshPending();
                    OfferLaunch(profileName);
                    return;
                }

                string Count(StepKind k, string label) => changes.Count(c => c.Kind == k) is var n && n > 0 ? $"• {label}: {n}\n" : "";
                string summary =
                    Count(StepKind.Install, "Install mod files") +
                    Count(StepKind.Restore, "Put back the original file") +
                    Count(StepKind.Remove, "Delete files added by mods that are now off") +
                    (plan.InstallersToRun.Count > 0
                        ? "• Run installer: " + string.Join(", ", plan.InstallersToRun.Select(m => m.Name)) +
                          " (its window opens: click its Install button, then close it)\n"
                        : "") +
                    "• Saves: every profile keeps its own save. The save that's live now is parked and \"" + profileName + "\"'s comes back " +
                    "(a profile used for the first time starts a new save).\n" +
                    (mp ? "• Enable Multiplayer: on (Assembly-CSharp.dll points at the community server)\n" : "") +
                    (plan.Warnings.Count > 0 ? "\nNote:\n• " + string.Join("\n• ", plan.Warnings.Take(8)) + "\n" : "") +
                    "\nEvery file the manager replaces is saved first, so switching a mod off puts it back.\nDeploy now?";
                if (MessageBox.Show(this, summary, $"Deploy \"{profileName}\"", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                {
                    SetBusy(false, "Not deployed.");
                    return;
                }

                var progress = new Progress<(int done, int total, string path)>(p =>
                    _status.Text = p.path.Length > 0 ? $"Applying {p.done + 1} of {p.total}: {Path.GetFileName(p.path)}" : "Finishing...");
                // saves per profile (old manager's SaveProfileManager): park the outgoing save, bring this profile's back, files in
                // between; a failure inside puts the outgoing save back (its journal also survives a crash)
                await Task.Run(() => SaveSlots.Manager().Deploy(profileName, () => Deployer.Apply(game, plan, ledger, progress, CancellationToken.None)));

                // Installer mods last, on top of everything else, one at a time.
                var ran = new List<string>();
                foreach (var mod in plan.InstallersToRun)
                {
                    MessageBox.Show(this, $"The {mod.Name} installer opens next.\n\nClick its Install button, wait until it says it's done, then close it. " +
                                          "The Mod Manager records every file it changes.",
                                    "Run installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    var status = new Progress<string>(t => _status.Text = t);
                    bool Confirm(string question) => (bool)Invoke(() =>
                        MessageBox.Show(this, question, mod.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
                    var result = await Task.Run(() => InstallerCapture.Run(game, mod, ledger, status, Confirm, CancellationToken.None));
                    if (result.Error is not null)
                    {
                        mod.Enabled = false;   // nothing recorded: leave it off so the list matches the game
                        MessageBox.Show(this, $"{mod.Name}: {result.Error}\n\nIt has been switched off.", "Installer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                        ran.Add($"{mod.Name} ({result.Changed} changed, {result.Added} new)");
                    _library.Save();
                }

                // 2nd pass: the multiplayer patch on top of what the installers left
                if (mp && !patchNow)
                {
                    _status.Text = "Enable Multiplayer...";
                    var second = await Task.Run(() => MakePlan(game, lib, ledger, stock, true));
                    if (second.Problems.Count > 0)
                        throw new IOException("Enable Multiplayer wasn't applied: " + string.Join(" ", second.Problems.Take(3)));
                    await Task.Run(() => Deployer.Apply(game, second, ledger, progress, CancellationToken.None));
                }

                // Installer mods that are off and fully put back: forget their run, so switching on runs them fresh.
                foreach (var mod in _library.Mods)
                    InstallerCapture.ForgetIfRemoved(mod, ledger);
                _library.Save();
                _profiles.Deployed = profileName;
                _profiles.Save();
                FillProfiles();
                Fill();

                SetBusy(false, $"Deployed \"{profileName}\": {changes.Count} file changes" + (ran.Count > 0 ? "; ran " + string.Join(", ", ran) : "") + ".");
                await RefreshPending();
                OfferLaunch(profileName);
                return;
            }
            catch (Exception ex)
            {
                SetBusy(false, "Deploy stopped: " + ex.Message);
                MessageBox.Show(this, "Deploy stopped part way:\n\n" + ex.Message +
                                      "\n\nEvery file it already changed is recorded, so pressing Deploy again continues safely.",
                                "Deploy", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            await RefreshPending();
        }

        // ---------- profiles (2026-11-16: the old NH5 Mod Manager's profiles, deploy, multiplayer) ----------

        /// <summary>The library's plan, plus the Enable Multiplayer step on top when the profile has it on.</summary>
        private static ApplyPlan MakePlan(string game, ModLibrary lib, Ledger ledger, StockIndex stock, bool multiplayer)
        {
            if (!multiplayer) return Deployer.Plan(game, lib, ledger, stock, CancellationToken.None);
            var patch = MultiplayerPatch.Build(game, lib, ledger, CancellationToken.None);
            return Deployer.Plan(game, MultiplayerPatch.WithPatch(lib, patch), ledger, stock, CancellationToken.None);
        }

        private void FillProfiles()
        {
            if (_profiles is null) return;
            _filling = true;
            _profileBox.Items.Clear();
            foreach (var p in _profiles.Profiles) _profileBox.Items.Add(p.Name);
            _profileBox.SelectedItem = _profiles.Current.Name;
            _multiplayer.Checked = _profiles.Current.Multiplayer;
            _filling = false;
            _deployedLabel.Text = _profiles.Deployed is null ? "" : $"In the game: {_profiles.Deployed}";
        }

        private void ProfileChanged()
        {
            if (_profiles is null) return;
            ProfileStore.CaptureFromLibrary(_profiles.Current, _library);
            _profiles.Save();
        }

        private async Task SelectProfile()
        {
            if (_filling || _profiles is null || _profileBox.SelectedItem is not string name) return;
            if (_busy) { FillProfiles(); return; }
            _profiles.Selected = name;
            _profiles.Save();
            ProfileStore.ApplyToLibrary(_profiles.Current, _library);
            _library.Save();
            FillProfiles();
            Fill();
            await RefreshPending();
            _status.Text = string.Equals(_profiles.Deployed, name, StringComparison.OrdinalIgnoreCase)
                ? $"\"{name}\" is the profile in the game."
                : $"\"{name}\" selected. The game doesn't change until you click Deploy.";
        }

        private async Task MultiplayerChanged()
        {
            if (_filling || _profiles is null) return;
            _profiles.Current.Multiplayer = _multiplayer.Checked;
            _profiles.Save();
            await RefreshPending();
        }

        private string? AskName(string title, string start)
        {
            using var f = new Form
            {
                Text = title, Size = new Size(380, 160), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false, ShowIcon = false, ShowInTaskbar = false, BackColor = Theme.WindowBack, ForeColor = Theme.NavText
            };
            var box = new TextBox { Text = start, Location = new Point(16, 18), Width = 330, BackColor = Theme.CardBack, ForeColor = Theme.NavText };
            var error = new Label { Location = new Point(16, 48), AutoSize = true, ForeColor = Color.Goldenrod };
            var ok = new Button { Text = "OK", Location = new Point(190, 76), Width = 75, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "Cancel", Location = new Point(271, 76), Width = 75, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                if (ProfileStore.CheckName(box.Text, _profiles!, start.Length > 0 && title.StartsWith("Rename") ? start : null) is { } msg)
                    error.Text = msg;
                else
                    f.DialogResult = DialogResult.OK;
            };
            f.Controls.AddRange([box, error, ok, cancel]);
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            return f.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
        }

        private void NewProfile()
        {
            if (_busy || _profiles is null || AskName("New profile", "") is not { } name) return;
            _profiles.Profiles.Add(new ModProfile { Name = name });   // starts with no mods on, like the old manager
            _profileBox.Items.Add(name);
            _profileBox.SelectedItem = name;   // SelectProfile does the rest
        }

        private void CopyProfile()
        {
            if (_busy || _profiles is null || AskName("Copy profile", _profiles.Current.Name + " copy") is not { } name) return;
            var src = _profiles.Current;
            _profiles.Profiles.Add(new ModProfile { Name = name, On = src.On.ToList(), Multiplayer = src.Multiplayer });
            _profileBox.Items.Add(name);
            _profileBox.SelectedItem = name;
        }

        private void RenameProfile()
        {
            if (_busy || _profiles is null) return;
            var cur = _profiles.Current;
            if (AskName("Rename profile", cur.Name) is not { } name || name == cur.Name) return;
            try
            {
                SaveSlots.Manager().RenameProfile(cur.Name, name);   // its parked save (and the save index) follows the name
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Not renamed: " + ex.Message, "Rename profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.Equals(_profiles.Deployed, cur.Name, StringComparison.OrdinalIgnoreCase)) _profiles.Deployed = name;
            cur.Name = name;
            _profiles.Selected = name;
            _profiles.Save();
            FillProfiles();
        }

        private async void DeleteProfile()
        {
            if (_busy || _profiles is null) return;
            var cur = _profiles.Current;
            if (_profiles.Profiles.Count == 1)
            {
                MessageBox.Show(this, "This is the only profile. Make another one first.", "Delete profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, $"Delete the profile \"{cur.Name}\"? Its mods stay in your mod list and the game isn't changed.",
                                "Delete profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _profiles.Profiles.Remove(cur);
            if (string.Equals(_profiles.Deployed, cur.Name, StringComparison.OrdinalIgnoreCase)) _profiles.Deployed = null;
            _profiles.Selected = _profiles.Profiles[0].Name;
            _profiles.Save();
            ProfileStore.ApplyToLibrary(_profiles.Current, _library);
            _library.Save();
            FillProfiles();
            Fill();
            await RefreshPending();
        }

        /// <summary>
        /// Profile package (.nh5tkprof, a zip): profile.json (name, Enable Multiplayer, mods in order with name / version) and
        /// mods/&lt;n&gt;/&lt;game-relative files&gt;. Import adds mods that aren't in the list yet and the profile.
        /// </summary>
        private async Task ExportProfile()
        {
            if (_busy || _profiles is null) return;
            var prof = _profiles.Current;
            var mods = prof.On.Select(id => _library.Mods.FirstOrDefault(m => m.Id == id)).OfType<ModPackage>().ToList();
            var installers = mods.Where(m => m.IsInstaller).ToList();
            using var d = new SaveFileDialog
            {
                Title = "Export profile", FileName = prof.Name + ".nh5tkprof", Filter = "Toolkit profile (*.nh5tkprof)|*.nh5tkprof|Zip (*.zip)|*.zip"
            };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string target = d.FileName;
            SetBusy(true, $"Exporting \"{prof.Name}\"...");
            try
            {
                await Task.Run(() =>
                {
                    string tmp = target + ".part";
                    if (File.Exists(tmp)) File.Delete(tmp);
                    using (var zip = System.IO.Compression.ZipFile.Open(tmp, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        var meta = new ExportMeta { Name = prof.Name, Multiplayer = prof.Multiplayer };
                        int n = 0;
                        foreach (var m in mods.Where(m => !m.IsInstaller))
                        {
                            string dir = $"mods/{n++}/";
                            meta.Mods.Add(new ExportMod { Folder = dir, Name = m.Name, Version = m.Version });
                            foreach (var (path, sha) in m.Files)
                                zip.CreateEntryFromFile(FileStore.PathFor(sha), dir + path.Replace('\\', '/'));
                        }
                        var entry = zip.CreateEntry("profile.json");
                        using var w = new StreamWriter(entry.Open());
                        w.Write(System.Text.Json.JsonSerializer.Serialize(meta, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    }
                    File.Move(tmp, target, overwrite: true);
                });
                SetBusy(false, $"Exported \"{prof.Name}\" to {Path.GetFileName(target)}." +
                               (installers.Count > 0 ? $" Installer mods aren't included ({string.Join(", ", installers.Select(m => m.Name))}): share their own download." : ""));
            }
            catch (Exception ex)
            {
                SetBusy(false, "Export failed: " + ex.Message);
            }
        }

        private sealed class ExportMeta
        {
            public string Name { get; set; } = "";
            public bool Multiplayer { get; set; }
            public List<ExportMod> Mods { get; set; } = new();
        }

        private sealed class ExportMod
        {
            public string Folder { get; set; } = "";
            public string Name { get; set; } = "";
            public string Version { get; set; } = "";
        }

        /// <summary>Imports a toolkit profile (.nh5tkprof) or an old NH5 Mod Manager profile package (.nh5prof / .zip).</summary>
        private async Task ImportProfile()
        {
            if (_busy || _profiles is null || _stock is null) return;
            using var d = new OpenFileDialog
            {
                Title = "Import profile",
                Filter = "Profile packages (*.nh5tkprof;*.nh5prof;*.zip)|*.nh5tkprof;*.nh5prof;*.zip"
            };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string tmp = Path.Combine(Path.GetTempPath(), $"nh5tk_import_{Guid.NewGuid():N}");
            SetBusy(true, $"Importing {Path.GetFileName(d.FileName)}... (big profiles take a minute: every file is copied and checked)");
            try
            {
                var stock = _stock;
                var lib = _library;
                var (name, multiplayer, folders) = await Task.Run(() =>
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(d.FileName, tmp);
                    var list = new List<(string Dir, string Name, string Version, HashSet<string>? Only)>();
                    string metaPath = Path.Combine(tmp, "profile.json");
                    if (File.Exists(metaPath))
                    {
                        var meta = System.Text.Json.JsonSerializer.Deserialize<ExportMeta>(File.ReadAllText(metaPath)) ?? new();
                        foreach (var m in meta.Mods)
                            list.Add((Path.Combine(tmp, m.Folder.Replace('/', '\\').TrimEnd('\\')), m.Name, m.Version, null));
                        return (meta.Name, meta.Multiplayer, list);
                    }
                    // old manager package: <profile>/ (game-folder mirror) + <profile>.json ("[Asset] rel" ticked files)
                    var dirs = Directory.GetDirectories(tmp);
                    if (dirs.Length == 0) throw new IOException("This isn't a profile package (no profile folder inside).");
                    string pname = Path.GetFileName(dirs[0]);
                    HashSet<string>? only = null;
                    string json = Path.Combine(tmp, pname + ".json");
                    if (File.Exists(json))
                    {
                        var ticked = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(json)) ?? new();
                        only = new HashSet<string>(ticked.Select(t => t.Contains("] ") ? t[(t.IndexOf("] ") + 2)..] : t), StringComparer.OrdinalIgnoreCase);
                    }
                    foreach (var sub in Directory.GetDirectories(dirs[0]).Where(x => Path.GetFileName(x).Equals("Save", StringComparison.OrdinalIgnoreCase)).ToList())
                        Directory.Delete(sub, true);   // never a save in a mod package
                    list.Add((dirs[0], pname, "", only));
                    return (pname, false, list);
                });

                var on = new List<string>();
                var messages = new List<string>();
                foreach (var (dir, modName, version, only) in folders)
                {
                    if (!Directory.Exists(dir)) continue;
                    var result = await Task.Run(() => Importer.Import(dir, lib, stock, CancellationToken.None));
                    if (result.Mod is null) { messages.Add($"{modName}: {result.Error}"); continue; }
                    var mod = result.Mod;
                    if (only is not null)
                        foreach (var path in mod.Files.Keys.Where(k => !only.Contains(k)).ToList()) mod.Files.Remove(path);
                    mod.Name = modName;
                    mod.Version = version;
                    // the same mod already in the list (same files): use that one
                    var same = _library.Mods.FirstOrDefault(m => m.Files.Count == mod.Files.Count &&
                        mod.Files.All(f => m.Files.TryGetValue(f.Key, out var sha) && sha.Equals(f.Value, StringComparison.OrdinalIgnoreCase)));
                    if (same is not null) { on.Add(same.Id); continue; }
                    mod.Size = mod.Files.Values.Distinct().Sum(sha => new FileInfo(FileStore.PathFor(sha)).Length);
                    _library.Add(mod);
                    on.Add(mod.Id);
                }
                _library.Save();

                string newName = name;
                for (int i = 2; ProfileStore.CheckName(newName, _profiles) is not null; i++) newName = $"{name} ({i})";
                _profiles.Profiles.Add(new ModProfile { Name = newName, On = on, Multiplayer = multiplayer });
                _profiles.Save();
                SetBusy(false);
                _profileBox.Items.Add(newName);
                _profileBox.SelectedItem = newName;
                _status.Text = $"Imported profile \"{newName}\" ({on.Count} mods). Click Deploy to put it in the game.";
                if (messages.Count > 0)
                    MessageBox.Show(this, string.Join("\n\n", messages), "Some mods weren't added", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                SetBusy(false, "Import failed: " + ex.Message);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        /// <summary>Profile menu > Import from old Mod Manager: pick old profiles, import them, adopt what the old manager deployed.</summary>
        private async Task ImportOldManager()
        {
            if (_busy || _profiles is null || _gameDir is null) return;
            if (!OldManagerImport.Available)
            {
                MessageBox.Show(this, "The old NASCAR Heat 5 Mod Manager's Profiles folder wasn't found.", "Import from old Mod Manager",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SetBusy(true, "Reading the old Mod Manager's profiles...");
            var old = await Task.Run(OldManagerImport.List);
            string live = OldManagerImport.LiveProfile();
            SetBusy(false, "");

            using var f = new Form
            {
                Text = "Import from old Mod Manager", Size = new Size(640, 520), StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, ShowIcon = false, ShowInTaskbar = false, BackColor = Theme.WindowBack, ForeColor = Theme.NavText
            };
            var intro = new Label
            {
                Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(600, 0), Padding = new Padding(14, 12, 14, 8), ForeColor = Theme.BodyText,
                Text = "Each profile you tick becomes a toolkit profile with the same name and one mod holding its files. A profile keeps its " +
                       "parked save (saves are matched by name). The files the old manager put in the game are taken over as they are: " +
                       "nothing in the game changes now. Files are copied into the toolkit's store (the same file is stored once)."
            };
            var list = new ListView { Dock = DockStyle.Fill, CheckBoxes = true, View = View.Details, FullRowSelect = true };
            list.Columns.Add("Profile", 230);
            list.Columns.Add("Files", 70, HorizontalAlignment.Right);
            list.Columns.Add("Size", 80, HorizontalAlignment.Right);
            list.Columns.Add("Save", 180);
            foreach (var op in old)
            {
                var it = new ListViewItem([op.Name, op.Files.Count.ToString(), ListStyle.FormatSize(op.Bytes),
                    op.Name.Equals(live, StringComparison.OrdinalIgnoreCase) ? "live in the game now" : op.HasSave ? "parked save" : ""])
                { Tag = op, Checked = true };
                list.Items.Add(it);
            }
            var foot = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(14, 6, 0, 0), ForeColor = Theme.Muted };
            void Sum()
            {
                long bytes = list.Items.Cast<ListViewItem>().Where(i => i.Checked).Sum(i => ((OldManagerImport.OldProfile)i.Tag!).Bytes);
                long free = 0;
                try { free = new DriveInfo(Path.GetPathRoot(ToolkitConfig.Dir)!).AvailableFreeSpace; } catch { }
                foot.Text = $"Selected: {ListStyle.FormatSize(bytes)} at most (less when profiles share files)  ·  free on {Path.GetPathRoot(ToolkitConfig.Dir)}: {ListStyle.FormatSize(free)}";
            }
            list.ItemChecked += (s, e) => Sum();
            Sum();
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var go = new Button { Text = "Import", Width = 90, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            buttons.Controls.AddRange([cancel, go]);
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 0) };
            pad.Controls.Add(list);
            f.Controls.Add(pad);
            f.Controls.Add(foot);
            f.Controls.Add(buttons);
            f.Controls.Add(intro);
            f.AcceptButton = go;
            f.CancelButton = cancel;
            if (f.ShowDialog(this) != DialogResult.OK) return;
            var chosen = list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (OldManagerImport.OldProfile)i.Tag!).ToList();
            if (chosen.Count == 0) return;

            string game = _gameDir;
            var lib = _library;
            var profiles = _profiles;
            SetBusy(true, "Importing from the old Mod Manager...");
            try
            {
                var progress = new Progress<string>(t => _status.Text = t);
                var ledger = await Task.Run(() => Ledger.Load(game));
                var result = await Task.Run(() => OldManagerImport.Import(game, chosen, lib, profiles, ledger, progress, CancellationToken.None));
                SetBusy(false);
                await Reload();   // everything again from disk: the list shows exactly what was saved
                _status.Text = $"Imported {result.Imported.Count} profiles from the old Mod Manager.";
                MessageBox.Show(this,
                    "Imported:\n• " + string.Join("\n• ", result.Imported) +
                    (result.Live is not null ? $"\n\n\"{result.Live}\" is the profile in the game; {result.Adopted.Count} files the old manager deployed were taken over." : "") +
                    (result.Notes.Count > 0 ? "\n\nNotes:\n• " + string.Join("\n• ", result.Notes.Distinct().Take(10)) : "") +
                    "\n\nFrom now on, deploy with the toolkit only (the old manager doesn't know what the toolkit changed).",
                    "Import from old Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                SetBusy(false, "Import stopped: " + ex.Message);
                MessageBox.Show(this, "Import stopped: " + ex.Message + "\n\nNothing in the game was changed. Profiles imported before the error stay.",
                                "Import from old Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ChooseSaveFolder()
        {
            using var d = new FolderBrowserDialog
            {
                Description = @"Select the game's SAVE folder (normally C:\Users\<you>\NASCAR Heat 5), not the Steam installation.",
                SelectedPath = SaveSlots.LiveSaveDir, UseDescriptionForTitle = true
            };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (SaveSlots.Unsafe(d.SelectedPath) is { } why)
            {
                MessageBox.Show(this, why, "Game save folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ToolkitConfig.SetPath("saveDir", Path.TrimEndingDirectorySeparator(Path.GetFullPath(d.SelectedPath)));
            _status.Text = "Game save folder: " + SaveSlots.LiveSaveDir;
        }

        private async void PickGameDir()
        {
            if (_busy) return;
            using var d = new FolderBrowserDialog { Description = "Pick the NASCAR Heat 5 folder (the one with NASCARHeat5.exe)" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (!GameScanner.IsGameDir(d.SelectedPath))
            {
                MessageBox.Show(this, "NASCARHeat5.exe isn't in that folder.", "Game folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ToolkitConfig.SetPath("gameDir", d.SelectedPath);
            await Reload();
        }

        private void OfferLaunch(string profile)
        {
            if (MessageBox.Show(this, $"\"{profile}\" is in the game. Start NASCAR Heat 5 now?", "Deploy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("steam://rungameid/1265860") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't start the game: " + ex.Message, "Deploy"); }
        }
    }
}
