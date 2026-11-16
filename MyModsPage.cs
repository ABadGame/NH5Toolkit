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
            top.Controls.Add(new Label
            {
                Text = "Click the On box to switch a mod on or off, then Apply. To add a mod, use Add mod or drop a .zip or folder here. " +
                       "Right-click a mod for details.",
                ForeColor = Theme.BodyText,
                AutoSize = false,
                Size = new Size(900, 22),
                Location = new Point(31, 8),
                AutoEllipsis = true
            });

            var row = new FlowLayoutPanel { Location = new Point(26, 38), AutoSize = true, WrapContents = false, BackColor = Theme.WindowBack };
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
            _status.AutoSize = true;
            _status.ForeColor = Theme.BodyText;
            top.Controls.Add(_status);

            _orderHint.AutoSize = true;
            _orderHint.ForeColor = Color.Goldenrod;
            _orderHint.Location = new Point(360, 46);
            _orderHint.Visible = false;
            top.Controls.Add(_orderHint);

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

            _rowMenu.Items.Add("Details...", null, (s, e) => ShowDetailsDialog());
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
            _apply = Primary("Apply", 120, async () => await ApplyChanges());
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
            try
            {
                _library = ModLibrary.Load();
            }
            catch (Exception ex)
            {
                SetBusy(true, ex.Message);   // stay locked: never work over a library we couldn't read
                return;
            }
            if (_gameDir is null)
            {
                SetBusy(true, "Game folder not found. Set it under Advanced > Game Check.");
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

        /// <summary>Everything the old details panel showed, in a window: where it came from, notes, every file.</summary>
        private void ShowDetailsDialog()
        {
            if (Selected() is not { } m) return;
            using var f = new Form
            {
                Text = m.Name, Size = new Size(820, 560), StartPosition = FormStartPosition.CenterParent,
                BackColor = Theme.WindowBack, ForeColor = Theme.NavText, MinimizeBox = false, ShowIcon = false, ShowInTaskbar = false
            };
            var notes = new List<string>
            {
                $"From {m.Source}  ·  added {m.Imported:yyyy-MM-dd}  ·  came as {m.Format}",
                $"{m.Files.Count} files, {ListStyle.FormatSize(m.Size)}" + (m.IsInstaller ? "  ·  installer mod" : "")
            };
            if (m.IsInstaller && !m.Captured)
                notes.Add("Not run yet. Switch it on and Apply: its installer opens, and the files it changes show up here.");
            notes.AddRange(m.Notes);
            var head = new Label
            {
                Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 12, 16, 10), ForeColor = Theme.BodyText,
                MaximumSize = new Size(780, 0), Text = string.Join(Environment.NewLine, notes.Select(n => "•  " + n))
            };
            var files = new ListView { Dock = DockStyle.Fill, MultiSelect = false };
            files.Columns.Add("File in the game folder", 600);
            files.Columns.Add("Size", 90, HorizontalAlignment.Right);
            ListStyle.Apply(files, fillColumn: 0);
            foreach (var (path, sha) in m.Files.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                long size = FileStore.Has(sha) ? new FileInfo(FileStore.PathFor(sha)).Length : 0;
                var item = new ListViewItem([path, ListStyle.FormatSize(size)]);
                item.ForeColor = _stock?.IsStockPath(path) == true ? Theme.Accent : Theme.NavText;   // blue = replaces a stock file
                files.Items.Add(item);
            }
            var foot = new Label
            {
                Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(16, 4, 0, 0), ForeColor = Theme.Muted,
                Text = "Blue = replaces a game file (the original is saved first). White = a new file."
            };
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 0), BackColor = Theme.WindowBack };
            pad.Controls.Add(files);
            f.Controls.Add(pad);
            f.Controls.Add(foot);
            f.Controls.Add(head);
            f.ShowDialog(this);
        }

        /// <summary>Shows the Apply bar while the game doesn't match the list yet.</summary>
        private async Task RefreshPending()
        {
            if (_gameDir is null || _stock is null) return;
            string game = _gameDir;
            var lib = _library;
            var stock = _stock;
            try
            {
                var plan = await Task.Run(() => Deployer.Plan(game, lib, Ledger.Load(game), stock, CancellationToken.None));
                int changes = plan.Changes.Count();
                int installers = plan.InstallersToRun.Count;
                _applyBar.Visible = changes > 0 || installers > 0;
                _pending.Text = changes > 0 || installers > 0
                    ? "Not applied yet: " + (changes > 0 ? $"{changes} file change{(changes == 1 ? "" : "s")}" : "") +
                      (changes > 0 && installers > 0 ? " and " : "") + (installers > 0 ? $"{installers} installer{(installers == 1 ? "" : "s")} to run" : "") +
                      ". The game changes only when you click Apply."
                    : "";
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
                _status.Text = $"Added \"{last.Name}\". It's off: tick its On box, then Apply.";
        }

        private async void ToggleSelected()
        {
            if (_busy || Selected() is not { } m) return;
            m.Enabled = !m.Enabled;
            _library.Save();
            Fill(m);
            await RefreshPending();
        }

        private async void MoveSelected(int delta)
        {
            if (_busy || Selected() is not { } m) return;
            _library.Move(m, delta);
            _library.Save();
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
            SetBusy(true, "Checking what needs to change...");
            try
            {
                var ledger = await Task.Run(() => Ledger.Load(game));
                var plan = await Task.Run(() => Deployer.Plan(game, lib, ledger, stock, CancellationToken.None));

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
                    SetBusy(false, "The game already matches your list. Nothing to do.");
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
                    (plan.Warnings.Count > 0 ? "\nNote:\n• " + string.Join("\n• ", plan.Warnings.Take(8)) + "\n" : "") +
                    "\nEvery file the manager replaces is saved first, so switching a mod off puts it back.\nApply now?";
                if (MessageBox.Show(this, summary, "Apply changes", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                {
                    SetBusy(false, "Not applied.");
                    return;
                }

                var progress = new Progress<(int done, int total, string path)>(p =>
                    _status.Text = p.path.Length > 0 ? $"Applying {p.done + 1} of {p.total}: {Path.GetFileName(p.path)}" : "Finishing...");
                await Task.Run(() => Deployer.Apply(game, plan, ledger, progress, CancellationToken.None));

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

                // Installer mods that are off and fully put back: forget their run, so switching on runs them fresh.
                foreach (var mod in _library.Mods)
                    InstallerCapture.ForgetIfRemoved(mod, ledger);
                _library.Save();
                Fill();

                SetBusy(false, $"Applied: {changes.Count} file changes" + (ran.Count > 0 ? "; ran " + string.Join(", ", ran) : "") +
                               ". Start the game to try it.");
            }
            catch (Exception ex)
            {
                SetBusy(false, "Apply stopped: " + ex.Message);
                MessageBox.Show(this, "Apply stopped part way:\n\n" + ex.Message +
                                      "\n\nEvery file it already changed is recorded, so pressing Apply again continues safely.",
                                "Apply changes", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            await RefreshPending();
        }
    }
}
