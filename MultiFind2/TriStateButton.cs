namespace MultiFind2;

/// <summary>
/// A compact button that cycles through three filter states on click, mirroring MultiFind's
/// small toolbar toggles. The glyph is terse because the button is only 28px wide, so the
/// tooltip always spells out what the current state actually does, and any state other than
/// the control's default is highlighted so an active filter is never invisible.
/// </summary>
public sealed class TriStateButton : Button
{
    private readonly Dictionary<TriState, string> _labels = new();
    private readonly Dictionary<TriState, string> _tips = new();
    private ToolTip? _tooltip;
    private TriState _state = TriState.Any;

    public event EventHandler? StateChanged;

    public TriStateButton()
    {
        Size = new Size(28, 23);
        TextAlign = ContentAlignment.MiddleCenter;
        // Always flat: a visual-style button ignores BackColor, which would make the palette
        // and the active-filter highlight invisible.
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
    }

    /// <summary>Re-reads <see cref="Theme.Current"/>; called when the user switches theme.</summary>
    public void RefreshTheme() => Repaint();

    /// <summary>The state considered "no filter applied", drawn without highlight.</summary>
    public TriState DefaultState { get; set; } = TriState.Any;

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

    public void Configure(ToolTip tooltip, TriState initial,
                          string anyLabel, string anyTip,
                          string yesLabel, string yesTip,
                          string noLabel, string noTip)
    {
        _tooltip = tooltip;
        _labels[TriState.Any] = anyLabel;
        _labels[TriState.Yes] = yesLabel;
        _labels[TriState.No] = noLabel;
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

    private void Repaint()
    {
        if (_labels.Count == 0) return;

        Text = _labels[_state];
        _tooltip?.SetToolTip(this, _tips[_state]);

        var t = Theme.Current;
        var active = _state != DefaultState;

        BackColor = active ? t.ActiveFilterBg : t.Surface0;
        ForeColor = active ? t.ActiveFilterText : t.Text;
        FlatAppearance.BorderColor = active ? t.ActiveFilterBorder : t.Surface1;
    }
}
