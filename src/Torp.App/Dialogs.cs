using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Torp.App
{
    internal static class Dialogs
    {
        internal static string Prompt(IWin32Window owner, string title, string message, string value)
        {
            using (Form form = new Form())
            {
                form.Text = title;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.ClientSize = new Size(480, 154);
                form.MinimizeBox = false; form.MaximizeBox = false;
                form.Font = new Font("Tahoma", 9F);
                Label label = new Label { Text = message, Location = new Point(16, 15), Size = new Size(448, 44) };
                TextBox input = new TextBox { Text = value, Location = new Point(16, 62), Width = 448, AccessibleName = "Note path" };
                Button accept = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(288, 110), Width = 84 };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(380, 110), Width = 84 };
                form.Controls.AddRange(new Control[] { label, input, accept, cancel });
                form.AcceptButton = accept; form.CancelButton = cancel;
                form.Shown += delegate { input.Focus(); input.SelectAll(); };
                return form.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
            }
        }

        internal static T Pick<T>(IWin32Window owner, string title, IEnumerable<T> items, Func<T, string> display, Theme theme) where T : class
        {
            List<T> all = items.ToList();
            using (Form form = new Form())
            {
                form.Text = title; form.StartPosition = FormStartPosition.CenterParent;
                form.Size = new Size(610, 410); form.MinimumSize = new Size(420, 280);
                form.Font = new Font("Tahoma", 10F); form.MinimizeBox = false; form.MaximizeBox = false;
                TextBox search = new TextBox { Dock = DockStyle.Top, AccessibleName = "Filter items" };
                ListBox list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, AccessibleName = title };
                Label hint = new Label { Text = "Type to filter  ·  Enter to open  ·  Esc to cancel", Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(8), Tag = "muted" };
                List<T> visible = new List<T>();
                Action refresh = delegate
                {
                    visible = all.Where(item => display(item).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).Take(300).ToList();
                    list.BeginUpdate(); list.Items.Clear();
                    foreach (T item in visible) { list.Items.Add(display(item)); }
                    if (list.Items.Count > 0) { list.SelectedIndex = 0; }
                    list.EndUpdate();
                };
                search.TextChanged += delegate { refresh(); };
                form.KeyPreview = true;
                form.KeyDown += delegate(object sender, KeyEventArgs args)
                {
                    if (args.KeyCode == Keys.Escape) { form.DialogResult = DialogResult.Cancel; }
                    else if (args.KeyCode == Keys.Enter && list.SelectedIndex >= 0) { form.DialogResult = DialogResult.OK; }
                    else if (args.KeyCode == Keys.Down) { if (list.SelectedIndex < list.Items.Count - 1) { list.SelectedIndex++; } }
                    else if (args.KeyCode == Keys.Up) { if (list.SelectedIndex > 0) { list.SelectedIndex--; } }
                    else { return; }
                    args.Handled = true; args.SuppressKeyPress = true;
                };
                list.DoubleClick += delegate { if (list.SelectedIndex >= 0) { form.DialogResult = DialogResult.OK; } };
                form.Controls.Add(list); form.Controls.Add(search); form.Controls.Add(hint);
                theme.Apply(form); refresh();
                form.Shown += delegate { search.Focus(); };
                return form.ShowDialog(owner) == DialogResult.OK && list.SelectedIndex >= 0 ? visible[list.SelectedIndex] : null;
            }
        }
    }
}
