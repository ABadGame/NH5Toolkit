using NH5Toolkit.ModCore;

namespace NH5Toolkit
{
    /// <summary>
    /// "Game Check": a read-only look at what is in the game folder right now, file by file:
    /// stock, replaced by a mod, added by a mod, or missing. First building block of the mod manager.
    /// </summary>
    public class GameCheckPage : UserControl
    {
        private readonly Label _gameDirValue = new();
        private readonly Label _stockValue = new();
        private readonly Label _summary = new();
        private readonly Label _intro = new();
        private readonly LinkLabel _change = new();
        private readonly Button _scanButton;
        private readonly Button _cancelButton;
        private readonly Button _copyButton;
        private readonly ProgressBar _progress = new();
        private readonly FlowLayoutPanel _filter = new();
        private int _filterIndex;   // 0 changes, 1 replaced, 2 added, 3 missing, 4 all
        private readonly ListView _list = new();
        private string? _gameDir;
        private ScanResult? _result;
        private CancellationTokenSource? _cts;

        public GameCheckPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.WindowBack;

            var top = new Panel { Dock = DockStyle.Top, Height = 192, BackColor = Theme.WindowBack };

            // No page title: this sits under the Mod Manager's "Game Check" tab.
            _intro.Text = "Shows which game files are stock, replaced by a mod, or added by a mod. " +
                          "Read-only: nothing in the game is changed.";
            _intro.ForeColor = Theme.BodyText;
            _intro.Location = new Point(31, 8);
            _intro.Height = 22;
            _intro.AutoEllipsis = true;
            top.Controls.Add(_intro);

            top.Controls.Add(Caption("Game folder", 40));
            _gameDirValue.Location = new Point(130, 40);
            _gameDirValue.Height = 22;
            _gameDirValue.ForeColor = Theme.BodyText;
            _gameDirValue.AutoEllipsis = true;
            top.Controls.Add(_gameDirValue);

            _change.Text = "Change...";
            _change.AutoSize = true;
            _change.Location = new Point(0, 40);
            _change.LinkColor = Theme.Accent;
            _change.ActiveLinkColor = Theme.AccentHover;
            _change.LinkBehavior = LinkBehavior.HoverUnderline;
            _change.LinkClicked += (s, e) => PickGameDir();
            top.Controls.Add(_change);

            top.Controls.Add(Caption("Stock copy", 64));
            _stockValue.Location = new Point(130, 64);
            _stockValue.Height = 22;
            _stockValue.ForeColor = Theme.BodyText;
            _stockValue.AutoEllipsis = true;
            top.Controls.Add(_stockValue);

            _scanButton = MakeButton("Scan game", Theme.Accent, Color.White, 130, new Point(28, 96));
            _scanButton.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            _scanButton.Click += async (s, e) => await RunScan();
            top.Controls.Add(_scanButton);

            _cancelButton = MakeButton("Cancel", Theme.WindowBack, Theme.Accent, 90, new Point(166, 96));
            _cancelButton.FlatAppearance.BorderColor = Theme.CardBorder;
            _cancelButton.FlatAppearance.BorderSize = 1;
            _cancelButton.Visible = false;
            _cancelButton.Click += (s, e) => _cts?.Cancel();
            top.Controls.Add(_cancelButton);

            // Hidden until there is a report to copy (a disabled flat button is unreadable in dark mode).
            _copyButton = MakeButton("Copy report", Theme.WindowBack, Theme.Accent, 110, new Point(166, 96));
            _copyButton.FlatAppearance.BorderColor = Theme.CardBorder;
            _copyButton.FlatAppearance.BorderSize = 1;
            _copyButton.Visible = false;
            _copyButton.Click += (s, e) => CopyReport();
            top.Controls.Add(_copyButton);

            _progress.Location = new Point(290, 106);
            _progress.Size = new Size(300, 18);
            _progress.Visible = false;
            top.Controls.Add(_progress);

            _summary.Location = new Point(31, 144);
            _summary.Height = 40;
            _summary.ForeColor = Theme.BodyText;
            top.Controls.Add(_summary);

            // Filter as a row of toggle buttons: a ComboBox's arrow stays white in dark mode.
            _filter.AutoSize = true;
            _filter.WrapContents = false;
            _filter.FlowDirection = FlowDirection.LeftToRight;
            _filter.BackColor = Theme.WindowBack;
            _filter.Location = new Point(0, 152);
            string[] names = ["Changes", "Replaced", "Added", "Missing", "All"];
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var b = new Button
                {
                    Text = names[i],
                    AutoSize = true,
                    Height = 28,
                    Margin = new Padding(2, 0, 2, 0),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    UseVisualStyleBackColor = false
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += (s, e) => { _filterIndex = index; PaintFilter(); FillList(); };
                _filter.Controls.Add(b);
            }
            PaintFilter();
            top.Controls.Add(_filter);

            top.Resize += (s, e) => LayoutTop(top.ClientSize.Width);

            _list.Dock = DockStyle.Fill;
            _list.Columns.Add("Status", 90);
            _list.Columns.Add("File", 420);
            _list.Columns.Add("Size", 90, HorizontalAlignment.Right);
            _list.Columns.Add("Probably from", 240);
            ListStyle.Apply(_list, fillColumn: 1);   // themed header, dark scrollbar, File column fills

            var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 0, 28, 20), BackColor = Theme.WindowBack };
            listHost.Controls.Add(_list);

            Controls.Add(listHost);
            Controls.Add(top);

            _gameDir = GameScanner.FindGameDir();
            RefreshLabels();
        }

        /// <summary>Stretches the path labels and puts Change... and the filter on the right edge.</summary>
        private void LayoutTop(int width)
        {
            int right = width - 28;
            _intro.Width = Math.Max(100, right - _intro.Left);
            _change.Left = right - _change.Width;
            _gameDirValue.Width = Math.Max(100, _change.Left - 12 - _gameDirValue.Left);
            _stockValue.Width = Math.Max(100, right - _stockValue.Left);
            _filter.Left = right - _filter.Width;
            _summary.Width = Math.Max(100, _filter.Left - 16 - _summary.Left);
        }

        private void PaintFilter()
        {
            for (int i = 0; i < _filter.Controls.Count; i++)
            {
                var b = (Button)_filter.Controls[i];
                bool on = i == _filterIndex;
                b.BackColor = on ? Theme.NavActiveBack : Theme.WindowBack;
                b.ForeColor = on ? Theme.NavActiveText : Theme.BodyText;
                b.Font = new Font("Segoe UI", 9.5f, on ? FontStyle.Bold : FontStyle.Regular);
            }
        }

        private static Label Caption(string text, int y) => new()
        {
            Text = text,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Theme.HeadingText,
            AutoSize = true,
            Location = new Point(28, y)
        };

        private static Button MakeButton(string text, Color back, Color fore, int width, Point location)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(width, 38),
                Location = location,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void RefreshLabels()
        {
            _gameDirValue.Text = _gameDir ?? "Not found - click Change... and pick the folder with NASCARHeat5.exe";
            _stockValue.Text = Directory.Exists(GameScanner.OldManagerStockDir)
                ? $"{GameScanner.OldManagerStockDir}  (old Mod Manager's Vanilla_Backup)"
                : "No stock copy found - every file will show as Added";
            _scanButton.Enabled = _gameDir is not null;
        }

        private void PickGameDir()
        {
            using var dialog = new FolderBrowserDialog { Description = "Pick the NASCAR Heat 5 folder (the one with NASCARHeat5.exe)" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            if (!GameScanner.IsGameDir(dialog.SelectedPath))
            {
                MessageBox.Show("That folder has no NASCARHeat5.exe.", "Game Check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _gameDir = dialog.SelectedPath;
            ToolkitConfig.SetPath("gameDir", _gameDir);
            RefreshLabels();
        }

        private async Task RunScan()
        {
            if (_gameDir is null) return;

            _cts = new CancellationTokenSource();
            _scanButton.Enabled = false;
            _copyButton.Visible = false;
            _cancelButton.Visible = true;
            _progress.Visible = true;
            _progress.Value = 0;
            _summary.Text = "Scanning... the first scan reads every file once (several minutes), later scans are quick.";

            var progress = new Progress<(long done, long total, string file)>(p =>
            {
                _progress.Value = p.total == 0 ? 100 : (int)(p.done * 100 / p.total);
                if (p.file.Length > 0) _summary.Text = $"Checking {Path.GetFileName(p.file)}  ({p.done / 1_000_000_000.0:0.0} of {p.total / 1_000_000_000.0:0.0} GB)";
            });

            string gameDir = _gameDir;
            try
            {
                _result = await Task.Run(() => GameScanner.Scan(gameDir, GameScanner.OldManagerStockDir, progress, _cts.Token));
                string saved = GameScanner.SaveReport(_result);
                _summary.Text =
                    $"{_result.Count(FileState.Stock)} stock   ·   {_result.Count(FileState.Replaced)} replaced   ·   " +
                    $"{_result.Count(FileState.Added)} added   ·   {_result.Count(FileState.Missing)} missing\nSaved to {saved}";
                FillList();
                _copyButton.Enabled = true;
            }
            catch (OperationCanceledException)
            {
                _summary.Text = "Scan cancelled. Files already checked are remembered, so the next scan picks up faster.";
            }
            catch (Exception ex)
            {
                _summary.Text = "Scan failed: " + ex.Message;
            }
            finally
            {
                _cancelButton.Visible = false;
                _copyButton.Visible = _result is not null;
                _progress.Visible = false;
                _scanButton.Enabled = true;
                _cts.Dispose();
                _cts = null;
            }
        }

        private IEnumerable<ScannedFile> Filtered() => _result is null ? [] : _filterIndex switch
        {
            0 => _result.Files.Where(f => f.State != FileState.Stock),
            1 => _result.Files.Where(f => f.State == FileState.Replaced),
            2 => _result.Files.Where(f => f.State == FileState.Added),
            3 => _result.Files.Where(f => f.State == FileState.Missing),
            _ => _result.Files
        };

        private void FillList()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var f in Filtered())
            {
                var item = new ListViewItem([f.State.ToString(), f.Path, FormatSize(f.Size), f.Owner]);
                item.ForeColor = f.State switch
                {
                    FileState.Replaced => Theme.Accent,
                    FileState.Missing => Color.IndianRed,
                    FileState.Stock => Theme.Muted,
                    _ => Theme.NavText
                };
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            ListStyle.Fit(_list, 1);
        }

        private void CopyReport()
        {
            if (_result is null) return;
            var lines = new List<string>
            {
                $"NH5 Toolkit game check, {_result.ScannedAt:yyyy-MM-dd HH:mm}",
                $"Game: {_result.GameDir}",
                $"Stock copy: {_result.StockDir}",
                _summary.Text.Split('\n')[0],
                ""
            };
            lines.AddRange(_result.Files.Where(f => f.State != FileState.Stock)
                                        .Select(f => $"{f.State,-9} {f.Path}  ({FormatSize(f.Size)})  {f.Owner}"));
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            MessageBox.Show("Report copied to the clipboard.", "Game Check", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string FormatSize(long bytes) => ListStyle.FormatSize(bytes);
    }
}
