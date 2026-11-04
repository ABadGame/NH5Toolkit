namespace NH5Toolkit
{
    /// <summary>
    /// Central light/dark palette. Pages read colors from here at construction
    /// time, so a theme switch just re-navigates and everything rebuilds with
    /// the new palette. The choice persists in config.json under "theme".
    /// </summary>
    public static class Theme
    {
        public static bool Dark { get; private set; }

        public static void Load() => Dark = ToolkitConfig.GetPath("theme") == "dark";

        public static void Set(bool dark)
        {
            Dark = dark;
            ToolkitConfig.SetPath("theme", dark ? "dark" : "light");
        }

        public static void Toggle() => Set(!Dark);

        // --- shared accents ---
        public static Color Accent       => Dark ? Color.FromArgb(68, 138, 255) : Color.FromArgb(0, 102, 204);
        public static Color AccentHover  => Dark ? Color.FromArgb(96, 158, 255) : Color.FromArgb(24, 88, 178);
        public static Color Ok           => Dark ? Color.FromArgb(96, 200, 130) : Color.FromArgb(30, 140, 70);

        // --- page surface ---
        public static Color WindowBack   => Dark ? Color.FromArgb(24, 26, 32)   : Color.White;
        public static Color HeadingText  => Dark ? Color.FromArgb(240, 242, 247) : Color.FromArgb(24, 26, 30);
        public static Color BodyText     => Dark ? Color.FromArgb(176, 180, 190) : Color.FromArgb(90, 90, 98);
        public static Color Muted        => Dark ? Color.FromArgb(130, 134, 144) : Color.FromArgb(150, 150, 156);

        // --- cards ---
        public static Color CardBack     => Dark ? Color.FromArgb(30, 33, 40)   : Color.White;
        public static Color CardHover    => Dark ? Color.FromArgb(38, 42, 52)   : Color.FromArgb(242, 248, 255);
        public static Color CardBorder   => Dark ? Color.FromArgb(64, 70, 82)   : Color.FromArgb(160, 180, 205);

        // --- sidebar ---
        public static Color SidebarBack   => Dark ? Color.FromArgb(17, 19, 24)  : Color.FromArgb(246, 248, 250);
        public static Color NavText       => Dark ? Color.FromArgb(222, 226, 234) : Color.FromArgb(30, 30, 34);
        public static Color NavHoverBack  => Dark ? Color.FromArgb(28, 31, 38)  : Color.FromArgb(237, 241, 246);
        public static Color NavActiveBack => Dark ? Color.FromArgb(34, 48, 74)  : Color.FromArgb(222, 234, 250);
        public static Color NavActiveText => Dark ? Color.FromArgb(140, 185, 255) : Color.FromArgb(10, 66, 150);
        public static Color FadedText     => Dark ? Color.FromArgb(120, 124, 134) : Color.FromArgb(130, 132, 140);
    }
}
