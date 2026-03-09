using System.Drawing.Drawing2D;
using System.Text;
using System.Threading;
using PixelPalace.Core.Models;

namespace PixelPalace.Gui.Controls;

public sealed class PixelCanvas : Control
{
    private const string ClipboardFormat = "PixelPalace.Selection.v1";
    private static Color?[,]? s_internalClipboard;
    private readonly System.Windows.Forms.Timer _selectionAntsTimer = new();

    private Point _hoverPixel = new(-1, -1);
    private Point? _previousPaintPoint;
    private Point? _lineStartPoint;
    private Point _panAnchorScreen;
    private PointF _panAnchorOffset;
    private bool _isPainting;
    private bool _isPanning;
    private bool _isSelecting;
    private bool _isMovingSelection;
    private Point _selectionStartPoint;
    private Point _selectionCurrentPoint;
    private Point _selectionMoveAnchor;
    private Point _selectionMoveDelta;
    private Rectangle? _selectionBounds;
    private Rectangle? _selectionMoveSourceBounds;
    private Color?[,]? _selectionMoveBuffer;
    private float _selectionDashOffset;

    public PixelCanvas()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(22, 24, 31);
        Zoom = 20;

        _selectionAntsTimer.Interval = 110;
        _selectionAntsTimer.Tick += (_, _) =>
        {
            if (!_isSelecting && !_selectionBounds.HasValue)
            {
                return;
            }

            _selectionDashOffset -= 1f;
            Invalidate();
        };
        _selectionAntsTimer.Start();
    }

    public PixelDocument? Document { get; set; }
    public int CurrentFrameIndex { get; set; }
    public int CurrentLayerIndex { get; set; }
    public PixelTool Tool { get; set; } = PixelTool.Pencil;
    public Color PrimaryColor { get; set; } = Color.Black;
    public Color SecondaryColor { get; set; } = Color.Transparent;
    public int BrushSize { get; set; } = 1;
    public int Zoom { get; private set; }
    public PointF CanvasOffset { get; private set; } = new(100, 80);
    public bool ShowGrid { get; set; } = true;
    public bool ShowOnionSkin { get; set; } = true;
    public bool ShowPixelPreview { get; set; } = true;
    public bool SpacePanning { get; set; }
    public bool HasSelection => _selectionBounds.HasValue;

    public event Action<Color>? ColorPicked;
    public event Action<Color>? PaintColorApplied;
    public event Action? CanvasChanged;
    public event Action<Point>? HoverPixelChanged;
    public event Action<int>? ZoomChanged;

    public void ResetView()
    {
        Zoom = 20;
        CanvasOffset = new PointF(100, 80);
        ZoomChanged?.Invoke(Zoom);
        Invalidate();
    }

    public void FitToView(int padding = 70)
    {
        if (Document is null || Width <= padding * 2 || Height <= padding * 2)
        {
            return;
        }

        var scaleX = (Width - padding * 2f) / Document.Width;
        var scaleY = (Height - padding * 2f) / Document.Height;
        var fit = Math.Clamp((int)Math.Floor(Math.Min(scaleX, scaleY)), 2, 72);

        Zoom = fit;
        CenterCanvas();
        ZoomChanged?.Invoke(Zoom);
    }

    public void CenterCanvas()
    {
        if (Document is null)
        {
            return;
        }

        var pixelWidth = Document.Width * Zoom;
        var pixelHeight = Document.Height * Zoom;
        CanvasOffset = new PointF((Width - pixelWidth) / 2f, (Height - pixelHeight) / 2f);
        Invalidate();
    }

    public void StepZoom(int deltaSteps, Point anchorScreen)
    {
        if (Document is null)
        {
            return;
        }

        var oldZoom = Zoom;
        var next = Math.Clamp(Zoom + deltaSteps * 2, 2, 80);
        if (next == oldZoom)
        {
            return;
        }

        if (TryPixelFromScreen(anchorScreen, out var anchorPixel))
        {
            Zoom = next;
            CanvasOffset = new PointF(
                anchorScreen.X - anchorPixel.X * Zoom,
                anchorScreen.Y - anchorPixel.Y * Zoom);
        }
        else
        {
            Zoom = next;
        }

        ZoomChanged?.Invoke(Zoom);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        StepZoom(e.Delta > 0 ? 1 : -1, e.Location);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (Document is null)
        {
            return;
        }

        Focus();

        if (e.Button == MouseButtons.Middle || (SpacePanning && e.Button == MouseButtons.Left))
        {
            StartPanning(e.Location);
            return;
        }

        if (!TryPixelFromScreen(e.Location, out var pixel))
        {
            return;
        }

        if (e.Button == MouseButtons.Right && Tool == PixelTool.Picker)
        {
            PickColorAt(pixel);
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (Tool == PixelTool.RectSelect)
        {
            HandleSelectionMouseDown(pixel);
            return;
        }

        _isPainting = true;
        _previousPaintPoint = pixel;
        _lineStartPoint ??= pixel;

        if (Tool is PixelTool.Fill or PixelTool.Picker)
        {
            ApplyToolAt(pixel, commit: true);
            _isPainting = false;
            return;
        }

        ApplyToolAt(pixel, commit: false);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (Document is null)
        {
            return;
        }

        if (_isPanning)
        {
            CanvasOffset = new PointF(
                _panAnchorOffset.X + (e.X - _panAnchorScreen.X),
                _panAnchorOffset.Y + (e.Y - _panAnchorScreen.Y));
            Invalidate();
            return;
        }

        if (TryPixelFromScreen(e.Location, out var pixel))
        {
            if (_hoverPixel != pixel)
            {
                _hoverPixel = pixel;
                HoverPixelChanged?.Invoke(pixel);
                Invalidate();
            }
        }
        else if (_hoverPixel.X != -1)
        {
            _hoverPixel = new Point(-1, -1);
            HoverPixelChanged?.Invoke(_hoverPixel);
            Invalidate();
        }

        if (!_isPainting || e.Button != MouseButtons.Left)
        {
            if (_isSelecting && e.Button == MouseButtons.Left && TryPixelFromScreen(e.Location, out var selectPixel))
            {
                _selectionCurrentPoint = selectPixel;
                Invalidate();
            }

            if (_isMovingSelection && e.Button == MouseButtons.Left && TryPixelFromScreen(e.Location, out var movePixel))
            {
                _selectionMoveDelta = new Point(movePixel.X - _selectionMoveAnchor.X, movePixel.Y - _selectionMoveAnchor.Y);
                Invalidate();
            }

            return;
        }

        if (!TryPixelFromScreen(e.Location, out var paintPixel))
        {
            return;
        }

        if ((ModifierKeys & Keys.Shift) == Keys.Shift || Tool == PixelTool.Line)
        {
            Invalidate();
            return;
        }

        if (_previousPaintPoint.HasValue)
        {
            foreach (var step in RasterLine(_previousPaintPoint.Value, paintPixel))
            {
                ApplyToolAt(step, commit: false);
            }
        }

        _previousPaintPoint = paintPixel;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Middle || (SpacePanning && e.Button == MouseButtons.Left))
        {
            _isPanning = false;
            Cursor = Cursors.Cross;
            return;
        }

        if (!_isPainting || e.Button != MouseButtons.Left)
        {
            if (Tool == PixelTool.RectSelect && e.Button == MouseButtons.Left)
            {
                HandleSelectionMouseUp(e.Location);
            }

            return;
        }

        var lineCommitted = false;
        if (TryPixelFromScreen(e.Location, out var pixel) && ((ModifierKeys & Keys.Shift) == Keys.Shift || Tool == PixelTool.Line) && _lineStartPoint.HasValue)
        {
            ApplyLine(_lineStartPoint.Value, pixel, true);
            lineCommitted = true;
        }

        _isPainting = false;
        _previousPaintPoint = null;
        _lineStartPoint = null;
        if (Tool == PixelTool.Pencil && !lineCommitted)
        {
            PaintColorApplied?.Invoke(PrimaryColor);
        }

        CanvasChanged?.Invoke();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

        DrawWorkspaceBackground(e.Graphics);

        if (Document is null)
        {
            DrawCenterMessage(e.Graphics, "Create or open a project");
            return;
        }

        DrawCanvasShadow(e.Graphics);

        if (ShowOnionSkin)
        {
            DrawOnionSkin(e.Graphics, CurrentFrameIndex - 1, Color.FromArgb(85, 70, 170, 255));
            DrawOnionSkin(e.Graphics, CurrentFrameIndex + 1, Color.FromArgb(85, 255, 125, 80));
        }

        DrawCheckerBackground(e.Graphics);
        DrawDocumentPixels(e.Graphics, Document.ComposeFrame(CurrentFrameIndex));

        if (ShowGrid && Zoom >= 8)
        {
            DrawGrid(e.Graphics);
        }

        DrawCanvasBorder(e.Graphics);

        if (ShowPixelPreview)
        {
            DrawHoverPreview(e.Graphics);
        }

        DrawSelectionOverlay(e.Graphics);
        DrawSelectionMovePreview(e.Graphics);

        if (_isPainting && (_lineStartPoint.HasValue && _hoverPixel.X >= 0) && (Tool == PixelTool.Line || (ModifierKeys & Keys.Shift) == Keys.Shift))
        {
            DrawPreviewLine(e.Graphics, _lineStartPoint.Value, _hoverPixel);
        }
    }

    private void StartPanning(Point screen)
    {
        _isPanning = true;
        _panAnchorScreen = screen;
        _panAnchorOffset = CanvasOffset;
        Cursor = Cursors.SizeAll;
    }

    private void PickColorAt(Point pixel)
    {
        if (Document is null)
        {
            return;
        }

        var composed = Document.ComposeFrame(CurrentFrameIndex);
        var color = composed[pixel.X, pixel.Y];
        if (color.HasValue)
        {
            ColorPicked?.Invoke(color.Value);
        }
    }

    private void ApplyToolAt(Point pixel, bool commit)
    {
        if (Document is null)
        {
            return;
        }

        var frame = Document.Frames[CurrentFrameIndex];
        if (CurrentLayerIndex < 0 || CurrentLayerIndex >= frame.Layers.Count)
        {
            return;
        }

        var layer = frame.Layers[CurrentLayerIndex];

        switch (Tool)
        {
            case PixelTool.Pencil:
                StampBrush(layer, pixel, PrimaryColor);
                if (commit)
                {
                    CanvasChanged?.Invoke();
                }

                Invalidate();
                break;
            case PixelTool.Eraser:
                StampBrush(layer, pixel, null);
                if (commit)
                {
                    CanvasChanged?.Invoke();
                }

                Invalidate();
                break;
            case PixelTool.Fill:
                FloodFill(layer, pixel, PrimaryColor);
                PaintColorApplied?.Invoke(PrimaryColor);
                CanvasChanged?.Invoke();
                Invalidate();
                break;
            case PixelTool.Picker:
                PickColorAt(pixel);
                break;
            case PixelTool.Line:
                if (_lineStartPoint.HasValue)
                {
                    ApplyLine(_lineStartPoint.Value, pixel, commit);
                }

                break;
            case PixelTool.RectSelect:
                break;
        }
    }

    public void ClearSelection()
    {
        _selectionBounds = null;
        _isSelecting = false;
        _isMovingSelection = false;
        _selectionMoveBuffer = null;
        _selectionMoveSourceBounds = null;
        _selectionMoveDelta = Point.Empty;
        Invalidate();
    }

    public bool CopySelectionToClipboard()
    {
        if (Document is null || !TryGetActiveLayer(out var layer) || !_selectionBounds.HasValue)
        {
            return false;
        }

        var bounds = _selectionBounds.Value;
        s_internalClipboard = ExtractLayerRegion(layer, bounds);
        var payload = BuildClipboardPayload(layer, bounds);
        if (!string.IsNullOrEmpty(payload))
        {
            return TrySetClipboardPayload(payload);
        }

        return true;
    }

    public bool CutSelectionToClipboard()
    {
        if (Document is null || !TryGetActiveLayer(out var layer) || !_selectionBounds.HasValue)
        {
            return false;
        }

        _ = CopySelectionToClipboard();

        ClearLayerRegion(layer, _selectionBounds.Value);
        CanvasChanged?.Invoke();
        Invalidate();
        return true;
    }

    public bool PasteClipboardToSelection()
    {
        if (Document is null || !TryGetActiveLayer(out var layer))
        {
            return false;
        }

        if (TryGetClipboardPayload(out var payload))
        {
            var destX = _selectionBounds?.X ?? 0;
            var destY = _selectionBounds?.Y ?? 0;
            var pastedBounds = PastePayload(layer, payload, destX, destY);
            if (!pastedBounds.HasValue)
            {
                return false;
            }

            _selectionBounds = pastedBounds;
            CanvasChanged?.Invoke();
            Invalidate();
            return true;
        }

        if (s_internalClipboard is null)
        {
            if (!TryPasteImageFromClipboard(layer, out var imageBounds))
            {
                return false;
            }

            _selectionBounds = imageBounds;
            CanvasChanged?.Invoke();
            Invalidate();
            return true;
        }

        var fallbackDestX = _selectionBounds?.X ?? 0;
        var fallbackDestY = _selectionBounds?.Y ?? 0;
        var fallbackPastedBounds = PastePayload(layer, (Color?[,])s_internalClipboard.Clone(), fallbackDestX, fallbackDestY);
        if (!fallbackPastedBounds.HasValue)
        {
            return false;
        }

        _selectionBounds = fallbackPastedBounds;
        CanvasChanged?.Invoke();
        Invalidate();
        return true;
    }

    public bool FlipSelectionHorizontal()
    {
        if (Document is null || !TryGetActiveLayer(out var layer) || !_selectionBounds.HasValue)
        {
            return false;
        }

        var rect = _selectionBounds.Value;
        var buffer = ExtractLayerRegion(layer, rect);
        var w = buffer.GetLength(0);
        var h = buffer.GetLength(1);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                layer.SetPixel(rect.X + x, rect.Y + y, buffer[w - 1 - x, y]);
            }
        }

        CanvasChanged?.Invoke();
        Invalidate();
        return true;
    }

    public bool FlipSelectionVertical()
    {
        if (Document is null || !TryGetActiveLayer(out var layer) || !_selectionBounds.HasValue)
        {
            return false;
        }

        var rect = _selectionBounds.Value;
        var buffer = ExtractLayerRegion(layer, rect);
        var w = buffer.GetLength(0);
        var h = buffer.GetLength(1);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                layer.SetPixel(rect.X + x, rect.Y + y, buffer[x, h - 1 - y]);
            }
        }

        CanvasChanged?.Invoke();
        Invalidate();
        return true;
    }

    public bool RotateSelection90Clockwise()
    {
        if (Document is null || !TryGetActiveLayer(out var layer) || !_selectionBounds.HasValue)
        {
            return false;
        }

        var rect = _selectionBounds.Value;
        var buffer = ExtractLayerRegion(layer, rect);
        var srcW = buffer.GetLength(0);
        var srcH = buffer.GetLength(1);
        var rotatedW = srcH;
        var rotatedH = srcW;

        var rotation = new Color?[rotatedW, rotatedH];
        for (var y = 0; y < srcH; y++)
        {
            for (var x = 0; x < srcW; x++)
            {
                rotation[srcH - 1 - y, x] = buffer[x, y];
            }
        }

        ClearLayerRegion(layer, rect);
        var newBounds = new Rectangle(rect.X, rect.Y, rotatedW, rotatedH);
        var clipped = ClipRectToLayer(newBounds, layer.Width, layer.Height);
        if (!clipped.HasValue)
        {
            CanvasChanged?.Invoke();
            Invalidate();
            return true;
        }

        var final = clipped.Value;
        for (var y = 0; y < final.Height; y++)
        {
            for (var x = 0; x < final.Width; x++)
            {
                layer.SetPixel(final.X + x, final.Y + y, rotation[x, y]);
            }
        }

        _selectionBounds = final;
        CanvasChanged?.Invoke();
        Invalidate();
        return true;
    }

    public bool RotateSelection90CounterClockwise()
    {
        if (Document is null || !TryGetActiveLayer(out var layer) || !_selectionBounds.HasValue)
        {
            return false;
        }

        var rect = _selectionBounds.Value;
        var buffer = ExtractLayerRegion(layer, rect);
        var srcW = buffer.GetLength(0);
        var srcH = buffer.GetLength(1);
        var rotatedW = srcH;
        var rotatedH = srcW;

        var rotation = new Color?[rotatedW, rotatedH];
        for (var y = 0; y < srcH; y++)
        {
            for (var x = 0; x < srcW; x++)
            {
                rotation[y, srcW - 1 - x] = buffer[x, y];
            }
        }

        ClearLayerRegion(layer, rect);
        var newBounds = new Rectangle(rect.X, rect.Y, rotatedW, rotatedH);
        var clipped = ClipRectToLayer(newBounds, layer.Width, layer.Height);
        if (!clipped.HasValue)
        {
            CanvasChanged?.Invoke();
            Invalidate();
            return true;
        }

        var final = clipped.Value;
        for (var y = 0; y < final.Height; y++)
        {
            for (var x = 0; x < final.Width; x++)
            {
                layer.SetPixel(final.X + x, final.Y + y, rotation[x, y]);
            }
        }

        _selectionBounds = final;
        CanvasChanged?.Invoke();
        Invalidate();
        return true;
    }

    public bool NudgeSelection(int deltaX, int deltaY)
    {
        if (Document is null || !TryGetActiveLayer(out var layer) || !_selectionBounds.HasValue)
        {
            return false;
        }

        if (deltaX == 0 && deltaY == 0)
        {
            return false;
        }

        var source = _selectionBounds.Value;
        var buffer = ExtractLayerRegion(layer, source);
        ClearLayerRegion(layer, source);

        var target = new Rectangle(source.X + deltaX, source.Y + deltaY, source.Width, source.Height);
        var clipped = ClipRectToLayer(target, layer.Width, layer.Height);
        if (!clipped.HasValue)
        {
            _selectionBounds = null;
            CanvasChanged?.Invoke();
            Invalidate();
            return true;
        }

        var final = clipped.Value;
        for (var y = 0; y < final.Height; y++)
        {
            for (var x = 0; x < final.Width; x++)
            {
                var srcX = x + (final.X - target.X);
                var srcY = y + (final.Y - target.Y);
                layer.SetPixel(final.X + x, final.Y + y, buffer[srcX, srcY]);
            }
        }

        _selectionBounds = final;
        CanvasChanged?.Invoke();
        Invalidate();
        return true;
    }

    public void SelectAll()
    {
        if (Document is null)
        {
            return;
        }

        _selectionBounds = new Rectangle(0, 0, Document.Width, Document.Height);
        _isSelecting = false;
        _isMovingSelection = false;
        Invalidate();
    }

    public bool DeleteSelection()
    {
        if (Document is null || !TryGetActiveLayer(out var layer) || !_selectionBounds.HasValue)
        {
            return false;
        }

        ClearLayerRegion(layer, _selectionBounds.Value);
        CanvasChanged?.Invoke();
        Invalidate();
        return true;
    }

    private void ApplyLine(Point start, Point end, bool commit)
    {
        if (Document is null)
        {
            return;
        }

        var frame = Document.Frames[CurrentFrameIndex];
        if (CurrentLayerIndex < 0 || CurrentLayerIndex >= frame.Layers.Count)
        {
            return;
        }

        var layer = frame.Layers[CurrentLayerIndex];
        foreach (var point in RasterLine(start, end))
        {
            StampBrush(layer, point, PrimaryColor);
        }

        if (commit)
        {
            PaintColorApplied?.Invoke(PrimaryColor);
            CanvasChanged?.Invoke();
        }

        Invalidate();
    }

    private void FloodFill(PixelLayer layer, Point start, Color fillColor)
    {
        var target = layer.GetPixel(start.X, start.Y);
        if (target.HasValue && target.Value.ToArgb() == fillColor.ToArgb())
        {
            return;
        }

        var visited = new bool[layer.Width, layer.Height];
        var queue = new Queue<Point>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (visited[p.X, p.Y])
            {
                continue;
            }

            visited[p.X, p.Y] = true;

            var current = layer.GetPixel(p.X, p.Y);
            var matchesTarget = target.HasValue
                ? current.HasValue && current.Value.ToArgb() == target.Value.ToArgb()
                : !current.HasValue;

            if (!matchesTarget)
            {
                continue;
            }

            layer.SetPixel(p.X, p.Y, fillColor);

            if (p.X > 0)
            {
                queue.Enqueue(new Point(p.X - 1, p.Y));
            }

            if (p.X < layer.Width - 1)
            {
                queue.Enqueue(new Point(p.X + 1, p.Y));
            }

            if (p.Y > 0)
            {
                queue.Enqueue(new Point(p.X, p.Y - 1));
            }

            if (p.Y < layer.Height - 1)
            {
                queue.Enqueue(new Point(p.X, p.Y + 1));
            }
        }
    }

    private IEnumerable<Point> RasterLine(Point start, Point end)
    {
        var x0 = start.X;
        var y0 = start.Y;
        var x1 = end.X;
        var y1 = end.Y;

        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;

        while (true)
        {
            yield return new Point(x0, y0);

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private void StampBrush(PixelLayer layer, Point center, Color? color)
    {
        var size = Math.Max(1, BrushSize);
        var radius = size / 2;

        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                var x = center.X + dx;
                var y = center.Y + dy;

                if (x < 0 || y < 0 || x >= layer.Width || y >= layer.Height)
                {
                    continue;
                }

                layer.SetPixel(x, y, color);
            }
        }
    }

    private bool TryPixelFromScreen(Point screen, out Point pixel)
    {
        pixel = Point.Empty;

        if (Document is null)
        {
            return false;
        }

        var x = (int)Math.Floor((screen.X - CanvasOffset.X) / Zoom);
        var y = (int)Math.Floor((screen.Y - CanvasOffset.Y) / Zoom);

        if (x < 0 || y < 0 || x >= Document.Width || y >= Document.Height)
        {
            return false;
        }

        pixel = new Point(x, y);
        return true;
    }

    private RectangleF GetDocumentScreenBounds()
    {
        if (Document is null)
        {
            return RectangleF.Empty;
        }

        return new RectangleF(CanvasOffset.X, CanvasOffset.Y, Document.Width * Zoom, Document.Height * Zoom);
    }

    private void DrawWorkspaceBackground(Graphics g)
    {
        using var brush = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(16, 20, 40),
            Color.FromArgb(11, 14, 26),
            LinearGradientMode.Vertical);
        g.FillRectangle(brush, ClientRectangle);
    }

    private void DrawCenterMessage(Graphics g, string message)
    {
        using var font = new Font("Segoe UI", 12, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(180, 192, 232));
        var size = g.MeasureString(message, font);
        g.DrawString(message, font, brush, (Width - size.Width) / 2f, (Height - size.Height) / 2f);
    }

    private void DrawCanvasShadow(Graphics g)
    {
        var bounds = GetDocumentScreenBounds();
        if (bounds.IsEmpty)
        {
            return;
        }

        using var shadowBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
        var shadowRect = bounds;
        shadowRect.Offset(8, 8);
        g.FillRectangle(shadowBrush, shadowRect);
    }

    private void DrawCheckerBackground(Graphics g)
    {
        if (Document is null)
        {
            return;
        }

        var a = Color.FromArgb(56, 56, 64);
        var b = Color.FromArgb(76, 76, 86);

        using var brushA = new SolidBrush(a);
        using var brushB = new SolidBrush(b);

        for (var x = 0; x < Document.Width; x++)
        {
            for (var y = 0; y < Document.Height; y++)
            {
                var drawX = CanvasOffset.X + x * Zoom;
                var drawY = CanvasOffset.Y + y * Zoom;
                g.FillRectangle((x + y) % 2 == 0 ? brushA : brushB, drawX, drawY, Zoom, Zoom);
            }
        }
    }

    private void DrawOnionSkin(Graphics g, int frameIndex, Color tint)
    {
        if (Document is null || frameIndex < 0 || frameIndex >= Document.Frames.Count)
        {
            return;
        }

        var composed = Document.ComposeFrame(frameIndex);

        for (var x = 0; x < Document.Width; x++)
        {
            for (var y = 0; y < Document.Height; y++)
            {
                var pixel = composed[x, y];
                if (!pixel.HasValue)
                {
                    continue;
                }

                var mixed = Color.FromArgb(
                    tint.A,
                    (pixel.Value.R + tint.R) / 2,
                    (pixel.Value.G + tint.G) / 2,
                    (pixel.Value.B + tint.B) / 2);

                using var brush = new SolidBrush(mixed);
                g.FillRectangle(brush, CanvasOffset.X + x * Zoom, CanvasOffset.Y + y * Zoom, Zoom, Zoom);
            }
        }
    }

    private void DrawDocumentPixels(Graphics g, Color?[,] pixels)
    {
        if (Document is null)
        {
            return;
        }

        for (var x = 0; x < Document.Width; x++)
        {
            for (var y = 0; y < Document.Height; y++)
            {
                var pixel = pixels[x, y];
                if (!pixel.HasValue)
                {
                    continue;
                }

                using var brush = new SolidBrush(pixel.Value);
                g.FillRectangle(brush, CanvasOffset.X + x * Zoom, CanvasOffset.Y + y * Zoom, Zoom, Zoom);
            }
        }
    }

    private void DrawGrid(Graphics g)
    {
        if (Document is null)
        {
            return;
        }

        using var pen = new Pen(Color.FromArgb(80, 0, 0, 0));

        for (var x = 0; x <= Document.Width; x++)
        {
            var drawX = CanvasOffset.X + x * Zoom;
            g.DrawLine(pen, drawX, CanvasOffset.Y, drawX, CanvasOffset.Y + Document.Height * Zoom);
        }

        for (var y = 0; y <= Document.Height; y++)
        {
            var drawY = CanvasOffset.Y + y * Zoom;
            g.DrawLine(pen, CanvasOffset.X, drawY, CanvasOffset.X + Document.Width * Zoom, drawY);
        }
    }

    private void DrawCanvasBorder(Graphics g)
    {
        var bounds = GetDocumentScreenBounds();
        if (bounds.IsEmpty)
        {
            return;
        }

        using var pen = new Pen(Color.FromArgb(180, 220, 226, 240), 2);
        g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private void DrawHoverPreview(Graphics g)
    {
        if (Document is null || _hoverPixel.X < 0 || _hoverPixel.Y < 0)
        {
            return;
        }

        var rect = new RectangleF(
            CanvasOffset.X + _hoverPixel.X * Zoom,
            CanvasOffset.Y + _hoverPixel.Y * Zoom,
            Zoom,
            Zoom);

        using var fill = new SolidBrush(Color.FromArgb(84, PrimaryColor));
        using var border = new Pen(Color.FromArgb(220, 255, 255, 255));

        if (Tool == PixelTool.Eraser)
        {
            using var eraseFill = new SolidBrush(Color.FromArgb(80, 255, 120, 120));
            g.FillRectangle(eraseFill, rect);
        }
        else
        {
            g.FillRectangle(fill, rect);
        }

        g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
    }

    private void HandleSelectionMouseDown(Point pixel)
    {
        if (Document is null || !TryGetActiveLayer(out var layer))
        {
            return;
        }

        if (_selectionBounds.HasValue && _selectionBounds.Value.Contains(pixel))
        {
            _isMovingSelection = true;
            _selectionMoveAnchor = pixel;
            _selectionMoveDelta = Point.Empty;
            _selectionMoveSourceBounds = _selectionBounds;
            _selectionMoveBuffer = ExtractLayerRegion(layer, _selectionBounds.Value);
            ClearLayerRegion(layer, _selectionBounds.Value);
            Invalidate();
            return;
        }

        _isSelecting = true;
        _selectionStartPoint = pixel;
        _selectionCurrentPoint = pixel;
        _selectionBounds = new Rectangle(pixel.X, pixel.Y, 1, 1);
        Invalidate();
    }

    private void HandleSelectionMouseUp(Point location)
    {
        if (Document is null || !TryGetActiveLayer(out var layer))
        {
            return;
        }

        if (_isMovingSelection && _selectionMoveSourceBounds.HasValue && _selectionMoveBuffer is not null)
        {
            var src = _selectionMoveSourceBounds.Value;
            var target = new Rectangle(src.X + _selectionMoveDelta.X, src.Y + _selectionMoveDelta.Y, src.Width, src.Height);
            var clipped = ClipRectToLayer(target, layer.Width, layer.Height);

            if (clipped.HasValue)
            {
                var final = clipped.Value;
                for (var y = 0; y < final.Height; y++)
                {
                    for (var x = 0; x < final.Width; x++)
                    {
                        var srcX = x + (final.X - target.X);
                        var srcY = y + (final.Y - target.Y);
                        layer.SetPixel(final.X + x, final.Y + y, _selectionMoveBuffer[srcX, srcY]);
                    }
                }

                _selectionBounds = final;
            }
            else
            {
                _selectionBounds = null;
            }

            _isMovingSelection = false;
            _selectionMoveBuffer = null;
            _selectionMoveSourceBounds = null;
            _selectionMoveDelta = Point.Empty;
            CanvasChanged?.Invoke();
            Invalidate();
            return;
        }

        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        if (TryPixelFromScreen(location, out var end))
        {
            _selectionCurrentPoint = end;
        }

        var normalized = NormalizeSelection(_selectionStartPoint, _selectionCurrentPoint);
        _selectionBounds = ClipRectToLayer(normalized, layer.Width, layer.Height);
        Invalidate();
    }

    private Rectangle NormalizeSelection(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X, b.X);
        var bottom = Math.Max(a.Y, b.Y);
        return new Rectangle(x, y, right - x + 1, bottom - y + 1);
    }

    private void DrawSelectionOverlay(Graphics g)
    {
        if (Document is null)
        {
            return;
        }

        Rectangle? rect = null;
        if (_isSelecting)
        {
            rect = NormalizeSelection(_selectionStartPoint, _selectionCurrentPoint);
        }
        else if (_selectionBounds.HasValue)
        {
            rect = _selectionBounds.Value;
        }

        if (!rect.HasValue)
        {
            return;
        }

        var r = rect.Value;
        var screen = new RectangleF(
            CanvasOffset.X + r.X * Zoom,
            CanvasOffset.Y + r.Y * Zoom,
            r.Width * Zoom,
            r.Height * Zoom);

        using var pen = new Pen(Color.FromArgb(235, 255, 255, 255), 1f)
        {
            DashStyle = DashStyle.Custom,
            DashPattern = [4f, 4f],
            DashOffset = _selectionDashOffset
        };
        g.DrawRectangle(pen, screen.X, screen.Y, screen.Width, screen.Height);
    }

    private void DrawSelectionMovePreview(Graphics g)
    {
        if (Document is null || !_isMovingSelection || !_selectionMoveSourceBounds.HasValue || _selectionMoveBuffer is null)
        {
            return;
        }

        var src = _selectionMoveSourceBounds.Value;
        var target = new Rectangle(src.X + _selectionMoveDelta.X, src.Y + _selectionMoveDelta.Y, src.Width, src.Height);
        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                var color = _selectionMoveBuffer[x, y];
                if (!color.HasValue)
                {
                    continue;
                }

                var pixelX = target.X + x;
                var pixelY = target.Y + y;
                if (pixelX < 0 || pixelY < 0 || pixelX >= Document.Width || pixelY >= Document.Height)
                {
                    continue;
                }

                using var brush = new SolidBrush(Color.FromArgb(210, color.Value));
                g.FillRectangle(brush, CanvasOffset.X + pixelX * Zoom, CanvasOffset.Y + pixelY * Zoom, Zoom, Zoom);
            }
        }
    }

    private bool TryGetActiveLayer(out PixelLayer layer)
    {
        layer = null!;
        if (Document is null || CurrentFrameIndex < 0 || CurrentFrameIndex >= Document.Frames.Count)
        {
            return false;
        }

        var frame = Document.Frames[CurrentFrameIndex];
        if (CurrentLayerIndex < 0 || CurrentLayerIndex >= frame.Layers.Count)
        {
            return false;
        }

        layer = frame.Layers[CurrentLayerIndex];
        return true;
    }

    private static void ClearLayerRegion(PixelLayer layer, Rectangle bounds)
    {
        for (var y = 0; y < bounds.Height; y++)
        {
            for (var x = 0; x < bounds.Width; x++)
            {
                layer.SetPixel(bounds.X + x, bounds.Y + y, null);
            }
        }
    }

    private static Color?[,] ExtractLayerRegion(PixelLayer layer, Rectangle bounds)
    {
        var data = new Color?[bounds.Width, bounds.Height];
        for (var y = 0; y < bounds.Height; y++)
        {
            for (var x = 0; x < bounds.Width; x++)
            {
                data[x, y] = layer.GetPixel(bounds.X + x, bounds.Y + y);
            }
        }

        return data;
    }

    private static Rectangle? ClipRectToLayer(Rectangle rect, int layerWidth, int layerHeight)
    {
        var clipped = Rectangle.Intersect(rect, new Rectangle(0, 0, layerWidth, layerHeight));
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return null;
        }

        return clipped;
    }

    private static string BuildClipboardPayload(PixelLayer layer, Rectangle bounds)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ClipboardFormat);
        sb.AppendLine($"{bounds.Width},{bounds.Height}");

        for (var y = 0; y < bounds.Height; y++)
        {
            var row = new string[bounds.Width];
            for (var x = 0; x < bounds.Width; x++)
            {
                var c = layer.GetPixel(bounds.X + x, bounds.Y + y);
                row[x] = c.HasValue ? c.Value.ToArgb().ToString("X8") : "-";
            }

            sb.AppendLine(string.Join(',', row));
        }

        return sb.ToString();
    }

    private static bool TryParseClipboardPayload(string text, out Color?[,] payload)
    {
        payload = new Color?[1, 1];
        var lines = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2 || !string.Equals(lines[0], ClipboardFormat, StringComparison.Ordinal))
        {
            return false;
        }

        var dim = lines[1].Split(',', StringSplitOptions.TrimEntries);
        if (dim.Length != 2 || !int.TryParse(dim[0], out var w) || !int.TryParse(dim[1], out var h) || w <= 0 || h <= 0)
        {
            return false;
        }

        if (lines.Length < h + 2)
        {
            return false;
        }

        payload = new Color?[w, h];
        for (var y = 0; y < h; y++)
        {
            var parts = lines[y + 2].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != w)
            {
                return false;
            }

            for (var x = 0; x < w; x++)
            {
                if (parts[x] == "-")
                {
                    payload[x, y] = null;
                    continue;
                }

                if (!uint.TryParse(parts[x], System.Globalization.NumberStyles.HexNumber, null, out var argb))
                {
                    return false;
                }

                payload[x, y] = Color.FromArgb(unchecked((int)argb));
            }
        }

        return true;
    }

    private Rectangle? PastePayload(PixelLayer layer, Color?[,] payload, int destX, int destY)
    {
        var width = payload.GetLength(0);
        var height = payload.GetLength(1);
        var target = new Rectangle(destX, destY, width, height);
        var clipped = ClipRectToLayer(target, layer.Width, layer.Height);
        if (!clipped.HasValue)
        {
            return null;
        }

        var final = clipped.Value;
        for (var y = 0; y < final.Height; y++)
        {
            for (var x = 0; x < final.Width; x++)
            {
                var srcX = x + (final.X - target.X);
                var srcY = y + (final.Y - target.Y);
                layer.SetPixel(final.X + x, final.Y + y, payload[srcX, srcY]);
            }
        }

        return final;
    }

    private bool TryPasteImageFromClipboard(PixelLayer layer, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!TryGetClipboardImage(out var bitmap) || bitmap is null)
        {
            return false;
        }

        using (bitmap)
        {
            var destX = _selectionBounds?.X ?? 0;
            var destY = _selectionBounds?.Y ?? 0;
            var payload = new Color?[bitmap.Width, bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    payload[x, y] = color.A == 0 ? null : color;
                }
            }

            var pasted = PastePayload(layer, payload, destX, destY);
            if (!pasted.HasValue)
            {
                return false;
            }

            bounds = pasted.Value;
            return true;
        }
    }

    private static bool TrySetClipboardPayload(string text)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.UnicodeText, text);
                data.SetData(ClipboardFormat, text);
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch
            {
                Thread.Sleep(20);
            }
        }

        return false;
    }

    private static bool TryGetClipboardPayload(out Color?[,] payload)
    {
        payload = new Color?[1, 1];
        for (var i = 0; i < 5; i++)
        {
            try
            {
                if (!Clipboard.ContainsData(ClipboardFormat) && !Clipboard.ContainsText())
                {
                    return false;
                }

                var raw = Clipboard.ContainsData(ClipboardFormat)
                    ? Clipboard.GetData(ClipboardFormat) as string
                    : Clipboard.GetText();

                if (string.IsNullOrWhiteSpace(raw))
                {
                    return false;
                }

                return TryParseClipboardPayload(raw, out payload);
            }
            catch
            {
                Thread.Sleep(20);
            }
        }

        return false;
    }

    private static bool TryGetClipboardImage(out Bitmap? bitmap)
    {
        bitmap = null;
        for (var i = 0; i < 5; i++)
        {
            try
            {
                if (!Clipboard.ContainsImage())
                {
                    return false;
                }

                var image = Clipboard.GetImage();
                if (image is null)
                {
                    return false;
                }

                bitmap = new Bitmap(image);
                return true;
            }
            catch
            {
                Thread.Sleep(20);
            }
        }

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _selectionAntsTimer.Stop();
            _selectionAntsTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawPreviewLine(Graphics g, Point start, Point end)
    {
        using var pen = new Pen(Color.FromArgb(180, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot };
        var x1 = CanvasOffset.X + (start.X + 0.5f) * Zoom;
        var y1 = CanvasOffset.Y + (start.Y + 0.5f) * Zoom;
        var x2 = CanvasOffset.X + (end.X + 0.5f) * Zoom;
        var y2 = CanvasOffset.Y + (end.Y + 0.5f) * Zoom;
        g.DrawLine(pen, x1, y1, x2, y2);
    }
}
