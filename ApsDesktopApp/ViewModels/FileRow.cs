using ApsDesktopApp.Models;

namespace ApsDesktopApp.ViewModels;

// One row in the file details grid. Projects a FileEntry's raw values into the
// display strings the DataGrid binds to (formatted size, version, local time).
public class FileRow
{
    public FileRow(FileEntry entry)
    {
        Name = entry.Name;
        FileType = entry.FileType;
        Version = entry.VersionNumber > 0 ? $"v{entry.VersionNumber}" : string.Empty;
        Size = FormatSize(entry.SizeBytes);
        LastModified = entry.LastModified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                       ?? string.Empty;
        ModifiedBy = entry.ModifiedBy;
    }

    public string Name { get; }
    public string FileType { get; }
    public string Version { get; }
    public string Size { get; }
    public string LastModified { get; }
    public string ModifiedBy { get; }

    // Human-readable byte size: scales through B/KB/MB/GB/TB, one decimal place
    // above KB (e.g. "12.3 MB"), whole bytes below 1 KB.
    private static string FormatSize(long bytes)
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
