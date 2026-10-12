using System.Drawing;
using System.Windows.Forms;

namespace Torp.App
{
    internal sealed class Theme
    {
        internal readonly Color Background;
        internal readonly Color Sidebar;
        internal readonly Color Foreground;
        internal readonly Color Muted;
        internal readonly Color Accent;
        internal readonly Color Border;

        internal Theme(bool dark)
        {
            Background = dark ? Color.FromArgb(31, 32, 38) : Color.FromArgb(253, 252, 249);
            Sidebar = dark ? Color.FromArgb(24, 25, 30) : Color.FromArgb(241, 239, 235);
            Foreground = dark ? Color.FromArgb(219, 219, 225) : Color.FromArgb(39, 40, 49);
            Muted = dark ? Color.FromArgb(146, 149, 164) : Color.FromArgb(105, 109, 123);
            Accent = dark ? Color.FromArgb(177, 157, 255) : Color.FromArgb(104, 75, 186);
            Border = dark ? Color.FromArgb(53, 54, 65) : Color.FromArgb(218, 216, 211);
        }

        internal void Apply(Control control)
        {
            control.BackColor = control is RichTextBox ? Background : Sidebar;
            control.ForeColor = Foreground;
            ToolStrip strip = control as ToolStrip;
            if (strip != null)
            {
                strip.Renderer = new ToolStripProfessionalRenderer(new Palette(this));
                foreach (ToolStripItem item in strip.Items) { ApplyItem(item); }
            }
            foreach (Control child in control.Controls) { Apply(child); }
            Label label = control as Label;
            if (label != null && (string)label.Tag == "muted") { label.ForeColor = Muted; }
        }

        private void ApplyItem(ToolStripItem item)
        {
            item.ForeColor = Foreground;
            ToolStripDropDownItem dropdown = item as ToolStripDropDownItem;
            if (dropdown == null) { return; }
            dropdown.DropDown.BackColor = Sidebar;
            foreach (ToolStripItem child in dropdown.DropDownItems) { ApplyItem(child); }
        }

        private sealed class Palette : ProfessionalColorTable
        {
            private readonly Theme theme;
            internal Palette(Theme theme) { this.theme = theme; UseSystemColors = false; }
            public override Color ToolStripGradientBegin { get { return theme.Sidebar; } }
            public override Color ToolStripGradientMiddle { get { return theme.Sidebar; } }
            public override Color ToolStripGradientEnd { get { return theme.Sidebar; } }
            public override Color MenuStripGradientBegin { get { return theme.Sidebar; } }
            public override Color MenuStripGradientEnd { get { return theme.Sidebar; } }
            public override Color ImageMarginGradientBegin { get { return theme.Sidebar; } }
            public override Color ImageMarginGradientMiddle { get { return theme.Sidebar; } }
            public override Color ImageMarginGradientEnd { get { return theme.Sidebar; } }
            public override Color MenuItemSelected { get { return theme.Border; } }
            public override Color MenuItemSelectedGradientBegin { get { return theme.Border; } }
            public override Color MenuItemSelectedGradientEnd { get { return theme.Border; } }
            public override Color MenuItemPressedGradientBegin { get { return theme.Border; } }
            public override Color MenuItemPressedGradientMiddle { get { return theme.Border; } }
            public override Color MenuItemPressedGradientEnd { get { return theme.Border; } }
        }
    }
}
