namespace MultiFind2;

/// <summary>
/// A non-activating popup that suggests filesystem directories below a ComboBox
/// as the user types. Supports semicolon-delimited multi-path fields: only the
/// segment around the cursor is completed.
/// </summary>
internal sealed class PathSuggestPopup : Form
{
    private readonly ListBox _list = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false,
    };

    /// <summary>Start index of the segment being completed.</summary>
    private int _segStart;
    /// <summary>End index (exclusive) of the segment being completed.</summary>
    private int _segEnd;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW — hidden from Alt+Tab
            return cp;
        }
    }

    // Clicking the popup must not steal focus from the combo box.
    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = 3; // MA_NOACTIVATE
            return;
        }
        base.WndProc(ref m);
    }

    public PathSuggestPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Controls.Add(_list);

        _list.MouseClick += (_, _) => AcceptCurrent();
    }

    /// <summary>
    /// Fired when a suggestion is accepted.
    /// Parameters: the full path, the segment start, and the segment end to replace.
    /// </summary>
    public event Action<string, int, int>? Accepted;

    public bool IsOpen => Visible;

    /// <summary>
    /// Show (or update) the popup below <paramref name="anchor"/>.
    /// </summary>
    public void ShowBelow(Control anchor, string[] items, int segStart, int segEnd)
    {
        if (items.Length == 0) { Dismiss(); return; }

        _segStart = segStart;
        _segEnd = segEnd;

        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Items.AddRange(items);
        _list.SelectedIndex = 0;
        _list.EndUpdate();

        var t = Theme.Current;
        BackColor = t.Crust;
        _list.BackColor = t.Crust;
        _list.ForeColor = t.Text;

        // Auto-size width to fit the longest item so paths aren't truncated.
        int width = anchor.Width;
        using (var g = _list.CreateGraphics())
        {
            foreach (var item in items)
            {
                var w = (int)g.MeasureString(item, _list.Font).Width + SystemInformation.VerticalScrollBarWidth + 12;
                if (w > width) width = w;
            }
        }

        // Cap at the screen width so the popup doesn't go off-screen.
        var pt = anchor.PointToScreen(new Point(0, anchor.Height));
        var screen = Screen.FromPoint(pt).WorkingArea;
        width = Math.Min(width, screen.Right - pt.X);

        int visibleCount = Math.Min(items.Length, 12);
        int itemH = _list.ItemHeight > 0 ? _list.ItemHeight : 15;
        Location = pt;
        Size = new Size(width, itemH * visibleCount + 4);

        if (!Visible) Show(anchor.FindForm());
    }

    public void Dismiss()
    {
        if (Visible) Hide();
    }

    /// <summary>Move the selection up or down. Returns true if the key was consumed.</summary>
    public bool Navigate(int delta)
    {
        if (!Visible || _list.Items.Count == 0) return false;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, _list.Items.Count - 1);
        return true;
    }

    public void AcceptCurrent()
    {
        if (_list.SelectedItem is string path)
            Accepted?.Invoke(path, _segStart, _segEnd);
        Dismiss();
    }

    // ---------------------------------------------------------------- static helpers

    /// <summary>
    /// Find the segment the cursor sits in. Segments are delimited by semicolons.
    /// Returns the typed text (segStart..cursor) and the full segment boundaries.
    /// </summary>
    public static (string typed, int segStart, int segEnd) SegmentAt(string text, int cursor)
    {
        cursor = Math.Clamp(cursor, 0, text.Length);

        int start = cursor > 0 ? text.LastIndexOf(';', cursor - 1) : -1;
        start = start < 0 ? 0 : start + 1;

        int end = text.IndexOf(';', cursor);
        if (end < 0) end = text.Length;

        // Skip leading whitespace
        int trimmed = start;
        while (trimmed < cursor && text[trimmed] == ' ') trimmed++;

        return (text[trimmed..cursor], trimmed, end);
    }

    /// <summary>
    /// Enumerate directories that match the partial path the user has typed.
    /// </summary>
    public static string[] Suggest(string typed, int max = 15)
    {
        if (typed.Length < 2) return [];

        int lastSep = typed.LastIndexOfAny(['\\', '/']);
        string dir, prefix;

        if (lastSep >= 0)
        {
            dir = typed[..(lastSep + 1)];
            prefix = typed[(lastSep + 1)..];
        }
        else if (typed.Length >= 2 && typed[1] == ':')
        {
            dir = typed[..2] + "\\";
            prefix = "";
        }
        else
        {
            return [];
        }

        try
        {
            if (!Directory.Exists(dir)) return [];
            return Directory.EnumerateDirectories(dir)
                .Where(d => Path.GetFileName(d)
                    .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .ToArray();
        }
        catch { return []; }
    }
}
