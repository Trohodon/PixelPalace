using System.Text.Json;
using PixelPalace.Core.Models;

namespace PixelPalace.Core.Serialization;

public static class PixelProjectSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void Save(string path, PixelDocument document)
    {
        var dto = new PixelProjectDto
        {
            Width = document.Width,
            Height = document.Height,
            Frames = document.Frames.Select(frame => new PixelFrameDto
            {
                Name = frame.Name,
                DurationMs = frame.DurationMs,
                Layers = frame.Layers.Select(layer => new PixelLayerDto
                {
                    Name = layer.Name,
                    Visible = layer.Visible,
                    Pixels = EncodeLayerPixels(layer)
                }).ToList()
            }).ToList()
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static PixelDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<PixelProjectDto>(json) ?? throw new InvalidDataException("Invalid project file.");

        if (dto.Width <= 0 || dto.Height <= 0)
        {
            throw new InvalidDataException("Project dimensions are invalid.");
        }

        if (dto.Frames.Count == 0)
        {
            throw new InvalidDataException("Project must contain at least one frame.");
        }

        var document = new PixelDocument(dto.Width, dto.Height);
        document.Frames.Clear();

        foreach (var frameDto in dto.Frames)
        {
            var frame = new PixelFrame(string.IsNullOrWhiteSpace(frameDto.Name) ? $"Frame {document.Frames.Count + 1}" : frameDto.Name)
            {
                DurationMs = Math.Max(10, frameDto.DurationMs)
            };

            if (frameDto.Layers.Count == 0)
            {
                frame.Layers.Add(new PixelLayer(dto.Width, dto.Height, "Layer 1"));
            }
            else
            {
                foreach (var layerDto in frameDto.Layers)
                {
                    var layer = new PixelLayer(dto.Width, dto.Height, string.IsNullOrWhiteSpace(layerDto.Name) ? "Layer" : layerDto.Name)
                    {
                        Visible = layerDto.Visible
                    };

                    DecodeLayerPixels(layerDto.Pixels, layer);
                    frame.Layers.Add(layer);
                }
            }

            document.Frames.Add(frame);
        }

        return document;
    }

    private static string[] EncodeLayerPixels(PixelLayer layer)
    {
        var rows = new string[layer.Height];

        for (var y = 0; y < layer.Height; y++)
        {
            var tokens = new string[layer.Width];
            for (var x = 0; x < layer.Width; x++)
            {
                var pixel = layer.GetPixel(x, y);
                tokens[x] = pixel.HasValue ? pixel.Value.ToArgb().ToString("X8") : "-";
            }

            rows[y] = string.Join(',', tokens);
        }

        return rows;
    }

    private static void DecodeLayerPixels(string[] rows, PixelLayer layer)
    {
        if (rows.Length != layer.Height)
        {
            return;
        }

        for (var y = 0; y < layer.Height; y++)
        {
            var tokens = rows[y].Split(',', StringSplitOptions.TrimEntries);
            if (tokens.Length != layer.Width)
            {
                continue;
            }

            for (var x = 0; x < layer.Width; x++)
            {
                if (tokens[x] == "-")
                {
                    layer.SetPixel(x, y, null);
                    continue;
                }

                if (uint.TryParse(tokens[x], System.Globalization.NumberStyles.HexNumber, null, out var argb))
                {
                    layer.SetPixel(x, y, Color.FromArgb(unchecked((int)argb)));
                }
            }
        }
    }
}

public sealed class PixelProjectDto
{
    public int Width { get; set; }
    public int Height { get; set; }
    public List<PixelFrameDto> Frames { get; set; } = [];
}

public sealed class PixelFrameDto
{
    public string Name { get; set; } = string.Empty;
    public int DurationMs { get; set; } = 100;
    public List<PixelLayerDto> Layers { get; set; } = [];
}

public sealed class PixelLayerDto
{
    public string Name { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;
    public string[] Pixels { get; set; } = [];
}
