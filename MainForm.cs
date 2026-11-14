namespace NH5Toolkit
{
    /// <summary>
    /// The hub shell: sidebar navigation on the left + a page area on the right.
    /// "Home" is a card overview; every tool gets its own page keyed by ToolInfo.Key.
    /// Colors come from Theme so dark mode re-themes the whole shell.
    /// </summary>
    public class MainForm : Form
    {
        private const int SidebarWidth = 216;

        private readonly Panel _content = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.WindowBack
        };

        private readonly Panel _sidebar = new();
        private readonly Panel _header = new();
        private readonly FlowLayoutPanel _nav = new();
        private readonly Label _footer = new();
        private readonly Label _wordmark = new();
        private readonly Label _wordmarkSub = new();
        private readonly Button _themeButton = new();
        private readonly Dictionary<string, Button> _navButtons = new();
        private string _activeKey = "home";

        public MainForm()
        {
            Text = "NASCAR Heat 5 Toolkit";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(920, 600);
            MinimumSize = new Size(760, 520);
            Font = new Font("Segoe UI", 10f);

            _sidebar.Dock = DockStyle.Left;
            _sidebar.Width = SidebarWidth;

            _nav.Dock = DockStyle.Fill;
            _nav.FlowDirection = FlowDirection.TopDown;
            _nav.WrapContents = false;
            _nav.AutoScroll = false;
            _nav.Padding = new Padding(0, 6, 0, 0);

            _header.Dock = DockStyle.Top;
            _header.Height = 74;

            _wordmark.Text = "NASCAR Heat 5";
            _wordmark.Font = new Font("Segoe UI Semibold", 12f);
            _wordmark.AutoSize = false;
            _wordmark.Size = new Size(SidebarWidth - 24, 26);
            _wordmark.Location = new Point(2, 20);

            _wordmarkSub.Text = "Toolkit";
            _wordmarkSub.Font = new Font("Segoe UI", 13f, FontStyle.Regular);
            _wordmarkSub.AutoSize = false;
            _wordmarkSub.Size = new Size(SidebarWidth - 24, 26);
            _wordmarkSub.Location = new Point(2, 44);

            _header.Controls.Add(_wordmark);
            _header.Controls.Add(_wordmarkSub);

            _footer.Dock = DockStyle.Bottom;
            _footer.Height = 44;
            _footer.Text = "  NASCAR Heat 5 modding tools";
            _footer.Font = new Font("Segoe UI", 8.5f);
            _footer.TextAlign = ContentAlignment.MiddleLeft;

            _themeButton.Dock = DockStyle.Top;
            _themeButton.Height = 38;
            _themeButton.TextAlign = ContentAlignment.MiddleLeft;
            _themeButton.FlatStyle = FlatStyle.Flat;
            _themeButton.Cursor = Cursors.Hand;
            _themeButton.FlatAppearance.BorderSize = 0;
            _themeButton.Font = new Font("Segoe UI", 9f);
            _themeButton.Click += (s, e) =>
            {
                Theme.Toggle();
                ApplyTheme();
                Navigate(_activeKey);   // pages read Theme at construction, so rebuild
            };

            AddNavButton("home", "Home");
            foreach (var tool in Tools.All)
                AddNavButton(tool.Key, tool.Name);

            _sidebar.Controls.Add(_nav);
            _sidebar.Controls.Add(_footer);
            _sidebar.Controls.Add(_themeButton);
            _sidebar.Controls.Add(_header);

            Controls.Add(_content);
            Controls.Add(_sidebar);

            ApplyTheme();
            Navigate("home");
        }

        private void AddNavButton(string key, string text)
        {
            var button = new Button
            {
                Text = "   " + text,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(SidebarWidth - 12, 38),
                Margin = new Padding(6, 3, 6, 3),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.TabStop = false;   // no white focus box around a nav item (seen after a dialog closed)

            button.Click += (s, e) => Navigate(key);

            button.MouseEnter += (s, e) =>
            {
                if (key != _activeKey)
                    button.BackColor = Theme.NavHoverBack;
            };
            button.MouseLeave += (s, e) =>
            {
                if (key != _activeKey)
                    button.BackColor = Theme.SidebarBack;
            };

            _navButtons[key] = button;
            _nav.Controls.Add(button);
        }

        /// <summary>
        /// Switches the visible page. Pages are rebuilt on each navigation so
        /// status text (e.g. tool paths) is always fresh.
        /// </summary>
        public void Navigate(string key)
        {
            UserControl page = key switch
            {
                "home" => new HomePage(Navigate),
                "modManager" => new ModManagerPage(),
                _ => new ToolPage(Tools.ByKey(key), Navigate)
            };

            var oldPages = _content.Controls.Cast<Control>().ToArray();
            _content.Controls.Clear();
            foreach (var old in oldPages)
                old.Dispose();

            _content.Controls.Add(page);
            _activeKey = key;
            SelectNavButton(key);
        }

        private void SelectNavButton(string key)
        {
            foreach (var pair in _navButtons)
            {
                bool active = pair.Key == key;
                pair.Value.BackColor = active ? Theme.NavActiveBack : Theme.SidebarBack;
                pair.Value.Font = new Font("Segoe UI", 10f, active ? FontStyle.Bold : FontStyle.Regular);
                pair.Value.ForeColor = active ? Theme.NavActiveText : Theme.NavText;
            }
        }

        private void ApplyTheme()
        {
            BackColor = Theme.WindowBack;
            _content.BackColor = Theme.WindowBack;

            _sidebar.BackColor = Theme.SidebarBack;
            _nav.BackColor = Theme.SidebarBack;
            _header.BackColor = Theme.SidebarBack;

            _wordmark.ForeColor = Theme.HeadingText;
            _wordmarkSub.ForeColor = Theme.FadedText;
            _footer.ForeColor = Theme.FadedText;
            _footer.BackColor = Theme.SidebarBack;

            _themeButton.BackColor = Theme.SidebarBack;
            _themeButton.ForeColor = Theme.NavText;
            _themeButton.Text = Theme.Dark ? "   ☀  Light mode" : "   🌙  Dark mode";

            SelectNavButton(_activeKey);
        }
    }
}
