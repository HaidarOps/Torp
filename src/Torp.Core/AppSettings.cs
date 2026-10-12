using System;
using System.IO;
using System.Xml;

namespace Torp.Core
{
    public sealed class AppSettings
    {
        public string VaultPath { get; set; }
        public string LastNote { get; set; }
        public bool DarkTheme { get; set; }
        public bool Preview { get; set; }
        public int FontSize { get; set; }
        public AppSettings() { VaultPath = String.Empty; LastNote = String.Empty; DarkTheme = true; Preview = true; FontSize = 12; }

        public static AppSettings Load(string path)
        {
            AppSettings settings = new AppSettings();
            if (!File.Exists(path)) { return settings; }
            XmlReaderSettings options = new XmlReaderSettings();
            options.DtdProcessing = DtdProcessing.Prohibit;
            options.XmlResolver = null;
            options.MaxCharactersInDocument = 16384;
            using (XmlReader reader = XmlReader.Create(path, options))
            {
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                document.Load(reader);
                XmlElement root = document.DocumentElement;
                if (root == null || root.Name != "torp") { throw new XmlException("Invalid Torp settings."); }
                settings.VaultPath = root.GetAttribute("vault");
                settings.LastNote = root.GetAttribute("note");
                settings.DarkTheme = root.GetAttribute("dark") != "false";
                settings.Preview = root.GetAttribute("preview") != "false";
                int size;
                if (Int32.TryParse(root.GetAttribute("fontSize"), out size)) { settings.FontSize = Math.Max(9, Math.Min(24, size)); }
            }
            return settings;
        }

        public void Save(string path)
        {
            XmlDocument document = new XmlDocument();
            XmlElement root = document.CreateElement("torp");
            root.SetAttribute("vault", VaultPath ?? String.Empty);
            root.SetAttribute("note", LastNote ?? String.Empty);
            root.SetAttribute("dark", DarkTheme ? "true" : "false");
            root.SetAttribute("preview", Preview ? "true" : "false");
            root.SetAttribute("fontSize", FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
            document.AppendChild(root);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            AtomicFile.Write(path, document.OuterXml, null);
        }
    }
}
