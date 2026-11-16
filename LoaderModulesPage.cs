using NH5Toolkit.ModCore;

namespace NH5Toolkit
{
    /// <summary>
    /// "Loader Modules": switch each H5 Track Loader module (IndyCar, NASCAR '14, the COT series, day/night ...) on or off
    /// without uninstalling it. Writes H5TrackLoader\mods.txt, which the loader reads at the next game start.
    /// </summary>
    public class LoaderModulesPage : UserControl
    {
        private readonly ListView _list = new();
        private readonly Label _status = new();
        private readonly Label _loader = new();
        private string? _gameDir;

        public LoaderModulesPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.WindowBack;

            var top = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Theme.WindowBack };
            top.Controls.Add(new Label
            {
                Text = "Switch H5 Track Loader mods off without uninstalling them. Click the On box (or double-click a row). " +
                       "Changes take effect the next time the game starts.",
                ForeColor = Theme.BodyText,
                AutoSize = false,
                Size = new Size(900, 22),
                Location = new Point(31, 8),
                AutoEllipsis = true
            });

            var row = new FlowLayoutPanel { Location = new Point(26, 40), AutoSize = true, WrapContents = false, BackColor = Theme.WindowBack };
            row.Controls.Add(Btn("On / off", 80, ToggleSelected));
            row.Controls.Add(Btn("All on", 70, () => SetAll(true)));
            row.Controls.Add(Btn("Refresh", 80, Reload));
            row.Controls.Add(Btn("Open mods.txt folder", 150, OpenFolder));
            top.Controls.Add(row);

            _loader.Location = new Point(31, 82);
            _loader.AutoSize = true;
            _loader.ForeColor = Theme.BodyText;
            top.Controls.Add(_loader);

            _list.Dock = DockStyle.Fill;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.Columns.Add("On", 44);
            _list.Columns.Add("Module", 220);
            _list.Columns.Add("Package", 110);
            _list.Columns.Add("What it does", 400);
            ListStyle.Apply(_list, fillColumn: 3);
            _list.MouseClick += (s, e) =>
            {
                var hit = _list.HitTest(e.Location);
                if (hit.Item is not null && hit.SubItem is not null && hit.Item.SubItems.IndexOf(hit.SubItem) == 0)
                    ToggleSelected();
            };
            _list.DoubleClick += (s, e) => ToggleSelected();

            _status.Dock = DockStyle.Bottom;
            _status.Height = 28;
            _status.ForeColor = Theme.BodyText;
            _status.TextAlign = ContentAlignment.MiddleLeft;

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 0, 28, 12), BackColor = Theme.WindowBack };
            host.Controls.Add(_list);
            host.Controls.Add(_status);
            Controls.Add(host);
            Controls.Add(top);

            Load += (s, e) => Reload();
        }

        private Button Btn(string text, int width, Action click)
        {
            var b = new Button
            {
                Text = text, Size = new Size(width, 34), Margin = new Padding(2, 0, 2, 0),
                FlatStyle = FlatStyle.Flat, BackColor = Theme.WindowBack, ForeColor = Theme.Accent,
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Theme.CardBorder;
            b.FlatAppearance.BorderSize = 1;
            b.Click += (s, e) => click();
            return b;
        }

        private void Reload()
        {
            _list.Items.Clear();
            _gameDir = GameScanner.FindGameDir();
            if (_gameDir is null)
            {
                _loader.Text = "Game folder not found. Set it under Advanced > Game Check.";
                return;
            }
            if (!LoaderModules.LoaderInstalled(_gameDir))
            {
                _loader.Text = "The H5 Track Loader is not installed (no H5TrackLoader\\H5TrackLoader.dll), so there is nothing to switch.";
                return;
            }
            var ver = LoaderModules.LoaderVersion(_gameDir);
            _loader.Text = LoaderModules.LoaderKnowsSwitch(_gameDir)
                ? $"Loader build {ver}. Settings file: H5TrackLoader\\mods.txt"
                : $"The installed loader ({ver?.ToString() ?? "unknown"}) is older than the module switch and ignores mods.txt. " +
                  "Install a package built on 2026-11-15 or later.";
            _loader.ForeColor = LoaderModules.LoaderKnowsSwitch(_gameDir) ? Theme.BodyText : Color.Goldenrod;

            Dictionary<string, bool> map;
            try { map = LoaderModules.Read(_gameDir); }
            catch (Exception ex) { _status.Text = "mods.txt could not be read: " + ex.Message; return; }

            foreach (var m in LoaderModules.All)
            {
                bool installed = LoaderModules.Installed(_gameDir, m);
                bool on = LoaderModules.IsOn(map, m.Key);
                var item = new ListViewItem(on ? "✔" : "")
                {
                    Tag = m,
                    ForeColor = installed ? Theme.NavText : Theme.FadedText
                };
                item.SubItems.Add(m.Name);
                item.SubItems.Add(m.Signs.Length == 0 ? "built in" : installed ? "installed" : "not installed");
                item.SubItems.Add(m.What);
                _list.Items.Add(item);
            }
            ListStyle.Fit(_list, 3);
            int off = LoaderModules.All.Count(m => !LoaderModules.IsOn(map, m.Key));
            _status.Text = off == 0 ? "Every module is on." : $"{off} module(s) off.";
        }

        private bool CanWrite()
        {
            if (_gameDir is null) return false;
            if (!LoaderModules.LoaderInstalled(_gameDir))
            {
                _status.Text = "The H5 Track Loader is not installed.";
                return false;
            }
            return true;
        }

        private void ToggleSelected()
        {
            if (!CanWrite() || _list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not LoaderModules.Module m) return;
            int index = _list.SelectedIndices[0];
            bool on = !LoaderModules.IsOn(LoaderModules.Read(_gameDir!), m.Key);
            Write(() => LoaderModules.Set(_gameDir!, m.Key, on), $"{m.Name} switched {(on ? "on" : "off")}.");
            if (index < _list.Items.Count) { _list.Items[index].Selected = true; _list.Items[index].Focused = true; }
        }

        private void SetAll(bool on)
        {
            if (!CanWrite()) return;
            Write(() => { foreach (var m in LoaderModules.All) LoaderModules.Set(_gameDir!, m.Key, on); }, "Every module switched on.");
        }

        private void Write(Action change, string done)
        {
            try
            {
                change();
                Reload();
                _status.Text = done + (LoaderModules.GameRunning()
                    ? " The game is running: restart it to apply."
                    : " Applies the next time the game starts.");
            }
            catch (Exception ex)
            {
                _status.Text = "mods.txt not written: " + ex.Message;
            }
        }

        private void OpenFolder()
        {
            if (_gameDir is null) return;
            var dir = LoaderModules.LoaderDir(_gameDir);
            if (Directory.Exists(dir))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
    }
}
