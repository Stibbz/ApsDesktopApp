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
    private const string LogCategory = "Issues";

    private readonly AccIssuesService  _issues;
    private readonly AccMembersService _members;
    private readonly AppLogger         _log;

    private readonly ObservableCollection<IssueRow> _allIssues = new();

    // Populated on load; used to show names in grid and resolve them back on import.
    private Dictionary<string, string> _memberIdToName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _memberNameToId = new(StringComparer.OrdinalIgnoreCase);

    private string? _loadedProjectId;

    // Cancels the in-flight load when the project changes or the tool resets,
    // so a slow older load can never populate the grid for the wrong project.
    private CancellationTokenSource? _loadCts;

    // Shared project context -- also exposed to IssuesView.xaml for binding.
    public ProjectContextViewModel ProjectContext { get; }

    public IssuesViewModel(
        AccIssuesService issues,
        AccMembersService members,
        ProjectContextViewModel projectContext,
        AppLogger log)
    {
        _issues        = issues;
        _members       = members;
        ProjectContext = projectContext;
        _log           = log;

        IssuesView = CollectionViewSource.GetDefaultView(_allIssues);
        IssuesView.Filter = FilterIssue;

        // Clear the grid whenever the user picks a different project.
        ProjectContext.PropertyChanged += OnProjectContextChanged;
    }

    private void OnProjectContextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProjectContextViewModel.SelectedProject)) return;

        _loadCts?.Cancel();
        _allIssues.Clear();
        _memberIdToName        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _memberNameToId        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _loadedProjectId = null;
        SearchText       = string.Empty;
        Status           = string.Empty;
        OnPropertyChanged(nameof(IssueCount));
        LoadCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
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

    private bool CanLoad() => !IsBusy && ProjectContext.SelectedProject is not null;

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync()
    {
        var project = ProjectContext.SelectedProject;
        if (project is null) return;

        IsBusy           = true;
        IsLoading        = true;
        LoadedCount      = 0;
        TotalCount       = 0;
        Status           = string.Empty;
        _allIssues.Clear();
        _memberIdToName        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _memberNameToId        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _loadedProjectId = project.ProjectId;

        _log.Info(LogCategory, $"Loading issues for {project.ProjectName}");

        var progress = new Progress<(int loaded, int total)>(p =>
        {
            LoadedCount = p.loaded;
            TotalCount  = p.total;
        });

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _loadCts = cts;

        try
        {
            // Load issues and members concurrently.
            var issuesTask  = _issues.GetAllIssuesAsync(project.ProjectId, progress, cts.Token);
            var membersTask = _members.GetMemberLookupAsync(project.ProjectId, cts.Token);
            await Task.WhenAll(issuesTask, membersTask);
            cts.Token.ThrowIfCancellationRequested();

            var memberResult = membersTask.Result;
            _memberIdToName  = memberResult.Lookup;
            // Reverse lookup for import: name -> id (case-insensitive).
            foreach (var kv in _memberIdToName)
                _memberNameToId.TryAdd(kv.Value, kv.Key);

            string NameOf(string? id) =>
                !string.IsNullOrEmpty(id) && _memberIdToName.TryGetValue(id, out var n) ? n : id ?? string.Empty;

            foreach (var issue in issuesTask.Result)
                _allIssues.Add(IssueRow.FromApi(issue, NameOf));

            if (!memberResult.IsComplete)
                Status = $"Member names unavailable ({memberResult.Error}) -- showing raw user IDs.";

            OnPropertyChanged(nameof(IssueCount));
            _log.Info(LogCategory, $"Loaded {_allIssues.Count} issues, {_memberIdToName.Count} members resolved");
        }
        catch (OperationCanceledException)
        {
            // Superseded by a project switch/reset, or the 5-minute cap elapsed.
            _log.Info(LogCategory, "Issue load cancelled.");
        }
        catch (Exception ex)
        {
            _log.Error(LogCategory, $"Load failed: {ex.Message}");
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
            _log.Info(LogCategory, $"Exported {_allIssues.Count} issues to {dialog.FileName}");
            Status = $"Exported {_allIssues.Count} issues.";
        }
        catch (Exception ex)
        {
            _log.Error(LogCategory, $"Export failed: {ex.Message}");
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
        _log.Info(LogCategory, $"Import started: {dialog.FileName}");

        // Capture the lookups for use on the thread pool.
        var nameToId = new Dictionary<string, string>(_memberNameToId, StringComparer.OrdinalIgnoreCase);

        int ok = 0, failed = 0;
        bool anyUpdated = false;
        try
        {
            var patches = await Task.Run(() => ReadExcelPatches(dialog.FileName, nameToId));
            if (patches.Count == 0)
            {
                Status = "No updatable rows found in the workbook.";
                return;
            }

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
                    _log.Warn(LogCategory, $"PATCH {issueId} failed: {ex.Message}");
                }
                await Task.Delay(150, cts.Token);
            }

            anyUpdated = ok > 0;
            _log.Info(LogCategory, $"Import done: {ok} updated, {failed} failed");
            Status = failed == 0
                ? $"Import complete: {ok} issue(s) updated. Refreshing..."
                : $"Import finished: {ok} updated, {failed} failed (see logs). Refreshing...";
        }
        catch (Exception ex)
        {
            _log.Error(LogCategory, $"Import failed: {ex.Message}");
            Status = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        // Refresh the grid so the user can see the changes they just wrote back.
        if (anyUpdated)
            await LoadAsync();
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
        {
            body["assignedTo"]     = nameToId.TryGetValue(assignedTo, out var uid) ? uid : assignedTo;
            body["assignedToType"] = "user";
        }

        if (!string.IsNullOrWhiteSpace(owner))
            body["ownerId"] = nameToId.TryGetValue(owner, out var oid) ? oid : owner;

        if (!string.IsNullOrWhiteSpace(dueDate))
            body["dueDate"] = dueDate;

        if (!string.IsNullOrWhiteSpace(description))
            body["description"] = description;

        return body;
    }

    // -- Lifecycle ------------------------------------------------------------

    public Task ActivateAsync()
    {
        if (_loadedProjectId is null && ProjectContext.SelectedProject is not null)
            return LoadAsync();
        return Task.CompletedTask;
    }

    public void Reset()
    {
        _loadCts?.Cancel();
        _allIssues.Clear();
        _memberIdToName        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _memberNameToId        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _loadedProjectId = null;
        SearchText       = string.Empty;
        Status           = string.Empty;
        LoadedCount      = 0;
        TotalCount       = 0;
        OnPropertyChanged(nameof(IssueCount));
    }
}
