using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ApsDesktopApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace ApsDesktopApp.ViewModels;

public record FormatOption(string DisplayName, string ApiValue, string FileExtension);

public partial class FileConverterViewModel : ObservableObject, IToolLifecycle
{
    private const string Cat = "FileConverter";

    private readonly ModelDerivativeService _service;
    private readonly AppLogger              _log;

    private string? _readyDerivativeUrn;
    private CancellationTokenSource? _pollCts;

    public FileConverterViewModel(ModelDerivativeService service, AppLogger log)
    {
        _service = service;
        _log     = log;
    }

    public FormatOption[] OutputFormats { get; } =
    {
        new("IFC - Open BIM standard",  "ifc",  ".ifc"),
        new("DWG - AutoCAD drawing",    "dwg",  ".dwg"),
        new("OBJ - 3D geometry (mesh)", "obj",  ".obj"),
        new("STL - 3D print format",    "stl",  ".stl"),
    };

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private string _versionUrn = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private FormatOption? _selectedFormat;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPolling;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool _isReadyToDownload;

    public Task ActivateAsync() => Task.CompletedTask;

    public void Reset()
    {
        StopPolling();
        FileName = string.Empty;
        VersionUrn = string.Empty;
        SelectedFormat = null;
        Status = string.Empty;
        IsReadyToDownload = false;
        IsPolling = false;
        _readyDerivativeUrn = null;
    }

    private bool CanRun() =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(VersionUrn)
        && SelectedFormat is not null;

    private bool CanDownload() => IsReadyToDownload && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ConvertAsync()
    {
        StopPolling();
        IsBusy = true;
        IsReadyToDownload = false;
        _readyDerivativeUrn = null;

        _log.Info(Cat, $"Convert requested: file={FileName} format={SelectedFormat?.ApiValue}");

        try
        {
            // Check for an existing successful derivative before submitting a paid job.
            Status = "Checking for existing conversion...";
            using var checkCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var manifest = await _service.GetManifestAsync(VersionUrn.Trim(), checkCts.Token);
            var existing = manifest?.Derivatives?
                .FirstOrDefault(d => string.Equals(
                    d.OutputType, SelectedFormat!.ApiValue,
                    StringComparison.OrdinalIgnoreCase));

            if (existing?.Status == "success")
            {
                _log.Info(Cat, $"Existing {SelectedFormat!.ApiValue} derivative found -- prompting user");

                var fmt = SelectedFormat.ApiValue.ToUpperInvariant();
                var choice = MessageBox.Show(
                    $"A {fmt} conversion already exists for this file.\n\n" +
                    "Generating a new one uses API processing credits.\n\n" +
                    "Use the existing conversion?",
                    "Existing Conversion Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes);

                if (choice == MessageBoxResult.Yes)
                {
                    _readyDerivativeUrn = existing.Children?
                        .FirstOrDefault(c => c.Type == "resource")?.Urn;

                    if (_readyDerivativeUrn is not null)
                    {
                        _log.Info(Cat, $"Using existing derivative: {_readyDerivativeUrn}");
                        IsReadyToDownload = true;
                        Status = string.Empty;
                        return;
                    }
                    // Existing derivative has no downloadable resource -- fall through to new job.
                    _log.Warn(Cat, "Existing derivative has no resource URN -- submitting new job");
                }
                else
                {
                    _log.Info(Cat, "User chose to regenerate -- submitting new job");
                }
            }

            Status = "Submitting conversion job...";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _service.StartTranslationAsync(
                VersionUrn.Trim(), SelectedFormat!.ApiValue, cts.Token);

            _log.Info(Cat, "Job submitted -- starting poll loop");
            Status = string.Empty;
            IsPolling = true;
            var pollCts = new CancellationTokenSource();
            _pollCts = pollCts;
            _ = PollManifestLoopAsync(pollCts.Token);
        }
        catch (Exception ex)
        {
            _log.Error(Cat, $"StartTranslation failed: {ex.Message}");
            Status = $"Could not start conversion: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PollManifestLoopAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(15);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (DateTime.UtcNow > deadline)
                {
                    _log.Warn(Cat, "Poll timeout: derivative not ready after 15 minutes");
                    Status = "Conversion timed out -- the APS job may still be running but could not be confirmed.";
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(5), ct);

                using var callCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                callCts.CancelAfter(TimeSpan.FromSeconds(30));
                var manifest = await _service.GetManifestAsync(VersionUrn.Trim(), callCts.Token);

                if (manifest is null)
                {
                    _log.Warn(Cat, "Poll: manifest not found -- job may have been cleared from APS");
                    Status = "Conversion record not found on APS. It may have been cleared.";
                    break;
                }

                var derivative = manifest.Derivatives?
                    .FirstOrDefault(d => string.Equals(
                        d.OutputType, SelectedFormat!.ApiValue,
                        StringComparison.OrdinalIgnoreCase));

                _log.Debug(Cat,
                    $"Poll tick: manifest={manifest.Status} derivative={derivative?.Status ?? "not found"}");

                if (derivative?.Status == "success")
                {
                    _readyDerivativeUrn = derivative.Children?
                        .FirstOrDefault(c => c.Type == "resource")?.Urn;

                    if (_readyDerivativeUrn is not null)
                    {
                        _log.Info(Cat, $"Conversion ready: derivative={_readyDerivativeUrn}");
                        IsReadyToDownload = true;
                        Status = string.Empty;
                    }
                    else
                    {
                        _log.Warn(Cat, "Conversion succeeded but no resource URN found in manifest children");
                        Status = "Conversion complete but no downloadable file was found in the manifest.";
                    }
                    break;
                }

                if (derivative?.Status == "failed" || manifest.Status == "failed" || manifest.Status == "timeout")
                {
                    _log.Warn(Cat,
                        $"Conversion failed: manifest={manifest.Status} derivative={derivative?.Status}");
                    Status = $"Conversion failed -- "
                           + $"{SelectedFormat!.ApiValue.ToUpperInvariant()} may not be supported for this file type.";
                    break;
                }
                // Still running -- spinner is the only visual cue needed.
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Error(Cat, $"Poll error: {ex.Message}");
            Status = $"Status check failed: {ex.Message}";
        }
        finally
        {
            IsPolling = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        if (_readyDerivativeUrn is null) return;

        var ext = SelectedFormat!.FileExtension;
        var dialog = new SaveFileDialog
        {
            Title = $"Save {SelectedFormat.ApiValue.ToUpperInvariant()} file",
            DefaultExt = ext,
            Filter = $"{SelectedFormat.ApiValue.ToUpperInvariant()} files (*{ext})|*{ext}|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        Status = "Downloading...";
        _log.Info(Cat, $"Download started: destination={dialog.FileName}");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var bytes = await _service.DownloadDerivativeAsync(
                VersionUrn.Trim(), _readyDerivativeUrn, cts.Token);
            await File.WriteAllBytesAsync(dialog.FileName, bytes, cts.Token);
            _log.Info(Cat, $"Download complete: {bytes.Length:N0} bytes -> {dialog.FileName}");
            Status = $"Saved to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            _log.Error(Cat, $"Download failed: {ex.Message}");
            Status = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }
}
