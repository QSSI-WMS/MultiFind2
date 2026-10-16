namespace MultiFind2;

partial class FindPage
{
    private System.ComponentModel.IContainer components = null;

    private Panel pnlTop;
    private GroupBox grpGeneral;
    private GroupBox grpAdvanced;
    private Panel pnlButtons;

    private Label lblPath;
    private ComboBox cmbPath;
    private Button btnPathBrowse;
    private Button btnPathMulti;
    private CheckBox btnSubfolders;

    private Label lblName;
    private ComboBox cmbName;
    private Button btnNameMulti;
    private TriStateButton tsHidden;
    private TriStateButton tsReadOnly;
    private TriStateButton tsFolders;

    private Label lblContents;
    private ComboBox cmbContents;
    private CheckBox chkCase;
    private CheckBox chkWhole;
    private CheckBox chkHex;
    private CheckBox chkRegex;

    private CheckBox chkSize;
    private TextBox txtSizeMin;
    private TextBox txtSizeMax;
    private ComboBox cmbSizeUnit;

    private CheckBox chkDate;
    private DateTimePicker dtFrom;
    private DateTimePicker dtTo;
    private ComboBox cmbDateField;

    private CheckBox chkReplace;
    private ComboBox cmbReplace;

    private CheckBox chkSkipBinary;
    private CheckBox chkBackup;
    private Button btnBackups;

    private Button btnFind;
    private Button btnCancel;
    private Button btnClearCriteria;
    private Button btnClearResults;
    private Button btnReplace;

    private SplitContainer splitResults;
    private TabControl tabResults;
    private TabPage tabFound;
    private TabPage tabSkipped;
    private TabPage tabErrors;
    private ThemedListView lvFound;
    private ThemedListView lvSkipped;
    private ThemedListView lvErrors;

    private TabControl tabPreview;
    private TabPage tabMatches;
    private TabPage tabFile;
    private PreviewList pvMatches;
    private PreviewList pvFile;
    private Panel pnlContext;
    private Label lblContext;
    private NumericUpDown numBefore;
    private NumericUpDown numAfter;

    private ToolTip tips;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _previewTimer.Dispose();
            _previewCts?.Cancel();
            _previewCts?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        pnlTop = new Panel();
        grpGeneral = new GroupBox();
        grpAdvanced = new GroupBox();
        pnlButtons = new Panel();
        lblPath = new Label();
        cmbPath = new ComboBox();
        btnPathBrowse = new Button();
        btnPathMulti = new Button();
        btnSubfolders = new CheckBox();
        lblName = new Label();
        cmbName = new ComboBox();
        btnNameMulti = new Button();
        tsHidden = new TriStateButton();
        tsReadOnly = new TriStateButton();
        tsFolders = new TriStateButton();
        lblContents = new Label();
        cmbContents = new ComboBox();
        chkCase = new CheckBox();
        chkWhole = new CheckBox();
        chkHex = new CheckBox();
        chkRegex = new CheckBox();
        chkSize = new CheckBox();
        txtSizeMin = new TextBox();
        txtSizeMax = new TextBox();
        cmbSizeUnit = new ComboBox();
        chkDate = new CheckBox();
        dtFrom = new DateTimePicker();
        dtTo = new DateTimePicker();
        cmbDateField = new ComboBox();
        chkReplace = new CheckBox();
        cmbReplace = new ComboBox();
        chkSkipBinary = new CheckBox();
        chkBackup = new CheckBox();
        btnBackups = new Button();
        btnFind = new Button();
        btnCancel = new Button();
        btnClearCriteria = new Button();
        btnClearResults = new Button();
        btnReplace = new Button();
        splitResults = new SplitContainer();
        tabResults = new ThemedTabControl();
        tabFound = new TabPage();
        tabSkipped = new TabPage();
        tabErrors = new TabPage();
        lvFound = new ThemedListView();
        lvSkipped = new ThemedListView();
        lvErrors = new ThemedListView();
        tabPreview = new ThemedTabControl();
        tabMatches = new TabPage();
        tabFile = new TabPage();
        pvMatches = new PreviewList();
        pvFile = new PreviewList();
        pnlContext = new Panel();
        lblContext = new Label();
        numBefore = new NumericUpDown();
        numAfter = new NumericUpDown();
        tips = new ToolTip(components);

        pnlTop.SuspendLayout();
        grpGeneral.SuspendLayout();
        grpAdvanced.SuspendLayout();
        pnlButtons.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitResults).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numBefore).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numAfter).BeginInit();
        splitResults.SuspendLayout();
        tabResults.SuspendLayout();
        tabFound.SuspendLayout();
        tabSkipped.SuspendLayout();
        tabErrors.SuspendLayout();
        tabPreview.SuspendLayout();
        tabMatches.SuspendLayout();
        tabFile.SuspendLayout();
        pnlContext.SuspendLayout();
        SuspendLayout();

        // Establish design-time sizes BEFORE adding right-anchored children. WinForms derives a
        // control's anchor offsets from its parent's size at layout time, so if the groups were
        // still at their default 200px width here, every Anchor.Right child would be pushed off
        // the visible area once docking stretched the parent.
        Size = new Size(1264, 700);
        pnlTop.Size = new Size(1264, 160);
        pnlTop.Padding = new Padding(4, 2, 4, 2);
        grpGeneral.Size = new Size(572, 156);
        grpAdvanced.Size = new Size(560, 156);
        pnlButtons.Size = new Size(124, 156);

        // pnlTop  (docking order matters: buttons and Advanced pin right, General fills)
        pnlTop.Name = "pnlTop";

        // grpGeneral
        grpGeneral.Controls.Add(lblPath);
        grpGeneral.Controls.Add(cmbPath);
        grpGeneral.Controls.Add(btnPathBrowse);
        grpGeneral.Controls.Add(btnPathMulti);
        grpGeneral.Controls.Add(btnSubfolders);
        grpGeneral.Controls.Add(lblName);
        grpGeneral.Controls.Add(cmbName);
        grpGeneral.Controls.Add(btnNameMulti);
        grpGeneral.Controls.Add(tsHidden);
        grpGeneral.Controls.Add(tsReadOnly);
        grpGeneral.Controls.Add(tsFolders);
        grpGeneral.Controls.Add(lblContents);
        grpGeneral.Controls.Add(cmbContents);
        grpGeneral.Controls.Add(chkCase);
        grpGeneral.Controls.Add(chkWhole);
        grpGeneral.Controls.Add(chkHex);
        grpGeneral.Controls.Add(chkRegex);
        grpGeneral.Controls.Add(chkReplace);
        grpGeneral.Controls.Add(cmbReplace);
        grpGeneral.Name = "grpGeneral";
        grpGeneral.Text = "General";
        grpGeneral.TabIndex = 0;

        lblPath.AutoSize = true;
        lblPath.Location = new Point(8, 24);
        lblPath.Name = "lblPath";
        lblPath.Text = "Search path:";

        cmbPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        cmbPath.Location = new Point(92, 20);
        cmbPath.Name = "cmbPath";
        cmbPath.Size = new Size(334, 23);
        cmbPath.TabIndex = 1;

        btnPathBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnPathBrowse.Location = new Point(432, 20);
        btnPathBrowse.Name = "btnPathBrowse";
        btnPathBrowse.Size = new Size(28, 23);
        btnPathBrowse.Text = "...";
        btnPathBrowse.TabIndex = 2;
        btnPathBrowse.UseVisualStyleBackColor = true;
        tips.SetToolTip(btnPathBrowse, "Browse for folder");

        btnPathMulti.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnPathMulti.Location = new Point(462, 20);
        btnPathMulti.Name = "btnPathMulti";
        btnPathMulti.Size = new Size(28, 23);
        // "..." browses for one thing; the list glyph means "edit the include/exclude lists".
        // Without the distinction the two adjacent buttons read as the same control.
        btnPathMulti.Text = "\u2261";
        btnPathMulti.TabIndex = 3;
        btnPathMulti.UseVisualStyleBackColor = true;
        tips.SetToolTip(btnPathMulti, "Browse for multiple folders");

        // Gap before the subfolder toggle groups the browse actions apart from the mode switch.
        btnSubfolders.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSubfolders.Appearance = Appearance.Button;
        btnSubfolders.Checked = true;
        btnSubfolders.CheckState = CheckState.Checked;
        btnSubfolders.Location = new Point(522, 20);
        btnSubfolders.Name = "btnSubfolders";
        btnSubfolders.Size = new Size(28, 23);
        btnSubfolders.Text = "S";
        btnSubfolders.TextAlign = ContentAlignment.MiddleCenter;
        btnSubfolders.TabIndex = 4;
        btnSubfolders.UseVisualStyleBackColor = true;
        tips.SetToolTip(btnSubfolders, "Include subfolders");

        lblName.AutoSize = true;
        lblName.Location = new Point(8, 52);
        lblName.Name = "lblName";
        lblName.Text = "File name:";

        cmbName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        cmbName.Location = new Point(92, 48);
        cmbName.Name = "cmbName";
        cmbName.Size = new Size(334, 23);
        cmbName.TabIndex = 5;

        btnNameMulti.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnNameMulti.Location = new Point(432, 48);
        btnNameMulti.Name = "btnNameMulti";
        btnNameMulti.Size = new Size(28, 23);
        btnNameMulti.Text = "\u2261";
        btnNameMulti.TabIndex = 6;
        btnNameMulti.UseVisualStyleBackColor = true;
        tips.SetToolTip(btnNameMulti, "Select multiple file names");

        ConfigureTriState(tsHidden, 462, 48, TriState.Any,
            "H", "Search all files (hidden and non-hidden)",
            "H+", "Search only hidden files",
            "H-", "Search only non-hidden files");
        tsHidden.TabIndex = 7;

        ConfigureTriState(tsReadOnly, 492, 48, TriState.Any,
            "R", "Search all files (read-only and writable)",
            "R+", "Search only read-only files",
            "R-", "Search only non-read-only files");
        tsReadOnly.TabIndex = 8;

        // Default is "don't match folders": this is a file search, so folders are noise unless asked for.
        ConfigureTriState(tsFolders, 522, 48, TriState.No,
            "F", "Match all (files and folders)",
            "F+", "Match only folders",
            "F-", "Do not match folders");
        tsFolders.TabIndex = 9;

        lblContents.AutoSize = true;
        lblContents.Location = new Point(8, 80);
        lblContents.Name = "lblContents";
        lblContents.Text = "File contents:";

        cmbContents.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        cmbContents.Location = new Point(92, 76);
        cmbContents.Name = "cmbContents";
        cmbContents.Size = new Size(334, 23);
        cmbContents.TabIndex = 10;

        ConfigureToggle(chkCase, "Aa", 432, 76, "Case sensitive search");
        ConfigureToggle(chkWhole, "W", 462, 76, "Whole words only");
        ConfigureToggle(chkHex, "0x", 492, 76, "Hex (e.g. '12AB3F')");
        ConfigureToggle(chkRegex, ".*", 522, 76, "Regular expression");
        chkCase.TabIndex = 11;
        chkWhole.TabIndex = 12;
        chkHex.TabIndex = 13;
        chkRegex.TabIndex = 14;

        // Replace sits directly under File contents because that is the text it rewrites;
        // it reads as one sentence, and the pairing makes it obvious what gets substituted.
        chkReplace.AutoSize = true;
        chkReplace.Location = new Point(8, 108);
        chkReplace.Name = "chkReplace";
        // "Replace with:" would run into the combo at this column; the other three rows label
        // themselves in the same 84px gutter, and lining up matters more than the extra word.
        chkReplace.Text = "Replace:";
        chkReplace.TabIndex = 15;
        chkReplace.UseVisualStyleBackColor = true;
        tips.SetToolTip(chkReplace, "Enable replacing the matched file contents");

        cmbReplace.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        cmbReplace.Location = new Point(92, 104);
        cmbReplace.Name = "cmbReplace";
        cmbReplace.Size = new Size(458, 23);
        cmbReplace.TabIndex = 16;
        tips.SetToolTip(cmbReplace, "Replacement text. In regex mode $1, $2... expand to captured groups.");

        // grpAdvanced
        grpAdvanced.Controls.Add(chkSize);
        grpAdvanced.Controls.Add(txtSizeMin);
        grpAdvanced.Controls.Add(txtSizeMax);
        grpAdvanced.Controls.Add(cmbSizeUnit);
        grpAdvanced.Controls.Add(chkDate);
        grpAdvanced.Controls.Add(dtFrom);
        grpAdvanced.Controls.Add(dtTo);
        grpAdvanced.Controls.Add(cmbDateField);
        grpAdvanced.Controls.Add(chkSkipBinary);
        grpAdvanced.Controls.Add(chkBackup);
        grpAdvanced.Controls.Add(btnBackups);
        grpAdvanced.Name = "grpAdvanced";
        grpAdvanced.Text = "Advanced";
        grpAdvanced.TabIndex = 11;

        chkSize.AutoSize = true;
        chkSize.Location = new Point(8, 24);
        chkSize.Name = "chkSize";
        chkSize.Text = "Size between:";
        chkSize.TabIndex = 12;
        chkSize.UseVisualStyleBackColor = true;

        txtSizeMin.Location = new Point(112, 21);
        txtSizeMin.Name = "txtSizeMin";
        txtSizeMin.Size = new Size(130, 23);
        txtSizeMin.PlaceholderText = "Minimum";
        txtSizeMin.TabIndex = 13;

        txtSizeMax.Location = new Point(248, 21);
        txtSizeMax.Name = "txtSizeMax";
        txtSizeMax.Size = new Size(130, 23);
        txtSizeMax.PlaceholderText = "Maximum";
        txtSizeMax.TabIndex = 14;

        cmbSizeUnit.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbSizeUnit.Location = new Point(384, 21);
        cmbSizeUnit.Name = "cmbSizeUnit";
        cmbSizeUnit.Size = new Size(90, 23);
        cmbSizeUnit.TabIndex = 15;

        chkDate.AutoSize = true;
        chkDate.Location = new Point(8, 52);
        chkDate.Name = "chkDate";
        chkDate.Text = "Date between:";
        chkDate.TabIndex = 16;
        chkDate.UseVisualStyleBackColor = true;

        dtFrom.Format = DateTimePickerFormat.Short;
        dtFrom.Location = new Point(112, 49);
        dtFrom.Name = "dtFrom";
        dtFrom.Size = new Size(130, 23);
        dtFrom.TabIndex = 17;

        dtTo.Format = DateTimePickerFormat.Short;
        dtTo.Location = new Point(248, 49);
        dtTo.Name = "dtTo";
        dtTo.Size = new Size(130, 23);
        dtTo.TabIndex = 18;

        cmbDateField.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbDateField.Location = new Point(384, 49);
        cmbDateField.Name = "cmbDateField";
        cmbDateField.Size = new Size(110, 23);
        cmbDateField.TabIndex = 19;

        chkSkipBinary.AutoSize = true;
        chkSkipBinary.Checked = true;
        chkSkipBinary.CheckState = CheckState.Checked;
        chkSkipBinary.Location = new Point(8, 80);
        chkSkipBinary.Name = "chkSkipBinary";
        chkSkipBinary.Text = "Skip binary files";
        chkSkipBinary.TabIndex = 20;
        chkSkipBinary.UseVisualStyleBackColor = true;

        chkBackup.AutoSize = true;
        chkBackup.Checked = true;
        chkBackup.CheckState = CheckState.Checked;
        chkBackup.Location = new Point(8, 108);
        chkBackup.Name = "chkBackup";
        chkBackup.Text = "Back up every replaced file";
        chkBackup.TabIndex = 21;
        chkBackup.UseVisualStyleBackColor = true;
        tips.SetToolTip(chkBackup,
            "The original is copied into a dated folder under %TMP%, not left beside the file. " +
            "Backups older than 10 days are discarded at startup.");

        // Beside the checkbox it belongs to, rather than with the action buttons: this one
        // opens a folder, it does not act on the files in the list.
        btnBackups.Location = new Point(222, 104);
        btnBackups.Name = "btnBackups";
        btnBackups.Size = new Size(96, 25);
        btnBackups.Text = "Backups...";
        btnBackups.TabIndex = 22;
        btnBackups.UseVisualStyleBackColor = true;
        tips.SetToolTip(btnBackups, "Open this page's backup folder");

        // pnlButtons
        pnlButtons.Controls.Add(btnFind);
        pnlButtons.Controls.Add(btnCancel);
        pnlButtons.Controls.Add(btnClearCriteria);
        pnlButtons.Controls.Add(btnClearResults);
        pnlButtons.Controls.Add(btnReplace);
        pnlButtons.Name = "pnlButtons";

        ConfigureActionButton(btnFind, "Find", 6);
        ConfigureActionButton(btnCancel, "Cancel", 35);
        ConfigureActionButton(btnClearCriteria, "Clear criteria", 64);
        ConfigureActionButton(btnClearResults, "Clear results", 93);
        // Set apart by a gap: everything above only reads files, this one rewrites them.
        ConfigureActionButton(btnReplace, "Replace...", 128);
        btnFind.TabIndex = 23;
        btnCancel.TabIndex = 24;
        btnClearCriteria.TabIndex = 25;
        btnClearResults.TabIndex = 26;
        btnReplace.Name = "btnReplace";
        btnReplace.TabIndex = 27;
        tips.SetToolTip(btnReplace, "Replace the text in the files found above");

        // tabResults
        tabResults.Controls.Add(tabFound);
        tabResults.Controls.Add(tabSkipped);
        tabResults.Controls.Add(tabErrors);
        tabResults.Dock = DockStyle.Fill;
        tabResults.Name = "tabResults";
        tabResults.SelectedIndex = 0;
        tabResults.TabIndex = 28;

        ConfigureResultsTab(tabFound, "tabFound", "Found (0)", lvFound);
        ConfigureResultsTab(tabSkipped, "tabSkipped", "Skipped (0)", lvSkipped);
        ConfigureResultsTab(tabErrors, "tabErrors", "Errors (0)", lvErrors);

        // --- preview --------------------------------------------------------------------
        // The context spinners belong to the Matches tab: they have no meaning on File, which
        // always shows every line.
        lblContext.AutoSize = true;
        lblContext.Location = new Point(6, 6);
        lblContext.Name = "lblContext";
        lblContext.Text = "Context lines, before / after:";

        ConfigureContextSpinner(numBefore, "numBefore", 182,
            "Lines of context shown before each match (grep -B)");
        ConfigureContextSpinner(numAfter, "numAfter", 232,
            "Lines of context shown after each match (grep -A)");

        pnlContext.Controls.Add(lblContext);
        pnlContext.Controls.Add(numBefore);
        pnlContext.Controls.Add(numAfter);
        pnlContext.Dock = DockStyle.Top;
        pnlContext.Height = 28;
        pnlContext.Name = "pnlContext";

        ConfigurePreviewTab(tabMatches, "tabMatches", "Matches", pvMatches);
        ConfigurePreviewTab(tabFile, "tabFile", "File", pvFile);
        // Added after the list so Dock.Top wins the space above a Dock.Fill sibling.
        tabMatches.Controls.Add(pnlContext);

        tabPreview.Controls.Add(tabMatches);
        tabPreview.Controls.Add(tabFile);
        tabPreview.Dock = DockStyle.Fill;
        tabPreview.Name = "tabPreview";
        tabPreview.SelectedIndex = 0;
        tabPreview.TabIndex = 30;

        // The results and the preview share the space under the criteria; a splitter rather than
        // a fixed height because which half matters depends entirely on what is being searched.
        splitResults.Dock = DockStyle.Fill;
        splitResults.Name = "splitResults";
        splitResults.Orientation = Orientation.Horizontal;
        splitResults.Size = new Size(1264, 540);
        splitResults.Panel1MinSize = 80;
        splitResults.Panel2MinSize = 60;
        splitResults.SplitterDistance = 320;
        splitResults.SplitterWidth = 5;
        splitResults.TabIndex = 29;
        splitResults.Panel1.Controls.Add(tabResults);
        splitResults.Panel2.Controls.Add(tabPreview);

        // Dock last, so every child above has already captured correct anchor offsets.
        // Add order sets dock precedence: buttons and Advanced pin right, General takes the rest.
        pnlTop.Controls.Add(grpGeneral);
        pnlTop.Controls.Add(grpAdvanced);
        pnlTop.Controls.Add(pnlButtons);
        pnlButtons.Dock = DockStyle.Right;
        grpAdvanced.Dock = DockStyle.Right;
        grpGeneral.Dock = DockStyle.Fill;
        pnlTop.Dock = DockStyle.Top;

        // FindPage
        Controls.Add(splitResults);
        Controls.Add(pnlTop);
        Name = "FindPage";

        pnlTop.ResumeLayout(false);
        grpGeneral.ResumeLayout(false);
        grpGeneral.PerformLayout();
        grpAdvanced.ResumeLayout(false);
        grpAdvanced.PerformLayout();
        pnlButtons.ResumeLayout(false);
        tabResults.ResumeLayout(false);
        tabFound.ResumeLayout(false);
        tabSkipped.ResumeLayout(false);
        tabErrors.ResumeLayout(false);
        tabPreview.ResumeLayout(false);
        tabMatches.ResumeLayout(false);
        tabFile.ResumeLayout(false);
        pnlContext.ResumeLayout(false);
        pnlContext.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)numBefore).EndInit();
        ((System.ComponentModel.ISupportInitialize)numAfter).EndInit();
        splitResults.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitResults).EndInit();
        ResumeLayout(false);
    }

    private void ConfigureContextSpinner(NumericUpDown spinner, string name, int x, string tip)
    {
        spinner.Location = new Point(x, 3);
        spinner.Maximum = 50;
        spinner.Name = name;
        spinner.Size = new Size(44, 23);
        spinner.TextAlign = HorizontalAlignment.Right;
        tips.SetToolTip(spinner, tip);
    }

    private static void ConfigurePreviewTab(TabPage page, string name, string text, PreviewList list)
    {
        list.Dock = DockStyle.Fill;
        list.Name = "pv" + name[3..];

        page.Controls.Add(list);
        page.Name = name;
        page.Text = text;
        page.Padding = new Padding(2);
        page.UseVisualStyleBackColor = true;
    }

    private void ConfigureToggle(CheckBox box, string text, int x, int y, string tip)
    {
        box.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        box.Appearance = Appearance.Button;
        box.Location = new Point(x, y);
        box.Name = "chk" + text;
        box.Size = new Size(28, 23);
        box.Text = text;
        box.TextAlign = ContentAlignment.MiddleCenter;
        box.UseVisualStyleBackColor = true;
        tips.SetToolTip(box, tip);
    }

    private void ConfigureTriState(TriStateButton button, int x, int y, TriState initial,
                                   string anyLabel, string anyTip,
                                   string yesLabel, string yesTip,
                                   string noLabel, string noTip)
    {
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        button.Location = new Point(x, y);
        button.Configure(tips, initial, anyLabel, anyTip, yesLabel, yesTip, noLabel, noTip);
    }

    private static void ConfigureActionButton(Button button, string text, int y)
    {
        button.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        button.Location = new Point(6, y);
        button.Name = "btn" + text.Replace(" ", "");
        button.Size = new Size(112, 25);
        button.Text = text;
        button.UseVisualStyleBackColor = true;
    }

    private static void ConfigureResultsTab(TabPage page, string name, string text, ListView list)
    {
        list.Dock = DockStyle.Fill;
        list.FullRowSelect = true;
        list.HideSelection = false;
        list.MultiSelect = true;
        list.Name = "lv" + name[3..];
        list.UseCompatibleStateImageBehavior = false;
        list.View = View.Details;
        list.VirtualMode = true;

        page.Controls.Add(list);
        page.Name = name;
        page.Text = text;
        page.Padding = new Padding(2);
        page.UseVisualStyleBackColor = true;
    }
}
