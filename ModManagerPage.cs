namespace NH5Toolkit
{
    /// <summary>
    /// The built-in Mod Manager. A title plus a strip of sections; each section is its own
    /// UserControl shown below the strip. Game Check is the first section; Mods, Profiles and
    /// Deploy are added here as they get built.
    /// </summary>
    public class ModManagerPage : UserControl
    {
        private static string _lastSection = "mymods";   // remembered while the hub is open

        private readonly Panel _body = new() { Dock = DockStyle.Fill };
        private readonly FlowLayoutPanel _strip = new();
        private readonly Dictionary<string, Button> _tabs = new();

        private static readonly (string Key, string Text, Func<UserControl> Make)[] Sections =
        [
            ("mymods", "My Mods", () => new MyModsPage()),
            ("mods", "In the Game", () => new ModsPage()),
            ("gamecheck", "Game Check", () => new GameCheckPage()),
        ];

        public ModManagerPage()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.WindowBack;
            _body.BackColor = Theme.WindowBack;

            var header = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Theme.WindowBack };
            header.Controls.Add(new Label
            {
                Text = "Mod Manager",
                Font = new Font("Segoe UI Semibold", 18f),
                ForeColor = Theme.HeadingText,
                AutoSize = true,
                Location = new Point(28, 18)
            });

            _strip.Location = new Point(24, 62);
            _strip.Size = new Size(700, 38);
            _strip.FlowDirection = FlowDirection.LeftToRight;
            _strip.WrapContents = false;
            _strip.BackColor = Theme.WindowBack;
            foreach (var (key, text, _) in Sections)
            {
                var tab = new Button
                {
                    Text = text,
                    AutoSize = true,
                    Height = 32,
                    Padding = new Padding(10, 0, 10, 0),
                    Margin = new Padding(4, 0, 4, 0),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    UseVisualStyleBackColor = false
                };
                tab.FlatAppearance.BorderSize = 0;
                tab.Click += (s, e) => Show(key);
                _tabs[key] = tab;
                _strip.Controls.Add(tab);
            }
            header.Controls.Add(_strip);

            Controls.Add(_body);
            Controls.Add(header);

            Show(Sections.Any(s => s.Key == _lastSection) ? _lastSection : Sections[0].Key);
        }

        private void Show(string key)
        {
            var section = Sections.First(s => s.Key == key);
            var old = _body.Controls.Cast<Control>().ToArray();
            _body.Controls.Clear();
            foreach (var c in old) c.Dispose();
            _body.Controls.Add(section.Make());
            _lastSection = key;

            foreach (var (k, tab) in _tabs)
            {
                bool active = k == key;
                tab.BackColor = active ? Theme.NavActiveBack : Theme.WindowBack;
                tab.ForeColor = active ? Theme.NavActiveText : Theme.NavText;
                tab.Font = new Font("Segoe UI", 10f, active ? FontStyle.Bold : FontStyle.Regular);
            }
        }
    }
}
