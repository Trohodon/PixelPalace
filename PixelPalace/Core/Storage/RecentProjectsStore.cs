using System.Text.Json;

namespace PixelPalace.Core.Storage;

public sealed class RecentProjectsStore
{
    private readonly string _filePath;

    public RecentProjectsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(appData, "PixelPalace");
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "recent-projects.json");
    }

    public List<RecentProjectEntry> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<RecentProjectEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Touch(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var entries = Load();
        entries.RemoveAll(x => string.Equals(x.Path, fullPath, StringComparison.OrdinalIgnoreCase));

        entries.Insert(0, new RecentProjectEntry
        {
            Path = fullPath,
            LastOpenedUtc = DateTime.UtcNow
        });

        entries = entries.Where(x => File.Exists(x.Path)).Take(25).ToList();
        Save(entries);
    }

    private void Save(List<RecentProjectEntry> entries)
    {
        try
        {
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Ignore persistence errors for recents.
        }
    }
}

public sealed class RecentProjectEntry
{
    public string Path { get; set; } = string.Empty;
    public DateTime LastOpenedUtc { get; set; }
}
