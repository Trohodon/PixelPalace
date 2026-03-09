using System.Text.Json;
using PixelPalace.Core.Models;
using PixelPalace.Core.Serialization;

namespace PixelPalace.Core.Storage;

public sealed class AutosaveRecoveryStore
{
    private readonly string _projectPath;
    private readonly string _metadataPath;

    public AutosaveRecoveryStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(appData, "PixelPalace");
        Directory.CreateDirectory(root);

        _projectPath = Path.Combine(root, "autosave-recovery.ppal");
        _metadataPath = Path.Combine(root, "autosave-recovery-meta.json");
    }

    public bool HasRecoverySnapshot() => File.Exists(_projectPath);

    public RecoverySnapshot? LoadSnapshot()
    {
        if (!File.Exists(_projectPath))
        {
            return null;
        }

        var document = PixelProjectSerializer.Load(_projectPath);
        var metadata = TryLoadMetadata();
        return new RecoverySnapshot(document, metadata?.SourceProjectPath, metadata?.SavedUtc ?? File.GetLastWriteTimeUtc(_projectPath));
    }

    public void SaveSnapshot(PixelDocument document, string? sourceProjectPath)
    {
        PixelProjectSerializer.Save(_projectPath, document);

        var metadata = new RecoveryMetadata
        {
            SourceProjectPath = string.IsNullOrWhiteSpace(sourceProjectPath) ? null : Path.GetFullPath(sourceProjectPath),
            SavedUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_metadataPath, json);
    }

    public void ClearSnapshot()
    {
        if (File.Exists(_projectPath))
        {
            File.Delete(_projectPath);
        }

        if (File.Exists(_metadataPath))
        {
            File.Delete(_metadataPath);
        }
    }

    private RecoveryMetadata? TryLoadMetadata()
    {
        try
        {
            if (!File.Exists(_metadataPath))
            {
                return null;
            }

            var json = File.ReadAllText(_metadataPath);
            return JsonSerializer.Deserialize<RecoveryMetadata>(json);
        }
        catch
        {
            return null;
        }
    }

    private sealed class RecoveryMetadata
    {
        public string? SourceProjectPath { get; set; }
        public DateTime SavedUtc { get; set; }
    }
}

public sealed record RecoverySnapshot(PixelDocument Document, string? SourceProjectPath, DateTime SavedUtc);
