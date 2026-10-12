using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Torp.App;
using Torp.Core;

namespace Torp.Tests
{
    internal static class TestRunner
    {
        private static int passed;
        private static int failed;
        private static int vaultNumber;
        private static string root;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Control.CheckForIllegalCrossThreadCalls = true;
            root = Path.Combine(Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/tests"), "run-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(root);
            Console.WriteLine("Torp component tests · " + Environment.OSVersion + " · CLR " + Environment.Version);
            Console.WriteLine("Isolated fixtures: " + root);
            StorageTests(); SessionTests(); MarkdownTests(); IndexTests(); SettingsTests(); DesktopTests();
            Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        private static void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + "\n" + error); }
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!Object.Equals(expected, actual)) { throw new Exception("Expected: " + expected + "; actual: " + actual); }
        }

        private static void True(bool value, string message)
        {
            if (!value) { throw new Exception(message); }
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }

        private static string NewFolder()
        {
            string folder = Path.Combine(root, "v" + (++vaultNumber).ToString("D3"));
            Directory.CreateDirectory(folder); return folder;
        }

        private static void WithVault(Action<Vault> action)
        {
            using (Vault vault = new Vault(NewFolder())) { action(vault); }
        }

        private static void StorageTests()
        {
            Test("storage: create, read and preserve Unicode", delegate { WithVault(v =>
            {
                NoteDocument note = v.Create("Ideas/日本語", "# café\nالعربية — 世界 🌱\n");
                Equal("Ideas/日本語.md", note.RelativePath); Equal(note.Text, v.Read(note.RelativePath).Text);
                True(note.Revision.Length == 64, "SHA-256 revision missing");
            }); });
            Test("storage: saves are readable after reopening vault", delegate
            {
                string folder = NewFolder();
                using (Vault vault = new Vault(folder)) { vault.Save(vault.Create("Note", "before"), "after"); }
                using (Vault vault = new Vault(folder)) { Equal("after", vault.Read("Note").Text); }
            });
            Test("storage: existing file survives duplicate create", delegate { WithVault(v =>
            {
                v.Create("Note", "original"); Throws<IOException>(() => v.Create("note", "replacement")); Equal("original", v.Read("Note").Text);
            }); });
            Test("storage: saved versions retain the previous text", delegate { WithVault(v =>
            {
                NoteDocument first = v.Create("Note", "one"); NoteDocument second = v.Save(first, "two"); v.Save(second, "three");
                IList<string> versions = v.GetHistory("Note"); Equal(2, versions.Count);
                True(versions.Select(version => v.ReadHistory("Note", version)).OrderBy(t => t).SequenceEqual(new string[] { "one", "two" }), "History contents differ");
            }); });
            Test("storage: no-op save does not add a version", delegate { WithVault(v => { NoteDocument n = v.Create("Note", "same"); v.Save(n, "same"); Equal(0, v.GetHistory("Note").Count); }); });
            Test("storage: external modification causes a conflict", delegate { WithVault(v =>
            {
                NoteDocument n = v.Create("Note", "original"); File.WriteAllText(Path.Combine(v.Root, "Note.md"), "external");
                Throws<VaultConflictException>(() => v.Save(n, "mine")); Equal("external", v.Read("Note").Text); Equal(0, v.GetHistory("Note").Count);
            }); });
            Test("storage: stale revision conflicts even with unchanged text", delegate { WithVault(v =>
            {
                NoteDocument n = v.Create("Note", "original"); v.Save(n, "new"); Throws<VaultConflictException>(() => v.Save(n, n.Text));
            }); });
            Test("storage: external deletion cannot be silently recreated", delegate { WithVault(v =>
            {
                NoteDocument n = v.Create("Note", "original"); File.Delete(Path.Combine(v.Root, "Note.md")); Throws<VaultConflictException>(() => v.Save(n, "mine")); True(!v.Exists("Note"), "Deleted note recreated");
            }); });
            Test("storage: lock prevents two Torp writers", delegate { WithVault(v => { Throws<IOException>(() => { using (Vault other = new Vault(v.Root)) { } }); }); });
            Test("storage: dispose releases vault lock", delegate
            {
                string folder = NewFolder(); using (Vault first = new Vault(folder)) { first.Create("Note", "ok"); }
                using (Vault second = new Vault(folder)) { Equal("ok", second.Read("Note").Text); }
            });
            Test("storage: disposed vault rejects access", delegate { Vault v = new Vault(NewFolder()); v.Dispose(); Throws<ObjectDisposedException>(() => v.GetPaths()); });
            string[] invalid = { "../escape", "folder/../escape", "C:\\outside", "\\\\server\\share", "/absolute", "a//b", ".torp/data", "x/.hidden", "CON", "nul.txt", "AUX", "COM1", "LPT9", "COM¹", "name.", "name ", " name", "a:b", "a?b", "a*b", "a|b", "a<b", "a>b", "a\"b", "a\0b", "", " " };
            foreach (string path in invalid)
            {
                string candidate = path;
                Test("paths: rejects " + candidate.Replace("\0", "<NUL>"), delegate { Throws<ArgumentException>(() => Vault.NormalizePath(candidate)); });
            }
            Test("paths: preserves casing and normalizes separators", delegate { Equal("Folder/Note.MD", Vault.NormalizePath("Folder\\Note.MD")); });
            Test("paths: rejects paths beyond XP limit", delegate { WithVault(v => { Throws<PathTooLongException>(() => v.Create(new string('a', 220), "text")); }); });
            Test("storage: hidden metadata excluded from discovery", delegate { WithVault(v =>
            {
                NoteDocument n = v.Create("b/Second", "two"); v.Save(n, "three"); v.Create("First", "one");
                Directory.CreateDirectory(Path.Combine(v.Root, ".hidden")); File.WriteAllText(Path.Combine(v.Root, ".hidden", "ignore.md"), "hidden");
                File.WriteAllText(Path.Combine(v.Root, "other.txt"), "ignore"); Equal(2, v.GetPaths().Count);
            }); });
            Test("storage: rename and move retains text", delegate { WithVault(v =>
            {
                NoteDocument n = v.Rename(v.Create("Original", "keep"), "Folder/Renamed"); Equal("Folder/Renamed.md", n.RelativePath); Equal("keep", n.Text); True(!v.Exists("Original"), "Old note remains");
            }); });
            Test("storage: rename never overwrites a destination", delegate { WithVault(v =>
            {
                NoteDocument n = v.Create("a", "A"); v.Create("b", "B"); Throws<IOException>(() => v.Rename(n, "b")); Equal("A", v.Read("a").Text); Equal("B", v.Read("b").Text);
            }); });
            Test("storage: stale rename and trash preserve external text", delegate { WithVault(v =>
            {
                NoteDocument n = v.Create("a", "A"); v.Save(n, "B"); Throws<VaultConflictException>(() => v.Rename(n, "b")); Throws<VaultConflictException>(() => v.Trash(n)); Equal("B", v.Read("a").Text);
            }); });
            Test("storage: trash and restore round-trip nested notes", delegate { WithVault(v =>
            {
                NoteDocument n = v.Create("Folder/a", "content"); v.Trash(n); True(!v.Exists(n.RelativePath), "Trashed note is still present");
                Equal(1, v.GetTrash().Count); NoteDocument restored = v.Restore(v.GetTrash()[0]); Equal(n.RelativePath, restored.RelativePath); Equal(n.Text, restored.Text); Equal(0, v.GetTrash().Count);
            }); });
            Test("storage: restore conflict preserves both versions", delegate { WithVault(v =>
            {
                v.Trash(v.Create("a", "old")); v.Create("a", "new"); Throws<IOException>(() => v.Restore(v.GetTrash()[0])); Equal("new", v.Read("a").Text); Equal(1, v.GetTrash().Count);
            }); });
            Test("storage: trash ID cannot traverse metadata", delegate { WithVault(v => { Throws<ArgumentException>(() => v.Restore(new TrashEntry("../outside", "a.md", DateTime.UtcNow))); }); });
            Test("storage: history version cannot traverse metadata", delegate { WithVault(v => { Throws<ArgumentException>(() => v.ReadHistory("a", "../escape.md")); }); });
            Test("storage: oversized creates leave no partial note", delegate { WithVault(v =>
            {
                Throws<IOException>(() => v.Create("Huge", new string('x', Vault.MaximumNoteBytes + 1))); True(!v.Exists("Huge"), "Partial oversized note exists");
            }); });
            Test("storage: oversized save preserves the original", delegate { WithVault(v =>
            {
                NoteDocument n = v.Create("a", "keep"); Throws<IOException>(() => v.Save(n, new string('x', Vault.MaximumNoteBytes + 1))); Equal("keep", v.Read("a").Text);
            }); });
            Test("storage: byte limit includes multibyte Unicode", delegate { WithVault(v => { Throws<IOException>(() => v.Create("a", new string('世', Vault.MaximumNoteBytes / 2))); }); });
            Test("storage: invalid UTF-8 is rejected, not corrupted", delegate { WithVault(v =>
            {
                File.WriteAllBytes(Path.Combine(v.Root, "binary.md"), new byte[] { 0xC3, 0x28 }); Throws<DecoderFallbackException>(() => v.Read("binary"));
            }); });
            Test("storage: UTF-8 BOM is decoded correctly", delegate { WithVault(v =>
            {
                File.WriteAllBytes(Path.Combine(v.Root, "bom.md"), new byte[] { 0xEF, 0xBB, 0xBF, 65 }); Equal("A", v.Read("bom").Text);
            }); });
            Test("storage: binary NUL cannot be truncated by the editor", delegate { WithVault(v =>
            {
                File.WriteAllBytes(Path.Combine(v.Root, "binary-nul.md"), new byte[] { 65, 0, 66 }); Throws<IOException>(() => v.Read("binary-nul.md"));
                Throws<IOException>(() => v.Create("new", "a\0b")); True(!v.Exists("new"), "Invalid text produced a file");
            }); });
            Test("storage: locked note save leaves no temporary files", delegate { WithVault(v =>
            {
                NoteDocument n = v.Create("a", "keep");
                using (FileStream hold = new FileStream(Path.Combine(v.Root, "a.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Throws<IOException>(() => v.Save(n, "new"));
                }
                Equal("keep", v.Read("a").Text); Equal(0, Directory.GetFiles(v.Root, "*.tmp").Length);
            }); });
            Test("storage: read-only note failure preserves original", delegate { WithVault(v =>
            {
                NoteDocument n = v.Create("a", "keep"); string path = Path.Combine(v.Root, "a.md");
                File.SetAttributes(path, FileAttributes.ReadOnly);
                try { Throws<UnauthorizedAccessException>(() => v.Save(n, "new")); Equal("keep", v.Read("a").Text); }
                finally { File.SetAttributes(path, FileAttributes.Normal); }
            }); });
            Test("storage: junctions cannot escape the vault", delegate { WithVault(v =>
            {
                string target = NewFolder(); File.WriteAllText(Path.Combine(target, "outside.md"), "private");
                string junction = Path.Combine(v.Root, "linked");
                ProcessStartInfo start = new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + junction + "\" \"" + target + "\"");
                start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
                using (Process process = Process.Start(start))
                {
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
                    Equal(0, process.ExitCode); True(Directory.Exists(junction), "Junction setup failed: " + output);
                }
                Throws<IOException>(() => v.Read("linked/outside")); Throws<IOException>(() => v.Create("linked/new", "bad")); Equal(0, v.GetPaths().Count);
                // Delete the junction itself; never recurse into its target.
                Directory.Delete(junction); Equal("private", File.ReadAllText(Path.Combine(target, "outside.md")));
            }); });
            Test("index builder: bad files yield warnings without hiding good notes", delegate { WithVault(v =>
            {
                v.Create("valid", "ok"); File.WriteAllBytes(Path.Combine(v.Root, "bad.md"), new byte[] { 0xFF, 0xFE });
                VaultIndex index = VaultIndex.Build(v); Equal(1, index.Notes.Count); Equal(1, index.Warnings.Count);
            }); });
        }

        private static void SessionTests()
        {
            Test("editor session: dirty tracking and save", delegate { WithVault(v =>
            {
                v.Create("a", "A"); EditorSession session = new EditorSession(v); session.Open("a"); True(!session.IsDirty, "Fresh note is dirty");
                session.Text = "B"; True(session.IsDirty, "Changed note is not dirty"); session.Save(); True(!session.IsDirty, "Saved note still dirty"); Equal("B", v.Read("a").Text);
            }); });
            Test("editor session: navigation saves before opening next note", delegate { WithVault(v =>
            {
                v.Create("a", "A"); v.Create("b", "B"); EditorSession session = new EditorSession(v); session.Open("a"); session.Text = "edited"; session.Open("b"); Equal("edited", v.Read("a").Text); Equal("B", session.Text);
            }); });
            Test("editor session: conflict blocks navigation and retains buffer", delegate { WithVault(v =>
            {
                v.Create("a", "A"); v.Create("b", "B"); EditorSession session = new EditorSession(v); session.Open("a"); session.Text = "mine"; v.Save(v.Read("a"), "external");
                Throws<VaultConflictException>(() => session.Open("b")); Equal("a.md", session.Document.RelativePath); Equal("mine", session.Text); True(session.IsDirty, "Conflict lost dirty state");
            }); });
            Test("editor session: save copy recovers conflicting edits", delegate { WithVault(v =>
            {
                v.Create("a", "A"); EditorSession session = new EditorSession(v); session.Open("a"); session.Text = "mine"; v.Save(v.Read("a"), "external");
                session.SaveCopy("recovered"); Equal("mine", v.Read("recovered").Text); Equal("external", v.Read("a").Text); True(!session.IsDirty, "Copy not saved");
            }); });
            Test("editor session: failed copy retains dirty buffer", delegate { WithVault(v =>
            {
                v.Create("a", "A"); EditorSession session = new EditorSession(v); session.Open("a"); session.Text = "mine"; Throws<IOException>(() => session.SaveCopy("a")); True(session.IsDirty, "Failed copy lost edits"); Equal("mine", session.Text);
            }); });
            Test("editor session: reload uses disk version", delegate { WithVault(v =>
            {
                v.Create("a", "A"); EditorSession session = new EditorSession(v); session.Open("a"); session.Text = "mine"; v.Save(v.Read("a"), "external"); session.Reload(); Equal("external", session.Text); True(!session.IsDirty, "Reload dirty");
            }); });
            Test("editor session: attaching a new note cannot discard edits", delegate { WithVault(v =>
            {
                v.Create("a", "A"); EditorSession session = new EditorSession(v); session.Open("a"); session.Text = "mine"; Throws<InvalidOperationException>(() => session.Attach(null)); Equal("mine", session.Text);
            }); });
            Test("editor session: failed open retains current document", delegate { WithVault(v =>
            {
                v.Create("a", "A"); EditorSession session = new EditorSession(v); session.Open("a"); Throws<FileNotFoundException>(() => session.Open("missing")); Equal("a.md", session.Document.RelativePath);
            }); });
        }

        private static void MarkdownTests()
        {
            Test("Markdown: headings and inline emphasis render as readable text", delegate
            {
                RenderedMarkdown result = Markdown.Render("# Title\n**bold** and *italic* and ~~gone~~ and `code`", true);
                Equal("Title\nbold and italic and gone and code\n", result.Text); True(result.Rtf.Contains(@"\b "), "Bold style missing");
            });
            Test("Markdown: tasks, lists, quotes and fences", delegate
            {
                RenderedMarkdown result = Markdown.Render("- [ ] todo\n- [x] done\n1. first\n> quote\n```cs\n[[literal]] #notag\n```", true);
                True(result.Text.Contains("☐  todo") && result.Text.Contains("☑  done") && result.Text.Contains("1. first"), "List rendering missing"); Equal(0, result.Links.Count);
            });
            Test("Markdown: wiki aliases and headings retain destinations", delegate
            {
                RenderedMarkdown result = Markdown.Render("See [[Folder/Note#Part|the part]].", false); Equal("See the part.\n", result.Text);
                Equal("Folder/Note#Part", result.Links[0].Target); Equal(4, result.Links[0].Start); Equal(8, result.Links[0].Length); True(result.Links[0].IsWiki, "Wiki link flag missing");
            });
            Test("Markdown: code and escaped wiki syntax are not indexed", delegate
            {
                IList<string> links = Markdown.FindWikiLinks("[[Yes|alias]] `[[No]]` \\[[Escaped]]\n```\n[[Fenced]]\n```\n[[Yes]]"); Equal(1, links.Count); Equal("Yes", links[0]);
            });
            Test("Markdown: tilde fences and mixed fence characters", delegate
            {
                IList<string> links = Markdown.FindWikiLinks("~~~\n```\n[[No]]\n~~~\n[[Yes]]"); Equal(1, links.Count); Equal("Yes", links[0]);
            });
            Test("Markdown: tags support Unicode and nested tags", delegate
            {
                IList<string> tags = Markdown.FindTags("# Heading\n#work/research #世界 #work/research `#ignore`\n~~~\n#hidden\n~~~"); Equal(2, tags.Count); True(tags.Contains("世界") && tags.Contains("work/research"), "Tags missing");
            });
            Test("Markdown: external link schemes are allowlisted", delegate
            {
                True(Markdown.IsSafeExternalLink("https://example.com"), "HTTPS rejected"); True(Markdown.IsSafeExternalLink("mailto:test@example.com"), "mailto rejected");
                foreach (string url in new string[] { "javascript:alert(1)", "file:///c:/secret", "data:text/html,test", "cmd:run", "ftp://example.com", "relative/path" })
                { True(!Markdown.IsSafeExternalLink(url), "Unsafe scheme accepted: " + url); }
            });
            Test("Markdown: raw HTML and RTF injection stay literal", delegate
            {
                string input = "<script>alert('x')</script> {\\rtf1 payload}";
                RenderedMarkdown result = Markdown.Render(input, true); Equal(0, result.Links.Count);
                using (RichTextBox box = new RichTextBox()) { box.Rtf = result.Rtf; True(box.Text.Contains("<script>"), "HTML removed"); True(box.Text.Contains("{rtf1 payload}"), "RTF injected"); }
            });
            Test("Markdown: unsafe links remain literal", delegate { RenderedMarkdown r = Markdown.Render("[bad](javascript:run) [good](https://example.com)", true); Equal(1, r.Links.Count); Equal("https://example.com", r.Links[0].Target); True(r.Text.Contains("javascript:run"), "Unsafe link hidden"); });
            Test("Markdown: Unicode rich text and link character offsets", delegate
            {
                RenderedMarkdown r = Markdown.Render("# 世界 🌱\n\nGo [[Note|there]]", true);
                using (RichTextBox box = new RichTextBox()) { box.Rtf = r.Rtf; True(box.Text.Contains("世界 🌱"), "Unicode rendering broken"); Equal("there", box.Text.Substring(r.Links[0].Start, r.Links[0].Length)); }
            });
            Test("Markdown: words handle punctuation and Unicode", delegate { Equal(5, Markdown.WordCount("hello, world! café isn't 世界")); Equal(0, Markdown.WordCount("  \n")); });
            Test("Markdown: hostile incomplete brackets complete promptly", delegate
            {
                string text = new string('[', 100000); Stopwatch watch = Stopwatch.StartNew();
                Markdown.Render(text, true); Markdown.FindWikiLinks(text); watch.Stop(); True(watch.ElapsedMilliseconds < 10000, "Malformed markup took " + watch.ElapsedMilliseconds + " ms");
            });
            Test("Markdown: empty document renders valid native rich text", delegate { using (RichTextBox box = new RichTextBox()) { box.Rtf = Markdown.Render(null, false).Rtf; Equal(String.Empty, box.Text.Trim()); } });
        }

        private static VaultIndex ExampleIndex()
        {
            return new VaultIndex(new NoteInfo[]
            {
                new NoteInfo("Home.md", "# Home\nA quiet place. [[Work/Plan]] [[Unique]] #welcome"),
                new NoteInfo("Work/Plan.md", "Project alpha, ship it. [[Home]] [[Sibling]] #work/research"),
                new NoteInfo("Work/Sibling.md", "[[Plan#Tasks|plan]] #work"),
                new NoteInfo("Other/Plan.md", "Other project beta #personal"),
                new NoteInfo("Notes/Unique.md", "Only one. [[Home]]")
            });
        }

        private static void IndexTests()
        {
            Test("search: case-insensitive full text", delegate { Equal(1, ExampleIndex().Search("ALPHA").Count); });
            Test("search: quoted phrases match contiguous text", delegate { Equal(1, ExampleIndex().Search("\"quiet place\"").Count); Equal(0, ExampleIndex().Search("\"place quiet\"").Count); });
            Test("search: all terms are required", delegate { Equal(1, ExampleIndex().Search("project alpha").Count); });
            Test("search: exclusions", delegate { Equal(1, ExampleIndex().Search("project -beta").Count); });
            Test("search: nested tag filter", delegate { Equal(2, ExampleIndex().Search("tag:work").Count); Equal(1, ExampleIndex().Search("#work/research").Count); });
            Test("search: path and phrase filters combine", delegate { Equal(1, ExampleIndex().Search("path:Work \"ship it\"").Count); });
            Test("search: negated tag filter", delegate { Equal(3, ExampleIndex().Search("-tag:work").Count); });
            Test("search: empty query returns all notes", delegate { Equal(5, ExampleIndex().Search("").Count); });
            Test("links: explicit vault paths", delegate { Equal("Work/Plan.md", ExampleIndex().ResolveLink("Home.md", "Work/Plan").RelativePath); });
            Test("links: sibling names resolve before ambiguous basenames", delegate { Equal("Work/Plan.md", ExampleIndex().ResolveLink("Work/Sibling.md", "Plan#Tasks").RelativePath); });
            Test("links: unique basename resolves across folders", delegate { Equal("Notes/Unique.md", ExampleIndex().ResolveLink("Home.md", "Unique").RelativePath); });
            Test("links: ambiguous basename does not guess", delegate { Equal<NoteInfo>(null, ExampleIndex().ResolveLink("Home.md", "Plan")); });
            Test("links: relative traversal and unsafe paths rejected", delegate { Equal<NoteInfo>(null, ExampleIndex().ResolveLink("Home.md", "../outside")); });
            Test("links: anchor-only target refers to current note", delegate { Equal("Home.md", ExampleIndex().ResolveLink("Home.md", "#Heading").RelativePath); });
            Test("links: extension and case insensitive resolution", delegate { Equal("Home.md", ExampleIndex().ResolveLink("Work/Plan.md", "HOME.MD").RelativePath); });
            Test("backlinks: incoming links only", delegate { IList<NoteInfo> links = ExampleIndex().Backlinks("Home.md"); Equal(2, links.Count); True(links.Any(n => n.RelativePath == "Work/Plan.md"), "Incoming link missing"); });
            Test("backlinks: headings and aliases resolve to note", delegate { Equal(2, ExampleIndex().Backlinks("Work/Plan.md").Count); });
            Test("backlinks: repeated links produce one incoming note", delegate
            {
                VaultIndex index = new VaultIndex(new NoteInfo[] { new NoteInfo("a.md", "[[b]] [[b#Part]]"), new NoteInfo("b.md", "B") });
                Equal(1, index.Backlinks("b.md").Count);
            });
            Test("index: ten thousand linked notes stay responsive", delegate
            {
                Stopwatch clock = Stopwatch.StartNew();
                VaultIndex index = new VaultIndex(Enumerable.Range(0, 10000).Select(i => new NoteInfo("Note" + i + ".md", "[[Note" + ((i + 1) % 10000) + "]] #scale")));
                Equal(1, index.Backlinks("Note5.md").Count); Equal(10000, index.Search("tag:scale").Count);
                clock.Stop(); True(clock.ElapsedMilliseconds < 15000, "Index operation took " + clock.ElapsedMilliseconds + " ms");
                Console.WriteLine("PERFORMANCE 10,000 linked notes: " + clock.ElapsedMilliseconds + " ms");
            });
        }

        private static void SettingsTests()
        {
            Test("settings: defaults on first run", delegate { AppSettings s = AppSettings.Load(Path.Combine(NewFolder(), "settings.xml")); True(s.DarkTheme && s.Preview, "Defaults differ"); Equal(12, s.FontSize); });
            Test("settings: XML Unicode and escaped paths round-trip", delegate
            {
                string path = Path.Combine(NewFolder(), "settings.xml"); AppSettings s = new AppSettings { VaultPath = "C:\\notes & ideas\\世界", LastNote = "a<b.md", DarkTheme = false, Preview = false, FontSize = 16 };
                s.Save(path); AppSettings actual = AppSettings.Load(path); Equal(s.VaultPath, actual.VaultPath); Equal(s.LastNote, actual.LastNote); Equal(16, actual.FontSize); True(!actual.DarkTheme && !actual.Preview, "Theme not persisted");
                s.FontSize = 18; s.Save(path); Equal(18, AppSettings.Load(path).FontSize);
            });
            Test("settings: font size clamped", delegate { string path = Path.Combine(NewFolder(), "settings.xml"); File.WriteAllText(path, "<torp fontSize=\"5000\" />"); Equal(24, AppSettings.Load(path).FontSize); });
            Test("settings: DTD and external entities are forbidden", delegate
            {
                string path = Path.Combine(NewFolder(), "settings.xml"); File.WriteAllText(path, "<!DOCTYPE torp [<!ENTITY x SYSTEM 'file:///c:/secret'>]><torp>&x;</torp>"); Throws<System.Xml.XmlException>(() => AppSettings.Load(path));
            });
            Test("settings: corrupt input reported explicitly", delegate { string path = Path.Combine(NewFolder(), "settings.xml"); File.WriteAllText(path, "not xml"); Throws<System.Xml.XmlException>(() => AppSettings.Load(path)); });
        }

        private static void PumpUntil(Func<bool> condition, int milliseconds)
        {
            Stopwatch clock = Stopwatch.StartNew();
            do
            {
                Application.DoEvents();
                if (condition()) { return; }
                Thread.Sleep(10);
            } while (clock.ElapsedMilliseconds < milliseconds);
            throw new Exception("Timed out after " + milliseconds + " ms waiting for the desktop component.");
        }

        private static void DesktopTests()
        {
            Test("dialogs: prompt returns input and cancellation returns null", delegate
            {
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 })
                {
                    timer.Tick += delegate
                    {
                        Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == "Prompt test");
                        if (dialog == null) { return; }
                        dialog.Controls.OfType<TextBox>().First().Text = "Folder/My note";
                        dialog.DialogResult = DialogResult.OK; timer.Stop();
                    };
                    timer.Start(); Equal("Folder/My note", Dialogs.Prompt(null, "Prompt test", "Name", "Original"));
                }
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 })
                {
                    timer.Tick += delegate
                    {
                        Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == "Cancel test");
                        if (dialog == null) { return; }
                        dialog.DialogResult = DialogResult.Cancel; timer.Stop();
                    };
                    timer.Start(); Equal<string>(null, Dialogs.Prompt(null, "Cancel test", "Name", "Original"));
                }
            });
            Test("dialogs: picker filters and returns the correct underlying item", delegate
            {
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 })
                {
                    timer.Tick += delegate
                    {
                        Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == "Picker test");
                        if (dialog == null) { return; }
                        dialog.Controls.OfType<TextBox>().First().Text = "second";
                        dialog.DialogResult = DialogResult.OK; timer.Stop();
                    };
                    timer.Start(); Equal("Second note", Dialogs.Pick(null, "Picker test", new string[] { "First note", "Second note", "Third note" }, s => s, new Theme(true)));
                }
            });
            Test("desktop: first launch, index, editor, preview, graph, themes and screenshot", delegate
            {
                string folder = NewFolder(); AppSettings settings = new AppSettings { VaultPath = folder };
                using (MainForm form = new MainForm(settings, Path.Combine(folder, "preferences.xml")))
                {
                    form.Show(); PumpUntil(() => !form.IsIndexing && form.Index.Notes.Count == 3, 10000);
                    True(form.ExplorerControl.Nodes.Count >= 2, "Explorer not populated");
                    True(form.PreviewControl.Text.Contains("A little space"), "Reading view is blank");
                    Equal("Welcome to Torp.md", form.Session.Document.RelativePath);
                    string screenshot = Path.Combine(root, "torp-dark.png");
                    Capture(form, screenshot);
                    Console.WriteLine("SCREENSHOT " + screenshot);
                    form.ToggleGraph(); Application.DoEvents();
                    Capture(form, Path.Combine(root, "torp-graph.png"));
                    form.ToggleGraph(); form.ToggleTheme(); Application.DoEvents();
                    Capture(form, Path.Combine(root, "torp-light.png"));
                    form.TogglePreview(); form.TogglePreview();
                    form.Size = new Size(900, 600); Application.DoEvents();
                    True(form.EditorControl.Width > 80 && form.PreviewControl.Width > 80, "Minimum-size layout collapsed");
                    form.OpenNote("Getting started.md"); Equal("Getting started.md", form.Session.Document.RelativePath);
                    True(form.PreviewControl.Text.Contains("Your vault"), "Navigation did not update preview");
                    form.Close(); True(!form.Visible, "Window did not close");
                }
                Equal("Getting started.md", AppSettings.Load(Path.Combine(folder, "preferences.xml")).LastNote);
            });
            Test("desktop: autosave timer persists editor changes", delegate
            {
                string folder = NewFolder();
                using (MainForm form = new MainForm(new AppSettings { VaultPath = folder }, Path.Combine(folder, "settings.xml")))
                {
                    form.Show(); PumpUntil(() => !form.IsIndexing, 10000);
                    form.EditorControl.AppendText("\nAutosaved text."); True(form.Session.IsDirty, "UI edit not dirty");
                    PumpUntil(() => !form.Session.IsDirty, 6000);
                    True(File.ReadAllText(Path.Combine(folder, "Welcome to Torp.md")).Contains("Autosaved text."), "Timer did not write text");
                    form.Close();
                }
            });
            Test("desktop: failed save keeps edits and prevents accidental close", delegate
            {
                string folder = NewFolder();
                using (MainForm form = new MainForm(new AppSettings { VaultPath = folder }, Path.Combine(folder, "settings.xml")))
                {
                    form.Show(); PumpUntil(() => !form.IsIndexing, 10000);
                    form.EditorControl.Text = "my unsaved text";
                    File.WriteAllText(Path.Combine(folder, "Welcome to Torp.md"), "external text");
                    True(!form.SaveCurrent(), "Conflicting save succeeded"); True(form.Session.IsDirty, "Dirty state lost"); True(form.HasAlert, "Conflict not visible");
                    form.Close(); True(form.Visible, "Conflicting edits discarded on close"); Equal("my unsaved text", form.EditorControl.Text);
                    form.Session.SaveCopy("recovered"); form.Close();
                }
                Equal("external text", File.ReadAllText(Path.Combine(folder, "Welcome to Torp.md"))); Equal("my unsaved text", File.ReadAllText(Path.Combine(folder, "recovered.md")));
            });
            Test("desktop: create note and navigation save through real controls", delegate
            {
                string folder = NewFolder();
                using (MainForm form = new MainForm(new AppSettings { VaultPath = folder }, Path.Combine(folder, "settings.xml")))
                {
                    form.Show(); PumpUntil(() => !form.IsIndexing, 10000);
                    form.CreateNote("Projects/Test", "# Test\n"); form.EditorControl.AppendText("[[Getting started]] #project\n"); form.OpenNote("Getting started.md");
                    True(File.ReadAllText(Path.Combine(folder, "Projects", "Test.md")).Contains("#project"), "Navigation lost changes");
                    form.RequestIndex(); PumpUntil(() => !form.IsIndexing && form.Index.Notes.Count == 4, 10000);
                    Equal(2, form.Index.Backlinks("Getting started.md").Count);
                    form.Close();
                }
            });
            Test("desktop: clean buffer follows external edits", delegate
            {
                string folder = NewFolder();
                using (MainForm form = new MainForm(new AppSettings { VaultPath = folder }, Path.Combine(folder, "settings.xml")))
                {
                    form.Show(); PumpUntil(() => !form.IsIndexing, 10000);
                    File.WriteAllText(Path.Combine(folder, "Welcome to Torp.md"), "# External update\n");
                    PumpUntil(() => form.EditorControl.Text.Contains("External update"), 7000);
                    True(!form.Session.IsDirty, "External refresh made note dirty"); form.Close();
                }
            });
            Test("desktop: unreadable initial note does not crash the vault", delegate
            {
                string folder = NewFolder(); File.WriteAllBytes(Path.Combine(folder, "bad.md"), new byte[] { 0xFF }); File.WriteAllText(Path.Combine(folder, "good.md"), "# Good\n");
                using (MainForm form = new MainForm(new AppSettings { VaultPath = folder, LastNote = "bad.md" }, Path.Combine(folder, "settings.xml")))
                {
                    form.Show(); PumpUntil(() => !form.IsIndexing, 10000);
                    True(form.HasAlert, "Unreadable file was hidden"); form.OpenNote("good.md"); True(form.EditorControl.Text.Contains("Good"), "Good note unavailable"); form.Close();
                }
            });
        }

        private static void Capture(Form form, string path)
        {
            form.BringToFront(); form.Activate(); form.Refresh(); Application.DoEvents(); Thread.Sleep(120);
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                // RichEdit does not implement DrawToBitmap; use its native printing API.
                // This captures the actual formatted control without needing desktop access.
                PrintRichText(form, bitmap);
                bitmap.Save(path);
            }
        }

        private static void PrintRichText(Control control, Bitmap bitmap)
        {
            RichTextBox box = control as RichTextBox;
            if (box != null && box.Visible)
            {
                Form form = box.FindForm();
                Point origin = box.PointToScreen(Point.Empty);
                origin.Offset(-form.Left, -form.Top);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    float factorX = 1440F / graphics.DpiX;
                    float factorY = 1440F / graphics.DpiY;
                    IntPtr context = graphics.GetHdc();
                    IntPtr memory = IntPtr.Zero;
                    try
                    {
                        FormatRange range = new FormatRange();
                        range.Device = context; range.Target = context;
                        range.Area = new NativeRect((int)(origin.X * factorX), (int)(origin.Y * factorY),
                            (int)((origin.X + box.ClientSize.Width - SystemInformation.VerticalScrollBarWidth) * factorX), (int)((origin.Y + box.ClientSize.Height) * factorY));
                        range.Page = range.Area; range.Start = 0; range.End = -1;
                        memory = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(System.Runtime.InteropServices.Marshal.SizeOf(range));
                        System.Runtime.InteropServices.Marshal.StructureToPtr(range, memory, false);
                        SendMessage(box.Handle, 0x0439, new IntPtr(1), memory);
                        SendMessage(box.Handle, 0x0439, IntPtr.Zero, IntPtr.Zero);
                    }
                    finally
                    {
                        if (memory != IntPtr.Zero) { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(memory); }
                        graphics.ReleaseHdc(context);
                    }
                }
            }
            foreach (Control child in control.Controls) { PrintRichText(child, bitmap); }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeRect
        {
            internal int Left, Top, Right, Bottom;
            internal NativeRect(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct FormatRange
        {
            internal IntPtr Device, Target;
            internal NativeRect Area, Page;
            internal int Start, End;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr word, IntPtr parameter);
    }
}
