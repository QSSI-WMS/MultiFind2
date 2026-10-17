namespace MultiFind2;

/// <summary>
/// A compact button that cycles through three filter states on click, mirroring the original
/// MultiFind's small toolbar toggles. The button is only 28px wide, so the state rides on the icon as a
/// badge -- nothing, a plus, a minus -- which is the same any / only / never the tooltips spell
/// out. The tooltip always says what the current state actually does, and any state other than
/// the control's default is highlighted so an active filter is never invisible.
/// </summary>
public sealed class TriStateButton : Button
{
    private static readonly Dictionary<TriState, Badge> Badges = new()
    {
        [TriState.Any] = Badge.None,
        [TriState.Yes] = Badge.Plus,
        [TriState.No] = Badge.Minus
    };

    private readonly Dictionary<TriState, string> _tips = new();
    private ToolTip? _tooltip;
    private Glyph _glyph;
    private TriState _state = TriState.Any;

    public event EventHandler? StateChanged;

    public TriStateButton()
    {
        Size = new Size(28, 23);
        TextAlign = ContentAlignment.MiddleCenter;
        ImageAlign = ContentAlignment.MiddleCenter;
        // Always flat: a visual-style button ignores BackColor, which would make the palette
        // and the active-filter highlight invisible.
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
    }

    /// <summary>Re-reads <see cref="Theme.Current"/>; called when the user switches theme.</summary>
    public void RefreshTheme() => Repaint();

    /// <summary>The state considered "no filter applied", drawn without highlight or badge.</summary>
    public TriState DefaultState { get; set; } = TriState.Any;

    /// <summary>What the button is a filter on, for anything that needs to name the icon.</summary>
    public Glyph Glyph => _glyph;

    public TriState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            Repaint();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Configure(ToolTip tooltip, Glyph glyph, TriState initial,
                          string anyTip, string yesTip, string noTip)
    {
        _tooltip = tooltip;
        _glyph = glyph;
        _tips[TriState.Any] = anyTip;
        _tips[TriState.Yes] = yesTip;
        _tips[TriState.No] = noTip;

        _state = initial;
        DefaultState = initial;
        Repaint();
    }

    public void ResetToDefault() => State = DefaultState;

    protected override void OnClick(EventArgs e)
    {
        State = _state switch
        {
            TriState.Any => TriState.Yes,
            TriState.Yes => TriState.No,
            _ => TriState.Any
        };
        base.OnClick(e);
    }

    /// <summary>The icon is drawn for one pixel size, so moving to another monitor redraws it.</summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Repaint();
    }

    private void Repaint()
    {
        if (_tips.Count == 0) return;

        _tooltip?.SetToolTip(this, _tips[_state]);

        var t = Theme.Current;
        var active = _state != DefaultState;

        BackColor = active ? t.ActiveFilterBg : t.Surface0;
        ForeColor = active ? t.ActiveFilterText : t.Text;
        FlatAppearance.BorderColor = active ? t.ActiveFilterBorder : t.Surface1;

        Text = "";
        Image = Icons.Get(_glyph, Badges[_state], ForeColor, BackColor, Icons.SizeFor(this));
    }
}
