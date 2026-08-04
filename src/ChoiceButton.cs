namespace WinFsTools;

public sealed class ChoiceButton : NoFocusButton
{
    private readonly ContextMenuStrip menu = new();
    private string[] choices = [];
    private int selectedIndex;
    private Color disabledForeground = SystemColors.GrayText;

    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= choices.Length || selectedIndex == value) return;
            selectedIndex = value;
            Text = choices[selectedIndex] + "  ▼";
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ChoiceButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        TextAlign = ContentAlignment.MiddleLeft;
        TabStop = false;
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
    }

    public void SetChoices(params string[] values)
    {
        choices = values;
        menu.Items.Clear();
        for (var index = 0; index < choices.Length; index++)
        {
            var menuItem = new ToolStripMenuItem(choices[index]) { Tag = index, AutoSize = false, Width = Math.Max(Width, 160) };
            menuItem.Click += (_, _) => SelectedIndex = (int)menuItem.Tag!;
            menu.Items.Add(menuItem);
        }

        if (choices.Length > 0)
        {
            selectedIndex = 0;
            Text = choices[0] + "  ▼";
        }
    }

    public void ApplyTheme(Color background, Color foreground, Color menuBackground, Color menuForeground)
    {
        BackColor = background;
        ForeColor = foreground;
        disabledForeground = foreground;
        FlatAppearance.MouseOverBackColor = background;
        FlatAppearance.MouseDownBackColor = background;
        menu.BackColor = menuBackground;
        menu.ForeColor = menuForeground;
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = menuBackground;
            item.ForeColor = menuForeground;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var color = Enabled ? ForeColor : disabledForeground;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (choices.Length > 0) menu.Show(this, new Point(0, Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) menu.Dispose();
        base.Dispose(disposing);
    }
}
