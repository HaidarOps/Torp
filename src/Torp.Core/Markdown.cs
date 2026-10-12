using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Torp.Core
{
    public sealed class LinkRange
    {
        public int Start { get; private set; }
        public int Length { get; private set; }
        public string Target { get; private set; }
        public bool IsWiki { get; private set; }
        public LinkRange(int start, int length, string target, bool wiki)
        {
            Start = start; Length = length; Target = target; IsWiki = wiki;
        }
    }

    public sealed class RenderedMarkdown
    {
        public string Rtf { get; private set; }
        public string Text { get; private set; }
        public IList<LinkRange> Links { get; private set; }
        public RenderedMarkdown(string rtf, string text, IList<LinkRange> links)
        {
            Rtf = rtf; Text = text; Links = links;
        }
    }

    public static class Markdown
    {
        private sealed class Writer
        {
            internal readonly StringBuilder Rtf = new StringBuilder();
            internal readonly StringBuilder Text = new StringBuilder();
            internal readonly List<LinkRange> Links = new List<LinkRange>();

            internal void Append(string value)
            {
                Text.Append(value);
                foreach (char c in value)
                {
                    if (c == '\\' || c == '{' || c == '}') { Rtf.Append('\\').Append(c); }
                    else if (c == '\n') { Rtf.Append(@"\par "); }
                    else if (c == '\t') { Rtf.Append(@"\tab "); }
                    else if (c > 127) { Rtf.Append(@"\u").Append((short)c).Append('?'); }
                    else if (c >= ' ') { Rtf.Append(c); }
                }
            }

            internal void Link(string label, string target, bool wiki)
            {
                Links.Add(new LinkRange(Text.Length, label.Length, target, wiki));
                Rtf.Append(@"{\cf2\ul "); Append(label); Rtf.Append('}');
            }
        }

        public static RenderedMarkdown Render(string markdown, bool dark)
        {
            Writer writer = new Writer();
            writer.Rtf.Append(@"{\rtf1\ansi\deff0\uc1{\fonttbl{\f0 Arial;}{\f1 Courier New;}}{\colortbl ;");
            writer.Rtf.Append(dark ? @"\red219\green219\blue225;\red177\green157\blue255;\red146\green149\blue164;" :
                @"\red39\green40\blue49;\red104\green75\blue186;\red105\green109\blue123;");
            writer.Rtf.Append(@"}\viewkind4\f0\fs23\cf1 ");
            bool code = false;
            string fence = null;
            foreach (string rawLine in (markdown ?? String.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = rawLine;
                string trimmed = line.TrimStart();
                if (IsFence(trimmed))
                {
                    if (!code) { code = true; fence = trimmed.Substring(0, 3); continue; }
                    if (trimmed.StartsWith(fence, StringComparison.Ordinal)) { code = false; continue; }
                }
                writer.Rtf.Append(@"\pard\sa110\sl310\slmult1\f0\fs23 ");
                if (code)
                {
                    writer.Rtf.Append(@"{\f1\fs20\li220 "); writer.Append(line); writer.Rtf.Append('}');
                }
                else
                {
                    int heading = 0;
                    while (heading < line.Length && heading < 6 && line[heading] == '#') { heading++; }
                    if (heading > 0 && heading < line.Length && line[heading] == ' ')
                    {
                        writer.Rtf.Append(@"{\b\sb180\fs").Append(heading == 1 ? 42 : heading == 2 ? 32 : 26).Append(' ');
                        Inline(writer, line.Substring(heading + 1)); writer.Rtf.Append('}');
                    }
                    else if (trimmed == "---" || trimmed == "***" || trimmed == "___")
                    {
                        writer.Rtf.Append(@"{\cf3 "); writer.Append("────────────────────────────"); writer.Rtf.Append('}');
                    }
                    else if (trimmed.StartsWith("> ", StringComparison.Ordinal))
                    {
                        writer.Rtf.Append(@"{\li300\cf3\i "); Inline(writer, trimmed.Substring(2)); writer.Rtf.Append('}');
                    }
                    else
                    {
                        Match list = Regex.Match(trimmed, @"^(?:[-*+] |[0-9]+\. )(.*)$");
                        if (list.Success)
                        {
                            writer.Rtf.Append(@"\li300 ");
                            string item = list.Groups[1].Value;
                            if (item.StartsWith("[ ] ", StringComparison.Ordinal)) { writer.Append("☐  "); item = item.Substring(4); }
                            else if (item.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase)) { writer.Append("☑  "); item = item.Substring(4); }
                            else { writer.Append(Char.IsDigit(trimmed[0]) ? trimmed.Substring(0, trimmed.IndexOf(' ') + 1) : "•  "); }
                            Inline(writer, item);
                        }
                        else { Inline(writer, line); }
                    }
                }
                writer.Append("\n");
            }
            writer.Rtf.Append('}');
            return new RenderedMarkdown(writer.Rtf.ToString(), writer.Text.ToString(), writer.Links);
        }

        private static bool IsFence(string line)
        {
            return line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal);
        }

        private static void Inline(Writer writer, string text)
        {
            int position = 0;
            while (position < text.Length)
            {
                if (text[position] == '\\' && position + 1 < text.Length)
                {
                    writer.Append(text.Substring(position + 1, 1)); position += 2; continue;
                }
                if (text[position] == '`')
                {
                    int endCode = FindWithin(text, "`", position + 1);
                    if (endCode > position + 1)
                    {
                        writer.Rtf.Append(@"{\f1\fs21 "); writer.Append(text.Substring(position + 1, endCode - position - 1)); writer.Rtf.Append('}');
                        position = endCode + 1; continue;
                    }
                }
                if (position + 1 < text.Length && text[position] == '[' && text[position + 1] == '[')
                {
                    int end = FindWithin(text, "]]", position + 2);
                    if (end > position + 2)
                    {
                        string body = text.Substring(position + 2, end - position - 2);
                        string[] parts = body.Split(new char[] { '|' }, 2);
                        writer.Link(parts.Length == 2 ? parts[1] : parts[0], parts[0].Trim(), true);
                        position = end + 2; continue;
                    }
                }
                if (text[position] == '[')
                {
                    int labelEnd = FindWithin(text, "](", position + 1);
                    int urlEnd = labelEnd < 0 ? -1 : FindWithin(text, ")", labelEnd + 2);
                    if (labelEnd > position && urlEnd > labelEnd)
                    {
                        string target = text.Substring(labelEnd + 2, urlEnd - labelEnd - 2);
                        if (IsSafeExternalLink(target))
                        {
                            writer.Link(text.Substring(position + 1, labelEnd - position - 1), target, false);
                            position = urlEnd + 1; continue;
                        }
                    }
                }
                string marker = null;
                string style = null;
                if (position + 1 < text.Length)
                {
                    string pair = text.Substring(position, 2);
                    if (pair == "**" || pair == "__") { marker = pair; style = @"{\b "; }
                    else if (pair == "~~") { marker = pair; style = @"{\strike "; }
                }
                if (marker == null && (text[position] == '*' || text[position] == '_'))
                {
                    marker = text.Substring(position, 1); style = @"{\i ";
                }
                if (marker != null)
                {
                    int end = FindWithin(text, marker, position + marker.Length);
                    if (end > position + marker.Length)
                    {
                        writer.Rtf.Append(style); writer.Append(text.Substring(position + marker.Length, end - position - marker.Length)); writer.Rtf.Append('}');
                        position = end + marker.Length; continue;
                    }
                }
                writer.Append(text.Substring(position, 1)); position++;
            }
        }

        private static int FindWithin(string text, string marker, int start)
        {
            // A bounded scan keeps incomplete markup linear in document size on .NET 4.0,
            // which has no regex timeout. Inline spans longer than 4096 characters are literal.
            return text.IndexOf(marker, start, Math.Min(4096, text.Length - start), StringComparison.Ordinal);
        }

        public static bool IsSafeExternalLink(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeMailto);
        }

        private static string Prose(string text)
        {
            StringBuilder prose = new StringBuilder();
            bool fenced = false;
            string fence = null;
            foreach (string line in (text ?? String.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (IsFence(trimmed))
                {
                    if (!fenced) { fenced = true; fence = trimmed.Substring(0, 3); continue; }
                    if (trimmed.StartsWith(fence, StringComparison.Ordinal)) { fenced = false; continue; }
                }
                if (!fenced) { prose.AppendLine(Regex.Replace(line, @"`[^`]*`", " ")); }
            }
            return prose.ToString();
        }

        public static IList<string> FindWikiLinks(string text)
        {
            List<string> links = new List<string>();
            string prose = Prose(text);
            int position = 0;
            while (position < prose.Length - 1)
            {
                int start = prose.IndexOf("[[", position, StringComparison.Ordinal);
                if (start < 0) { break; }
                int end = FindWithin(prose, "]]", start + 2);
                if ((start == 0 || prose[start - 1] != '\\') && end >= 0)
                {
                    string body = prose.Substring(start + 2, end - start - 2);
                    if (body.IndexOfAny(new char[] { '[', ']', '\r', '\n' }) < 0)
                    {
                        string target = body.Split('|')[0].Trim();
                        if (target.Length > 0) { links.Add(target); }
                        position = end + 2; continue;
                    }
                }
                position = start + 2;
            }
            return links.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static IList<string> FindTags(string text)
        {
            return Regex.Matches(Prose(text), @"(?<![\w/\\])#([\p{L}\p{N}_-]+(?:/[\p{L}\p{N}_-]+)*)").Cast<Match>()
                .Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static int WordCount(string text)
        {
            int count = 0;
            bool inWord = false;
            text = text ?? String.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                if (Char.IsLetterOrDigit(text[i]))
                {
                    if (!inWord) { count++; }
                    inWord = true;
                }
                else if (!(inWord && (text[i] == '\'' || text[i] == '’' || text[i] == '-') &&
                    i + 1 < text.Length && Char.IsLetterOrDigit(text[i + 1]))) { inWord = false; }
            }
            return count;
        }
    }
}
