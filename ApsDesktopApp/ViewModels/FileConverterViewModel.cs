using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApsDesktopApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace ApsDesktopApp.ViewModels;

// One entry in the output-format dropdown.
public record FormatOption(string DisplayName, string ApiValue, string FileExtension);

// File Converter tool: select an output format, start a conversion job on
// Autodesk's servers, then download the converted file -- no Revit or
// Navisworks needed. Paste the version link from the Data Browser.
public partial class FileConverterViewModel : ObservableObject, IToolLifecycle
{
    private readonly ModelDerivativeService _service;

    // URN of the derivative child resource, set when the manifest shows success.
    private string? _readyDerivativeUrn;

    public FileConverterViewModel(ModelDerivativeService service)
    {
        _service = service;
    }

    // -- Format list --------------------------------------------------------

    public FormatOption[] OutputFormats { get; } =
    {
        new("IFC - Open BIM standard",  "ifc",  ".ifc"),
        new("DWG - AutoCAD drawing",    "dwg",  ".dwg"),
        new("OBJ - 3D geometry (mesh)", "obj",  ".obj"),
        new("STL - 3D print format",    "stl",  ".stl"),
    };

    // -- Observable state ---------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckStatusCommand))]
    private string _versionUrn = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckStatusCommand))]
    private FormatOption? _selectedFormat;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool _isReadyToDownload;

    // -- IToolLifecycle -----------------------------------------------------

    public Task ActivateAsync() => Task.CompletedTask;

    public void Reset()
    {
        VersionUrn = string.Empty;
        SelectedFormat = null;
        Status = string.Empty;
        IsReadyToDownload = false;
        _readyDerivativeUrn = null;
    }

    // -- Guards -------------------------------------------------------------

    private bool CanRun() =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(VersionUrn)
        && SelectedFormat is not null;

    private bool CanDownload() => IsReadyToDownload && !IsBusy;

    // -- Commands -----------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ConvertAsync()
    {
        IsBusy = true;
        IsReadyToDownload = false;
        _readyDerivativeUrn = null;
        Status = "Submitting conversion job...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _service.StartTranslationAsync(
                VersionUrn.Trim(), SelectedFormat!.ApiValue, cts.Token);
            Status = "Job accepted. Click \"Check status\" to see when it is ready.";
        }
        catch (Exception ex)
        {
            Status = $"Could not start conversion: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CheckStatusAsync()
    {
        IsBusy = true;
        Status = "Checking...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var manifest = await _service.GetManifestAsync(VersionUrn.Trim(), cts.Token);

            if (manifest is null)
            {
                Status = "No conversion found for this version link. Start one first.";
                return;
            }

            // Find the derivative for the selected output format.
            var derivative = manifest.Derivatives?
                .FirstOrDefault(d => string.Equals(
                    d.OutputType, SelectedFormat!.ApiValue,
                    StringComparison.OrdinalIgnoreCase));

            if (derivative is null)
            {
                Status = $"Status: {manifest.Status} -- no {SelectedFormat!.ApiValue.ToUpperInvariant()} "
                       + "derivative found. Try starting the conversion again.";
                return;
            }

            if (derivative.Status == "success")
            {
                // Find the first downloadable resource child.
                _readyDerivativeUrn = derivative.Children?
                    .FirstOrDefault(c => c.Type == "resource")?.Urn;

                if (_readyDerivativeUrn is not null)
                {
                    IsReadyToDownload = true;
                    Status = $"Ready to download as {SelectedFormat!.ApiValue.ToUpperInvariant()}.";
                }
                else
                {
                    Status = "Conversion complete but no downloadable file was found in the manifest.";
                }
            }
            else if (derivative.Status == "failed")
            {
                Status = $"Conversion failed. This file type may not support "
                       + $"{SelectedFormat!.ApiValue.ToUpperInvariant()} output.";
            }
            else
            {
                Status = $"Still converting ({manifest.Progress}). Check again in a moment.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Could not check status: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var bytes = await _service.DownloadDerivativeAsync(
                VersionUrn.Trim(), _readyDerivativeUrn, cts.Token);
            await File.WriteAllBytesAsync(dialog.FileName, bytes, cts.Token);
            Status = $"Saved to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            Status = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
