using System.Drawing.Drawing2D;

namespace PixelPalace.Gui.Controls;

public sealed class HsvColorPicker : Control
{
    private const int HueBarWidth = 24;
    private const int PickerPadding = 8;

    private float _hue;
    private float _saturation = 1f;
    private float _value = 1f;
    private bool _trackingHue;
    private bool _trackingSv;

    public event Action<Color>? ColorChanged;

    public HsvColorPicker()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(22, 24, 34);
        MinimumSize = new Size(220, 160);
    }

    public Color SelectedColor
    {
        get => ColorFromHsv(_hue, _saturation, _value);
        set => SetColor(value, emitEvent: false);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var svRect = GetSvRect();
        var hueRect = GetHueRect();

        DrawSvSquare(e.Graphics, svRect);
        DrawHueBar(e.Graphics, hueRect);
        DrawHandles(e.Graphics, svRect, hueRect);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        HandlePointer(e.Location, true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        HandlePointer(e.Location, false);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _trackingHue = false;
        _trackingSv = false;
    }

    private void HandlePointer(Point point, bool freshPress)
    {
        var svRect = GetSvRect();
        var hueRect = GetHueRect();

        if (freshPress)
        {
            _trackingSv = svRect.Contains(point);
            _trackingHue = hueRect.Contains(point);
        }

        if (_trackingSv)
        {
            UpdateSvFromPoint(point, svRect);
            EmitColorChanged();
            Invalidate();
            return;
        }

        if (_trackingHue)
        {
            UpdateHueFromPoint(point, hueRect);
            EmitColorChanged();
            Invalidate();
            return;
        }
    }

    private Rectangle GetSvRect()
    {
        var width = Math.Max(40, Width - HueBarWidth - PickerPadding * 3);
        var height = Math.Max(40, Height - PickerPadding * 2);
        return new Rectangle(PickerPadding, PickerPadding, width, height);
    }

    private Rectangle GetHueRect()
    {
        var svRect = GetSvRect();
        return new Rectangle(svRect.Right + PickerPadding, PickerPadding, HueBarWidth, svRect.Height);
    }

    private void DrawSvSquare(Graphics g, Rectangle rect)
    {
        using (var hueBrush = new SolidBrush(ColorFromHsv(_hue, 1f, 1f)))
        {
            g.FillRectangle(hueBrush, rect);
        }

        using (var satBrush = new LinearGradientBrush(rect, Color.White, Color.Transparent, LinearGradientMode.Horizontal))
        {
            g.FillRectangle(satBrush, rect);
        }

        using (var valBrush = new LinearGradientBrush(rect, Color.Transparent, Color.Black, LinearGradientMode.Vertical))
        {
            g.FillRectangle(valBrush, rect);
        }

        using var border = new Pen(Color.FromArgb(170, 220, 228, 248));
        g.DrawRectangle(border, rect);
    }

    private void DrawHueBar(Graphics g, Rectangle rect)
    {
        using var brush = new LinearGradientBrush(rect, Color.Red, Color.Red, LinearGradientMode.Vertical);
        var blend = new ColorBlend
        {
            Colors =
            [
                Color.Red,
                Color.Yellow,
                Color.Lime,
                Color.Cyan,
                Color.Blue,
                Color.Magenta,
                Color.Red
            ],
            Positions = [0f, 0.167f, 0.333f, 0.5f, 0.667f, 0.833f, 1f]
        };
        brush.InterpolationColors = blend;
        g.FillRectangle(brush, rect);

        using var border = new Pen(Color.FromArgb(170, 220, 228, 248));
        g.DrawRectangle(border, rect);
    }

    private void DrawHandles(Graphics g, Rectangle svRect, Rectangle hueRect)
    {
        var svX = svRect.X + _saturation * svRect.Width;
        var svY = svRect.Y + (1f - _value) * svRect.Height;
        var svHandle = new RectangleF(svX - 5, svY - 5, 10, 10);
        using (var ring = new Pen(Color.Black, 2))
        {
            g.DrawEllipse(ring, svHandle);
        }
        using (var ring = new Pen(Color.White, 1))
        {
            g.DrawEllipse(ring, svHandle);
        }

        var hueY = hueRect.Y + (_hue / 360f) * hueRect.Height;
        using var huePen = new Pen(Color.White, 2);
        g.DrawLine(huePen, hueRect.X - 3, hueY, hueRect.Right + 3, hueY);
    }

    private void UpdateSvFromPoint(Point point, Rectangle svRect)
    {
        var clampedX = Math.Clamp(point.X, svRect.Left, svRect.Right);
        var clampedY = Math.Clamp(point.Y, svRect.Top, svRect.Bottom);
        _saturation = (clampedX - svRect.Left) / (float)svRect.Width;
        _value = 1f - (clampedY - svRect.Top) / (float)svRect.Height;
    }

    private void UpdateHueFromPoint(Point point, Rectangle hueRect)
    {
        var clampedY = Math.Clamp(point.Y, hueRect.Top, hueRect.Bottom);
        var ratio = (clampedY - hueRect.Top) / (float)hueRect.Height;
        _hue = Math.Clamp(ratio * 360f, 0f, 360f);
    }

    private void EmitColorChanged()
    {
        ColorChanged?.Invoke(ColorFromHsv(_hue, _saturation, _value));
    }

    private void SetColor(Color color, bool emitEvent)
    {
        ToHsv(color, out _hue, out _saturation, out _value);
        Invalidate();
        if (emitEvent)
        {
            EmitColorChanged();
        }
    }

    private static Color ColorFromHsv(float hue, float saturation, float value)
    {
        hue = Math.Clamp(hue, 0f, 360f);
        saturation = Math.Clamp(saturation, 0f, 1f);
        value = Math.Clamp(value, 0f, 1f);

        var c = value * saturation;
        var x = c * (1f - Math.Abs((hue / 60f % 2f) - 1f));
        var m = value - c;

        float r1 = 0;
        float g1 = 0;
        float b1 = 0;

        if (hue < 60f)
        {
            r1 = c; g1 = x; b1 = 0;
        }
        else if (hue < 120f)
        {
            r1 = x; g1 = c; b1 = 0;
        }
        else if (hue < 180f)
        {
            r1 = 0; g1 = c; b1 = x;
        }
        else if (hue < 240f)
        {
            r1 = 0; g1 = x; b1 = c;
        }
        else if (hue < 300f)
        {
            r1 = x; g1 = 0; b1 = c;
        }
        else
        {
            r1 = c; g1 = 0; b1 = x;
        }

        var r = (int)Math.Round((r1 + m) * 255f);
        var g = (int)Math.Round((g1 + m) * 255f);
        var b = (int)Math.Round((b1 + m) * 255f);
        return Color.FromArgb(255, r, g, b);
    }

    private static void ToHsv(Color color, out float hue, out float saturation, out float value)
    {
        hue = color.GetHue();
        var max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255f;
        var min = Math.Min(color.R, Math.Min(color.G, color.B)) / 255f;
        value = max;
        var delta = max - min;
        saturation = max == 0f ? 0f : delta / max;
    }
}
