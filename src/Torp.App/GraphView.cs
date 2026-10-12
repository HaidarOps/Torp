using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Torp.Core;

namespace Torp.App
{
    internal sealed class GraphView : Control
    {
        private readonly Dictionary<string, PointF> positions = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
        private VaultIndex index = new VaultIndex(new NoteInfo[0]);
        private Theme theme = new Theme(true);
        private string current;
        private readonly ToolTip tip = new ToolTip();
        private string hovered;
        internal event Action<string> NoteClicked;

        internal GraphView()
        {
            DoubleBuffered = true; Dock = DockStyle.Fill;
            AccessibleName = "Note graph. Use the explorer or quick switcher for keyboard navigation.";
        }

        internal void SetData(VaultIndex value, string selected, Theme palette)
        {
            index = value; current = selected; theme = palette; BackColor = theme.Background; Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            positions.Clear();
            List<NoteInfo> nodes = index.Notes.Take(120).ToList();
            float radius = Math.Max(20, Math.Min(Width - 130, Height - 140) / 2F);
            for (int i = 0; i < nodes.Count; i++)
            {
                double angle = i * Math.PI * 2 / nodes.Count - Math.PI / 2;
                float ring = nodes.Count == 1 ? 0 : (i % 3 == 0 && nodes.Count > 10 ? radius * 0.6F : radius);
                positions[nodes[i].RelativePath] = new PointF(Width / 2F + (float)Math.Cos(angle) * ring, Height / 2F + (float)Math.Sin(angle) * ring);
            }
            using (Pen edge = new Pen(theme.Border, 1.5F))
            using (Brush normal = new SolidBrush(theme.Muted))
            using (Brush accent = new SolidBrush(theme.Accent))
            using (Brush text = new SolidBrush(theme.Foreground))
            {
                foreach (NoteInfo node in nodes)
                {
                    foreach (string link in node.Links)
                    {
                        NoteInfo target = index.ResolveLink(node.RelativePath, link);
                        if (target != null && positions.ContainsKey(target.RelativePath))
                        {
                            e.Graphics.DrawLine(edge, positions[node.RelativePath], positions[target.RelativePath]);
                        }
                    }
                }
                foreach (NoteInfo node in nodes)
                {
                    PointF point = positions[node.RelativePath];
                    bool selected = String.Equals(node.RelativePath, current, StringComparison.OrdinalIgnoreCase);
                    int size = selected ? 16 : 10;
                    e.Graphics.FillEllipse(selected ? accent : normal, point.X - size / 2, point.Y - size / 2, size, size);
                    string label = node.Title.Length > 24 ? node.Title.Substring(0, 21) + "…" : node.Title;
                    SizeF labelSize = e.Graphics.MeasureString(label, Font);
                    e.Graphics.DrawString(label, Font, text, point.X - labelSize.Width / 2, point.Y + 12);
                }
                string caption = nodes.Count == 0 ? "Link your notes with [[wiki links]] to grow your graph." : "YOUR CONNECTED NOTES  ·  Click a node to open";
                e.Graphics.DrawString(caption, Font, normal, 20, 20);
                if (index.Notes.Count > 120) { e.Graphics.DrawString("Showing the first 120 notes. Use search for the full vault.", Font, normal, 20, Height - 34); }
            }
        }

        private string Hit(Point point)
        {
            foreach (KeyValuePair<string, PointF> item in positions)
            {
                if (Math.Abs(item.Value.X - point.X) < 16 && Math.Abs(item.Value.Y - point.Y) < 16) { return item.Key; }
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string next = Hit(e.Location);
            Cursor = next == null ? Cursors.Default : Cursors.Hand;
            if (hovered != next) { hovered = next; tip.SetToolTip(this, next ?? String.Empty); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            string path = Hit(e.Location);
            if (path != null && NoteClicked != null) { NoteClicked(path); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
