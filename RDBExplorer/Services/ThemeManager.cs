using System.Runtime.InteropServices;
using RDBExplorer.Controls.CheckedComboBox;

namespace RDBExplorer.Services
{
    // One palette for the explorer, preview, menus, dialogs and embedded tool windows.
    internal static class ThemeManager
    {
        private static readonly Color DarkSurface = Color.FromArgb(31, 33, 37);
        private static readonly Color DarkPanel = Color.FromArgb(39, 42, 47);
        private static readonly Color DarkField = Color.FromArgb(46, 49, 55);
        private static readonly Color DarkText = Color.FromArgb(232, 235, 240);
        private static readonly Color DarkBorder = Color.FromArgb(69, 75, 84);
        private static readonly Color DarkSelection = Color.FromArgb(52, 85, 123);
        private static readonly string PreferencePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RDBExplorer", "theme.txt");

        // Dark is the default for a fresh install; the Settings toggle persists the choice.
        internal static bool DarkEnabled { get; private set; } = LoadPreference();
        internal static Color Surface => DarkEnabled ? DarkSurface : SystemColors.Control;
        internal static Color Panel => DarkEnabled ? DarkPanel : SystemColors.Control;
        internal static Color Field => DarkEnabled ? DarkField : SystemColors.Window;
        internal static Color Text => DarkEnabled ? DarkText : SystemColors.ControlText;
        internal static Color Border => DarkEnabled ? DarkBorder : SystemColors.ControlDark;
        internal static Color Selection => DarkEnabled ? DarkSelection : SystemColors.Highlight;

        private static bool LoadPreference()
        {
            try { return !File.Exists(PreferencePath) || File.ReadAllText(PreferencePath).Trim() != "light"; }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        internal static void SetDark(bool enabled)
        {
            if (DarkEnabled == enabled) return;
            DarkEnabled = enabled;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
                File.WriteAllText(PreferencePath, enabled ? "dark" : "light");
            }
            catch (IOException) { /* A read-only profile must not prevent switching themes. */ }
            catch (UnauthorizedAccessException) { }
            UpdateMenuRenderer();
            foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
                if (!form.IsDisposed) Apply(form);
        }

        internal static void Register(Form form)
        {
            // Some resource views create controls in their Shown/Load handlers.
            form.Shown += (_, _) => { if (!form.IsDisposed) Apply(form); };
            Apply(form);
        }

        internal static void Apply(Form form)
        {
            if (form.IsDisposed) return;
            UpdateMenuRenderer();
            PaintControl(form);
            if (form.IsHandleCreated)
                SetDarkTitleBar(form.Handle);
            else
                form.HandleCreated += (_, _) => { if (!form.IsDisposed) SetDarkTitleBar(form.Handle); };
            form.Invalidate(true);
        }

        private static void UpdateMenuRenderer()
        {
            ToolStripManager.Renderer = DarkEnabled
                ? new ToolStripProfessionalRenderer(new DarkMenuColors())
                : new ToolStripProfessionalRenderer();
        }

        private static void PaintControl(Control control)
        {
            // Keep the G1T image canvas dark in BOTH themes: transparent images need
            // a neutral background, and an opaque theme panel would mask the image.
            bool isCanvas = control.Name.StartsWith("G1TPreviewCanvas", StringComparison.Ordinal);
            bool isCanvasMessage = control.Name == "G1TPreviewCanvasMessage";
            if (isCanvas)
            {
                control.BackColor = Color.FromArgb(37, 37, 37);
                control.ForeColor = Color.White;
            }
            else
            {
                bool isInput = control is TextBoxBase || control is ListControl ||
                               control is ListView || control is TreeView || control is PropertyGrid;
                control.BackColor = isInput ? Field : control is Button ? Panel : Surface;
                control.ForeColor = Text;
            }

            if (control is TabPage page)
                page.UseVisualStyleBackColor = !DarkEnabled;
            if (control is Button button)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = DarkEnabled ? Color.FromArgb(62, 69, 80) : SystemColors.ControlLight;
                button.FlatAppearance.MouseDownBackColor = DarkEnabled ? Selection : SystemColors.ControlDark;
                // The tiny container chevron is borderless in the original layout.
                if (button.Name == "ContainerDropdownButton") button.FlatAppearance.BorderSize = 0;
            }
            if (control is SplitContainer split)
            {
                split.Panel1.BackColor = Surface;
                split.Panel2.BackColor = Surface;
            }
            if (control is CheckedComboBox checkedCombo)
                checkedCombo.ApplyDropdownTheme(Field, Text);
            if (control is ToolStrip strip)
                ApplyToStrip(strip);
            if (control is PropertyGrid grid)
            {
                grid.ViewBackColor = Field;
                grid.ViewForeColor = Text;
                grid.HelpBackColor = Panel;
                grid.HelpForeColor = Text;
                grid.LineColor = Border;
            }
            if (control is ListView list)
            {
                ApplyNativeScrollTheme(list);
                list.Invalidate();
            }
            if (control is ListBox box)
                ApplyNativeScrollTheme(box);
            foreach (Control child in control.Controls)
                PaintControl(child);
        }

        internal static void ApplyToStrip(ToolStrip strip)
        {
            strip.BackColor = Panel;
            strip.ForeColor = Text;
            strip.Renderer = ToolStripManager.Renderer;
            foreach (ToolStripItem item in strip.Items)
            {
                item.BackColor = Panel;
                item.ForeColor = Text;
                if (item is ToolStripDropDownItem menu)
                    ApplyToStrip(menu.DropDown);
                if (item is ToolStripControlHost host)
                    PaintControl(host.Control);
            }
        }

        // WinForms does not expose a scrollbar color property for native ListView
        // and ListBox controls. Ask Windows for its themed dark scrollbar, while
        // restoring the normal Explorer theme when Dark Mode is disabled.
        // Windows versions without this theme can keep their system scrollbars.
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

        internal static void ApplyNativeScrollTheme(Control control)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10) || !control.IsHandleCreated)
                return;
            try
            {
                SetWindowTheme(control.Handle, DarkEnabled ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        // Native Windows caption buttons should match the client-area theme.
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private static void SetDarkTitleBar(IntPtr handle)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
            try
            {
                int value = DarkEnabled ? 1 : 0;
                DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => DarkPanel;
            public override Color MenuStripGradientBegin => DarkPanel;
            public override Color MenuStripGradientEnd => DarkPanel;
            public override Color ImageMarginGradientBegin => DarkPanel;
            public override Color ImageMarginGradientMiddle => DarkPanel;
            public override Color ImageMarginGradientEnd => DarkPanel;
            public override Color MenuItemSelected => DarkSelection;
            public override Color MenuItemSelectedGradientBegin => DarkSelection;
            public override Color MenuItemSelectedGradientEnd => DarkSelection;
            public override Color MenuItemPressedGradientBegin => DarkSelection;
            public override Color MenuItemPressedGradientMiddle => DarkSelection;
            public override Color MenuItemPressedGradientEnd => DarkSelection;
            public override Color MenuItemBorder => DarkBorder;
            public override Color MenuBorder => DarkBorder;
            public override Color SeparatorDark => DarkBorder;
            public override Color SeparatorLight => DarkPanel;
            public override Color ToolStripBorder => DarkBorder;
            public override Color ButtonSelectedHighlight => DarkSelection;
            public override Color ButtonSelectedGradientBegin => DarkSelection;
            public override Color ButtonSelectedGradientMiddle => DarkSelection;
            public override Color ButtonSelectedGradientEnd => DarkSelection;
            public override Color CheckBackground => DarkSelection;
            public override Color CheckSelectedBackground => DarkSelection;
            public override Color CheckPressedBackground => DarkSelection;
        }
    }
}
