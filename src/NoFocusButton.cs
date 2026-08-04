namespace WinFsTools;

public class NoFocusButton : Button
{
    public NoFocusButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    protected override bool ShowFocusCues => false;

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        flags |= TextAlign switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
            ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter
        };
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
    }
}
