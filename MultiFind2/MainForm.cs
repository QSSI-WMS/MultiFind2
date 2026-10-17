namespace MultiFind2;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Current;

    private readonly TabControl _tabs = new ThemedTabControl { Dock = DockStyle.Fill };
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _lblSummary = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _lblElapsed = new() { AutoSize = false, Width = 120, TextAlign = ContentAlignment.MiddleLeft, BorderSides = ToolStripStatusLabelBorderSides.Left };
    private readonly ToolStripStatusLabel _lblState = new() { AutoSize = false, Width = 140, TextAlign = ContentAlignment.MiddleLeft, BorderSides = ToolStripStatusLabelBorderSides.Left };
    private readonly ComboBox _themeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };

    private int _pageCounter;

    public MainForm()
    {
        Text = "MultiFind2";
        // The exe carries the same icon for Explorer, but a form does not pick that up: without
        // this the window and its taskbar button show the stock WinForms square.
        Icon = AppIcon.ForWindow();
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(1000, 520);
        StartPosition = FormStartPosition.CenterScreen;

        Theme.Current = Theme.Resolve(_settings.Theme);

        _status.Items.AddRange(new ToolStripItem[] { _lblSummary, _lblElapsed, _lblState });

        Controls.Add(_tabs);
        Controls.Add(_status);
        MainMenuStrip = BuildMenu();
        Controls.Add(MainMenuStrip);

        _tabs.SelectedIndexChanged += (_, _) => SyncStatus();

        BuildTheme();
        PopulateThemeCombo();

        RestorePages();
    }

    /// <summary>Brings back last session's tabs. Criteria only -- results would be stale.</summary>
    private void RestorePages()
    {
        _restoring = true;
        try
        {
            if (_settings.Pages.Count == 0)
            {
                AddPage();
                return;
            }

            foreach (var state in _settings.Pages) AddPage().RestoreState(state);
            _tabs.SelectedIndex = Math.Clamp(_settings.ActivePage, 0, _tabs.TabPages.Count - 1);
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>Set while the tabs are being rebuilt from the file, when there is nothing new to
    /// write and a half-restored session is all there would be to write it from.</summary>
    private bool _restoring;

    /// <summary>
    /// Writes the open pages and the shared drop-down history. The form owns the settings file
    /// because the pages share it: a page saving its own slice would drop the others' changes.
    /// </summary>
    private void SaveSession(bool closing = false)
    {
        if (_restoring) return;

        _settings.Pages = AllPages().Select(p => p.CaptureState()).ToList();
        _settings.ActivePage = Math.Max(0, _tabs.SelectedIndex);

        if (closing) _settings.SaveUnlessSuperseded();
        else _settings.Save();
    }

    // ------------------------------------------------------------------ theme

    static Color CBase => Theme.Current.Base;
    static Color CMantle => Theme.Current.Mantle;
    static Color CCrust => Theme.Current.Crust;
    static Color CText => Theme.Current.Text;
    static Color CSubtext => Theme.Current.Subtext;

    private readonly List<Action> _restyle = new();

    private void Style(Action apply)
    {
        apply();
        _restyle.Add(apply);
    }

    private void BuildTheme()
    {
        Style(() => { BackColor = CBase; ForeColor = CText; });
        // Tab pages are restyled by iteration rather than per-tab closures, so closing a page
        // cannot leave a closure behind holding a disposed TabPage.
        Style(() =>
        {
            _tabs.BackColor = CMantle;
            _tabs.ForeColor = CText;
            foreach (TabPage tab in _tabs.TabPages) tab.BackColor = CBase;
        });

        MainMenuStrip!.Renderer = new ThemeMenuRenderer();
        _status.Renderer = new ThemeMenuRenderer();
        Style(() =>
        {
            MainMenuStrip.BackColor = CMantle;
            MainMenuStrip.ForeColor = CText;
            _status.BackColor = CMantle;
            _lblSummary.ForeColor = CSubtext;
            _lblElapsed.ForeColor = CSubtext;
            _lblState.ForeColor = CSubtext;
        });

        _themeCombo.FlatStyle = FlatStyle.Flat;
        Style(() => { _themeCombo.BackColor = CCrust; _themeCombo.ForeColor = CText; });
    }

    private void PopulateThemeCombo()
    {
        foreach (var t in Theme.All) _themeCombo.Items.Add(Theme.Label(t));
        _themeCombo.SelectedIndex = Theme.All.ToList().IndexOf(Theme.Current);

        // SelectionChangeCommitted fires only when the user picks from the list. SelectedIndexChanged
        // also fires when the combo's handle is recreated -- which it is, being hosted on the menu
        // bar -- and that would write a theme preference nobody chose, so the app would stop
        // following Windows the first time it was launched.
        _themeCombo.SelectionChangeCommitted += (_, _) =>
        {
            if (_themeCombo.SelectedIndex < 0) return;

            var theme = Theme.All[_themeCombo.SelectedIndex];
            _settings.Theme = theme.Name;
            _settings.Save();
            ApplyTheme(theme);
        };
    }

    private void ApplyTheme(Theme theme)
    {
        Theme.Current = theme;
        SuspendLayout();

        foreach (var restyle in _restyle) restyle();
        foreach (var page in AllPages()) page.ApplyTheme();

        ResumeLayout(true);
        Theme.ApplyTitleBar(this);
        Invalidate(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top };

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("&New Page", null, (_, _) => AddPage()).ShortcutKey(Keys.Control | Keys.N);
        file.DropDownItems.Add("&Close Page", null, (_, _) => CloseActivePage()).ShortcutKey(Keys.Control | Keys.W);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("E&xit", null, (_, _) => Close());

        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.Add("&Copy", null, (_, _) => ActivePage?.CopySelectionToClipboard()).ShortcutKey(Keys.Control | Keys.C);
        edit.DropDownItems.Add("Select &All", null, (_, _) => ActivePage?.SelectAllResults()).ShortcutKey(Keys.Control | Keys.A);

        var pages = new ToolStripMenuItem("&Pages");
        pages.DropDownItems.Add("&New Page", null, (_, _) => AddPage());
        pages.DropDownItems.Add("&Duplicate Page", null, (_, _) => DuplicateActivePage());
        pages.DropDownItems.Add("&Rename...", null, (_, _) => RenameActivePage()).ShortcutKey(Keys.F2);
        pages.DropDownItems.Add(new ToolStripSeparator());
        pages.DropDownItems.Add("Ne&xt Page", null, (_, _) => StepPage(1)).ShortcutKey(Keys.Control | Keys.Tab);
        pages.DropDownItems.Add("&Previous Page", null, (_, _) => StepPage(-1)).ShortcutKey(Keys.Control | Keys.Shift | Keys.Tab);
        pages.DropDownItems.Add(new ToolStripSeparator());
        pages.DropDownItems.Add("&Close Page", null, (_, _) => CloseActivePage());

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add("&About", null, (_, _) => ShowAbout());

        // The theme picker rides on the right of the menu bar -- this app has no other toolbar.
        var themeHost = new ToolStripControlHost(_themeCombo)
        {
            Alignment = ToolStripItemAlignment.Right,
            Margin = new Padding(0, 1, 6, 1),
            ToolTipText = "Color theme"
        };

        menu.Items.AddRange(new ToolStripItem[] { file, edit, pages, help, themeHost });
        return menu;
    }

    private FindPage? ActivePage => _tabs.SelectedTab?.Controls.OfType<FindPage>().FirstOrDefault();

    private IEnumerable<FindPage> AllPages()
        => _tabs.TabPages.Cast<TabPage>().SelectMany(t => t.Controls.OfType<FindPage>());

    private FindPage AddPage()
    {
        var page = new FindPage { Dock = DockStyle.Fill };
        page.Title = _pageCounter == 0 ? "New page" : $"New page ({_pageCounter})";
        _pageCounter++;

        var tab = new TabPage(page.Title) { Padding = new Padding(2), UseVisualStyleBackColor = false, BackColor = CBase };
        tab.Controls.Add(page);

        page.StatusChanged += (s, e) =>
        {
            if (!ReferenceEquals(s, ActivePage)) return;
            _lblSummary.Text = e.Summary;
            _lblElapsed.Text = e.Elapsed;
            _lblState.Text = e.State;
        };
        page.TitleChanged += (_, _) => tab.Text = page.Title;

        // Running a search is also the moment the session is worth persisting: it is the only
        // point at which the criteria are known to be ones the user meant.
        page.HistoryCommitted += (s, _) =>
        {
            foreach (var other in AllPages().Where(p => !ReferenceEquals(p, s))) other.ReloadHistory();
            SaveSession();
        };

        _tabs.TabPages.Add(tab);
        _tabs.SelectedTab = tab;

        // Closing the window is too late to be the only time pages are stored: a crash would
        // take the session with it, and crashes are the reason this program exists.
        SaveSession();
        return page;
    }

    private void DuplicateActivePage()
    {
        var source = ActivePage;
        if (source == null) return;

        var copy = AddPage();
        source.CopyCriteriaTo(copy);
        copy.Title = source.Title + " (copy)";
        SaveSession();
    }

    private void RenameActivePage()
    {
        var page = ActivePage;
        if (page == null) return;

        var name = Prompt.Show(this, "Rename page", "New name:", page.Title);
        if (string.IsNullOrWhiteSpace(name)) return;

        page.Title = name.Trim();
        SaveSession();
    }

    private void CloseActivePage()
    {
        var tab = _tabs.SelectedTab;
        var page = ActivePage;
        if (tab == null || page == null) return;

        if (page.IsRunning)
        {
            var answer = MessageBox.Show(this, "A search is still running on this page. Stop it and close?",
                                         "Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            page.CancelIfRunning();
        }

        _tabs.TabPages.Remove(tab);
        tab.Dispose();

        if (_tabs.TabPages.Count == 0) AddPage();
        SaveSession();
        SyncStatus();
    }

    private void StepPage(int delta)
    {
        if (_tabs.TabPages.Count < 2) return;
        var next = (_tabs.SelectedIndex + delta + _tabs.TabPages.Count) % _tabs.TabPages.Count;
        _tabs.SelectedIndex = next;
    }

    private void SyncStatus()
    {
        _lblSummary.Text = _lblElapsed.Text = _lblState.Text = "";
    }

    private void ShowAbout()
        => MessageBox.Show(this,
            "MultiFind2\n\nA multi-criteria file search tool.\n\n" +
            "Modified column shows date and time, and sorts chronologically.\n" +
            "Cancel stops a running search with a single click.",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var running = AllPages().Where(p => p.IsRunning).ToList();

        if (running.Count > 0)
        {
            var answer = MessageBox.Show(this, "A search is still running. Exit anyway?", "Confirmation",
                                         MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            foreach (var page in running) page.CancelIfRunning();
        }

        SaveSession(closing: true);
        base.OnFormClosing(e);
    }
}

internal static class MenuExtensions
{
    /// <summary>ToolStripItemCollection.Add returns ToolStripItem, so this keeps the menu code fluent.</summary>
    public static void ShortcutKey(this ToolStripItem item, Keys keys)
    {
        if (item is ToolStripMenuItem menuItem) menuItem.ShortcutKeys = keys;
    }
}
