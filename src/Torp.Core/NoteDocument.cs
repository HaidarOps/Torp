using System;
using System.Collections.Generic;

namespace Torp.Core
{
    public sealed class NoteDocument
    {
        public string RelativePath { get; private set; }
        public string Text { get; private set; }
        public string Revision { get; private set; }

        public NoteDocument(string relativePath, string text, string revision)
        {
            RelativePath = relativePath;
            Text = text;
            Revision = revision;
        }
    }

    public sealed class NoteInfo
    {
        public string RelativePath { get; private set; }
        public string Title { get; private set; }
        public string Text { get; private set; }
        public IList<string> Links { get; private set; }
        public IList<string> Tags { get; private set; }

        public NoteInfo(string path, string text)
        {
            RelativePath = path;
            Title = System.IO.Path.GetFileNameWithoutExtension(path);
            Text = text;
            Links = Markdown.FindWikiLinks(text);
            Tags = Markdown.FindTags(text);
        }
    }

    public sealed class VaultConflictException : System.IO.IOException
    {
        public VaultConflictException(string message) : base(message) { }
    }

    public sealed class TrashEntry
    {
        public string Id { get; private set; }
        public string RelativePath { get; private set; }
        public DateTime DeletedUtc { get; private set; }

        public TrashEntry(string id, string path, DateTime deletedUtc)
        {
            Id = id;
            RelativePath = path;
            DeletedUtc = deletedUtc;
        }

        public override string ToString()
        {
            return RelativePath + "  ·  " + DeletedUtc.ToLocalTime().ToString("g");
        }
    }
}
