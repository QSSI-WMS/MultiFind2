using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MultiFind2;

public sealed record PageStatus(string Summary, string Elapsed, string State, string? Folder = null);

public partial class FindPage : UserControl
{
    private readonly List<FileMatch> _matches = new();
    private readonly List<SkippedItem> _skipped = new();
    private readonly List<ErrorItem> _errors = new();

    private CancellationTokenSource? _cts;
    private bool _running;
    private string? _currentFolder;

    private int _sortColumn = -1;
    private bool _sortAscending = true;

    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 150 };
    private CancellationTokenSource? _previewCts;

    /// <summary>Only built if the shell ever declines to produce a menu, which it rarely does.</summary>
    private ContextMenuStrip? _ownMenu;

    /// <summary>What the preview highlights: the search text after a Find, the replacement text
    /// after a Replace, so the pane always shows the result of whatever just ran.</summary>
    private Regex? _previewPattern;

    public event EventHandler<PageStatus>? StatusChanged;
    public event EventHandler? TitleChanged;

    private string _title = "New page";
    public string Title
    {
        get => _title;
        set { _title = value; TitleChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Stable across renames and restarts; it is what ties a page to its backup folder.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    public bool IsRunning => _running;

    public FindPage()
    {
        InitializeComponent();
        BuildColumns();
        PopulateChoices();
        ReloadHistory();
        WireEvents();
        BuildTheme();
        SetRunning(false);
        SyncReplacesColumn();
        pvMatches.Clear("Select a file above to preview it.");
    }

    // ------------------------------------------------------------------ theme

    static Color CBase => Theme.Current.Base;
    static Color CMantle => Theme.Current.Mantle;
    static Color CCrust => Theme.Current.Crust;
    static Color CSurface0 => Theme.Current.Surface0;
    static Color CSurface1 => Theme.Current.Surface1;
    static Color COverlay => Theme.Current.Overlay;
    static Color CText => Theme.Current.Text;
    static Color CSubtext => Theme.Current.Subtext;
    static Color CBlue => Theme.Current.Blue;

    /// <summary>Re-applies the colors of every control on the page; replayed on theme change.</summary>
    private readonly List<Action> _restyle = new();

    private void Style(Action apply)
    {
        apply();
        _restyle.Add(apply);
    }

    public void ApplyTheme()
    {
        SuspendLayout();
        foreach (var restyle in _restyle) restyle();
        ResumeLayout(true);
        Invalidate(true);
    }

    /// <summary>
    /// The icons are drawn for one pixel size, so dragging the window to a monitor at another
    /// scale has to redraw them. Replaying the whole restyle is the same work as the theme
    /// switch it already handles, and happens about as often.
    /// </summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyTheme();
    }

    private void BuildTheme()
    {
        Style(() => { BackColor = CBase; ForeColor = CText; });
        Style(() => { pnlTop.BackColor = CBase; pnlButtons.BackColor = CBase; });

        // Flat group boxes derive their border from BackColor; the system style would keep
        // drawing a fixed light etch regardless of palette.
        foreach (var group in new[] { grpGeneral, grpAdvanced })
        {
            var g = group;
            g.FlatStyle = FlatStyle.Flat;
            Style(() => { g.BackColor = CBase; g.ForeColor = CSubtext; });
        }

        // Labels take ForeColor only, so they inherit whatever their parent is painted with.
        foreach (var label in new[] { lblPath, lblName, lblContents, lblContext })
        {
            var l = label;
            Style(() => l.ForeColor = COverlay);
        }

        foreach (var combo in new[] { cmbPath, cmbName, cmbContents, cmbReplace, cmbSizeUnit, cmbDateField })
            StyleInput(combo);

        foreach (var box in new[] { txtSizeMin, txtSizeMax })
        {
            var t = box;
            t.BorderStyle = BorderStyle.FixedSingle;
            Style(() => { t.BackColor = CCrust; t.ForeColor = CText; });
        }

        foreach (var spinner in new[] { numBefore, numAfter })
        {
            var n = spinner;
            n.BorderStyle = BorderStyle.FixedSingle;
            Style(() => { n.BackColor = CCrust; n.ForeColor = CText; });
        }

        foreach (var picker in new[] { dtFrom, dtTo })
        {
            var d = picker;
            Style(() => { ThemePainting.Unthemed(d); d.BackColor = CCrust; d.ForeColor = CText; });
        }

        foreach (var check in new[] { chkSize, chkDate, chkSkipBinary, chkBackup, chkReplace })
        {
            var c = check;
            c.FlatStyle = FlatStyle.Flat;
            Style(() => { c.BackColor = CBase; c.ForeColor = CText; });
        }

        StyleSmallButton(btnPathBrowse, Glyph.Browse);
        StyleSmallButton(btnPathMulti, Glyph.FolderStack);
        StyleSmallButton(btnNameMulti, Glyph.DocStack);
        StyleSmallButton(btnBackups);

        // Content toggles highlight when switched on, and carry a tick while they are.
        StyleToggle(chkCase, highlightWhenChecked: true, on => (Glyph.MatchCase, Mark(on)));
        StyleToggle(chkWhole, highlightWhenChecked: true, on => (Glyph.WholeWord, Mark(on)));
        StyleToggle(chkHex, highlightWhenChecked: true, on => (Glyph.Hex, Mark(on)));
        StyleToggle(chkRegex, highlightWhenChecked: true, on => (Glyph.Regex, Mark(on)));

        // btnSubfolders is inverted: it ships checked, so the state worth flagging is the
        // unchecked one. Its two states are different pictures rather than one with a badge,
        // because searching a tree and searching a single folder are different things, not a
        // setting turned on and off.
        StyleToggle(btnSubfolders, highlightWhenChecked: false,
                    on => (on ? Glyph.SubfolderTree : Glyph.FolderOnly, Badge.None));

        // Ships off, so the state worth flagging is the checked one, like the content toggles.
        StyleToggle(chkArchives, highlightWhenChecked: true, on => (Glyph.Archive, Mark(on)));

        static Badge Mark(bool on) => on ? Badge.Check : Badge.None;

        foreach (var tri in new[] { tsHidden, tsReadOnly, tsFolders })
        {
            var t = tri;
            Style(t.RefreshTheme);
        }

        StyleActionButton(btnFind, accent: true);
        StyleActionButton(btnCancel, accent: false);
        StyleActionButton(btnClearCriteria, accent: false);
        StyleActionButton(btnClearResults, accent: false);
        StyleActionButton(btnReplace, accent: false);

        foreach (var tabs in new[] { tabResults, tabPreview })
        {
            var t = tabs;
            Style(() =>
            {
                t.BackColor = CMantle;
                t.ForeColor = CText;
                foreach (TabPage page in t.TabPages)
                {
                    page.UseVisualStyleBackColor = false;
                    page.BackColor = CBase;
                }
            });
        }

        // The splitter itself is the SplitContainer's own background; the panels have to be
        // painted separately or they inherit it and the grab bar disappears.
        Style(() =>
        {
            splitResults.BackColor = CSurface1;
            splitResults.Panel1.BackColor = CBase;
            splitResults.Panel2.BackColor = CBase;
            pnlContext.BackColor = CBase;
        });

        ThemePainting.AttachList(lvFound, sortState: () => (_sortColumn, _sortAscending));
        ThemePainting.AttachList(lvSkipped, _ => Theme.Current.SkippedRowBg);
        ThemePainting.AttachList(lvErrors, _ => Theme.Current.ErrorRowBg);

        // The preview lists attach their own painter in their constructor.
        foreach (var list in new ListView[] { lvFound, lvSkipped, lvErrors, pvMatches, pvFile })
        {
            var lv = list;
            Style(() =>
            {
                lv.BackColor = CBase;
                lv.ForeColor = CText;
                ThemePainting.ExplorerTheme(lv);
            });
        }
    }

    private void StyleInput(ComboBox combo)
    {
        combo.FlatStyle = FlatStyle.Flat;
        Style(() => { combo.BackColor = CCrust; combo.ForeColor = CText; });
    }

    private void StyleSmallButton(Button button, Glyph? glyph = null)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        Style(() =>
        {
            button.BackColor = CSurface0;
            button.ForeColor = CText;
            button.FlatAppearance.BorderColor = CSurface1;
            if (glyph is { } g) SetIcon(button, g, Badge.None);
        });
    }

    /// <summary>
    /// Draws the icon in the colours the button has just been given. Order matters: the glyph is
    /// rendered in the foreground against the background, so it has to come after they are set.
    /// </summary>
    private static void SetIcon(ButtonBase button, Glyph glyph, Badge badge)
    {
        button.Text = "";
        button.Image = Icons.Get(glyph, badge, button.ForeColor, button.BackColor,
                                 Icons.SizeFor(button));
    }

    /// <summary>
    /// A button-style CheckBox. Flat appearance is required: a visual-style button ignores
    /// BackColor, which would hide both the palette and the active-filter highlight.
    /// </summary>
    private void StyleToggle(CheckBox box, bool highlightWhenChecked, Func<bool, (Glyph, Badge)> art)
    {
        box.FlatStyle = FlatStyle.Flat;
        box.FlatAppearance.BorderSize = 1;

        void Paint()
        {
            var t = Theme.Current;
            var active = box.Checked == highlightWhenChecked;
            box.BackColor = active ? t.ActiveFilterBg : t.Surface0;
            box.ForeColor = active ? t.ActiveFilterText : t.Text;
            box.FlatAppearance.BorderColor = active ? t.ActiveFilterBorder : t.Surface1;
            box.FlatAppearance.CheckedBackColor = highlightWhenChecked ? t.ActiveFilterBg : t.Surface0;

            var (glyph, badge) = art(box.Checked);
            SetIcon(box, glyph, badge);
        }

        box.CheckedChanged += (_, _) => Paint();
        box.DpiChangedAfterParent += (_, _) => Paint();
        Style(Paint);
    }

    /// <summary>
    /// Find and Cancel spend their lives toggling Enabled (fix #2), so the disabled look has to
    /// be explicit -- a flat button left to WinForms' default greying is nearly invisible on a
    /// dark palette.
    /// </summary>
    private void StyleActionButton(Button button, bool accent)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;

        void Paint()
        {
            var t = Theme.Current;
            if (!button.Enabled)
            {
                button.BackColor = t.Surface0;
                button.ForeColor = t.Overlay;
                button.FlatAppearance.BorderColor = t.Surface0;
            }
            else if (accent)
            {
                button.BackColor = CBlue;
                button.ForeColor = t.Base;
                button.FlatAppearance.BorderColor = CBlue;
            }
            else
            {
                button.BackColor = t.Surface0;
                button.ForeColor = t.Text;
                button.FlatAppearance.BorderColor = t.Surface1;
            }
        }

        button.EnabledChanged += (_, _) => Paint();
        Style(Paint);
    }

    private void BuildColumns()
    {
        lvFound.Columns.Add("File name", 220);
        lvFound.Columns.Add("Folder", 320);
        // Always present so the column indices the sort and the retrieve handler use never move;
        // it is simply given no width until there is something to put in it.
        lvFound.Columns.Add("Replaces", 0, HorizontalAlignment.Right);
        lvFound.Columns.Add("Size", 90, HorizontalAlignment.Right);
        lvFound.Columns.Add("Modified", 170);          // date AND time
        lvFound.Columns.Add("Type", 70);

        lvSkipped.Columns.Add("File", 460);
        lvSkipped.Columns.Add("Reason", 400);

        lvErrors.Columns.Add("Path", 460);
        lvErrors.Columns.Add("Error", 400);
    }

    private void PopulateChoices()
    {
        cmbSizeUnit.Items.AddRange(new object[] { "Bytes", "Kb", "Mb", "Gb" });
        cmbSizeUnit.SelectedIndex = 1;

        cmbDateField.Items.AddRange(new object[] { "Modified", "Created", "Accessed" });
        cmbDateField.SelectedIndex = 0;

        dtFrom.Value = DateTime.Today.AddMonths(-1);
        dtTo.Value = DateTime.Today;

        cmbPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        cmbName.Text = "*.*";
    }

    private void WireEvents()
    {
        btnFind.Click += (_, _) => _ = StartSearchAsync();
        btnCancel.Click += (_, _) => CancelSearch();
        btnClearCriteria.Click += (_, _) => ClearCriteria();
        btnClearResults.Click += (_, _) => ClearResults();
        btnReplace.Click += (_, _) => _ = StartReplaceAsync();
        btnBackups.Click += (_, _) => OpenBackups();

        chkReplace.CheckedChanged += (_, _) => SyncReplaceEnabled();

        btnPathBrowse.Click += (_, _) => BrowseForFolder();
        btnPathMulti.Click += (_, _) => PromptFolderLists();
        btnNameMulti.Click += (_, _) => PromptFileNameLists();

        btnSubfolders.CheckedChanged += (_, _) => tips.SetToolTip(
            btnSubfolders,
            btnSubfolders.Checked ? "Include subfolders" : "Search this folder only");

        chkArchives.CheckedChanged += (_, _) => tips.SetToolTip(
            chkArchives,
            chkArchives.Checked
                ? "Searching inside compressed files (zip, 7z, rar, gzip, tar)"
                : "Search inside compressed files (zip, 7z, rar, gzip, tar)");

        // Hex and Regex are mutually exclusive content modes.
        chkHex.CheckedChanged += (_, _) => { if (chkHex.Checked) chkRegex.Checked = false; };
        chkRegex.CheckedChanged += (_, _) => { if (chkRegex.Checked) chkHex.Checked = false; };

        lvFound.RetrieveVirtualItem += OnRetrieveFound;
        lvSkipped.RetrieveVirtualItem += OnRetrieveSkipped;
        lvErrors.RetrieveVirtualItem += OnRetrieveError;

        lvFound.ColumnClick += OnFoundColumnClick;
        lvFound.DoubleClick += (_, _) => ShellOpen(ShellTarget(FirstSelectedMatch()));

        // No ContextMenuStrip: the shell menu is shown by hand so it can be tracked natively.
        // _ownMenu is what gets used when the shell declines -- a row whose file has since been
        // deleted, most often.
        lvFound.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) ShowResultsMenu(lvFound.PointToScreen(e.Location));
        };
        lvFound.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Apps) return;
            e.Handled = true;
            ShowResultsMenu(ContextMenuAnchor());
        };

        // Every input to the preview reschedules it; the timer is what keeps holding Down on a
        // 5,000-row result from queueing 5,000 file reads.
        lvFound.SelectedIndexChanged += (_, _) => SchedulePreview();
        tabPreview.SelectedIndexChanged += (_, _) => SchedulePreview();
        numBefore.ValueChanged += (_, _) => SchedulePreview();
        numAfter.ValueChanged += (_, _) => SchedulePreview();
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); _ = LoadPreviewAsync(); };

        chkReplace.CheckedChanged += (_, _) => SyncReplacesColumn();

        foreach (var combo in new[] { cmbPath, cmbName, cmbContents })
            combo.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                if (!_running) _ = StartSearchAsync();
            };
    }

    // ---------------------------------------------------------------- search

    private async Task StartSearchAsync()
    {
        if (_running) return;   // belt and braces; btnFind is already disabled while running

        SearchCriteria criteria;
        try
        {
            criteria = BuildCriteria();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "MultiFind2", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ClearResults();
        CommitHistory();

        // Hex has no textual form to highlight, so the pane says so rather than lying.
        _previewPattern = chkHex.Checked
            ? null
            : PreviewScanner.Pattern(criteria.Contents, criteria.CaseSensitive,
                                     criteria.WholeWords, criteria.ContentMode == ContentMode.Regex);

        _cts = new CancellationTokenSource();
        _currentFolder = null;
        SetRunning(true);
        Report("Searching...", "In progress...");

        var clock = Stopwatch.StartNew();
        var progress = new Progress<SearchBatch>(OnBatch);
        SearchResult result;

        try
        {
            result = await new SearchEngine(criteria).RunAsync(progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            result = new SearchResult(SearchOutcome.Canceled, _matches.Count, 0, clock.Elapsed);
        }
        catch (Exception ex)
        {
            result = new SearchResult(SearchOutcome.Failed, _matches.Count, 0, clock.Elapsed, ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetRunning(false);
        }

        if (_sortColumn >= 0) ApplySort();
        RefreshCounts();

        var state = result.Outcome switch
        {
            SearchOutcome.Canceled => "Canceled",
            SearchOutcome.Failed => "Failed",
            SearchOutcome.LimitReached => "Limit reached",
            _ => "Finished"
        };
        Report($"{_matches.Count} files found", state, result.Elapsed);

        if (result.Outcome == SearchOutcome.Failed && result.Message != null)
            MessageBox.Show(this, result.Message, "MultiFind2", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>
    /// Fix #2: one click stops the search. The button latches disabled here rather than waiting for
    /// the worker to acknowledge, so a second click cannot land and there is no restart path at all --
    /// Find and Cancel are separate buttons, so "cancel" can never be interpreted as "start".
    /// </summary>
    private void CancelSearch()
    {
        if (!_running || _cts == null) return;

        btnCancel.Enabled = false;
        Report($"{_matches.Count} files found", "Canceling...");

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Worker finished between the click and the cancel; nothing to stop.
        }
    }

    // ------------------------------------------------------------- replace

    /// <summary>
    /// Replace is a separate button rather than a mode on Find: a search only ever reads, and
    /// nothing that rewrites files should be reachable by clicking the button you click all day.
    /// It works on the rows already found -- the selected ones if there is a selection.
    /// </summary>
    private async Task StartReplaceAsync()
    {
        if (_running || !chkReplace.Checked) return;

        var find = cmbContents.Text;
        if (find.Length == 0)
        {
            Warn("Replace needs the text to look for in \"File contents\".");
            return;
        }
        if (chkHex.Checked)
        {
            Warn("Replace does not support hex searches; it would change the length of the file.");
            return;
        }

        var selected = SelectedMatches().ToList();
        var scoped = selected.Count > 0;
        var candidates = (scoped ? selected : _matches).Where(m => !m.IsFolder).ToList();

        // Replace only ever rewrites files on disk. Rewriting a file inside an archive would mean
        // recompressing the archive around it -- a different and far more destructive operation
        // than the one this button promises -- so those rows are left out.
        var pool = candidates.Where(m => !m.IsInArchive).ToList();
        var inArchives = candidates.Count - pool.Count;
        var targets = pool.Select(m => m.FullPath).ToList();

        if (targets.Count == 0)
        {
            Warn(inArchives > 0
                ? $"All {inArchives} file(s) found are inside compressed files.\n\n" +
                  "Replace only changes files on disk; it never writes into an archive."
                : "There are no files to replace in.");
            return;
        }

        var backup = AskForBackup(pool);
        if (backup == BackupChoice.Cancel) return;

        var plan = BuildReplacePlan() with { Backup = backup == BackupChoice.Keep ? NewBackupStore() : null };
        if (!ConfirmReplace(plan, targets.Count, scoped, inArchives)) return;

        CommitHistory();

        _cts = new CancellationTokenSource();
        SetRunning(true);
        Report($"Replacing in {targets.Count} files", "In progress...");

        var clock = Stopwatch.StartNew();
        var progress = new Progress<int>(done => Report($"{done} of {targets.Count} files", "In progress..."));
        ReplaceResult result;

        try
        {
            result = await Replacer.RunAsync(targets, plan, progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            result = new ReplaceResult(0, 0, Empty(), new List<ErrorItem>(), true);
        }
        catch (Exception ex)
        {
            result = new ReplaceResult(0, 0, Empty(), new List<ErrorItem> { new("", ex.Message) }, false);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetRunning(false);
        }

        foreach (var match in _matches)
            match.Replaces = result.PerFile.TryGetValue(match.FullPath, out var n) ? n : 0;

        // The files now contain the replacement, so that is what the preview should point at.
        _previewPattern = PreviewScanner.Pattern(plan.ReplaceWith, plan.CaseSensitive,
                                                 wholeWords: false, useRegex: false);

        _errors.AddRange(result.Errors);
        lvFound.Refresh();
        RefreshCounts();
        SchedulePreview();

        var summary = $"{result.Replacements} replacements in {result.FilesChanged} files";
        Report(summary, result.Canceled ? "Canceled" : "Finished", clock.Elapsed);

        // The rows still show the sizes and timestamps from before the edit, so say so rather
        // than leaving the user to wonder whether the list is lying.
        MessageBox.Show(this,
            $"{summary}.\n" +
            (result.Errors.Count > 0 ? $"{result.Errors.Count} file(s) could not be changed -- see the Errors tab.\n" : "") +
            (plan.Backup?.Count > 0 ? $"{plan.Backup.Count} original(s) backed up -- see Backups.\n" : "") +
            (result.FilesChanged > 0 ? "\nRun Find again to refresh the sizes and timestamps." : ""),
            "Replace", MessageBoxButtons.OK,
            result.Errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    private static Dictionary<string, int> Empty() => new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------- backups

    private enum BackupChoice { Keep, Skip, Cancel }

    /// <summary>
    /// Backups go to %TMP%, which is a shared and finite resource: filling the system drive to
    /// keep copies nobody asked for would be a worse outcome than the replace itself. So when
    /// the worst case is a large share of what is free, the choice is handed over rather than
    /// taken -- and the replace can still go ahead without backups.
    /// </summary>
    private BackupChoice AskForBackup(List<FileMatch> targets)
    {
        if (!chkBackup.Checked) return BackupChoice.Skip;

        // The upper bound: every candidate copied. Which files actually contain the text is
        // not known until the replace has run, so the estimate deliberately over-states.
        var needed = targets.Sum(m => m.Size);
        var free = BackupStore.FreeSpace();
        if (!BackupStore.IsTight(needed, free)) return BackupChoice.Keep;

        var answer = MessageBox.Show(this,
            $"Backing up {targets.Count} file(s) needs up to {FileMatch.FormatSize(needed)}, " +
            $"and only {FileMatch.FormatSize(free)} is free on the volume holding %TMP%.\n\n" +
            "Yes\tback up anyway\n" +
            "No\treplace without backups -- this cannot be undone\n" +
            "Cancel\tdo not replace",
            "Not much room for backups", MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);

        return answer switch
        {
            DialogResult.Yes => BackupChoice.Keep,
            DialogResult.No => BackupChoice.Skip,
            _ => BackupChoice.Cancel
        };
    }

    /// <summary>The header makes a run folder self-describing; a wall of timestamps does not.</summary>
    private BackupStore NewBackupStore()
    {
        var header =
            $"MultiFind2 backup{Environment.NewLine}{Environment.NewLine}" +
            $"Page:     {Title}{Environment.NewLine}" +
            $"Started:  {DateTime.Now:G}{Environment.NewLine}" +
            $"Find:     {cmbContents.Text}{Environment.NewLine}" +
            $"Replace:  {cmbReplace.Text}{Environment.NewLine}" +
            $"Options:  {(chkCase.Checked ? "case-sensitive" : "case-insensitive")}" +
            $"{(chkWhole.Checked ? ", whole words" : "")}{(chkRegex.Checked ? ", regex" : "")}" +
            Environment.NewLine;

        return new BackupStore(BackupStore.PageFolder(Id, Title), header);
    }

    private void OpenBackups()
    {
        var folder = BackupStore.PageFolder(Id, Title);

        if (!Directory.Exists(folder))
        {
            Warn($"No backups have been kept from \"{Title}\" yet.\n\n" +
                 $"They would go to:\n{folder}");
            return;
        }

        ShellOpen(folder);
    }

    /// <summary>The replace options are the content-search options: what Find matched is what
    /// gets rewritten, so case, whole-words and regex are never set independently. Where the
    /// originals go is decided per run, by <see cref="AskForBackup"/>.</summary>
    private ReplacePlan BuildReplacePlan()
        => new(cmbContents.Text, cmbReplace.Text, chkCase.Checked, chkWhole.Checked,
               chkRegex.Checked, Backup: null);

    private bool ConfirmReplace(ReplacePlan plan, int fileCount, bool scoped, int inArchives)
    {
        var what = plan.UseRegex ? "the matches of" : plan.WholeWords ? "the whole word" : "";
        var message =
            $"Replace {what} \"{plan.Find}\"\nwith \"{plan.ReplaceWith}\"\n\n" +
            $"in {fileCount} {(scoped ? "selected" : "found")} file(s)?\n\n" +
            (inArchives > 0
                ? $"{inArchives} more are inside compressed files and will be left alone.\n\n"
                : "") +
            (plan.Backup != null
                ? $"The original of each changed file is copied to a dated folder under %TMP%, " +
                  $"kept for {BackupStore.KeepDays} days and reachable from Backups."
                : "No backups will be kept. This cannot be undone.");

        // Defaults to No: the dangerous answer should never be one stray Enter away.
        return MessageBox.Show(this, message, "Confirm replace", MessageBoxButtons.YesNo,
                               MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void Warn(string message)
        => MessageBox.Show(this, message, "MultiFind2", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void SetRunning(bool running)
    {
        _running = running;
        btnFind.Enabled = !running;
        btnCancel.Enabled = running;
        btnClearCriteria.Enabled = !running;
        btnClearResults.Enabled = !running;
        SyncReplaceEnabled();
        Cursor = running ? Cursors.AppStarting : Cursors.Default;
    }

    /// <summary>Replace is only ever offered when it has something to act on, so the button
    /// itself answers "why can't I replace?" before the user has to click it.</summary>
    private void SyncReplaceEnabled()
    {
        cmbReplace.Enabled = chkReplace.Checked && !_running;
        chkBackup.Enabled = chkReplace.Checked && !_running;
        btnReplace.Enabled = chkReplace.Checked && !_running && _matches.Count > 0;
    }

    private void OnBatch(SearchBatch batch)
    {
        _matches.AddRange(batch.Matches);
        _skipped.AddRange(batch.Skipped);
        _errors.AddRange(batch.Errors);

        // A chosen order has to survive the next batch, or clicking a header during a long search
        // appears to do nothing: the rows jump back to discovery order as soon as more arrive.
        if (_sortColumn >= 0) ApplySort();

        RefreshCounts();

        if (batch.CurrentFolder != null) _currentFolder = batch.CurrentFolder;
        if (_running) Report($"{_matches.Count} files found", "In progress...", folder: _currentFolder);
    }

    private void RefreshCounts()
    {
        SetVirtualSize(lvFound, _matches.Count);
        SetVirtualSize(lvSkipped, _skipped.Count);
        SetVirtualSize(lvErrors, _errors.Count);

        tabFound.Text = $"Found ({_matches.Count})";
        tabSkipped.Text = $"Skipped ({_skipped.Count})";
        tabErrors.Text = $"Errors ({_errors.Count})";

        SyncReplaceEnabled();
    }

    private static void SetVirtualSize(ListView list, int count)
    {
        var previous = list.VirtualListSize;
        if (previous == count) return;

        list.VirtualListSize = count;

        // The viewport does not follow the list when it shrinks, and emptying it does not bring
        // the viewport home either. A long search followed by a short one therefore leaves it
        // parked thousands of rows past the last result: the count says 43 and the list looks
        // empty. Only on a fresh or shrinking list -- scrolling during a search is the user's
        // business, and batches arrive several times a second.
        // Going to the last row before the first is not redundant: asked to show row 0 from far
        // below the end of a list that has just shrunk, the control scrolls to a position it has
        // already clamped and leaves row 0 at the bottom edge. Landing on a valid row first gives
        // it a sane offset to scroll from.
        if (count > 0 && list.IsHandleCreated && (previous == 0 || count < previous))
        {
            list.EnsureVisible(count - 1);
            list.EnsureVisible(0);
        }

        list.Invalidate();
    }

    private void Report(string summary, string state, TimeSpan? elapsed = null, string? folder = null)
        => StatusChanged?.Invoke(this, new PageStatus(
            summary,
            elapsed is { } e ? $"{e.TotalSeconds:0.0} secs" : "",
            state,
            folder));

    // ------------------------------------------------------------- criteria

    private SearchCriteria BuildCriteria()
    {
        var paths = cmbPath.Text
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (paths.Count == 0) throw new InvalidOperationException("Please specify a search path.");

        foreach (var p in paths.Where(p => !Directory.Exists(p)))
            throw new InvalidOperationException($"Path \"{p}\" does not exist");

        var criteria = new SearchCriteria
        {
            SearchPaths = paths,
            IncludeSubfolders = btnSubfolders.Checked,
            ExcludeFolders = new List<string>(_excludeFolders),
            FileNamePatterns = cmbName.Text.Trim() is "*.*" or "*" ? "" : cmbName.Text.Trim(),
            ExcludeFileNames = _excludeNames,
            Hidden = tsHidden.State,
            ReadOnly = tsReadOnly.State,
            Contents = cmbContents.Text,
            CaseSensitive = chkCase.Checked,
            WholeWords = chkWhole.Checked,
            SkipBinary = chkSkipBinary.Checked,
            SearchArchives = chkArchives.Checked,
            ContentMode = chkHex.Checked ? ContentMode.Hex
                        : chkRegex.Checked ? ContentMode.Regex
                        : ContentMode.PlainText,
            // Tri-state cycles Any -> Yes -> No; here that reads as "all" / "folders only" / "files only".
            FolderMatch = tsFolders.State switch
            {
                TriState.Any => FolderMatch.FoldersToo,
                TriState.Yes => FolderMatch.FoldersOnly,
                _ => FolderMatch.Files
            },
            UseSize = chkSize.Checked,
            UseDate = chkDate.Checked
        };

        if (criteria.UseSize)
        {
            var unit = (long)Math.Pow(1024, cmbSizeUnit.SelectedIndex);
            criteria.MinSize = ParseSize(txtSizeMin.Text, 0) * unit;
            criteria.MaxSize = ParseSize(txtSizeMax.Text, long.MaxValue / unit) * unit;
            if (criteria.MinSize > criteria.MaxSize)
                throw new InvalidOperationException("Incorrect size interval");
        }

        if (criteria.UseDate)
        {
            criteria.DateField = (DateField)(cmbDateField.SelectedIndex switch { 1 => 0, 2 => 2, _ => 1 });
            criteria.DateFrom = dtFrom.Value.Date;
            // Inclusive of the whole end day, otherwise "today to today" matches nothing.
            criteria.DateTo = dtTo.Value.Date.AddDays(1).AddTicks(-1);
            if (criteria.DateFrom > criteria.DateTo)
                throw new InvalidOperationException("Incorrect date interval");
        }

        // Surface bad patterns now rather than silently finding nothing.
        if (criteria.HasContentFilter)
        {
            if (criteria.ContentMode == ContentMode.Hex) ContentMatcher.ParseHex(criteria.Contents);
            if (criteria.ContentMode == ContentMode.Regex)
            {
                try { _ = new Regex(criteria.Contents); }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException($"Invalid regular expression: {ex.Message}");
                }
            }
        }

        return criteria;
    }

    private static long ParseSize(string text, long fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        if (!long.TryParse(text.Trim(), out var value) || value < 0)
            throw new InvalidOperationException("Incorrect size interval");
        return value;
    }

    private string _excludeNames = "";
    private List<string> _excludeFolders = new();

    private static List<string> Split(string text)
        => text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// "Browse for multiple folders". The include list round-trips through the search path box, which
    /// stays the single source of truth so typing and the dialog can never disagree; the exclude list
    /// has no box of its own and lives in <see cref="_excludeFolders"/>.
    /// </summary>
    private void PromptFolderLists()
    {
        using var dialog = new FolderListDialog(Split(cmbPath.Text), _excludeFolders);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        cmbPath.Text = string.Join(';', dialog.IncludeFolders);
        _excludeFolders = dialog.ExcludeFolders;
    }

    /// <summary>"Select multiple file names" -- same split: include drives cmbName, exclude is page state.</summary>
    private void PromptFileNameLists()
    {
        using var dialog = new FileNameListDialog(Split(cmbName.Text), Split(_excludeNames));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var include = dialog.IncludeNames;
        cmbName.Text = include.Count == 0 ? "*.*" : string.Join(';', include);
        _excludeNames = string.Join(';', dialog.ExcludeNames);
    }

    private void BrowseForFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Path to look in", UseDescriptionForTitle = true };

        var current = cmbPath.Text.Split(';', StringSplitOptions.TrimEntries).LastOrDefault();
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            dialog.SelectedPath = current;

        if (dialog.ShowDialog(this) == DialogResult.OK) cmbPath.Text = dialog.SelectedPath;
    }

    private void ClearCriteria()
    {
        cmbName.Text = "*.*";
        cmbContents.Text = "";
        cmbReplace.Text = "";
        chkReplace.Checked = false;
        chkBackup.Checked = true;
        _excludeNames = "";
        _excludeFolders.Clear();
        chkCase.Checked = chkWhole.Checked = chkHex.Checked = chkRegex.Checked = false;
        chkSize.Checked = chkDate.Checked = false;
        txtSizeMin.Clear();
        txtSizeMax.Clear();
        btnSubfolders.Checked = true;
        chkArchives.Checked = false;
        chkSkipBinary.Checked = true;
        tsHidden.ResetToDefault();
        tsReadOnly.ResetToDefault();
        tsFolders.ResetToDefault();
    }

    public void ClearResults()
    {
        _matches.Clear();
        _skipped.Clear();
        _errors.Clear();
        _sortColumn = -1;
        _previewPattern = null;
        lvFound.Refresh();
        RefreshCounts();
        ClearPreview();
        Report("", "");
    }

    // ------------------------------------------------------------- preview

    /// <summary>
    /// Reading the selected file is file I/O on an arbitrary path, so it happens off the UI
    /// thread; a debounce in front of it means arrowing through the results reads only the file
    /// the user actually stopped on.
    /// </summary>
    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void ClearPreview()
    {
        _previewTimer.Stop();
        _previewCts?.Cancel();
        pvMatches.Clear();
        pvFile.Clear();
    }

    private async Task LoadPreviewAsync()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        var selected = FirstSelectedMatch();
        if (selected == null || selected.IsFolder)
        {
            pvMatches.Clear(selected == null ? "Select a file above to preview it." : null);
            pvFile.Clear();
            return;
        }

        // Only the visible pane is read. The other is emptied rather than left showing the
        // previous file, and reloads when its tab is selected.
        var whole = ReferenceEquals(tabPreview.SelectedTab, tabFile);
        var file = selected;
        var pattern = _previewPattern;
        var before = (int)numBefore.Value;
        var after = (int)numAfter.Value;

        List<PreviewRow> rows;
        try
        {
            rows = await Task.Run(() => whole
                ? PreviewScanner.WholeFile(file, pattern, token)
                : PreviewScanner.Matches(file, pattern, before, after, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            rows = PreviewScanner.Note(ex.Message);
        }

        if (token.IsCancellationRequested || IsDisposed) return;

        (whole ? pvFile : pvMatches).SetRows(rows);
        (whole ? pvMatches : pvFile).Clear();
    }

    /// <summary>The Replaces column only earns its width once replacing is on the table.</summary>
    private void SyncReplacesColumn()
        => lvFound.Columns[2].Width = chkReplace.Checked ? 70 : 0;

    // ------------------------------------------------------------- history

    /// <summary>
    /// Raised once the criteria have been promoted into the shared history. MainForm owns the
    /// settings file, so it does the saving and tells the other pages to re-read the lists --
    /// the history belongs to the app, not to whichever page happened to run the search.
    /// </summary>
    public event EventHandler? HistoryCommitted;

    private void CommitHistory()
    {
        var settings = AppSettings.Current;
        AppSettings.Remember(settings.PathHistory, cmbPath.Text);
        AppSettings.Remember(settings.NameHistory, cmbName.Text);
        AppSettings.Remember(settings.ContentsHistory, cmbContents.Text);
        if (chkReplace.Checked) AppSettings.Remember(settings.ReplaceHistory, cmbReplace.Text);

        ReloadHistory();
        HistoryCommitted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-reads the shared drop-down history into this page's combo boxes.</summary>
    public void ReloadHistory()
    {
        var settings = AppSettings.Current;
        Fill(cmbPath, settings.PathHistory);
        Fill(cmbName, settings.NameHistory);
        Fill(cmbContents, settings.ContentsHistory);
        Fill(cmbReplace, settings.ReplaceHistory);
    }

    /// <summary>Clearing Items wipes the edit box as well, so whatever is typed is put back.</summary>
    private static void Fill(ComboBox combo, List<string> history)
    {
        var typed = combo.Text;
        combo.Items.Clear();
        combo.Items.AddRange(history.Cast<object>().ToArray());
        combo.Text = typed;
    }

    // ------------------------------------------------------------- results

    private void OnRetrieveFound(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if ((uint)e.ItemIndex >= (uint)_matches.Count)
        {
            e.Item = new ListViewItem("");
            return;
        }

        var m = _matches[e.ItemIndex];
        e.Item = new ListViewItem(new[]
        {
            m.FileName, m.Folder, m.ReplacesText, m.SizeText, m.ModifiedText, m.Type
        });
    }

    private void OnRetrieveSkipped(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if ((uint)e.ItemIndex >= (uint)_skipped.Count) { e.Item = new ListViewItem(""); return; }
        var s = _skipped[e.ItemIndex];
        e.Item = new ListViewItem(new[] { s.Path, s.Reason });
    }

    private void OnRetrieveError(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if ((uint)e.ItemIndex >= (uint)_errors.Count) { e.Item = new ListViewItem(""); return; }
        var x = _errors[e.ItemIndex];
        e.Item = new ListViewItem(new[] { x.Path, x.Message });
    }

    private void OnFoundColumnClick(object? sender, ColumnClickEventArgs e)
    {
        // The trailing filler column owns the header's dead space; it carries no data to sort by.
        if (lvFound.IsFiller(e.Column)) return;

        _sortAscending = _sortColumn == e.Column ? !_sortAscending : true;
        _sortColumn = e.Column;
        ApplySort();
    }

    /// <summary>
    /// Fix #3: sorting uses the underlying typed values -- long for Size, DateTime for Modified --
    /// so ordering by time is exact rather than alphabetical on a formatted string.
    /// </summary>
    private void ApplySort()
    {
        Comparison<FileMatch> comparison = _sortColumn switch
        {
            0 => (a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => string.Compare(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase),
            2 => (a, b) => a.Replaces.CompareTo(b.Replaces),
            3 => (a, b) => a.Size.CompareTo(b.Size),
            4 => (a, b) => a.Modified.CompareTo(b.Modified),
            5 => (a, b) => string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase),
            _ => (_, _) => 0
        };

        // Ties fall back to full path, so equal sizes or timestamps still have one definite order.
        // That makes the comparison a total order, which is what lets this sort in place: Sort is
        // not stable, but with no ties left there is nothing for stability to decide. In place
        // matters because this now runs on every batch of a running search, not once at the end.
        _matches.Sort((a, b) =>
        {
            var cmp = comparison(a, b);
            if (cmp == 0) cmp = string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
            return _sortAscending ? cmp : -cmp;
        });

        // The header is owner-drawn now, so the arrow comes from _sortColumn/_sortAscending;
        // Refresh repaints the header child window as well as the rows.
        lvFound.Refresh();
    }

    /// <summary>
    /// Explorer's own menu for the selected rows, with this app's few additions below it. The
    /// shell paints it, so it follows the Windows theme rather than the palette -- which is the
    /// right trade: a hand-drawn imitation could not offer Open with, Send to, or whatever the
    /// user has installed.
    /// </summary>
    private void ShowResultsMenu(Point screen)
    {
        // What the shell is given has to exist, so a row from inside an archive stands in for
        // the archive -- Extract here, Properties and the rest then apply to the file that is
        // really there. Copy full path still copies the row, which is what it says.
        var paths = SelectedMatches().Select(m => ShellTarget(m)!)
                                     .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0) return;

        var extra = new (string Text, Action Run)[]
        {
            ("Show in Explorer", () => ShellMenu.Reveal(paths)),
            ("Copy full path", () => CopyToClipboard(paths)),
            ("Copy file name", () => CopyToClipboard(SelectedMatches().Select(m => m.FileName))),
            ("Select all rows", SelectAllResults)
        };

        if (ShellMenu.Show(this, paths, screen, extra)) return;

        _ownMenu ??= BuildResultsMenu();
        _ownMenu.Show(screen);
    }

    /// <summary>Where the menu key should open a menu: on the focused row, as Explorer does.</summary>
    private Point ContextMenuAnchor()
    {
        var row = lvFound.FocusedItem?.Bounds ?? new Rectangle(0, 0, 0, 0);
        return lvFound.PointToScreen(new Point(row.Left + 16, row.Bottom));
    }

    private ContextMenuStrip BuildResultsMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new ThemeMenuRenderer() };
        Style(() => { menu.BackColor = CMantle; menu.ForeColor = CText; });
        menu.Items.Add("&Open", null, (_, _) => ShellOpen(ShellTarget(FirstSelectedMatch())));
        menu.Items.Add("Show in &Explorer", null, (_, _) => ShellMenu.Reveal(SelectedShellTargets()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("&Copy full path", null, (_, _) => CopyToClipboard(SelectedPaths()));
        menu.Items.Add("Copy file &name", null, (_, _) => CopyToClipboard(SelectedMatches().Select(m => m.FileName)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Select &All", null, (_, _) => SelectAllResults());
        return menu;
    }

    private IEnumerable<FileMatch> SelectedMatches()
        => lvFound.SelectedIndices.Cast<int>()
                  .Where(i => (uint)i < (uint)_matches.Count)
                  .Select(i => _matches[i]);

    private IEnumerable<string> SelectedPaths() => SelectedMatches().Select(m => m.FullPath);

    /// <summary>
    /// The file an Explorer action should act on. For a row inside an archive that is the
    /// archive itself: its own <see cref="FileMatch.FullPath"/> names a place on no disk.
    /// </summary>
    private static string? ShellTarget(FileMatch? match) => match?.ArchivePath ?? match?.FullPath;

    /// <summary>
    /// The selected rows as files Explorer can act on. Capped, because this menu is the one that
    /// appears when the shell declined the selection -- which is what a Select All over a few
    /// hundred thousand results produces -- and no window needs that many items highlighted.
    /// </summary>
    private List<string> SelectedShellTargets()
        => SelectedMatches().Select(m => ShellTarget(m)!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(1000).ToList();

    /// <summary>
    /// The topmost selected row. Separate from <see cref="SelectedMatches"/> because asking for
    /// the whole selection to take the first of it walks every selected row, and after a Select
    /// All over a few hundred thousand results that is seconds -- for one file's preview.
    /// </summary>
    private FileMatch? FirstSelectedMatch()
    {
        var i = lvFound.FirstSelectedIndex;
        return (uint)i < (uint)_matches.Count ? _matches[i] : null;
    }

    public void SelectAllResults()
    {
        if (_matches.Count == 0) return;
        lvFound.SetAllSelected(true);
    }

    public void CopySelectionToClipboard() => CopyToClipboard(SelectedPaths());

    private void CopyToClipboard(IEnumerable<string> values)
    {
        var text = string.Join(Environment.NewLine, values);
        if (text.Length == 0) return;

        try { Clipboard.SetText(text); }
        catch (ExternalException) { /* another process owns the clipboard; not worth interrupting the user */ }
    }

    private void ShellOpen(string? path)
    {
        if (path == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "File open error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------------------------------------------------------- page state

    /// <summary>Everything that makes this page this page, for the settings file and for Duplicate.</summary>
    public PageState CaptureState() => new()
    {
        Id = Id,
        Title = Title,
        Path = cmbPath.Text,
        Name = cmbName.Text,
        Contents = cmbContents.Text,
        ExcludeNames = _excludeNames,
        ExcludeFolders = new List<string>(_excludeFolders),
        Subfolders = btnSubfolders.Checked,
        SearchArchives = chkArchives.Checked,
        CaseSensitive = chkCase.Checked,
        WholeWords = chkWhole.Checked,
        Hex = chkHex.Checked,
        Regex = chkRegex.Checked,
        UseSize = chkSize.Checked,
        SizeMin = txtSizeMin.Text,
        SizeMax = txtSizeMax.Text,
        SizeUnit = cmbSizeUnit.SelectedIndex,
        UseDate = chkDate.Checked,
        DateFrom = dtFrom.Value,
        DateTo = dtTo.Value,
        DateField = cmbDateField.SelectedIndex,
        SkipBinary = chkSkipBinary.Checked,
        Hidden = tsHidden.State,
        ReadOnly = tsReadOnly.State,
        Folders = tsFolders.State,
        Replace = chkReplace.Checked,
        ReplaceWith = cmbReplace.Text,
        Backup = chkBackup.Checked,
        ContextBefore = (int)numBefore.Value,
        ContextAfter = (int)numAfter.Value
    };

    public void RestoreState(PageState state)
    {
        Id = state.Id;
        Title = state.Title;
        cmbPath.Text = state.Path;
        cmbName.Text = state.Name;
        cmbContents.Text = state.Contents;
        _excludeNames = state.ExcludeNames;
        _excludeFolders = new List<string>(state.ExcludeFolders);
        btnSubfolders.Checked = state.Subfolders;
        chkArchives.Checked = state.SearchArchives;
        chkCase.Checked = state.CaseSensitive;
        chkWhole.Checked = state.WholeWords;
        chkHex.Checked = state.Hex;
        chkRegex.Checked = state.Regex;
        chkSize.Checked = state.UseSize;
        txtSizeMin.Text = state.SizeMin;
        txtSizeMax.Text = state.SizeMax;
        chkDate.Checked = state.UseDate;
        chkSkipBinary.Checked = state.SkipBinary;
        tsHidden.State = state.Hidden;
        tsReadOnly.State = state.ReadOnly;
        tsFolders.State = state.Folders;
        chkReplace.Checked = state.Replace;
        cmbReplace.Text = state.ReplaceWith;
        chkBackup.Checked = state.Backup;

        // A hand-edited or older settings file must not be able to throw here.
        cmbSizeUnit.SelectedIndex = Math.Clamp(state.SizeUnit, 0, cmbSizeUnit.Items.Count - 1);
        cmbDateField.SelectedIndex = Math.Clamp(state.DateField, 0, cmbDateField.Items.Count - 1);
        dtFrom.Value = Clamp(state.DateFrom, dtFrom);
        dtTo.Value = Clamp(state.DateTo, dtTo);
        numBefore.Value = Math.Clamp((decimal)state.ContextBefore, numBefore.Minimum, numBefore.Maximum);
        numAfter.Value = Math.Clamp((decimal)state.ContextAfter, numAfter.Minimum, numAfter.Maximum);
    }

    private static DateTime Clamp(DateTime value, DateTimePicker picker)
        => value < picker.MinDate ? picker.MinDate : value > picker.MaxDate ? picker.MaxDate : value;

    /// <summary>Copies criteria (not results) into a new page, for Pages &gt; Duplicate.</summary>
    public void CopyCriteriaTo(FindPage target)
    {
        var state = CaptureState();
        state.Title = target.Title;   // the caller names the copy
        state.Id = target.Id;         // ...and it keeps its own identity, so its own backups
        target.RestoreState(state);
    }

    public void CancelIfRunning() => CancelSearch();
}
