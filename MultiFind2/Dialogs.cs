namespace MultiFind2;

internal static class DialogChrome
{
    /// <summary>The banded title/subtitle header MultiFind2 puts at the top of its pickers.</summary>
    public static Panel Header(string title, string subtitle, int width)
    {
        var t = Theme.Current;

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 10.5f, FontStyle.Bold),
            ForeColor = t.Blue,
            Location = new Point(16, 8)
        };

        // Position the subtitle below the title using the actual rendered height,
        // so the two lines never overlap regardless of DPI.
        int subtitleY = 8 + titleLabel.PreferredHeight + 2;

        var subtitleLabel = new Label
        {
            Text = subtitle,
            AutoSize = true,
            ForeColor = t.Subtext,
            Location = new Point(18, subtitleY)
        };

        int panelH = subtitleY + subtitleLabel.PreferredHeight + 6;

        var panel = new Panel
        {
            BackColor = t.Mantle,
            Location = new Point(0, 0),
            Size = new Size(width, panelH)
        };

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(subtitleLabel);
        panel.Controls.Add(new Panel
        {
            BackColor = t.Surface1,
            Location = new Point(0, panelH - 1),
            Size = new Size(width, 1)
        });

        return panel;
    }

    public static TextBox ListBox(int x, int y, int w, int h) => new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        AcceptsReturn = true,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Current.Crust,
        ForeColor = Theme.Current.Text,
        Location = new Point(x, y),
        Size = new Size(w, h)
    };

    public static Label Caption(string text, int x, int y)
        => new() { Text = text, AutoSize = true, ForeColor = Theme.Current.Overlay, Location = new Point(x, y) };

    public static Button Action(string text, DialogResult result, int x, int y, bool accent = false)
    {
        var t = Theme.Current;
        var button = new Button
        {
            Text = text,
            DialogResult = result,
            Location = new Point(x, y),
            Size = new Size(90, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = accent ? t.Blue : t.Surface0,
            ForeColor = accent ? t.Base : t.Text
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = accent ? t.Blue : t.Surface1;
        return button;
    }

    /// <summary>Dialogs are modal and short-lived, so they just take the palette at construction.</summary>
    public static void Apply(Form form)
    {
        form.BackColor = Theme.Current.Base;
        form.ForeColor = Theme.Current.Text;
        form.HandleCreated += (_, _) => Theme.ApplyTitleBar(form);
    }

    /// <summary>Splits a multiline box into clean entries, dropping blanks.</summary>
    public static List<string> Entries(TextBox box)
        => box.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
}

/// <summary>"Browse for multiple folders" -- include and exclude folder lists, one path per line.</summary>
public sealed class FolderListDialog : Form
{
    private readonly TextBox _include;
    private readonly TextBox _exclude;

    public List<string> IncludeFolders => DialogChrome.Entries(_include);
    public List<string> ExcludeFolders => DialogChrome.Entries(_exclude);

    public FolderListDialog(IEnumerable<string> include, IEnumerable<string> exclude)
    {
        Text = "Browse for multiple folders";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 400);

        SuspendLayout();

        _include = DialogChrome.ListBox(12, 88, 420, 110);
        _exclude = DialogChrome.ListBox(12, 232, 420, 110);
        _include.Lines = include.ToArray();
        _exclude.Lines = exclude.ToArray();

        var browseInclude = DialogChrome.Action("Browse...", DialogResult.None, 444, 88);
        var browseExclude = DialogChrome.Action("Browse...", DialogResult.None, 444, 232);
        browseInclude.Size = browseExclude.Size = new Size(100, 26);
        browseInclude.Click += (_, _) => AppendFolder(_include);
        browseExclude.Click += (_, _) => AppendFolder(_exclude);

        var ok = DialogChrome.Action("OK", DialogResult.OK, 360, 358, accent: true);
        var cancel = DialogChrome.Action("Cancel", DialogResult.Cancel, 458, 358);

        DialogChrome.Apply(this);
        Controls.Add(DialogChrome.Header("Select Folders", "Here you can choose what folders will be searched", ClientSize.Width));
        Controls.Add(DialogChrome.Caption("Include folders list:", 12, 68));
        Controls.Add(_include);
        Controls.Add(browseInclude);
        Controls.Add(DialogChrome.Caption("Exclude folders list:", 12, 212));
        Controls.Add(_exclude);
        Controls.Add(browseExclude);
        Controls.Add(ok);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true);
    }

    private void AppendFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog { Description = "Path to look in", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var existing = DialogChrome.Entries(target);
        if (existing.Any(p => p.Equals(dialog.SelectedPath, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "The selected folder is already in the list",
                            "Browse for multiple folders", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        existing.Add(dialog.SelectedPath);
        target.Lines = existing.ToArray();
    }
}

/// <summary>"Select multiple file names" -- include and exclude name patterns, one per line.</summary>
public sealed class FileNameListDialog : Form
{
    private readonly TextBox _include;
    private readonly TextBox _exclude;

    public List<string> IncludeNames => DialogChrome.Entries(_include);
    public List<string> ExcludeNames => DialogChrome.Entries(_exclude);

    public FileNameListDialog(IEnumerable<string> include, IEnumerable<string> exclude)
    {
        Text = "Select multiple file names";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 330);

        SuspendLayout();

        _include = DialogChrome.ListBox(12, 88, 240, 180);
        _exclude = DialogChrome.ListBox(268, 88, 240, 180);
        _include.Lines = include.ToArray();
        _exclude.Lines = exclude.ToArray();

        var ok = DialogChrome.Action("OK", DialogResult.OK, 320, 288, accent: true);
        var cancel = DialogChrome.Action("Cancel", DialogResult.Cancel, 418, 288);

        DialogChrome.Apply(this);
        Controls.Add(DialogChrome.Header("Select file names", "Here you can choose names of files to search for", ClientSize.Width));
        Controls.Add(DialogChrome.Caption("Include file names:", 12, 68));
        Controls.Add(_include);
        Controls.Add(DialogChrome.Caption("Exclude file names list:", 268, 68));
        Controls.Add(_exclude);
        Controls.Add(ok);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true);
    }
}
