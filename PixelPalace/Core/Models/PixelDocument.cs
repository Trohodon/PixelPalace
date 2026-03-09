using System.Drawing;

namespace PixelPalace.Core.Models;

public enum PixelTool
{
    RectSelect,
    Pencil,
    Eraser,
    Fill,
    Picker,
    Line
}

public sealed class PixelLayer
{
    private readonly Color?[,] _pixels;

    public PixelLayer(int width, int height, string name)
    {
        Name = name;
        _pixels = new Color?[width, height];
    }

    public string Name { get; set; }
    public bool Visible { get; set; } = true;

    public int Width => _pixels.GetLength(0);
    public int Height => _pixels.GetLength(1);

    public Color? GetPixel(int x, int y) => _pixels[x, y];

    public void SetPixel(int x, int y, Color? color) => _pixels[x, y] = color;

    public PixelLayer Clone(string? name = null)
    {
        var clone = new PixelLayer(Width, Height, name ?? Name)
        {
            Visible = Visible
        };

        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                clone._pixels[x, y] = _pixels[x, y];
            }
        }

        return clone;
    }
}

public sealed class PixelFrame
{
    public PixelFrame(string name)
    {
        Name = name;
    }

    public string Name { get; set; }
    public int DurationMs { get; set; } = 100;
    public List<PixelLayer> Layers { get; } = [];

    public PixelFrame Clone(string? name = null)
    {
        var clone = new PixelFrame(name ?? Name)
        {
            DurationMs = DurationMs
        };

        foreach (var layer in Layers)
        {
            clone.Layers.Add(layer.Clone());
        }

        return clone;
    }
}

public sealed class PixelDocument
{
    public PixelDocument(int width, int height)
    {
        Width = width;
        Height = height;

        var frame = new PixelFrame("Frame 1");
        frame.Layers.Add(new PixelLayer(width, height, "Layer 1"));
        Frames.Add(frame);
    }

    public int Width { get; }
    public int Height { get; }
    public List<PixelFrame> Frames { get; } = [];

    public PixelDocument Clone()
    {
        var clone = new PixelDocument(Width, Height);
        clone.Frames.Clear();

        foreach (var frame in Frames)
        {
            clone.Frames.Add(frame.Clone());
        }

        return clone;
    }

    public Color?[,] ComposeFrame(int frameIndex)
    {
        var composed = new Color?[Width, Height];
        var frame = Frames[frameIndex];

        foreach (var layer in frame.Layers)
        {
            if (!layer.Visible)
            {
                continue;
            }

            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var pixel = layer.GetPixel(x, y);
                    if (pixel.HasValue)
                    {
                        composed[x, y] = pixel;
                    }
                }
            }
        }

        return composed;
    }

    public Bitmap RenderFrameBitmap(int frameIndex)
    {
        var bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var composed = ComposeFrame(frameIndex);

        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                bitmap.SetPixel(x, y, composed[x, y] ?? Color.Transparent);
            }
        }

        return bitmap;
    }

    public Bitmap RenderSpriteSheet()
    {
        var totalWidth = Width * Frames.Count;
        var sheet = new Bitmap(totalWidth, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.Transparent);

        for (var i = 0; i < Frames.Count; i++)
        {
            using var frameImage = RenderFrameBitmap(i);
            g.DrawImage(frameImage, i * Width, 0);
        }

        return sheet;
    }
}
