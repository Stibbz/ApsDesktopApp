using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using ApsDesktopApp.Models;
using ApsDesktopApp.Services;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace ApsDesktopApp.ViewModels;

public partial class IssuesViewModel : ObservableObject, IToolLifecycle
{
    private const string Cat = "Issues";

    private readonly ApsDataService    _data;
    private readonly AccIssuesService  _issues;
    private readonly AccMembersService _members;
    private readonly AppLogger         _log;

    private readonly ObservableCollection<IssueRow> _allIssues = new();

    // Populated on load; used to show names in grid and resolve them back on import.
    private Dictionary<string, string> _idToName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _nameToId = new(StringComparer.OrdinalIgnoreCase);

    private string? _loadedProjectId;

    public IssuesViewModel(
        ApsDataService data,
        AccIssuesService issues,
        AccMembersService members,
        AppLogger log)
    {
        _data    = data;
        _issues  = issues;
        _members = members;
        _log     = log;

        IssuesView = CollectionViewSource.GetDefaultView(_allIssues);
        IssuesView.Filter = FilterIssue;
    }

    // -- Project picker -------------------------------------------------------

    public ObservableCollection<Hub>     Hubs     { get; } = new();
    public ObservableCollection<Project> Projects { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private Hub? _selectedHub;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private Project? _selectedProject;

    partial void OnSelectedHubChanged(Hub? value)
    {
        Projects.Clear();
        SelectedProject = null;
        if (value is not null)
            _ = LoadProjectsAsync(value.Id);
    }

    private async Task LoadProjectsAsync(string hubId)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var projects = await _data.GetProjectsAsync(hubId, cts.Token);
            foreach (var p in projects)
                Projects.Add(p);
        }
        catch (Exception ex)
        {
            _log.Warn(Cat, $"Failed to load projects: {ex.Message}");
        }
    }

    // -- Table / search -------------------------------------------------------

    public ICollectionView IssuesView { get; }

    [ObservableProperty] private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => IssuesView.Refresh();

    private bool FilterIssue(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var row = (IssueRow)obj;
        var q   = SearchText.ToUpperInvariant();
        return row.Title.ToUpperInvariant().Contains(q)
            || row.Status.ToUpperInvariant().Contains(q)
            || row.Type.ToUpperInvariant().Contains(q)
            || row.DisplayId.ToString().Contains(q)
            || row.AssignedTo.ToUpperInvariant().Contains(q)
            || row.Owner.ToUpperInvariant().Contains(q);
    }

    // -- Load -----------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private int    _loadedCount;
    [ObservableProperty] private int    _totalCount;
    [ObservableProperty] private string _status = string.Empty;

    public int IssueCount => _allIssues.Count;

    private bool CanLoad() => !IsBusy && SelectedProject is not null;

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync()
    {
        if (SelectedProject is null) return;

        IsBusy           = true;
        IsLoading        = true;
        LoadedCount      = 0;
        TotalCount       = 0;
        Status           = string.Empty;
        _allIssues.Clear();
        _idToName        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _nameToId        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _loadedProjectId = SelectedProject.Id;

        _log.Info(Cat, $"Loading issues for {SelectedProject.Name}");

        var progress = new Progress<(int loaded, int total)>(p =>
        {
            LoadedCount = p.loaded;
            TotalCount  = p.total;
        });

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

            // Load issues and members concurrently.
            var issuesTask  = _issues.GetAllIssuesAsync(SelectedProject.Id, progress, cts.Token);
            var membersTask = _members.GetMemberLookupAsync(SelectedProject.Id, cts.Token);
            await Task.WhenAll(issuesTask, membersTask);

            _idToName = membersTask.Result;
            // Reverse lookup for import: name -> id (case-insensitive).
            foreach (var kv in _idToName)
                _nameToId.TryAdd(kv.Value, kv.Key);

            string NameOf(string? id) =>
                !string.IsNullOrEmpty(id) && _idToName.TryGetValue(id, out var n) ? n : id ?? string.Empty;

            foreach (var issue in issuesTask.Result)
                _allIssues.Add(IssueRow.FromApi(issue, NameOf));

            OnPropertyChanged(nameof(IssueCount));
            _log.Info(Cat, $"Loaded {_allIssues.Count} issues, {_idToName.Count} members resolved");
        }
        catch (Exception ex)
        {
            _log.Error(Cat, $"Load failed: {ex.Message}");
            Status = $"Failed to load issues: {ex.Message}";
        }
        finally
        {
            IsBusy    = false;
            IsLoading = false;
            ExportCommand.NotifyCanExecuteChanged();
            ImportCommand.NotifyCanExecuteChanged();
        }
    }

    // -- Export ---------------------------------------------------------------

    private bool CanExport() => !IsBusy && _allIssues.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title      = "Export Issues to Excel",
            DefaultExt = ".xlsx",
            Filter     = "Excel workbook (*.xlsx)|*.xlsx",
            FileName   = $"Issues_{DateTime.Now:yyyy-MM-dd}.xlsx",
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        Status = "Exporting...";
        try
        {
            await Task.Run(() => WriteExcel(dialog.FileName));
            _log.Info(Cat, $"Exported {_allIssues.Count} issues to {dialog.FileName}");
            Status = $"Exported {_allIssues.Count} issues.";
        }
        catch (Exception ex)
        {
            _log.Error(Cat, $"Export failed: {ex.Message}");
            Status = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void WriteExcel(string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Issues");

        // -- Header row -------------------------------------------------------
        string[] headers =
        {
            "ID", "#", "Title", "Status", "Type",
            "Assigned To", "Created By", "Owner",
            "Created At", "Due Date", "Closed At", "Description"
        };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
            cell.Style.Font.FontColor       = XLColor.White;
        }

        // Shade read-only columns so it is obvious they should not be edited.
        foreach (int col in new[] { 1, 2, 5, 7, 9, 11 })
            ws.Column(col).Style.Fill.BackgroundColor = XLColor.FromHtml("#EBEBEB");

        // -- Data rows --------------------------------------------------------
        int row = 2;
        foreach (IssueRow issue in IssuesView)    // exports the current filtered view
        {
            ws.Cell(row, 1).Value  = issue.Id;
            ws.Cell(row, 2).Value  = issue.DisplayId;
            ws.Cell(row, 3).Value  = issue.Title;
            ws.Cell(row, 4).Value  = issue.Status;
            ws.Cell(row, 5).Value  = issue.Type;
            ws.Cell(row, 6).Value  = issue.AssignedTo;
            ws.Cell(row, 7).Value  = issue.CreatedBy;
            ws.Cell(row, 8).Value  = issue.Owner;
            WriteDateCell(ws.Cell(row, 9),  issue.CreatedAt);
            WriteDateCell(ws.Cell(row, 10), issue.DueDate);
            WriteDateCell(ws.Cell(row, 11), issue.ClosedAt);
            ws.Cell(row, 12).Value = issue.Description ?? string.Empty;
            row++;
        }

        // Set "yyyy-mm-dd" display format for the date columns on the data range.
        // This ensures Excel shows dates in the same format regardless of locale,
        // and that new dates the user types in column 10 are formatted consistently.
        if (row > 2)
        {
            foreach (int col in new[] { 9, 10, 11 })
                ws.Range(2, col, row - 1, col).Style.NumberFormat.Format = "yyyy-mm-dd";
        }

        ws.Columns().AdjustToContents();
        ws.Column(12).Width = 40;
        ws.Column(12).Style.Alignment.WrapText = true;
        ws.SheetView.FreezeRows(1);

        wb.SaveAs(path);
    }

    // Writes a date string ("2026-05-28") or pre-formatted string as an actual
    // Excel DateTime value so the cell is typed as a date, not text.
    private static void WriteDateCell(IXLCell cell, string value)
    {
        if (DateTime.TryParse(value, out var dt))
            cell.Value = dt.Date;   // strip time component
        else
            cell.Value = value;     // leave as text if unparseable
    }

    // -- Import ---------------------------------------------------------------

    private bool CanImport() => !IsBusy && _loadedProjectId is not null;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (_loadedProjectId is null) return;

        var dialog = new OpenFileDialog
        {
            Title  = "Import Issues from Excel",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        Status = "Reading workbook...";
        _log.Info(Cat, $"Import started: {dialog.FileName}");

        // Capture the lookups for use on the thread pool.
        var nameToId = new Dictionary<string, string>(_nameToId, StringComparer.OrdinalIgnoreCase);

        try
        {
            var patches = await Task.Run(() => ReadExcelPatches(dialog.FileName, nameToId));
            if (patches.Count == 0)
            {
                Status = "No updatable rows found in the workbook.";
                return;
            }

            int ok = 0, failed = 0;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

            foreach (var (issueId, body) in patches)
            {
                Status = $"Updating {ok + failed + 1} / {patches.Count}...";
                try
                {
                    await _issues.PatchIssueAsync(_loadedProjectId, issueId, body, cts.Token);
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _log.Warn(Cat, $"PATCH {issueId} failed: {ex.Message}");
                }
                await Task.Delay(150, cts.Token);
            }

            _log.Info(Cat, $"Import done: {ok} updated, {failed} failed");
            Status = failed == 0
                ? $"Import complete: {ok} issue(s) updated."
                : $"Import finished: {ok} updated, {failed} failed (see logs).";
        }
        catch (Exception ex)
        {
            _log.Error(Cat, $"Import failed: {ex.Message}");
            Status = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static List<(string issueId, Dictionary<string, object?> body)> ReadExcelPatches(
        string path, Dictionary<string, string> nameToId)
    {
        var result = new List<(string, Dictionary<string, object?>)>();

        using var wb  = new XLWorkbook(path);
        var ws        = wb.Worksheets.First();
        int lastRow   = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            var id = ws.Cell(r, 1).GetString().Trim();
            if (string.IsNullOrEmpty(id)) continue;

            var body = BuildPatchBody(
                title:       ws.Cell(r, 3).GetString(),
                status:      ws.Cell(r, 4).GetString(),
                assignedTo:  ws.Cell(r, 6).GetString(),
                owner:       ws.Cell(r, 8).GetString(),
                dueDate:     ReadDateCell(ws.Cell(r, 10)),
                description: ws.Cell(r, 12).GetString(),
                nameToId:    nameToId);

            if (body.Count > 0)
                result.Add((id, body));
        }

        return result;
    }

    // Reads a cell value as a normalized "yyyy-MM-dd" string, handling both Excel
    // date cells and text cells that the user may have typed directly.
    private static string ReadDateCell(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime().ToString("yyyy-MM-dd");

        var s = cell.GetString().Trim();
        if (string.IsNullOrEmpty(s)) return s;

        // Parse whatever the user typed ("5/28/2026", "28-May-2026", etc.)
        return DateTime.TryParse(s, out var dt) ? dt.ToString("yyyy-MM-dd") : s;
    }

    // Option A: only include fields the user explicitly filled in.
    // Empty cell = preserve the existing ACC value (field is omitted from PATCH).
    // Names in assignedTo/owner are translated back to user IDs via nameToId.
    private static Dictionary<string, object?> BuildPatchBody(
        string title, string status, string assignedTo, string owner,
        string dueDate, string description,
        Dictionary<string, string> nameToId)
    {
        var body = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(title))
            body["title"] = title;

        if (!string.IsNullOrWhiteSpace(status))
            body["status"] = status;

        if (!string.IsNullOrWhiteSpace(assignedTo))
            body["assignedTo"] = nameToId.TryGetValue(assignedTo, out var uid) ? uid : assignedTo;

        if (!string.IsNullOrWhiteSpace(owner))
            body["ownerId"] = nameToId.TryGetValue(owner, out var oid) ? oid : owner;

        if (!string.IsNullOrWhiteSpace(dueDate))
            body["dueDate"] = dueDate;

        if (!string.IsNullOrWhiteSpace(description))
            body["description"] = description;

        return body;
    }

    // -- Lifecycle ------------------------------------------------------------

    public async Task ActivateAsync()
    {
        if (Hubs.Count == 0)
            await LoadHubsAsync();
    }

    public void Reset()
    {
        Hubs.Clear();
        Projects.Clear();
        SelectedHub      = null;
        SelectedProject  = null;
        _allIssues.Clear();
        _idToName        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _nameToId        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _loadedProjectId = null;
        SearchText       = string.Empty;
        Status           = string.Empty;
        LoadedCount      = 0;
        TotalCount       = 0;
        OnPropertyChanged(nameof(IssueCount));
    }

    private async Task LoadHubsAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var hubs = await _data.GetHubsAsync(cts.Token);
            foreach (var h in hubs)
                Hubs.Add(h);
        }
        catch (Exception ex)
        {
            _log.Warn(Cat, $"Failed to load hubs: {ex.Message}");
            Status = $"Could not load hubs: {ex.Message}";
        }
    }
}

// Immutable row shown in the DataGrid. Names (assignedTo/owner/createdBy) are
// already resolved from user IDs at construction time via the nameOf delegate.
public class IssueRow
{
    public string  Id          { get; private init; } = string.Empty;
    public int     DisplayId   { get; private init; }
    public string  Title       { get; private init; } = string.Empty;
    public string  Status      { get; private init; } = string.Empty;
    public string  Type        { get; private init; } = string.Empty;
    public string  AssignedTo  { get; private init; } = string.Empty;
    public string  CreatedBy   { get; private init; } = string.Empty;
    public string  Owner       { get; private init; } = string.Empty;
    public string  CreatedAt   { get; private init; } = string.Empty;
    public string  DueDate     { get; private init; } = string.Empty;
    public string  ClosedAt    { get; private init; } = string.Empty;
    public string? Description { get; private init; }

    public static IssueRow FromApi(AccIssue a, Func<string?, string> nameOf) => new()
    {
        Id         = a.Id,
        DisplayId  = a.DisplayId,
        Title      = a.Title      ?? string.Empty,
        Status     = a.Status     ?? string.Empty,
        Type       = a.IssueType?.Title ?? string.Empty,
        AssignedTo = nameOf(a.AssignedTo),
        CreatedBy  = nameOf(a.CreatedBy),
        Owner      = nameOf(a.OwnerId),
        CreatedAt  = a.CreatedAt.HasValue
                       ? a.CreatedAt.Value.ToLocalTime().ToString("yyyy-MM-dd")
                       : string.Empty,
        DueDate    = a.DueDate    ?? string.Empty,
        ClosedAt   = a.ClosedAt.HasValue
                       ? a.ClosedAt.Value.ToLocalTime().ToString("yyyy-MM-dd")
                       : string.Empty,
        Description = a.Description,
    };
}
