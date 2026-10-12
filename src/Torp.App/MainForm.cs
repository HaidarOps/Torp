using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Torp.Core;

namespace Torp.App
{
    internal sealed class MainForm : Form
    {
        private readonly AppSettings settings;
        private readonly string settingsPath;
        private readonly object workerGate = new object();
        private Vault vault;
        private EditorSession session;
        private VaultIndex index = new VaultIndex(new NoteInfo[0]);
        private Theme theme;
        private FileSystemWatcher watcher;
        private readonly BackgroundWorker indexWorker = new BackgroundWorker();
        private Vault indexingVault;
        private bool workerReading;
        private bool indexPending;
        private bool indexAgain;
        private volatile bool closing;
        private bool loading;
        private bool autosaveBlocked;
        private int refreshRequested;
        private readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer previewTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer();
        private RenderedMarkdown rendered;
        private readonly MenuStrip menu = new MenuStrip();
        private readonly ToolStrip toolbar = new ToolStrip();
        private readonly StatusStrip status = new StatusStrip();
        private readonly ToolStripStatusLabel stateLabel = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel countLabel = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel indexLabel = new ToolStripStatusLabel();
        private readonly ToolStripLabel vaultLabel = new ToolStripLabel();
        private readonly TextBox search = new TextBox();
        private readonly TreeView explorer = new TreeView();
        private readonly RichTextBox editor = new RichTextBox();
        private readonly RichTextBox preview = new RichTextBox();
        private readonly SplitContainer pages = new SplitContainer();
        private readonly SplitContainer workspace = new SplitContainer();
        private readonly SplitContainer detail = new SplitContainer();
        private readonly Label title = new Label();
        private readonly Label breadcrumb = new Label();
        private readonly Label alert = new Label();
        private readonly Panel alertPanel = new Panel();
        private readonly ListBox backlinks = new ListBox();
        private readonly ListBox outgoing = new ListBox();
        private readonly ListBox tags = new ListBox();
        private readonly GraphView graph = new GraphView();
        private readonly Stack<string> backHistory = new Stack<string>();
        private readonly Stack<string> forwardHistory = new Stack<string>();
        private readonly Dictionary<string, Action> commands = new Dictionary<string, Action>();

        internal RichTextBox EditorControl { get { return editor; } }
        internal RichTextBox PreviewControl { get { return preview; } }
        internal TreeView ExplorerControl { get { return explorer; } }
        internal EditorSession Session { get { return session; } }
        internal VaultIndex Index { get { return index; } }
        internal bool IsIndexing { get { return indexPending; } }
        internal bool HasAlert { get { return alertPanel.Visible; } }

        internal MainForm(AppSettings settings, string settingsPath)
        {
            this.settings = settings; this.settingsPath = settingsPath;
            theme = new Theme(settings.DarkTheme);
            Text = "Torp"; Font = new Font("Tahoma", 9F);
            Size = new Size(1220, 800); MinimumSize = new Size(880, 550);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            BuildMenu(); BuildWorkspace();
            Controls.Add(workspace); Controls.Add(toolbar); Controls.Add(menu); Controls.Add(status);
            MainMenuStrip = menu;
            ApplyTheme();
            WireEvents();
            OpenVault(settings.VaultPath);
        }

        private void AddCommand(ToolStripMenuItem parent, string name, Keys shortcut, Action action)
        {
            commands[name.Replace("&", String.Empty)] = action;
            ToolStripMenuItem item = new ToolStripMenuItem(name);
            item.ShortcutKeys = shortcut;
            item.Click += delegate { Guard(action); };
            parent.DropDownItems.Add(item);
        }

        private void BuildMenu()
        {
            ToolStripMenuItem file = new ToolStripMenuItem("&File");
            AddCommand(file, "&New note", Keys.Control | Keys.N, NewNote);
            AddCommand(file, "Open &vault…", Keys.Control | Keys.Shift | Keys.O, ChooseVault);
            AddCommand(file, "&Daily note", Keys.Control | Keys.D, DailyNote);
            AddCommand(file, "&Save", Keys.Control | Keys.S, delegate { SaveCurrent(); });
            AddCommand(file, "Save a &copy…", Keys.Control | Keys.Shift | Keys.S, SaveCopy);
            file.DropDownItems.Add(new ToolStripSeparator());
            AddCommand(file, "&Rename or move note…", Keys.F2, RenameNote);
            AddCommand(file, "Move note to &trash", Keys.Control | Keys.Delete, TrashNote);
            AddCommand(file, "Restore from trash…", Keys.None, RestoreTrash);
            AddCommand(file, "Version &history…", Keys.None, ShowHistory);
            AddCommand(file, "&Export Markdown…", Keys.None, ExportNote);
            file.DropDownItems.Add(new ToolStripSeparator());
            AddCommand(file, "E&xit", Keys.Alt | Keys.F4, Close);
            ToolStripMenuItem edit = new ToolStripMenuItem("&Edit");
            AddCommand(edit, "&Undo", Keys.Control | Keys.Z, delegate { TextBoxBase input = FocusedTextBox(); if (input != null && !input.ReadOnly) { input.Undo(); } });
            AddCommand(edit, "&Redo", Keys.Control | Keys.Y, delegate { if (editor.Focused) { editor.Redo(); } });
            AddCommand(edit, "Cu&t", Keys.Control | Keys.X, delegate { TextBoxBase input = FocusedTextBox(); if (input != null && !input.ReadOnly) { input.Cut(); } });
            AddCommand(edit, "&Copy", Keys.Control | Keys.C, delegate { TextBoxBase input = FocusedTextBox(); if (input != null) { input.Copy(); } });
            AddCommand(edit, "&Paste", Keys.Control | Keys.V, delegate { TextBoxBase input = FocusedTextBox(); if (input != null && !input.ReadOnly) { input.Paste(); } });
            AddCommand(edit, "Select &all", Keys.Control | Keys.A, delegate { TextBoxBase input = FocusedTextBox(); if (input != null) { input.SelectAll(); } });
            AddCommand(edit, "&Bold", Keys.Control | Keys.B, delegate { WrapSelection("**"); });
            AddCommand(edit, "&Italic", Keys.Control | Keys.I, delegate { WrapSelection("*"); });
            AddCommand(edit, "Insert wiki &link", Keys.Control | Keys.K, InsertWikiLink);
            AddCommand(edit, "&Find in note…", Keys.Control | Keys.F, FindInNote);
            AddCommand(edit, "Search &vault", Keys.Control | Keys.Shift | Keys.F, delegate { search.Focus(); search.SelectAll(); });
            ToolStripMenuItem view = new ToolStripMenuItem("&View");
            AddCommand(view, "&Quick switcher…", Keys.Control | Keys.O, QuickSwitcher);
            AddCommand(view, "&Command palette…", Keys.Control | Keys.P, CommandPalette);
            AddCommand(view, "Toggle &reading view", Keys.Control | Keys.E, TogglePreview);
            AddCommand(view, "Toggle &graph", Keys.Control | Keys.G, ToggleGraph);
            AddCommand(view, "Toggle &light / dark theme", Keys.Control | Keys.L, ToggleTheme);
            AddCommand(view, "Navigate &back", Keys.Alt | Keys.Left, delegate { NavigateHistory(backHistory, forwardHistory); });
            AddCommand(view, "Navigate &forward", Keys.Alt | Keys.Right, delegate { NavigateHistory(forwardHistory, backHistory); });
            AddCommand(view, "&Refresh vault", Keys.F5, delegate { RefreshExternal(); RequestIndex(); });
            AddCommand(view, "Larger editor text", Keys.Control | Keys.Oemplus, delegate { SetEditorSize(settings.FontSize + 1); });
            AddCommand(view, "Smaller editor text", Keys.Control | Keys.OemMinus, delegate { SetEditorSize(settings.FontSize - 1); });
            ToolStripMenuItem help = new ToolStripMenuItem("&Help");
            AddCommand(help, "Keyboard shortcuts and &help", Keys.F1, ShowHelp);
            AddCommand(help, "&About Torp", Keys.None, delegate
            {
                MessageBox.Show(this, "Torp 1.0\nA quiet place for connected ideas.\n\nPlain Markdown files · Native Windows · Works offline\nTarget: Windows XP SP3 / .NET Framework 4.0\n\nAn independent notebook inspired by Obsidian.", "About Torp", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            menu.Items.AddRange(new ToolStripItem[] { file, edit, view, help });
            menu.Padding = new Padding(7, 3, 0, 3);
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.Padding = new Padding(12, 8, 12, 8);
            toolbar.Items.Add(new ToolStripLabel("T O R P") { Font = new Font("Tahoma", 11F, FontStyle.Bold) });
            toolbar.Items.Add(new ToolStripSeparator());
            AddTool("+ New note", NewNote); AddTool("Today", DailyNote); AddTool("Quick open", QuickSwitcher);
            AddTool("Graph", ToggleGraph); AddTool("Reading view", TogglePreview);
            vaultLabel.Alignment = ToolStripItemAlignment.Right;
            toolbar.Items.Add(vaultLabel);
            AddTool("Open vault", ChooseVault);
            stateLabel.Spring = true; stateLabel.TextAlign = ContentAlignment.MiddleLeft;
            indexLabel.IsLink = true;
            indexLabel.Click += delegate
            {
                MessageBox.Show(this, index.Warnings.Count == 0 ? "All discovered notes are indexed." : String.Join("\n", index.Warnings.Take(20).ToArray()), "Vault index");
            };
            status.Items.AddRange(new ToolStripItem[] { stateLabel, countLabel, indexLabel });
            status.Padding = new Padding(10, 3, 10, 3);
        }

        private void AddTool(string name, Action action)
        {
            ToolStripButton button = new ToolStripButton(name);
            button.Padding = new Padding(6, 1, 6, 1);
            button.Click += delegate { Guard(action); };
            toolbar.Items.Add(button);
        }

        private TextBoxBase FocusedTextBox()
        {
            if (search.Focused) { return search; }
            if (preview.Focused) { return preview; }
            return editor.Focused ? editor : null;
        }

        private static Label Caption(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Top, Height = 35, Padding = new Padding(12, 11, 0, 0), Tag = "muted", Font = new Font("Tahoma", 8F) };
        }

        private void BuildWorkspace()
        {
            workspace.Dock = DockStyle.Fill; workspace.Size = new Size(1180, 660);
            workspace.SplitterDistance = 220; workspace.Panel1MinSize = 170; workspace.Panel2MinSize = 600;
            workspace.FixedPanel = FixedPanel.Panel1;
            Panel searchPanel = new Panel { Dock = DockStyle.Top, Height = 80, Padding = new Padding(12, 8, 12, 12) };
            Label explorerTitle = Caption("SEARCH & EXPLORER"); explorerTitle.Padding = new Padding(0, 2, 0, 0); explorerTitle.Height = 28;
            search.Dock = DockStyle.Bottom; search.BorderStyle = BorderStyle.FixedSingle;
            search.AccessibleName = "Search notes. Supports tag:, path:, quoted phrases, and -exclusions.";
            searchPanel.Controls.Add(search); searchPanel.Controls.Add(explorerTitle);
            explorer.Dock = DockStyle.Fill; explorer.BorderStyle = BorderStyle.None;
            explorer.ShowLines = false; explorer.ShowRootLines = false; explorer.FullRowSelect = true;
            explorer.HideSelection = false; explorer.ItemHeight = 29; explorer.Indent = 16;
            explorer.AccessibleName = "Vault folders and notes";
            Label searchHint = new Label { Text = "Ctrl+P  Command palette\nCtrl+O  Jump to a note", Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(12, 10, 0, 0), Tag = "muted" };
            workspace.Panel1.Controls.Add(explorer); workspace.Panel1.Controls.Add(searchPanel); workspace.Panel1.Controls.Add(searchHint);
            detail.Dock = DockStyle.Fill; detail.Size = new Size(950, 660); detail.SplitterDistance = 740;
            detail.FixedPanel = FixedPanel.Panel2; detail.Panel1MinSize = 390; detail.Panel2MinSize = 155;
            workspace.Panel2.Controls.Add(detail);
            TableLayoutPanel inspector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); inspector.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); inspector.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); inspector.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            inspector.Controls.Add(Caption("BACKLINKS"), 0, 0); inspector.Controls.Add(backlinks, 0, 1);
            inspector.Controls.Add(Caption("OUTGOING LINKS"), 0, 2); inspector.Controls.Add(outgoing, 0, 3);
            inspector.Controls.Add(Caption("TAGS"), 0, 4); inspector.Controls.Add(tags, 0, 5);
            foreach (ListBox list in new ListBox[] { backlinks, outgoing, tags })
            {
                list.Dock = DockStyle.Fill; list.BorderStyle = BorderStyle.None; list.IntegralHeight = false;
                list.Margin = new Padding(12, 0, 4, 4); list.HorizontalScrollbar = true;
            }
            backlinks.AccessibleName = "Backlinks"; outgoing.AccessibleName = "Outgoing wiki links"; tags.AccessibleName = "Note tags";
            detail.Panel2.Controls.Add(inspector);
            Panel heading = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(18, 9, 10, 9) };
            breadcrumb.Dock = DockStyle.Top; breadcrumb.Height = 23; breadcrumb.Tag = "muted";
            title.Dock = DockStyle.Fill; title.Font = new Font("Tahoma", 17F, FontStyle.Bold);
            heading.Controls.Add(title); heading.Controls.Add(breadcrumb);
            alertPanel.Dock = DockStyle.Top; alertPanel.Height = 84; alertPanel.Visible = false;
            alert.Dock = DockStyle.Fill; alert.Padding = new Padding(12, 6, 12, 0);
            FlowLayoutPanel alertActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 31, Padding = new Padding(10, 0, 0, 0) };
            Button saveCopy = new Button { Text = "Save a copy…", Width = 108 };
            Button reload = new Button { Text = "Reload disk version", Width = 144 };
            saveCopy.Click += delegate { Guard(SaveCopy); }; reload.Click += delegate { Guard(ReloadFromDisk); };
            alertActions.Controls.Add(saveCopy); alertActions.Controls.Add(reload);
            alertPanel.Controls.Add(alert); alertPanel.Controls.Add(alertActions);
            pages.Dock = DockStyle.Fill; pages.Size = new Size(730, 560); pages.SplitterDistance = 340;
            pages.Panel1MinSize = 150; pages.Panel2MinSize = 150;
            editor.Dock = DockStyle.Fill; editor.BorderStyle = BorderStyle.None; editor.AcceptsTab = true;
            editor.DetectUrls = false; editor.HideSelection = false; editor.WordWrap = true;
            editor.ScrollBars = RichTextBoxScrollBars.Vertical; editor.AccessibleName = "Markdown editor";
            editor.MaxLength = Vault.MaximumNoteBytes;
            preview.Dock = DockStyle.Fill; preview.BorderStyle = BorderStyle.None; preview.ReadOnly = true;
            preview.DetectUrls = false; preview.AccessibleName = "Markdown reading view. Click wiki links to navigate.";
            preview.ScrollBars = RichTextBoxScrollBars.Vertical;
            Panel editPadding = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 10, 14, 10) };
            Panel previewPadding = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 10, 16, 10) };
            editPadding.Controls.Add(editor); previewPadding.Controls.Add(preview);
            pages.Panel1.Controls.Add(editPadding); pages.Panel1.Controls.Add(Caption("MARKDOWN"));
            pages.Panel2.Controls.Add(previewPadding); pages.Panel2.Controls.Add(Caption("READING VIEW"));
            pages.Panel2Collapsed = !settings.Preview;
            graph.Visible = false;
            detail.Panel1.Controls.Add(pages); detail.Panel1.Controls.Add(graph);
            detail.Panel1.Controls.Add(alertPanel); detail.Panel1.Controls.Add(heading);
        }

        private void WireEvents()
        {
            editor.TextChanged += delegate
            {
                if (loading || session == null || session.Document == null) { return; }
                session.Text = editor.Text; autosaveBlocked = false;
                saveTimer.Stop(); saveTimer.Start(); previewTimer.Stop(); previewTimer.Start(); UpdateStatus();
            };
            saveTimer.Interval = 900;
            saveTimer.Tick += delegate { saveTimer.Stop(); if (!autosaveBlocked) { SaveCurrent(); } };
            previewTimer.Interval = 300;
            previewTimer.Tick += delegate { previewTimer.Stop(); RenderPreview(); RefreshRelations(); };
            refreshTimer.Interval = 1500;
            refreshTimer.Tick += delegate
            {
                if (Interlocked.Exchange(ref refreshRequested, 0) != 0) { Guard(RefreshExternal); RequestIndex(); }
            };
            searchTimer.Interval = 180;
            searchTimer.Tick += delegate { searchTimer.Stop(); RefreshExplorer(); };
            search.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); };
            search.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Escape) { search.Clear(); editor.Focus(); args.SuppressKeyPress = true; }
                else if (args.KeyCode == Keys.Enter && explorer.Nodes.Count > 0) { explorer.Focus(); explorer.SelectedNode = explorer.Nodes[0]; args.SuppressKeyPress = true; }
            };
            explorer.AfterSelect += delegate(object sender, TreeViewEventArgs args)
            {
                string path = args.Node.Tag as string;
                if (!loading && path != null) { Guard(delegate { OpenNote(path); }); }
            };
            backlinks.DoubleClick += delegate { if (backlinks.SelectedItem is NoteChoice) { Guard(delegate { OpenNote(((NoteChoice)backlinks.SelectedItem).Path); }); } };
            outgoing.DoubleClick += delegate { if (outgoing.SelectedItem is string) { Guard(delegate { FollowWiki((string)outgoing.SelectedItem); }); } };
            tags.DoubleClick += delegate { if (tags.SelectedItem is string) { search.Text = "tag:" + tags.SelectedItem; } };
            foreach (ListBox list in new ListBox[] { backlinks, outgoing, tags })
            {
                list.KeyDown += delegate(object sender, KeyEventArgs args)
                {
                    if (args.KeyCode != Keys.Enter) { return; }
                    if (sender == backlinks && backlinks.SelectedItem is NoteChoice) { Guard(delegate { OpenNote(((NoteChoice)backlinks.SelectedItem).Path); }); }
                    if (sender == outgoing && outgoing.SelectedItem is string) { Guard(delegate { FollowWiki((string)outgoing.SelectedItem); }); }
                    if (sender == tags && tags.SelectedItem is string) { search.Text = "tag:" + tags.SelectedItem; }
                    args.SuppressKeyPress = true;
                };
            }
            preview.MouseMove += delegate(object sender, MouseEventArgs args) { preview.Cursor = PreviewLinkAt(args.Location) == null ? Cursors.IBeam : Cursors.Hand; };
            preview.MouseClick += delegate(object sender, MouseEventArgs args)
            {
                LinkRange link = PreviewLinkAt(args.Location);
                if (link == null || args.Button != MouseButtons.Left) { return; }
                Guard(delegate
                {
                    if (link.IsWiki) { FollowWiki(link.Target); }
                    else if (Markdown.IsSafeExternalLink(link.Target)) { Process.Start(new ProcessStartInfo(link.Target) { UseShellExecute = true }); }
                });
            };
            graph.NoteClicked += delegate(string path) { Guard(delegate { OpenNote(path); }); };
            indexWorker.DoWork += delegate(object sender, DoWorkEventArgs args)
            {
                Vault target = (Vault)args.Argument;
                try { args.Result = VaultIndex.Build(target); }
                finally
                {
                    lock (workerGate)
                    {
                        workerReading = false;
                        if (closing || !Object.ReferenceEquals(target, vault)) { target.Dispose(); }
                    }
                }
            };
            indexWorker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs args)
            {
                if (closing || IsDisposed || !IsHandleCreated) { return; }
                // BackgroundWorker may capture a default context between desktop windows.
                // Marshal explicitly; never depend on an ambient synchronization context.
                if (InvokeRequired)
                {
                    try { BeginInvoke(new Action(delegate { CompleteIndex(args); })); }
                    catch (InvalidOperationException) { if (!closing) { throw; } }
                }
                else { CompleteIndex(args); }
            };
            FormClosing += OnClosing;
            Shown += delegate { editor.Focus(); refreshTimer.Start(); indexAgain = false; RequestIndex(); };
        }

        private void CompleteIndex(RunWorkerCompletedEventArgs args)
        {
            if (closing || IsDisposed) { return; }
            indexPending = false;
            if (Object.ReferenceEquals(indexingVault, vault))
            {
                if (args.Error != null) { ShowAlert("Could not refresh the vault: " + args.Error.Message); }
                else
                {
                    index = (VaultIndex)args.Result;
                    RefreshExplorer(); RefreshRelations(); UpdateGraph(); UpdateStatus();
                }
            }
            if (indexAgain) { indexAgain = false; RequestIndex(); }
        }

        private LinkRange PreviewLinkAt(Point point)
        {
            if (rendered == null) { return null; }
            int position = preview.GetCharIndexFromPosition(point);
            Point actual = preview.GetPositionFromCharIndex(position);
            if (Math.Abs(actual.Y - point.Y) > preview.Font.Height + 8 || Math.Abs(actual.X - point.X) > 24) { return null; }
            return rendered.Links.FirstOrDefault(link => position >= link.Start && position < link.Start + link.Length);
        }

        private void OpenVault(string path)
        {
            Vault next = new Vault(path);
            try
            {
                IList<string> paths = next.GetPaths();
                if (paths.Count == 0)
                {
                    next.Create("Welcome to Torp", WelcomeText);
                    next.Create("Getting started", GettingStartedText);
                    next.Create("Ideas/A place for ideas", "# A place for ideas\n\nGood ideas rarely arrive fully formed. Give them somewhere to land.\n\n- Capture a question\n- Follow a connection\n- Come back tomorrow\n\n[[Welcome to Torp]]\n\n#ideas\n");
                    paths = next.GetPaths();
                }
                EditorSession nextSession = new EditorSession(next);
                string initial = paths.FirstOrDefault(p => String.Equals(p, settings.LastNote, StringComparison.OrdinalIgnoreCase)) ??
                    paths.FirstOrDefault(p => p == "Welcome to Torp.md") ?? paths.FirstOrDefault();
                // A single unreadable note should not prevent opening the rest of a vault.
                string initialError = null;
                if (initial != null)
                {
                    try { nextSession.Open(initial); }
                    catch (Exception error)
                    {
                        if (!(error is IOException) && !(error is ArgumentException) && !(error is UnauthorizedAccessException)) { throw; }
                        initialError = "Could not open " + initial + ": " + error.Message;
                    }
                }
                if (watcher != null) { watcher.Dispose(); watcher = null; }
                lock (workerGate)
                {
                    Vault previous = vault; vault = next; session = nextSession;
                    if (previous != null && (!workerReading || !Object.ReferenceEquals(previous, indexingVault))) { previous.Dispose(); }
                }
                settings.VaultPath = next.Root; backHistory.Clear(); forwardHistory.Clear();
                index = new VaultIndex(new NoteInfo[0]); search.Clear();
                vaultLabel.Text = new DirectoryInfo(next.Root).Name;
                LoadEditor();
                if (initialError != null) { ShowAlert(initialError); }
                try
                {
                    watcher = new FileSystemWatcher(next.Root);
                    watcher.IncludeSubdirectories = true;
                    watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
                    watcher.Changed += WatchChanged; watcher.Created += WatchChanged; watcher.Deleted += WatchChanged; watcher.Renamed += WatchChanged;
                    watcher.Error += delegate { Interlocked.Exchange(ref refreshRequested, 1); };
                    watcher.EnableRaisingEvents = true;
                }
                catch (IOException) { ShowAlert("Automatic folder monitoring is unavailable. Press F5 to refresh external changes."); }
                RequestIndex();
            }
            catch { if (!Object.ReferenceEquals(next, vault)) { next.Dispose(); } throw; }
        }

        private void WatchChanged(object sender, FileSystemEventArgs args)
        {
            if (closing) { return; }
            string relative = args.FullPath.Substring(((FileSystemWatcher)sender).Path.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar);
            if (relative.StartsWith(".torp", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(relative).StartsWith(".torp-", StringComparison.Ordinal)) { return; }
            Interlocked.Exchange(ref refreshRequested, 1);
        }

        internal void RequestIndex()
        {
            if (closing || vault == null) { return; }
            if (!IsHandleCreated || indexPending) { indexAgain = true; return; }
            indexLabel.Text = "Indexing…";
            indexPending = true;
            lock (workerGate) { indexingVault = vault; workerReading = true; }
            indexWorker.RunWorkerAsync(vault);
        }

        private void RefreshExplorer()
        {
            if (session == null) { return; }
            loading = true;
            explorer.BeginUpdate();
            try
            {
                HashSet<string> expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectExpanded(explorer.Nodes, expanded);
                explorer.Nodes.Clear();
                IList<NoteInfo> notes = index.Search(search.Text);
                bool flat = !String.IsNullOrWhiteSpace(search.Text);
                Dictionary<string, TreeNode> folders = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
                foreach (NoteInfo note in notes)
                {
                    TreeNodeCollection parent = explorer.Nodes;
                    string[] parts = note.RelativePath.Split('/');
                    string folderPath = String.Empty;
                    if (!flat)
                    {
                        for (int i = 0; i < parts.Length - 1; i++)
                        {
                            folderPath += parts[i] + "/";
                            TreeNode folder;
                            if (!folders.TryGetValue(folderPath, out folder))
                            {
                                folder = new TreeNode(parts[i]) { Name = folderPath };
                                parent.Add(folder); folders.Add(folderPath, folder);
                            }
                            parent = folder.Nodes;
                        }
                    }
                    TreeNode node = new TreeNode(flat ? note.RelativePath : note.Title) { Tag = note.RelativePath, ToolTipText = note.RelativePath };
                    parent.Add(node);
                    if (session.Document != null && String.Equals(session.Document.RelativePath, note.RelativePath, StringComparison.OrdinalIgnoreCase)) { explorer.SelectedNode = node; }
                }
                foreach (KeyValuePair<string, TreeNode> folder in folders) { if (expanded.Contains(folder.Key) || expanded.Count == 0) { folder.Value.Expand(); } }
                if (notes.Count == 0) { explorer.Nodes.Add(new TreeNode(flat ? "No matching notes" : "No notes yet") { ForeColor = theme.Muted }); }
            }
            finally { explorer.EndUpdate(); loading = false; }
        }

        private static void CollectExpanded(TreeNodeCollection nodes, HashSet<string> expanded)
        {
            foreach (TreeNode node in nodes) { if (node.IsExpanded) { expanded.Add(node.Name); } CollectExpanded(node.Nodes, expanded); }
        }

        internal void OpenNote(string path)
        {
            if (session.Document != null && String.Equals(session.Document.RelativePath, path, StringComparison.OrdinalIgnoreCase)) { graph.Visible = false; pages.Visible = true; return; }
            string old = session.Document == null ? null : session.Document.RelativePath;
            session.Open(path);
            if (old != null) { backHistory.Push(old); forwardHistory.Clear(); }
            LoadEditor();
        }

        private void LoadEditor()
        {
            loading = true;
            try
            {
                editor.Text = session.Text; editor.ClearUndo(); editor.ReadOnly = session.Document == null;
                title.Text = session.Document == null ? "Your next idea starts here" : Path.GetFileNameWithoutExtension(session.Document.RelativePath);
                breadcrumb.Text = session.Document == null ? "Create a note with Ctrl+N" : vaultLabel.Text + "  /  " + session.Document.RelativePath;
                alertPanel.Visible = false; autosaveBlocked = false;
                pages.Visible = true; graph.Visible = false;
                RenderPreview(); RefreshRelations(); UpdateStatus();
            }
            finally { loading = false; }
        }

        internal bool SaveCurrent()
        {
            if (session == null || !session.IsDirty) { return true; }
            try
            {
                session.Save(); alertPanel.Visible = false; autosaveBlocked = false;
                UpdateStatus(); RequestIndex(); return true;
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is ArgumentException)) { throw; }
                autosaveBlocked = true; ShowAlert(error.Message); UpdateStatus(); return false;
            }
        }

        private bool RequireSave() { return SaveCurrent(); }

        private void RefreshExternal()
        {
            if (session.Document == null) { return; }
            try
            {
                NoteDocument current = vault.Read(session.Document.RelativePath);
                if (current.Revision == session.Document.Revision) { return; }
                if (session.IsDirty) { autosaveBlocked = true; ShowAlert("This note changed outside Torp. Save your edits as a copy, or reload the disk version."); }
                else { session.Reload(); LoadEditor(); }
            }
            catch (IOException error) { autosaveBlocked = true; ShowAlert(error.Message + " Save a copy to keep this open note."); }
        }

        private void RenderPreview()
        {
            if (session == null) { return; }
            // Editing large documents remains responsive; full text still saves and searches.
            string text = session.Text;
            if (text.Length > 200000) { text = text.Substring(0, 200000) + "\n\n> Reading view is limited to the first 200,000 characters."; }
            rendered = Markdown.Render(text, settings.DarkTheme);
            preview.Rtf = rendered.Rtf;
            UpdateStatus();
        }

        private void RefreshRelations()
        {
            backlinks.Items.Clear(); outgoing.Items.Clear(); tags.Items.Clear();
            if (session == null || session.Document == null) { return; }
            foreach (NoteInfo note in index.Backlinks(session.Document.RelativePath)) { backlinks.Items.Add(new NoteChoice(note.RelativePath)); }
            foreach (string link in Markdown.FindWikiLinks(session.Text)) { outgoing.Items.Add(link); }
            foreach (string tag in Markdown.FindTags(session.Text)) { tags.Items.Add(tag); }
            if (backlinks.Items.Count == 0) { backlinks.Items.Add(new EmptyChoice("No linked mentions")); }
            if (outgoing.Items.Count == 0) { outgoing.Items.Add(new EmptyChoice("Add a [[wiki link]]")); }
            if (tags.Items.Count == 0) { tags.Items.Add(new EmptyChoice("Add #tags to organize")); }
        }

        private void UpdateStatus()
        {
            if (session == null) { return; }
            string path = session.Document == null ? "Torp" : Path.GetFileNameWithoutExtension(session.Document.RelativePath) + " — Torp";
            Text = (session.IsDirty ? "● " : String.Empty) + path;
            stateLabel.Text = session.IsDirty ? (autosaveBlocked ? "Not saved — your edits are still in the editor" : "Editing…") : "All changes saved locally";
            countLabel.Text = Markdown.WordCount(session.Text).ToString("N0") + " words  ·  " + session.Text.Length.ToString("N0") + " characters   ";
            if (!indexPending) { indexLabel.Text = index.Notes.Count + " notes" + (index.Warnings.Count > 0 ? " · " + index.Warnings.Count + " warnings" : String.Empty); }
        }

        private void NewNote()
        {
            if (!RequireSave()) { return; }
            string name = Dialogs.Prompt(this, "New note", "Name your note. Use a folder/name to organize it.", "Untitled");
            if (name == null) { return; }
            CreateNote(name, "# " + Path.GetFileNameWithoutExtension(name) + "\n\n");
        }

        internal void CreateNote(string path, string text)
        {
            if (!RequireSave()) { return; }
            session.Attach(vault.Create(path, text)); LoadEditor(); RequestIndex(); editor.Focus(); editor.SelectionStart = editor.TextLength;
        }

        private void DailyNote()
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            string path = "Daily/" + date + ".md";
            if (vault.Exists(path)) { OpenNote(path); }
            else { CreateNote(path, "# " + DateTime.Now.ToString("dddd, d MMMM yyyy") + "\n\n## On my mind\n\n\n## Today\n\n- [ ] \n\n#daily\n"); }
        }

        private void SaveCopy()
        {
            if (session.Document == null) { return; }
            string name = Dialogs.Prompt(this, "Save a copy", "Your current editor text will be saved as a new note.", Path.GetFileNameWithoutExtension(session.Document.RelativePath) + " (copy)");
            if (name == null) { return; }
            session.SaveCopy(name); LoadEditor(); RequestIndex();
        }

        private void ReloadFromDisk()
        {
            if (session.Document == null) { return; }
            if (session.IsDirty && MessageBox.Show(this, "Discard your unsaved edits and reload the version on disk? Use Save a copy to keep both versions.", "Reload note", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) { return; }
            session.Reload(); LoadEditor(); RequestIndex();
        }

        private void RenameNote()
        {
            if (session.Document == null || !RequireSave()) { return; }
            string name = Dialogs.Prompt(this, "Rename or move note", "Use folder/name.md. Existing wiki links and history stay at the old path; update links after renaming.", session.Document.RelativePath);
            if (name == null) { return; }
            session.Attach(vault.Rename(session.Document, name)); LoadEditor(); RequestIndex();
        }

        private void TrashNote()
        {
            if (session.Document == null || !RequireSave()) { return; }
            if (MessageBox.Show(this, "Move “" + session.Document.RelativePath + "” to vault trash? You can restore it from File → Restore from trash.", "Move to trash", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) { return; }
            vault.Trash(session.Document); session.Attach(null); LoadEditor(); RequestIndex();
        }

        private void RestoreTrash()
        {
            if (!RequireSave()) { return; }
            IList<TrashEntry> entries = vault.GetTrash();
            if (entries.Count == 0) { MessageBox.Show(this, "The vault trash is empty.", "Restore from trash"); return; }
            TrashEntry entry = Dialogs.Pick(this, "Restore a deleted note", entries, e => e.ToString(), theme);
            if (entry == null) { return; }
            session.Attach(vault.Restore(entry)); LoadEditor(); RequestIndex();
        }

        private void ShowHistory()
        {
            if (session.Document == null || !RequireSave()) { return; }
            IList<string> history = vault.GetHistory(session.Document.RelativePath);
            if (history.Count == 0) { MessageBox.Show(this, "Previous versions appear here after a saved edit.", "Version history"); return; }
            string version = Dialogs.Pick(this, "Version history · newest first (UTC)", history, v => v.Substring(0, 4) + "-" + v.Substring(4, 2) + "-" + v.Substring(6, 2) + "  " + v.Substring(8, 2) + ":" + v.Substring(10, 2) + ":" + v.Substring(12, 2), theme);
            if (version == null) { return; }
            string previous = vault.ReadHistory(session.Document.RelativePath, version);
            using (Form dialog = new Form())
            {
                dialog.Text = "Previous version · " + session.Document.RelativePath; dialog.Size = new Size(740, 530); dialog.StartPosition = FormStartPosition.CenterParent;
                RichTextBox text = new RichTextBox { Text = previous, Dock = DockStyle.Fill, ReadOnly = true, Font = editor.Font, DetectUrls = false };
                Button restore = new Button { Text = "Restore this version", Dock = DockStyle.Bottom, Height = 36, DialogResult = DialogResult.OK };
                dialog.Controls.Add(text); dialog.Controls.Add(restore); theme.Apply(dialog);
                if (dialog.ShowDialog(this) == DialogResult.OK) { editor.Text = previous; SaveCurrent(); }
            }
        }

        private void ChooseVault()
        {
            if (!RequireSave()) { return; }
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose a folder of Markdown notes, or create a new vault.";
                dialog.SelectedPath = vault.Root;
                if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
                if (String.Equals(Path.GetFullPath(dialog.SelectedPath).TrimEnd('\\'), vault.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { return; }
                OpenVault(dialog.SelectedPath);
            }
        }

        private void ExportNote()
        {
            if (session.Document == null) { return; }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Markdown note (*.md)|*.md"; dialog.FileName = Path.GetFileName(session.Document.RelativePath);
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
                string target = Path.GetFullPath(dialog.FileName);
                if (target.StartsWith(vault.Root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Export outside this vault. Use Save a copy to create another note inside it.");
                }
                // Export through a temporary sibling, preserving an existing export on write failure.
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, session.Text, new System.Text.UTF8Encoding(false));
                    if (File.Exists(target)) { File.Replace(temporary, target, null); } else { File.Move(temporary, target); }
                }
                finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
            }
        }

        private void QuickSwitcher()
        {
            NoteInfo note = Dialogs.Pick(this, "Quick switcher", index.Notes, n => n.RelativePath, theme);
            if (note != null) { OpenNote(note.RelativePath); editor.Focus(); }
        }

        private void CommandPalette()
        {
            string name = Dialogs.Pick(this, "Command palette", commands.Keys.OrderBy(k => k), k => k, theme);
            if (name != null) { commands[name](); }
        }

        private void InsertWikiLink()
        {
            if (session.Document == null) { return; }
            NoteInfo note = Dialogs.Pick(this, "Link to a note", index.Notes, n => n.RelativePath, theme);
            if (note != null) { editor.SelectedText = "[[" + note.RelativePath.Substring(0, note.RelativePath.Length - 3) + "]]"; editor.Focus(); }
        }

        private void FollowWiki(string target)
        {
            if (session.Document == null) { return; }
            NoteInfo resolved = index.ResolveLink(session.Document.RelativePath, target);
            if (resolved != null)
            {
                OpenNote(resolved.RelativePath);
                int separator = target.IndexOf('#');
                if (separator >= 0)
                {
                    string heading = target.Substring(separator + 1);
                    int position = editor.Text.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
                    if (position >= 0) { editor.Select(position, heading.Length); editor.ScrollToCaret(); }
                }
                return;
            }
            string path = target.Split('#')[0];
            if (String.IsNullOrWhiteSpace(path)) { return; }
            path = Vault.NormalizePath(path);
            if (vault.Exists(path)) { OpenNote(path); return; }
            if (MessageBox.Show(this, "No unique note matches [[" + target + "]]. Create “" + path + "” in this vault?", "Create linked note", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
            {
                CreateNote(path, "# " + Path.GetFileNameWithoutExtension(path) + "\n\n");
            }
        }

        private void NavigateHistory(Stack<string> source, Stack<string> destination)
        {
            if (source.Count == 0 || !RequireSave()) { return; }
            string next = source.Peek();
            string previous = session.Document == null ? null : session.Document.RelativePath;
            session.Open(next); source.Pop(); if (previous != null) { destination.Push(previous); } LoadEditor();
        }

        private void WrapSelection(string marker)
        {
            if (session.Document == null) { return; }
            int start = editor.SelectionStart; int length = editor.SelectionLength;
            editor.SelectedText = marker + editor.SelectedText + marker;
            editor.Select(start + marker.Length, length); editor.Focus();
        }

        private void FindInNote()
        {
            string text = Dialogs.Prompt(this, "Find in note", "Find text in the current note (case-insensitive).", editor.SelectedText);
            if (String.IsNullOrEmpty(text)) { return; }
            int start = Math.Min(editor.TextLength, editor.SelectionStart + editor.SelectionLength);
            int found = editor.Text.IndexOf(text, start, StringComparison.OrdinalIgnoreCase);
            if (found < 0) { found = editor.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase); }
            if (found < 0) { MessageBox.Show(this, "No matches in this note.", "Find in note"); return; }
            editor.Focus(); editor.Select(found, text.Length); editor.ScrollToCaret();
        }

        internal void TogglePreview()
        {
            settings.Preview = !settings.Preview; pages.Panel2Collapsed = !settings.Preview; pages.Visible = true; graph.Visible = false;
        }

        internal void ToggleGraph()
        {
            graph.Visible = !graph.Visible; pages.Visible = !graph.Visible; UpdateGraph();
        }

        private void UpdateGraph() { graph.SetData(index, session == null || session.Document == null ? null : session.Document.RelativePath, theme); }

        internal void ToggleTheme() { settings.DarkTheme = !settings.DarkTheme; theme = new Theme(settings.DarkTheme); ApplyTheme(); RenderPreview(); UpdateGraph(); }

        private void ApplyTheme()
        {
            theme.Apply(this); editor.BackColor = theme.Background; preview.BackColor = theme.Background;
            editor.Parent.BackColor = theme.Background; preview.Parent.BackColor = theme.Background;
            title.BackColor = theme.Sidebar; alert.ForeColor = theme.Accent;
            indexLabel.LinkColor = theme.Accent; indexLabel.ActiveLinkColor = theme.Accent;
            SetEditorSize(settings.FontSize);
        }

        private void SetEditorSize(int size)
        {
            settings.FontSize = Math.Max(9, Math.Min(24, size));
            Font old = editor.Font;
            editor.Font = new Font("Courier New", settings.FontSize);
            if (old != Font && old != Control.DefaultFont) { old.Dispose(); }
        }

        private void ShowAlert(string message) { alert.Text = message; alertPanel.Visible = true; }

        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is ArgumentException) && !(error is Win32Exception)) { throw; }
                ShowAlert(error.Message);
            }
        }

        private void OnClosing(object sender, FormClosingEventArgs args)
        {
            if (!SaveCurrent()) { args.Cancel = true; return; }
            settings.LastNote = session.Document == null ? String.Empty : session.Document.RelativePath;
            try { settings.Save(settingsPath); }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException)) { throw; }
                // Notes are already safe; settings failure must not prevent an intentional exit.
                MessageBox.Show(this, "Your notes are saved, but preferences could not be saved.\n\n" + error.Message, "Torp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            closing = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closing = true;
                saveTimer.Dispose(); previewTimer.Dispose(); refreshTimer.Dispose(); searchTimer.Dispose();
                if (watcher != null) { watcher.Dispose(); }
                lock (workerGate) { if (vault != null && (!workerReading || !Object.ReferenceEquals(vault, indexingVault))) { vault.Dispose(); } }
                indexWorker.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ShowHelp()
        {
            MessageBox.Show(this,
                "Ctrl+N  New note\nCtrl+O  Quick switcher\nCtrl+P  Command palette\nCtrl+S  Save (also automatic)\nCtrl+Shift+S  Save a copy\nCtrl+D  Daily note\nCtrl+K  Insert wiki link\nCtrl+F  Find in note\nCtrl+Shift+F  Search vault\nCtrl+E  Reading view\nCtrl+G  Graph\nCtrl+L  Light / dark theme\nAlt+Left / Right  Back / forward\nF2  Rename or move\nF5  Refresh external changes\n\nSearch: quoted phrases, tag:ideas, path:Daily, -exclude\nLinks: [[Note]], [[folder/Note|label]], [[Note#Heading]]\n\nNotes save as UTF-8 Markdown. Prior versions and deleted notes stay in .torp inside your vault. Back up the entire vault folder.\n\nReading view supports headings, emphasis, lists, tasks, quotations, fences and links. HTML is displayed as text. Images and advanced Markdown extensions are not rendered.",
                "Torp help", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private sealed class NoteChoice
        {
            internal string Path;
            internal NoteChoice(string path) { Path = path; }
            public override string ToString() { return System.IO.Path.GetFileNameWithoutExtension(Path); }
        }

        private sealed class EmptyChoice
        {
            private readonly string text;
            internal EmptyChoice(string text) { this.text = text; }
            public override string ToString() { return text; }
        }

        internal const string WelcomeText = "# A little space for big ideas.\n\nWelcome to **Torp**, your personal notebook. A quiet place to think, write, and connect the dots.\n\n## Make yourself at home\n\nYour notes are ordinary Markdown files, right here on your computer. No accounts. No cloud required. Just you and your ideas.\n\n- **Capture** a thought with Ctrl+N\n- **Connect** your notes with [[Getting started|wiki links]]\n- **Discover** patterns in the graph\n\n## A thought worth keeping\n\n> The best way to have a good idea is to write the first one down.\n\nStart a daily note, collect a few questions, or find [[Ideas/A place for ideas|a place for ideas]]. There is no wrong place to begin.\n\n---\n\n- [x] Find a home for your notes\n- [ ] Write something that matters to you\n\n#welcome #ideas\n";
        internal const string GettingStartedText = "# Getting started\n\n## Your vault\n\nA vault is just a folder. Use **Open vault** to choose an existing collection of .md notes. Torp writes UTF-8 and keeps your files yours.\n\n## Connect your thinking\n\nType `[[Note name]]` to link notes, or press Ctrl+K to pick one. The right sidebar shows incoming links, outgoing links, and tags.\n\n[[Welcome to Torp]] is a good place to begin.\n\n## Find anything\n\nSearch the sidebar for words, a \"quoted phrase\", `tag:ideas`, `path:Daily`, or `-exclude`. Ctrl+O jumps to a note. Ctrl+P opens every command.\n\n## Keep your work safe\n\nEdits save automatically after a short pause. Previous saved versions are in File → Version history. Deleted notes go to vault trash. If another app changes a note, Torp keeps your edits and asks you to save a copy or reload.\n\nBack up your entire vault regularly, including its .torp folder.\n\n#guide #welcome\n";
    }
}
