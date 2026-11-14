using System.Runtime.InteropServices;

namespace NH5Toolkit
{
    /// <summary>
    /// Makes a details-view ListView follow the toolkit theme: a hand-drawn header (the stock one is
    /// always white), Windows' dark scrollbars in dark mode, and one column that soaks up spare width
    /// so no white strip is left after the last column.
    /// </summary>
    public static class ListStyle
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

        public static void Apply(ListView list, int fillColumn)
        {
            list.View = View.Details;
            list.FullRowSelect = true;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.CardBack;
            list.ForeColor = Theme.NavText;
            list.OwnerDraw = true;
            list.DrawColumnHeader += DrawHeader;
            // Rows are drawn by hand too: Windows' own selection turns grey-on-dark (unreadable) when
            // the list loses focus, and paints over each row's own colour.
            list.DrawItem += (s, e) => e.DrawDefault = false;
            list.DrawSubItem += DrawCell;
            list.SelectedIndexChanged += (s, e) => list.Invalidate();
            list.LostFocus += (s, e) => list.Invalidate();
            list.GotFocus += (s, e) => list.Invalidate();
            list.Resize += (s, e) => Fit(list, fillColumn);
            DarkScrollbars(list);
        }

        /// <summary>Windows' own dark scrollbars (the theme Explorer uses) on any control, in dark mode only.</summary>
        public static void DarkScrollbars(Control control)
        {
            control.HandleCreated += (s, e) =>
            {
                if (Theme.Dark)
                    try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch { /* older Windows */ }
            };
        }

        /// <summary>Call after filling too: a vertical scrollbar appearing narrows the list.</summary>
        public static void Fit(ListView list, int fillColumn)
        {
            if (list.Columns.Count <= fillColumn) return;
            int others = 0;
            for (int i = 0; i < list.Columns.Count; i++)
                if (i != fillColumn) others += list.Columns[i].Width;
            list.Columns[fillColumn].Width = Math.Max(160, list.ClientSize.Width - others);
        }

        private static void DrawHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
        {
            using var back = new SolidBrush(Theme.SidebarBack);
            e.Graphics.FillRectangle(back, e.Bounds);
            using var line = new Pen(Theme.CardBorder);
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                        (e.Header?.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
            using var font = new Font("Segoe UI Semibold", 9f);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, font, Rectangle.Inflate(e.Bounds, -6, 0), Theme.HeadingText, flags);
        }

        private static void DrawCell(object? sender, DrawListViewSubItemEventArgs e)
        {
            if (sender is not ListView list || e.Item is null) return;

            bool selected = e.Item.Selected;
            using (var back = new SolidBrush(selected ? Theme.NavActiveBack : list.BackColor))
                e.Graphics.FillRectangle(back, e.Bounds);

            // Thin column divider, like the header's.
            using (var line = new Pen(Color.FromArgb(60, Theme.CardBorder)))
                e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);

            // Selected rows keep the row's own colour (blue for replaced, gold for a conflict...),
            // except plain text, which gets the bright selection colour.
            Color fore = e.Item.ForeColor;
            if (selected && fore.ToArgb() == Theme.NavText.ToArgb())
                fore = Theme.NavActiveText;

            var align = list.Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right
                ? TextFormatFlags.Right : TextFormatFlags.Left;
            string text = e.ColumnIndex < e.Item.SubItems.Count ? e.Item.SubItems[e.ColumnIndex].Text : "";
            TextRenderer.DrawText(e.Graphics, text, e.Item.Font ?? list.Font, Rectangle.Inflate(e.Bounds, -6, 0), fore,
                                  align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                                  TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        public static string FormatSize(long bytes) =>
            bytes >= 1_000_000_000 ? $"{bytes / 1_000_000_000.0:0.00} GB" :
            bytes >= 1_000_000 ? $"{bytes / 1_000_000.0:0.0} MB" :
            $"{Math.Max(1, bytes / 1000)} KB";
    }
}
