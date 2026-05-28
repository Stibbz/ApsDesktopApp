using ApsDesktopApp.Models;

namespace ApsDesktopApp.ViewModels;

// One row in the file details / folder-contents grid.
// Either a regular file (IsFolder = false) or a subdirectory (IsFolder = true).
public class FileRow
{
    // -- File constructor -----------------------------------------------------

    public FileRow(FileEntry entry, string projectId)
    {
        ItemId = entry.ItemId;
        ProjectId = projectId;
        Name = entry.Name;
        FileType = entry.FileType;
        Version = entry.VersionNumber > 0 ? $"v{entry.VersionNumber}" : string.Empty;
        Size = FormatSize(entry.SizeBytes);
        LastModified = entry.LastModified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                       ?? string.Empty;
        ModifiedBy = entry.ModifiedBy;
        TipVersionUrn = entry.TipVersionUrn;
    }

    // -- Folder constructor ---------------------------------------------------

    public FileRow(FolderEntry entry, string projectId)
    {
        IsFolder = true;
        FolderId = entry.Id;
        ProjectId = projectId;
        Name = entry.Name;
        // File-specific fields stay at their defaults (empty / zero)
    }

    // True when this row represents a subdirectory rather than a file.
    // Folder rows don't carry version/size/modification metadata.
    public bool IsFolder { get; }

    // Identifiers: one of FolderId or ItemId is populated depending on IsFolder.
    public string FolderId   { get; } = string.Empty;
    public string ItemId     { get; } = string.Empty;
    public string ProjectId  { get; } = string.Empty;
    public string TipVersionUrn { get; } = string.Empty;

    public string Name         { get; } = string.Empty;
    public string FileType     { get; } = string.Empty;
    public string Version      { get; } = string.Empty;
    public string Size         { get; } = string.Empty;
    public string LastModified { get; } = string.Empty;
    public string ModifiedBy   { get; } = string.Empty;

    // Human-readable byte size (B / KB / MB / GB / TB).
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0)
            return string.Empty;

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }
}
