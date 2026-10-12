using System;

namespace Torp.Core
{
    public sealed class EditorSession
    {
        private readonly Vault vault;
        public NoteDocument Document { get; private set; }
        public string Text { get; set; }
        public bool IsDirty { get { return Document != null && Text != Document.Text; } }

        public EditorSession(Vault vault) { this.vault = vault; Text = String.Empty; }

        public void Open(string path)
        {
            Save();
            NoteDocument next = vault.Read(path);
            Document = next; Text = next.Text;
        }

        public void Attach(NoteDocument document)
        {
            if (IsDirty) { throw new InvalidOperationException("Save the current note before switching."); }
            Document = document; Text = document == null ? String.Empty : document.Text;
        }

        public void Save()
        {
            if (IsDirty) { Document = vault.Save(Document, Text); }
        }

        public void Reload()
        {
            if (Document == null) { return; }
            NoteDocument next = vault.Read(Document.RelativePath);
            Document = next; Text = next.Text;
        }

        public void SaveCopy(string path)
        {
            NoteDocument next = vault.Create(path, Text);
            Document = next; Text = next.Text;
        }
    }
}
