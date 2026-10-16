using System.Runtime.InteropServices;

namespace MultiFind2;

/// <summary>
/// Owner-draw plumbing for the controls that otherwise keep painting themselves from the
/// system theme: menus, status bar, tab strips, list headers and rows. Without these a light
/// palette on a dark Windows (or the reverse) leaves bright strips across the window.
/// </summary>
internal static class ThemePainting
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hWnd, string? app, string? id);

    /// <summary>
    /// DateTimePicker and friends are painted by comctl32 and ignore BackColor while visual
    /// styles are on. Passing empty strings turns theming off for just that window, after which
    /// it honours the colors we set. Best effort: a failure here only costs us the tint.
    /// </summary>
    public static void Unthemed(Control c)
    {
        if (!c.IsHandleCreated) return;
        try { SetWindowTheme(c.Handle, "", ""); } catch { }
    }

    /// <summary>Asks comctl32 for its dark scrollbars/hot-track on a list. No-op where unsupported.</summary>
    public static void ExplorerTheme(Control c)
    {
        if (!c.IsHandleCreated) return;
        try { SetWindowTheme(c.Handle, Theme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
    }

    // ------------------------------------------------------------------ lists

    /// <summary>
    /// Switches a ListView to owner-draw so its header stops tracking the system theme.
    /// <paramref name="rowTint"/> returns the background for a row (null = plain Base),
    /// <paramref name="sortState"/> reports the sorted column so the arrow can be drawn here --
    /// the native HDF_SORTUP flag is ignored once the header is owner-drawn -- and
    /// <paramref name="drawText"/> replaces the plain-text pass for lists that paint their own
    /// content, such as the preview's highlighted source lines.
    /// </summary>
    public static void AttachList(ListView lv, Func<int, Color?>? rowTint = null,
                                  Func<(int Column, bool Ascending)>? sortState = null,
                                  Action<DrawListViewSubItemEventArgs, Rectangle>? drawText = null)
    {
        lv.OwnerDraw = true;

        lv.DrawColumnHeader += (_, e) =>
        {
            var t = Theme.Current;
            using (var bg = new SolidBrush(t.Mantle)) e.Graphics.FillRectangle(bg, e.Bounds);
            using (var line = new Pen(t.Surface1))
            {
                e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            var sort = sortState?.Invoke() ?? (-1, true);
            var text = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
            if (sort.Column == e.ColumnIndex)
            {
                text.Width -= 10;
                DrawSortArrow(e.Graphics, e.Bounds, sort.Ascending, t.Subtext);
            }

            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", lv.Font, text, t.Subtext,
                Align(e.Header?.TextAlign ?? HorizontalAlignment.Left) |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };

        // Details view paints per-subitem; DrawItem only needs to clear the row, including the
        // dead space to the right of the last column which no subitem covers.
        lv.DrawItem += (_, e) =>
        {
            using var bg = new SolidBrush(RowColor(lv, e.ItemIndex, e.Item.Selected, rowTint));
            e.Graphics.FillRectangle(bg, e.Bounds);
        };

        lv.DrawSubItem += (_, e) =>
        {
            var t = Theme.Current;
            using (var bg = new SolidBrush(RowColor(lv, e.ItemIndex, e.Item?.Selected ?? false, rowTint)))
                e.Graphics.FillRectangle(bg, e.Bounds);

            var pad = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            if (drawText != null) { drawText(e, pad); return; }

            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", lv.Font, pad, t.Text,
                Align(lv.Columns[e.ColumnIndex].TextAlign) |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    static Color RowColor(ListView lv, int index, bool selected, Func<int, Color?>? rowTint)
    {
        var t = Theme.Current;
        if (selected) return lv.Focused ? t.SelectionBg : Theme.Current.Surface0;
        return rowTint?.Invoke(index) ?? t.Base;
    }

    static TextFormatFlags Align(HorizontalAlignment a) => a switch
    {
        HorizontalAlignment.Right => TextFormatFlags.Right,
        HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
        _ => TextFormatFlags.Left
    };

    static void DrawSortArrow(Graphics g, Rectangle bounds, bool ascending, Color color)
    {
        var x = bounds.Right - 14;
        var y = bounds.Y + bounds.Height / 2 - (ascending ? 2 : 3);
        var pts = ascending
            ? new[] { new Point(x, y + 4), new Point(x + 8, y + 4), new Point(x + 4, y) }
            : new[] { new Point(x, y), new Point(x + 8, y), new Point(x + 4, y + 4) };

        using var brush = new SolidBrush(color);
        var mode = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.FillPolygon(brush, pts);
        g.SmoothingMode = mode;
    }
}

/// <summary>
/// A ListView that owns a trailing empty "filler" column. The header only raises
/// DrawColumnHeader for real columns; the strip to the right of the last one is painted by
/// comctl32 in the system colour, which shows up as a bright band across a dark palette.
/// The filler hands that space to our own painting. Rows don't need it -- DrawItem already
/// clears the whole row width.
/// </summary>
internal sealed class ThemedListView : ListView
{
    private ColumnHeader? _filler;
    private bool _sizing;

    /// <summary>True for the trailing filler, which carries no data and cannot be sorted.</summary>
    public bool IsFiller(int column) => _filler != null && column == Columns.Count - 1;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _filler ??= Columns.Add("", 0);
        SizeFiller();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SizeFiller();
    }

    protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
    {
        base.OnColumnWidthChanged(e);
        SizeFiller();
    }

    /// <summary>
    /// VirtualMode throws unless every column has a subitem, and the filler is a column the
    /// rest of the app doesn't know about -- so pad here rather than in every retrieve handler.
    /// </summary>
    protected override void OnRetrieveVirtualItem(RetrieveVirtualItemEventArgs e)
    {
        base.OnRetrieveVirtualItem(e);
        if (e.Item == null) return;
        while (e.Item.SubItems.Count < Columns.Count) e.Item.SubItems.Add("");
    }

    private void SizeFiller()
    {
        if (_sizing || _filler == null) return;
        _sizing = true;
        var used = Columns.Cast<ColumnHeader>().Where(c => c != _filler).Sum(c => c.Width);
        _filler.Width = Math.Max(0, ClientSize.Width - used);
        _sizing = false;
    }
}

/// <summary>
/// A TabControl that paints its whole strip, border and items from the palette. Owner-drawing
/// just the items is not enough: the strip behind them and the 3D frame around the page area
/// are painted by comctl32 from the system theme, so a light palette on a dark Windows (or the
/// reverse) leaves grey bands above and below every page.
/// </summary>
internal sealed class ThemedTabControl : TabControl
{
    public ThemedTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;

        // Mantle everywhere, then Base under the page: the pages are child windows and paint
        // themselves, so this only shows through as the thin frame around them.
        using (var strip = new SolidBrush(t.Mantle)) e.Graphics.FillRectangle(strip, ClientRectangle);
        using (var body = new SolidBrush(t.Base)) e.Graphics.FillRectangle(body, DisplayRectangle);

        for (var i = 0; i < TabCount; i++)
        {
            var bounds = GetTabRect(i);
            var selected = i == SelectedIndex;

            using (var bg = new SolidBrush(selected ? t.Surface0 : t.Mantle))
                e.Graphics.FillRectangle(bg, bounds);

            TextRenderer.DrawText(e.Graphics, TabPages[i].Text, Font, bounds,
                selected ? t.Text : t.Subtext,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>Palette-backed colors for MenuStrip, StatusStrip and drop-downs.</summary>
internal sealed class ThemeColorTable : ProfessionalColorTable
{
    static Theme T => Theme.Current;

    public override Color MenuStripGradientBegin => T.Mantle;
    public override Color MenuStripGradientEnd => T.Mantle;
    public override Color StatusStripGradientBegin => T.Mantle;
    public override Color StatusStripGradientEnd => T.Mantle;
    public override Color ToolStripDropDownBackground => T.Mantle;

    public override Color MenuItemSelected => T.Surface0;
    public override Color MenuItemSelectedGradientBegin => T.Surface0;
    public override Color MenuItemSelectedGradientEnd => T.Surface0;
    public override Color MenuItemPressedGradientBegin => T.Surface0;
    public override Color MenuItemPressedGradientMiddle => T.Surface0;
    public override Color MenuItemPressedGradientEnd => T.Surface0;
    public override Color MenuItemBorder => T.Surface1;
    public override Color MenuBorder => T.Surface1;

    public override Color ImageMarginGradientBegin => T.Mantle;
    public override Color ImageMarginGradientMiddle => T.Mantle;
    public override Color ImageMarginGradientEnd => T.Mantle;

    public override Color SeparatorDark => T.Surface1;
    public override Color SeparatorLight => T.Surface1;
}

internal sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemeMenuRenderer() : base(new ThemeColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item!.Enabled ? Theme.Current.Text : Theme.Current.Overlay;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Current.Text;
        base.OnRenderArrow(e);
    }
}
