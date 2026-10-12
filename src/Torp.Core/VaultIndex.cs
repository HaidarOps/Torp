using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Torp.Core
{
    public sealed class VaultIndex
    {
        private readonly List<NoteInfo> notes;
        private readonly Dictionary<string, NoteInfo> byPath = new Dictionary<string, NoteInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<NoteInfo>> byName = new Dictionary<string, List<NoteInfo>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<NoteInfo>> incoming = new Dictionary<string, List<NoteInfo>>(StringComparer.OrdinalIgnoreCase);
        public IList<NoteInfo> Notes { get { return notes.AsReadOnly(); } }
        public IList<string> Warnings { get; private set; }

        public VaultIndex(IEnumerable<NoteInfo> notes)
        {
            this.notes = notes.OrderBy(n => n.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (NoteInfo note in this.notes)
            {
                byPath[note.RelativePath] = note;
                string name = Path.GetFileName(note.RelativePath);
                if (!byName.ContainsKey(name)) { byName[name] = new List<NoteInfo>(); }
                byName[name].Add(note);
            }
            foreach (NoteInfo note in this.notes)
            {
                HashSet<string> targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string link in note.Links)
                {
                    NoteInfo target = ResolveLink(note.RelativePath, link);
                    if (target == null || !targets.Add(target.RelativePath)) { continue; }
                    if (!incoming.ContainsKey(target.RelativePath)) { incoming[target.RelativePath] = new List<NoteInfo>(); }
                    incoming[target.RelativePath].Add(note);
                }
            }
            Warnings = new List<string>();
        }

        public static VaultIndex Build(Vault vault)
        {
            List<NoteInfo> notes = new List<NoteInfo>();
            List<string> warnings = new List<string>();
            long characters = 0;
            foreach (string path in vault.GetPaths())
            {
                try
                {
                    NoteDocument document = vault.Read(path);
                    characters += document.Text.Length;
                    if (characters > 24 * 1024 * 1024 || notes.Count >= 10000)
                    {
                        warnings.Add("Search index limit reached (24 million characters / 10,000 notes). Some notes are not indexed."); break;
                    }
                    notes.Add(new NoteInfo(path, document.Text));
                }
                catch (IOException error) { warnings.Add(path + ": " + error.Message); }
                catch (UnauthorizedAccessException error) { warnings.Add(path + ": " + error.Message); }
                catch (ArgumentException error) { warnings.Add(path + ": " + error.Message); }
            }
            VaultIndex result = new VaultIndex(notes);
            result.Warnings = warnings;
            return result;
        }

        public NoteInfo ResolveLink(string sourcePath, string target)
        {
            string name = (target ?? String.Empty).Split('#')[0].Trim().Replace('\\', '/');
            NoteInfo result;
            if (name.Length == 0) { return byPath.TryGetValue(sourcePath, out result) ? result : null; }
            string normalized;
            try { normalized = Vault.NormalizePath(name); }
            catch (ArgumentException) { return null; }
            string parent = (Path.GetDirectoryName(sourcePath) ?? String.Empty).Replace('\\', '/');
            string sibling = parent.Length == 0 ? normalized : parent + "/" + normalized;
            if (byPath.TryGetValue(sibling, out result)) { return result; }
            if (byPath.TryGetValue(normalized, out result)) { return result; }
            if (name.Contains("/")) { return null; }
            List<NoteInfo> candidates;
            return byName.TryGetValue(normalized, out candidates) && candidates.Count == 1 ? candidates[0] : null;
        }

        public IList<NoteInfo> Backlinks(string path)
        {
            List<NoteInfo> links;
            return incoming.TryGetValue(path, out links) ? (IList<NoteInfo>)links.AsReadOnly() : new List<NoteInfo>();
        }

        public IList<NoteInfo> Search(string query)
        {
            List<string> terms = Regex.Matches(query ?? String.Empty, "(?:[^\\s\"]|\"[^\"]*\")+").Cast<Match>()
                .Select(m => m.Value.Replace("\"", String.Empty)).Where(t => t.Length > 0).ToList();
            return notes.Where(note => terms.All(term => Matches(note, term))).OrderByDescending(n =>
                String.Equals(n.Title, query, StringComparison.OrdinalIgnoreCase) ? 2 :
                n.Title.IndexOf(query ?? String.Empty, StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0)
                .ThenBy(n => n.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool Matches(NoteInfo note, string term)
        {
            bool negate = term.StartsWith("-", StringComparison.Ordinal) && term.Length > 1;
            if (negate) { term = term.Substring(1); }
            bool match;
            if (term.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) || term.StartsWith("#", StringComparison.Ordinal))
            {
                string tag = term.StartsWith("#", StringComparison.Ordinal) ? term.Substring(1) : term.Substring(4).TrimStart('#');
                match = note.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase) || t.StartsWith(tag + "/", StringComparison.OrdinalIgnoreCase));
            }
            else if (term.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
            {
                match = note.RelativePath.IndexOf(term.Substring(5), StringComparison.OrdinalIgnoreCase) >= 0;
            }
            else
            {
                match = note.RelativePath.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    note.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return negate ? !match : match;
        }
    }
}
