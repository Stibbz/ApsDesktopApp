namespace ApsDesktopApp.ViewModels;

// Flat hub+project pair shown in the unified project picker ComboBox.
// DisplayName is the ComboBox display string: "HubName / ProjectName".
public record ProjectEntry(string HubId, string HubName, string ProjectId, string ProjectName)
{
    public string DisplayName => $"{HubName} / {ProjectName}";
}
