using RDBExplorer.Services;

namespace RDBExplorer.Forms
{
    public partial class ExplolerForm
    {
        private void SetupDarkMode()
        {
            var darkMode = new ToolStripMenuItem("Dark Mode")
            {
                Name = "darkModeToolStripMenuItem",
                CheckOnClick = true,
                Checked = ThemeManager.DarkEnabled
            };
            darkMode.CheckedChanged += (_, _) =>
            {
                ThemeManager.SetDark(darkMode.Checked);
                ApplyExplorerTheme();
            };
            settingsToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            settingsToolStripMenuItem.DropDownItems.Add(darkMode);

            // Owner draw only the resource ListView. In WinForms the native details
            // header ignores BackColor even when the list itself has a dark palette.
            archiveList.DrawColumnHeader += ArchiveList_DrawColumnHeader;
            archiveList.DrawItem += (_, e) =>
            {
                if (archiveList.View != View.Details) e.DrawDefault = true;
            };
            archiveList.DrawSubItem += ArchiveList_DrawSubItem;
            ThemeManager.Register(this);
            ApplyExplorerTheme();
        }

        private void ApplyExplorerTheme()
        {
            archiveList.OwnerDraw = ThemeManager.DarkEnabled;
            archiveList.GridLines = !ThemeManager.DarkEnabled;
            archiveList.Invalidate();
            if (_contextMenu != null) ThemeManager.ApplyToStrip(_contextMenu);
            if (_containerPopup != null) ThemeManager.ApplyToStrip(_containerPopup);
        }

        private void ArchiveList_DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
        {
            using var fill = new SolidBrush(ThemeManager.Panel);
            using var edge = new Pen(ThemeManager.Border);
            e.Graphics.FillRectangle(fill, e.Bounds);
            e.Graphics.DrawLine(edge, e.Bounds.Left, e.Bounds.Bottom - 1,
                e.Bounds.Right - 1, e.Bounds.Bottom - 1);
            var bounds = Rectangle.Inflate(e.Bounds, -6, 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, archiveList.Font, bounds,
                ThemeManager.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void ArchiveList_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item.Selected;
            Color background = selected ? ThemeManager.Selection : ThemeManager.Field;
            using var fill = new SolidBrush(background);
            e.Graphics.FillRectangle(fill, e.Bounds);
            var textBounds = Rectangle.Inflate(e.Bounds, -4, 0);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? string.Empty, archiveList.Font,
                textBounds, selected ? Color.White : ThemeManager.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
