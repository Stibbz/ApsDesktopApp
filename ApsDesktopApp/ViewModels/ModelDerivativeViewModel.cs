using System;
using System.Threading;
using System.Threading.Tasks;
using ApsDesktopApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ApsDesktopApp.ViewModels;

// Model Derivative tool: paste a version URN, start an SVF2 translation, and
// poll the manifest until it finishes. A first cut -- the URN is entered by
// hand for now; later it can be wired from a file selected in the data browser.
public partial class ModelDerivativeViewModel : ObservableObject, IToolLifecycle
{
    private readonly ModelDerivativeService _derivative;

    public ModelDerivativeViewModel(ModelDerivativeService derivative)
    {
        _derivative = derivative;
    }

    // Raw version URN (urn:adsk.wipprod:fs.file:vf....?version=1) to translate.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TranslateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckStatusCommand))]
    private string _versionUrn = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public Task ActivateAsync() => Task.CompletedTask;

    public void Reset()
    {
        VersionUrn = string.Empty;
        Status = string.Empty;
    }

    private bool CanRun() => !IsBusy && !string.IsNullOrWhiteSpace(VersionUrn);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task TranslateAsync()
    {
        IsBusy = true;
        Status = "Submitting translation job...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _derivative.StartTranslationAsync(VersionUrn.Trim(), cts.Token);
            Status = "Job accepted. Use \"Check status\" to poll progress.";
        }
        catch (Exception ex)
        {
            Status = $"Translation failed to start: {ex.Message}";
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
        Status = "Fetching manifest...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var manifest = await _derivative.GetManifestAsync(VersionUrn.Trim(), cts.Token);
            Status = manifest is null
                ? "No manifest yet -- start a translation first."
                : $"Status: {manifest.Status} ({manifest.Progress}).";
        }
        catch (Exception ex)
        {
            Status = $"Could not fetch manifest: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
