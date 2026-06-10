using System.Threading.Tasks;

namespace ApsDesktopApp.ViewModels;

// Metadata for a tool shown on the home page and switched into the shell's
// content area. Adding a tool = register its ViewModel + view and add a
// descriptor to MainViewModel.Tools.
public class ToolDescriptor
{
    // The IToolLifecycle constraint is deliberate: every tool MUST implement it,
    // otherwise a forgotten interface would silently keep stale state across
    // disconnects (the shell would have no Reset hook to call).
    public ToolDescriptor(string name, string description, string badge, IToolLifecycle viewModel)
    {
        Name = name;
        Description = description;
        Badge = badge;
        Lifecycle = viewModel;
    }

    public string Name { get; }
    public string Description { get; }

    // Short ASCII badge text shown on the home-page card (e.g. "DM"). Kept ASCII
    // so the .cs encoding hook is happy; no icon-font dependency.
    public string Badge { get; }

    // The tool's ViewModel as its lifecycle interface (activate/reset hooks).
    public IToolLifecycle Lifecycle { get; }

    // The tool's ViewModel; the shell binds a ContentControl to it and a
    // DataTemplate in MainWindow resolves the matching view.
    public object ViewModel => Lifecycle;
}

// Hooks every tool ViewModel must implement to react to being opened
// (load data on demand) and to a disconnect (clear state).
public interface IToolLifecycle
{
    Task ActivateAsync();
    void Reset();
}
