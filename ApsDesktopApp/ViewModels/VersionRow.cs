using ApsDesktopApp.Models;

namespace ApsDesktopApp.ViewModels;

// One row in the file metadata inspector's version-history grid. Reuses
// FileRow.FormatSize so size formatting stays consistent across both grids.
public class VersionRow
{
    public VersionRow(VersionEntry entry)
    {
        Version = $"v{entry.VersionNumber}";
        FileType = entry.FileType;
        Size = FileRow.FormatSize(entry.SizeBytes);
        LastModified = entry.LastModified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                       ?? string.Empty;
        ModifiedBy = entry.ModifiedBy;
    }

    public string Version { get; }
    public string FileType { get; }
    public string Size { get; }
    public string LastModified { get; }
    public string ModifiedBy { get; }
}
