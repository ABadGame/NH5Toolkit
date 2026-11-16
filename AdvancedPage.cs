namespace NH5Toolkit
{
    /// <summary>
    /// Mod Manager > "Advanced" (2026-11-16): the two read-only scan tools that were their own tabs, "In the Game" (what's in the game
    /// folder, grouped into mods) and "Game Check" (stock / replaced / added files, and the game folder setting), behind one tab
    /// with a small switch at the top. Neither one changes anything in the game.
    /// </summary>
    public class AdvancedPage : UserControl
    {
        private static string _last = "mods";   // remembered while the hub is open

        private readonly Panel _body = new() { Dock = DockStyle.Fill };
        private readonly Dictionary<string, Button> _buttons = new();

        private static readonly (string Key, string Text, Func<UserControl> Make)[] Tools =
        [
            ("mods", "What's in the game", () => new ModsPage()),
            ("gamecheck", "Game Check (files + game folder)", () => new GameCheckPage()),
        ];

        public AdvancedPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.WindowBack;
            _body.BackColor = Theme.WindowBack;

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 40, Padding = new Padding(26, 4, 0, 0), WrapContents = false, BackColor = Theme.WindowBack
            };
            foreach (var (key, text, _) in Tools)
            {
                var b = new Button
                {
                    Text = text, AutoSize = true, Height = 30, Padding = new Padding(8, 0, 8, 0), Margin = new Padding(2, 0, 6, 0),
                    FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
                };
                b.FlatAppearance.BorderColor = Theme.CardBorder;
                b.Click += (s, e) => Show(key);
                _buttons[key] = b;
                bar.Controls.Add(b);
            }

            Controls.Add(_body);
            Controls.Add(bar);
            Show(Tools.Any(t => t.Key == _last) ? _last : Tools[0].Key);
        }

        private void Show(string key)
        {
            var tool = Tools.First(t => t.Key == key);
            var old = _body.Controls.Cast<Control>().ToArray();
            _body.Controls.Clear();
            foreach (var c in old) c.Dispose();
            _body.Controls.Add(tool.Make());
            _last = key;
            foreach (var (k, b) in _buttons)
            {
                bool active = k == key;
                b.BackColor = active ? Theme.NavActiveBack : Theme.WindowBack;
                b.ForeColor = active ? Theme.NavActiveText : Theme.Accent;
                b.FlatAppearance.BorderSize = active ? 0 : 1;
                b.Font = new Font("Segoe UI", 9.5f, active ? FontStyle.Bold : FontStyle.Regular);
            }
        }
    }
}
