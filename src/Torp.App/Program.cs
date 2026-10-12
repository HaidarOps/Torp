using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Torp.Core;

namespace Torp.App
{
    internal static class Program
    {
        private static string errorLogPath;
        private static bool startupProbe;
        private static bool uiError;
        [STAThread]
        private static int Main(string[] args)
        {
            startupProbe = Array.IndexOf(args, "--smoke-test") >= 0;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs error)
            {
                uiError = true;
                Report(error.Exception);
                if (startupProbe) { Application.ExitThread(); }
            };
            try
            {
                string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Torp", "settings.xml");
                string requestedVault = null;
                bool explicitSettings = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--vault" && i + 1 < args.Length) { requestedVault = args[++i]; }
                    else if (args[i] == "--settings" && i + 1 < args.Length) { settingsPath = args[++i]; explicitSettings = true; }
                    else if (args[i] != "--smoke-test") { throw new ArgumentException("Usage: Torp.exe [--vault folder] [--settings settings.xml] [--smoke-test]"); }
                }
                if (startupProbe && (requestedVault == null || !explicitSettings)) { throw new ArgumentException("--smoke-test requires explicit --vault and --settings paths to isolated test fixtures."); }
                errorLogPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "errors.log");
                AppSettings settings;
                try { settings = AppSettings.Load(settingsPath); }
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is System.Xml.XmlException) && !(error is UnauthorizedAccessException)) { throw; }
                    MessageBox.Show("Settings could not be read. Torp will use defaults.\n\n" + error.Message, "Torp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    settings = new AppSettings();
                }
                if (requestedVault != null) { settings.VaultPath = requestedVault; settings.LastNote = String.Empty; }
                if (String.IsNullOrEmpty(settings.VaultPath))
                {
                    settings.VaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Torp Notes");
                }
                using (MainForm form = new MainForm(settings, settingsPath))
                {
                    if (startupProbe) { return VerifyStartup(form); }
                    Application.Run(form);
                }
                return uiError ? 1 : 0;
            }
            catch (Exception error) { Report(error); return 1; }
        }

        private static int VerifyStartup(MainForm form)
        {
            bool verified = false;
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            using (Timer timer = new Timer())
            {
                timer.Interval = 100;
                timer.Tick += delegate
                {
                    if (form.IsIndexing && DateTime.UtcNow < deadline) { return; }
                    verified = !form.IsIndexing && form.Index.Notes.Count > 0 && form.Session.Document != null &&
                        form.EditorControl.IsHandleCreated && form.PreviewControl.IsHandleCreated && !form.HasAlert;
                    timer.Stop(); form.Close();
                };
                form.Shown += delegate { timer.Start(); };
                Application.Run(form);
            }
            return verified && !uiError ? 0 : 2;
        }

        internal static void Report(Exception error)
        {
            // Do not record exception data or stack arguments containing document text.
            if (errorLogPath != null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(errorLogPath));
                    if (!File.Exists(errorLogPath) || new FileInfo(errorLogPath).Length < 1024 * 1024)
                    {
                        File.AppendAllText(errorLogPath, DateTime.UtcNow.ToString("o") + " " + error.GetType().FullName + "\n" + error.StackTrace + "\n\n", Encoding.UTF8);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (startupProbe) { Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); return; }
            MessageBox.Show("Torp could not complete the operation. Your notes remain in their vault.\n\n" + error.Message,
                "Torp", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
