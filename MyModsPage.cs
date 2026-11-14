using NH5Toolkit.ModCore;

namespace NH5Toolkit
{
    /// <summary>
    /// "My Mods": the mods the toolkit manages. Add a mod (zip or folder, or drop it on the page),
    /// switch mods on and off, order them (the higher one wins a file both ship), then Apply.
    /// Nothing in the game changes until Apply, and Apply shows exactly what it will do first.
    /// </summary>
    public class MyModsPage : UserControl
    {
        private readonly ListView _list = new();
        private readonly ListView _files = new();
        private readonly Label _status = new();
        private readonly Label _pending = new();
        private readonly Label _detailTitle = new();
        private readonly Label _notes = new();
        private readonly SplitContainer _split = new();
        private readonly List<Button> _busyButtons = new();
        private readonly Button _apply;
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
                Text = "Add mods, switch them on or off, put them in order (higher wins when two mods change the same file), then Apply. " +
                       "Drop a .zip or folder anywhere here.",
                ForeColor = Theme.BodyText,
                AutoSize = false,
                Size = new Size(900, 22),
                Location = new Point(31, 8),
                AutoEllipsis = true
            });

            var row = new FlowLayoutPanel
            {
                Location = new Point(26, 40),
                AutoSize = true,
                WrapContents = false,
                BackColor = Theme.WindowBack
            };
            _apply = Primary("Apply changes", 140, async () => await ApplyChanges());
            row.Controls.Add(_apply);
            row.Controls.Add(Secondary("Add .zip...", 96, async () => await PickZips()));
            row.Controls.Add(Secondary("Add folder...", 104, async () => await PickFolder()));
            row.Controls.Add(Secondary("On / off", 80, () => { ToggleSelected(); return Task.CompletedTask; }));
            row.Controls.Add(Secondary("▲", 40, () => { MoveSelected(-1); return Task.CompletedTask; }));
            row.Controls.Add(Secondary("▼", 40, () => { MoveSelected(+1); return Task.CompletedTask; }));
            row.Controls.Add(Secondary("Remove", 80, () => { RemoveSelected(); return Task.CompletedTask; }));
            top.Controls.Add(row);

            _status.Location = new Point(31, 82);
            _status.AutoSize = true;
            _status.ForeColor = Theme.BodyText;
            top.Controls.Add(_status);

            _pending.AutoSize = true;
            _pending.ForeColor = Color.Goldenrod;
            _pending.Font = new Font("Segoe UI Semibold", 9.5f);
            top.Controls.Add(_pending);
            top.Resize += (s, e) => _pending.Location = new Point(Math.Max(400, top.Width - _pending.Width - 30), 82);

            _list.Dock = DockStyle.Fill;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.Columns.Add("On", 44);
            _list.Columns.Add("Mod", 280);
            _list.Columns.Add("Type", 120);
            _list.Columns.Add("Files", 60, HorizontalAlignment.Right);
            _list.Columns.Add("Size", 84, HorizontalAlignment.Right);
            _list.Columns.Add("Came as", 260);
            ListStyle.Apply(_list, fillColumn: 5);
            _list.SelectedIndexChanged += (s, e) => ShowDetails();
            _list.MouseClick += (s, e) =>
            {
                var hit = _list.HitTest(e.Location);
                if (hit.Item is not null && hit.SubItem is not null && hit.Item.SubItems.IndexOf(hit.SubItem) == 0)
                    ToggleSelected();
            };
            _list.DoubleClick += (s, e) => ToggleSelected();

            _detailTitle.Dock = DockStyle.Top;
            _detailTitle.Height = 30;
            _detailTitle.Font = new Font("Segoe UI Semibold", 11f);
            _detailTitle.ForeColor = Theme.HeadingText;
            _detailTitle.TextAlign = ContentAlignment.BottomLeft;

            _notes.Dock = DockStyle.Top;
            _notes.AutoSize = true;
            _notes.Padding = new Padding(0, 4, 0, 10);
            _notes.ForeColor = Theme.BodyText;

            _files.Dock = DockStyle.Fill;
            _files.Columns.Add("File in the game folder", 600);
            _files.Columns.Add("Size", 90, HorizontalAlignment.Right);
            ListStyle.Apply(_files, fillColumn: 0);

            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Horizontal;
            _split.SplitterWidth = 8;
            _split.Panel1MinSize = 60;
            _split.Panel2MinSize = 60;
            _split.BackColor = _split.Panel1.BackColor = _split.Panel2.BackColor = Theme.WindowBack;
            _split.Panel1.Controls.Add(_list);
            _split.Panel2.Controls.Add(_files);
            _split.Panel2.Controls.Add(_notes);
            _split.Panel2.Controls.Add(_detailTitle);
            _split.Panel2.Resize += (s, e) => _notes.MaximumSize = new Size(Math.Max(200, _split.Panel2.ClientSize.Width), 0);

            bool placed = false;
            _split.Resize += (s, e) =>
            {
                if (placed) return;
                int max = _split.Height - _split.Panel2MinSize - _split.SplitterWidth;
                if (max <= _split.Panel1MinSize) return;
                _split.SplitterDistance = Math.Clamp(_split.Height * 50 / 100, _split.Panel1MinSize, max);
                placed = true;
            };

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 0, 28, 20), BackColor = Theme.WindowBack };
            host.Controls.Add(_split);
            Controls.Add(host);
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
                SetBusy(true, "Game folder not found. Set it on the Game Check tab.");
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

        private void Fill(ModPackage? select = null)
        {
            select ??= Selected();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var m in _library.Mods.OrderBy(m => m.Priority))
            {
                var item = new ListViewItem(
                [
                    m.Enabled ? "  ●" : "  ○",
                    m.Name + (m.Version.Length > 0 ? "  " + m.Version : ""),
                    m.IsInstaller ? (m.Captured ? "Installer (ran)" : "Installer") : _stock is null ? "" : m.KindFor(_stock),
                    m.Files.Count.ToString(),
                    ListStyle.FormatSize(m.Size),
                    m.Format
                ]) { Tag = m };
                item.ForeColor = m.Enabled ? Theme.NavText : Theme.Muted;
                _list.Items.Add(item);
                if (m == select) item.Selected = true;
            }
            _list.EndUpdate();
            ListStyle.Fit(_list, 5);

            int on = _library.Mods.Count(m => m.Enabled);
            if (!_busy)
                _status.Text = _library.Mods.Count == 0
                    ? "No mods yet. Add a .zip or a folder, or drop one here."
                    : $"{_library.Mods.Count} mods, {on} on";
            if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
            ShowDetails();
        }

        private ModPackage? Selected() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ModPackage : null;

        private void ShowDetails()
        {
            var m = Selected();
            _detailTitle.Text = m?.Name ?? "";
            var notes = new List<string>();
            if (m is not null)
            {
                notes.Add($"From {m.Source}  ·  added {m.Imported:yyyy-MM-dd}");
                if (m.IsInstaller && !m.Captured)
                    notes.Add("Not run yet. Switch it on and Apply: its installer opens, and the files it changes show up here.");
                notes.AddRange(m.Notes);
            }
            _notes.Text = string.Join(Environment.NewLine, notes.Select(n => "•  " + n));

            _files.BeginUpdate();
            _files.Items.Clear();
            if (m is not null)
                foreach (var (path, sha) in m.Files.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                {
                    long size = FileStore.Has(sha) ? new FileInfo(FileStore.PathFor(sha)).Length : 0;
                    var item = new ListViewItem([path, ListStyle.FormatSize(size)]);
                    item.ForeColor = _stock?.IsStockPath(path) == true ? Theme.Accent : Theme.NavText;   // blue = replaces a stock file
                    _files.Items.Add(item);
                }
            _files.EndUpdate();
            ListStyle.Fit(_files, 0);
        }

        /// <summary>Shows "changes not applied" when the game doesn't match the list yet.</summary>
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
                _pending.Text = changes > 0 ? $"{changes} file changes not applied yet" : "";
                _apply.Text = changes > 0 ? $"Apply changes ({changes})" : "Apply changes";
            }
            catch (Exception ex)
            {
                _pending.Text = ex.Message;
            }
            _pending.Left = Math.Max(400, _pending.Parent!.Width - _pending.Width - 30);
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
                _status.Text = $"Added \"{last.Name}\". It's off: switch it on, then Apply.";
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
