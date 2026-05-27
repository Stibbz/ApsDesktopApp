# ApsDesktopApp

WPF desktop platform for BIM coordination tools built on the Autodesk APS API.
Grows tool-by-tool; sole user now, intended for colleague distribution later.

## Build & Run
- `dotnet build` — run from `C:\Users\sdraak\source\repos\ApsDesktopApp` (contains the .sln)
- `dotnet run --project ApsDesktopApp` — launches the WPF app
- No test project yet — don't search for one.
- Target framework is **net8.0-windows** (LTS). The machine has the .NET 9 SDK
  installed — do NOT "upgrade" the csproj to net9.0.
- `dotnet new wpf --framework` takes `net8.0` (NOT `net8.0-windows`, which errors).
- `bin/`/`obj/` lock while the app is running — stop it before deleting them.

## Critical Conventions
- **.cs files must be ASCII-only.** A PostToolUse hook
  (`~/.claude/check-cs-encoding.py`) blocks any byte > 127. Use `\uXXXX` escapes
  in string literals; plain ASCII (`-`, `->`, `|`) in comments — no box-drawing
  chars, em-dashes, or arrows.
- **`Services/` has zero WPF dependencies** — keeps the service layer portable
  for a future headless/web variant. Only ViewModels/Views reference UI types.
- **MVVM via CommunityToolkit.Mvvm source generators**: `[ObservableProperty]`
  on private fields, `[RelayCommand]` on methods. No manual INotifyPropertyChanged.
- **DI in `App.xaml.cs`** (Microsoft.Extensions.DependencyInjection). `StartupUri`
  is removed from App.xaml; the main window is resolved from the container.

## APS Authentication
- 3-legged OAuth 2.0 with **PKCE (public client, no client secret)**.
- Register the APS app as "Desktop, Mobile, Single-Page App".
- Redirect URI: `http://localhost:8080/callback` (must match the portal exactly).
- Config + tokens live in `%APPDATA%\ApsDesktopApp` (settings.json, tokens.dat).
- Tokens are DPAPI-encrypted (CurrentUser scope) — not portable between machines.

## Layout
- `Models/` — DTOs (TokenInfo, UserProfile)
- `Services/` — auth, PKCE, callback server, token/settings storage
- `ViewModels/` — MainViewModel (connection state machine)
- `Views/` — tool panels added per feature (Phase 2+)

## Known Stubs
- `ApsAuthService.EnsureValidAccessTokenAsync` throws NotImplementedException
  by design — owner is implementing the refresh strategy. Don't auto-fill it.
