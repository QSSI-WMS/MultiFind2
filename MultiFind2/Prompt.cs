namespace MultiFind2;

/// <summary>Minimal modal text prompt, used for renaming pages and editing the exclude list.</summary>
internal static class Prompt
{
    public static string? Show(IWin32Window owner, string title, string label, string value, string? hint = null)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(440, hint == null ? 108 : 128)
        };

        DialogChrome.Apply(form);

        var caption = DialogChrome.Caption(label, 12, 14);
        var input = new TextBox
        {
            Text = value,
            Location = new Point(12, 36),
            Width = 416,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Current.Crust,
            ForeColor = Theme.Current.Text
        };

        var ok = DialogChrome.Action("OK", DialogResult.OK, 246, form.ClientSize.Height - 36, accent: true);
        var cancel = DialogChrome.Action("Cancel", DialogResult.Cancel, 340, form.ClientSize.Height - 36);
        ok.Width = cancel.Width = 86;

        form.Controls.Add(caption);
        form.Controls.Add(input);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);

        if (hint != null)
            form.Controls.Add(new Label
            {
                Text = hint,
                AutoSize = true,
                ForeColor = Theme.Current.Subtext,
                Location = new Point(12, 62)
            });

        form.AcceptButton = ok;
        form.CancelButton = cancel;
        input.SelectAll();

        return form.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
    }
}
