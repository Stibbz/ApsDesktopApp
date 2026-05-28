using System.Threading.Tasks;

namespace ApsDesktopApp.ViewModels;

// Metadata for a tool shown on the home page and switched into the shell's
// content area. Adding a tool = register its ViewModel + view and add a
// descriptor to MainViewModel.Tools.
public class ToolDescriptor
{
    public ToolDescriptor(string name, string description, string badge, object viewModel)
    {
        Name = name;
        Description = description;
        Badge = badge;
        ViewModel = viewModel;
    }

    public string Name { get; }
    public string Description { get; }

    // Short ASCII badge text shown on the home-page card (e.g. "DM"). Kept ASCII
    // so the .cs encoding hook is happy; no icon-font dependency.
    public string Badge { get; }

    // The tool's ViewModel; the shell binds a ContentControl to it and a
    // DataTemplate in MainWindow resolves the matching view.
    public object ViewModel { get; }
}

// Optional hooks a tool ViewModel can implement to react to being opened
// (load data on demand) or to a disconnect (clear state).
public interface IToolLifecycle
{
    Task ActivateAsync();
    void Reset();
}
