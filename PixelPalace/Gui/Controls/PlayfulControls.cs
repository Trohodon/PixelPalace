using System.Drawing.Drawing2D;

namespace PixelPalace.Gui.Controls;

public sealed class GradientPanel : Panel
{
    public Color StartColor { get; set; } = Color.FromArgb(32, 34, 48);
    public Color EndColor { get; set; } = Color.FromArgb(20, 22, 33);
    public float Angle { get; set; } = 90f;

    public GradientPanel()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, StartColor, EndColor, Angle);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}

public sealed class CandyButton : Button
{
    public Color AccentColor { get; set; } = Color.FromArgb(95, 140, 255);

    public CandyButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(86, 34);
        Padding = new Padding(14, 7, 14, 7);
        ForeColor = Color.White;
        BackColor = AccentColor;
        Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        using var path = BuildRoundedRect(rect, 11);
        using var brush = new LinearGradientBrush(rect, ControlPaint.Light(AccentColor, 0.15f), ControlPaint.Dark(AccentColor, 0.10f), 90f);
        pevent.Graphics.FillPath(brush, path);

        using var pen = new Pen(Color.FromArgb(120, 255, 255, 255));
        pevent.Graphics.DrawPath(pen, path);

        TextRenderer.DrawText(pevent.Graphics, Text, Font, rect, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath BuildRoundedRect(Rectangle rect, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}

public sealed class NeonCard : Panel
{
    public Color BorderColor { get; set; } = Color.FromArgb(90, 130, 255);

    public NeonCard()
    {
        DoubleBuffered = true;
        Padding = new Padding(10);
        BackColor = Color.FromArgb(26, 28, 40);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        using var pen = new Pen(BorderColor, 1.4f);
        using var glow = new Pen(Color.FromArgb(36, BorderColor), 6f);
        using var path = BuildRoundedRect(rect, 10);

        e.Graphics.DrawPath(glow, path);
        e.Graphics.DrawPath(pen, path);
    }

    private static GraphicsPath BuildRoundedRect(Rectangle rect, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
