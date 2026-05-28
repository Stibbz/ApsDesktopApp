using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ApsDesktopApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ApsDesktopApp.Views;

// Lightweight view model for a single log row. String-only so no WPF types escape
// into this file and no INotifyPropertyChanged is needed (entries never change).
public class LogEntryRow
{
    public string TimestampText { get; }
    public string LevelText     { get; }
    public string Category      { get; }
    public string Message       { get; }
    public LogLevel Level       { get; }

    public LogEntryRow(LogEntry entry)
    {
        TimestampText = entry.Timestamp.ToString("HH:mm:ss.fff");
        LevelText = entry.Level switch
        {
            LogLevel.Debug   => "DBG",
            LogLevel.Info    => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error   => "ERR",
            _                => "???"
        };
        Category = entry.Category;
        Message  = entry.Message;
        Level    = entry.Level;
    }

    // Plain-text representation used when copying to clipboard.
    public string ToClipboardLine() =>
        $"[{TimestampText}] [{LevelText}] [{Category,-20}] {Message}";
}

public partial class LogViewerWindow : Window
{
    private readonly AppLogger _logger;
    private readonly List<LogEntryRow> _all = new();
    private readonly ObservableCollection<LogEntryRow> _visible = new();
    private LogLevel _minLevel = LogLevel.Debug;

    public LogViewerWindow()
    {
        InitializeComponent();
        _logger = App.Services.GetRequiredService<AppLogger>();

        LogItems.ItemsSource = _visible;

        LevelFilter.Items.Add("All (Debug+)");
        LevelFilter.Items.Add("Info+");
        LevelFilter.Items.Add("Warning+");
        LevelFilter.Items.Add("Error");
        LevelFilter.SelectedIndex = 0;

        LogFilePathText.Text = _logger.LogFilePath;

        // Load the snapshot before subscribing so there is no gap between the
        // snapshot and live events. New entries fired while we are iterating will
        // queue on the dispatcher and arrive after this block completes.
        foreach (var entry in _logger.GetSnapshot())
            AddRow(new LogEntryRow(entry));

        UpdateCount();
        ScrollToBottom();

        _logger.EntryAdded += OnEntryAdded;
        Closed += (_, _) => _logger.EntryAdded -= OnEntryAdded;
    }

    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        var row = new LogEntryRow(entry);
        Dispatcher.BeginInvoke(() =>
        {
            _all.Add(row);
            if (row.Level >= _minLevel)
            {
                _visible.Add(row);
                UpdateCount();
                if (AutoScrollCheck.IsChecked == true)
                    LogItems.ScrollIntoView(row);
            }
        });
    }

    private void AddRow(LogEntryRow row)
    {
        _all.Add(row);
        if (row.Level >= _minLevel)
            _visible.Add(row);
    }

    private void RebuildVisible()
    {
        _visible.Clear();
        foreach (var row in _all)
            if (row.Level >= _minLevel)
                _visible.Add(row);
        UpdateCount();
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        if (_visible.Count > 0)
            LogItems.ScrollIntoView(_visible[^1]);
    }

    private void UpdateCount() =>
        EntryCountText.Text = $"{_visible.Count} of {_all.Count} entries";

    // -- Copy support -------------------------------------------------------

    private void LogItems_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySelectedToClipboard();
            e.Handled = true;
        }
    }

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e) =>
        CopySelectedToClipboard();

    private void CopySelectedToClipboard()
    {
        var rows = LogItems.SelectedItems.Cast<LogEntryRow>().ToList();
        if (rows.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var row in rows)
            sb.AppendLine(row.ToClipboardLine());
        Clipboard.SetText(sb.ToString().TrimEnd());
    }

    // -- Toolbar handlers ---------------------------------------------------

    private void LevelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _minLevel = LevelFilter.SelectedIndex switch
        {
            0 => LogLevel.Debug,
            1 => LogLevel.Info,
            2 => LogLevel.Warning,
            3 => LogLevel.Error,
            _ => LogLevel.Debug
        };
        RebuildVisible();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _all.Clear();
        _visible.Clear();
        UpdateCount();
    }

    private void OpenLogFileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = _logger.LogFilePath,
                UseShellExecute = true
            });
        }
        catch { /* file may not exist yet if nothing has been logged to disk */ }
    }
}
