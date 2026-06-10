# ApsDesktopApp

WPF desktop platform for BIM coordination tools built on the Autodesk APS API.
Grows tool-by-tool; sole user now, intended for colleague distribution later.
`REVIEW.md` (repo root) holds the severity-ranked findings of the 2026-06-10 full review --
check it before fixing/refactoring in those areas; remove entries as they are resolved.

## Build & Run
- `dotnet build` — run from `C:\Users\sdraak\source\repos\ApsDesktopApp` (contains the .sln)
- `dotnet run --project ApsDesktopApp` — launches the WPF app
- No test project yet — don't search for one.
- Target framework is **net8.0-windows** (LTS). The machine has the .NET 9 SDK
  installed — do NOT "upgrade" the csproj to net9.0.
- `dotnet new wpf --framework` takes `net8.0` (NOT `net8.0-windows`, which errors).
- `bin/`/`obj/` lock while the app is running — stop it before deleting them.
- Transient build errors can come from the IDE's C# Dev Kit compiling
  concurrently; re-run `dotnet build` to confirm before trusting a failure.

## Critical Conventions
- **.cs files must be ASCII-only.** A PostToolUse hook
  (`~/.claude/check-cs-encoding.py`) blocks any byte > 127. Use `\uXXXX` escapes
  in string literals; plain ASCII (`-`, `->`, `|`) in comments — no box-drawing
  chars, em-dashes, or arrows.
- **`Services/` has zero WPF dependencies** — keeps the service layer portable
  for a future headless/web variant. Only ViewModels/Views reference UI types.
- **`AppSettings` is not DI-injected**: call `AppSettings.Load()` directly in any service or
  ViewModel that needs settings; call `.Save()` after mutating. Cheap (small JSON file).
  **Never cache it in a field** -- a `_settings` field + `ReloadSettings()` that goes unwired
  creates a silent stale-settings bug (region, client ID, etc. won't update after Settings dialog).
- **MVVM via CommunityToolkit.Mvvm source generators**: `[ObservableProperty]`
  on private fields, `[RelayCommand]` on methods. No manual INotifyPropertyChanged.
- **DI in `App.xaml.cs`** (Microsoft.Extensions.DependencyInjection). `StartupUri`
  is removed from App.xaml; the main window is resolved from the container.
  Because there is no `StartupUri`, App.xaml is NOT compiled to BAML and its
  `<Application.Resources>` never load (StaticResource throws at runtime though
  the build is clean). Register app-wide resources in code in `App.OnStartup`
  (e.g. `Resources.Add("BoolToVisibility", new BooleanToVisibilityConverter())`).
- **Shared styles**: `Styles/AppStyles.xaml` is a `ResourceDictionary` (dark theme,
  adapted from SDX Tools' `SDXStyles.xaml`). Merged into `Application.Resources` in
  `App.OnStartup` via a `pack://` URI, NOT from App.xaml markup (same BAML reason).
- **Pin theme on Window roots**: app-level implicit `Style TargetType="Window"` does
  NOT reliably apply to window roots here (App.xaml isn't BAML-compiled), so each
  Window sets `Background`/`Foreground`/font explicitly via `{StaticResource ...}`.
  Implicit styles for controls *inside* the tree work fine.
- **Dark-theming a Menu dropdown needs a `MenuItem` ControlTemplate** (one style, two
  templates switched by a `Role` trigger) — the popup background isn't reachable via
  setters. Keep the `PART_Popup` name.

## APS Authentication
- 3-legged OAuth 2.0 with **PKCE (public client, no client secret)**.
- **Scopes MUST include `offline_access`** or APS returns no `refresh_token`
  and refresh is impossible (works for ~1h, then forces re-login).
- **Two HttpClients**: a plain one for token endpoints, and a separate client
  wrapped with `ApsAuthHandler` (injects bearer, retries once on 401) for all
  authenticated calls. Separate clients prevent refresh-through-handler recursion.
- Empty hub/project list with **HTTP 200 = app not provisioned** on the account
  (Custom Integrations), NOT a code bug.
- Register the APS app as "Desktop, Mobile, Single-Page App".
- Redirect URI: `http://localhost:8080/callback` (must match the portal exactly).
- Config + tokens live in `%APPDATA%\ApsDesktopApp` (settings.json, tokens.dat).
- Tokens are DPAPI-encrypted (CurrentUser scope) — not portable between machines.
- **Server-side revocation**: `POST /authentication/v2/revoke` with `token=<refresh_token>&token_type_hint=refresh_token&client_id=<id>`. Best-effort (5s timeout, exceptions swallowed). `ApsAuthService.RevokeTokenAsync()` handles this; `DisconnectCommand` calls it before `SignOut()`. The internal `SignOut()` on refresh failure does NOT revoke (error path, no network guarantee).

## Model Derivative API
- Requires a **separate "Server-side Web App" APS app** in the portal — the "Desktop/Mobile/SPA"
  type cannot enable Model Derivative. AUTH-001 error = wrong app type, not a scope issue.
- Uses **2-legged OAuth (client_credentials)**: `TwoLeggedTokenService` fetches/caches the token;
  `TwoLeggedAuthHandler` injects it. Client ID stored in `settings.json`; client secret
  DPAPI-encrypted at `%APPDATA%\ApsDesktopApp\md_secret.dat` via `SecretStorage`.
- Three keyed HttpClients in DI: plain (token endpoints), `"data"` (3-legged), `"modelderivative"` (2-legged).
- `EnsureSuccessAsync` in `ModelDerivativeService` reads the APS error body before throwing --
  always use it instead of `EnsureSuccessStatusCode()` so error detail is not discarded.
- **Always send `x-ads-force: true`** on POST `/job` -- without it, APS returns HTTP 201 but
  silently skips adding a new format to an already-complete manifest (ACC auto-processes files
  to SVF on upload, so manifests are almost always pre-existing and complete).
- **Derivative download uses the signedcookies flow** (the direct GET was decommissioned):
  `GET .../manifest/{derivativeUrn}/signedcookies` returns a CloudFront URL + Set-Cookie values;
  the second GET goes through a dedicated `HttpClient` with `UseCookies = false` -- the default
  cookie container silently DROPS a manually set `Cookie` header.
- **Region header is named `region`** (the legacy `x-ads-region` spelling is deprecated);
  valid values US/EMEA/AUS/CAN/DEU/IND/JPN/GBR -- **APAC was renamed AUS**.
  `ModelDerivativeService.RegionHeader()` maps a stored legacy "APAC" to "AUS".

## Data Management API
- `topFolders` is under **project/v1** (`.../hubs/{h}/projects/{p}/topFolders`);
  folder **contents** is under **data/v1** (`.../projects/{p}/folders/{f}/contents`)
  — different bases. Hub/project endpoints are NOT region-routed.
- Contents is JSON:API: `data[]` mixes `type` "folders"/"items"; a file's
  metadata (version, size, modified-by) lives in `included[]` "versions",
  joined via the item's `relationships.tip.data.id`.
- **Pagination uses JSON:API convention**: `page[number]` (0-based) + `page[limit]` (max 200) -- NOT `offset`/`limit`. `GetAllPagesAsync()` in `ApsDataService` handles this for all three endpoints (topFolders, folder contents, item versions). Termination follows the response's `links.next` (the API's own signal) -- never the "short page" heuristic, which silently truncates if the server returns a non-full page mid-stream. ACC Issues/Admin use `offset`/`limit` and terminate on `pagination.totalResults`.

## ACC Admin API
- Project members endpoint: `GET /construction/admin/v1/projects/{projectId}/users` -- **no account ID in the path**.
  The `/accounts/{accountId}/projects/...` shape does NOT exist and returns 404 silently.
- Requires `account:read` scope on the 3-legged token. Without it, APS returns 404 (not 401/403) even for Project Admins.
- Adding a scope to `ApsAuthService.Scopes` takes effect only after the user signs out and back in -- token refresh reuses the old scope set.
- Ground-truth endpoint URLs: check `github.com/autodesk-platform-services/aps-sdk-net` -- generated
  clients live at `{module}/source/Http/*.gen.cs` (e.g. `modelderivative/source/Http/DerivativesApi.gen.cs`);
  fetch via raw.githubusercontent.com. Trust these over prose docs.
- **Do NOT send `x-ads-region` on Construction Issues API requests.** The Issues API resolves
  the regional server from the container ID internally; sending the header routes to the wrong
  regional deployment if the project's actual region differs from `AppSettings.Region`, causing 404.
  (Model Derivative needs the header because it creates new jobs; Issues does not.)

## Layout
- `Models/` — DTOs (TokenInfo, UserProfile)
- `Services/` — auth, PKCE, callback server, token/settings storage
- `Styles/` — shared WPF ResourceDictionary (AppStyles.xaml), merged in App.OnStartup
- `ViewModels/MainViewModel` — connection state machine; hosts the tool hub
- `ViewModels/ProjectContextViewModel` — shared singleton for the active hub/project
  selection. All project-scoped tools inject it and subscribe to `PropertyChanged`
  (on `SelectedProject`). Never add per-tool hub/project pickers.
- `ViewModels/DataBrowserViewModel` — starts at the selected project's top folders
  (not a hub tree). `NavigationPath: ObservableCollection<FolderNode>` tracks breadcrumbs.
- `Views/` — tool panels added per feature; `MainWindow`/`SettingsWindow` XAML live at
  the project root, not here
- `Views/ConvertFileWindow.xaml` — thin Window shell hosting `FileConverterView`;
  opened as a modal popup by the Data Browser right-click flow.
- `Views/FileConverterView.xaml` — the converter UserControl (format picker, spinner, download).
- `Views/LogViewerWindow.xaml` — non-modal log viewer, one instance per tool. Each tool
  code-behind holds a `_logWindow` field and a `ShowLogs_Click` handler. Button placed
  top-right of the tool's header row.
- `IToolLifecycle` (defined in `ViewModels/ToolDescriptor.cs`, not its own file) — interface every
  tool ViewModel MUST implement: `ActivateAsync()` (tool opens; lazy-loads) and `Reset()` (disconnect;
  clears state). `ToolDescriptor`'s constructor takes `IToolLifecycle`, so forgetting it is a compile error.
- **Fire-and-forget tasks**: never `_ = SomethingAsync()`. Use
  `SomethingAsync().LogFaults(_log, LogCategory)` (`Services/TaskExtensions.cs`) so an escaping
  exception is logged instead of becoming a silent unobserved-task fault.
- **Latest-call-wins loads**: ViewModels that reload on selection change (DataBrowser, Issues) keep a
  `CancellationTokenSource` field per load family; each load cancels+replaces it and checks the token
  before touching collections, so a slow stale response can't overwrite newer data.

## Styles
- Valid `AppStyles.xaml` resource keys: `WindowBg`, `Surface`, `Elevated`, `Border`, `TextPri`,
  `TextMuted`, `Accent`, `AccentHover`, `AccentPress`. **`InputBg` and `TextSec` do not exist**
  -- using either crashes at runtime with no build warning.
- Each `Window` root must set `Background`/`Foreground`/`FontFamily` explicitly (see Critical Conventions).
- For left/right split in an action row, use a two-column `Grid` (`Auto` + `*`) -- not `StackPanel` or `DockPanel`.
- **Dark title bar**: call `DwmSetWindowAttribute(hwnd, 20, ref 1, 4)` (`dwmapi.dll`) in
  `SourceInitialized` -- NOT the constructor (HWND is null until the OS creates the window).
- **Grouped ComboBox** (hub headers + project items): create a `CollectionViewSource` in the
  ViewModel with `new CollectionViewSource { Source = collection }` + `PropertyGroupDescription`,
  expose as `ICollectionView`. Never use `CollectionViewSource.GetDefaultView` for a grouped
  view -- it modifies the shared default view and affects all other bindings to that collection.
- **XAML is strict XML**: `--` inside `<!-- -->` comments is a parse error (MC3000). Use a
  single `-` or rephrase the sentence.
- **`Trigger.Property` for attached properties**: use `TypeName.PropertyName` (no parens).
  Parentheses are property-path syntax for bindings/animations only; using them in
  `Trigger.Property` causes MC4106.
- **`AlternationIndex` in templates**: `DataTrigger` + `RelativeSource FindAncestor` inside
  `DataTemplate.Triggers` is unreliable (index not stamped when trigger evaluates). Use a
  `ListBox`, put the separator in `ItemContainerStyle` `ControlTemplate`, and use a plain
  `Trigger Property="ItemsControl.AlternationIndex"`.
- **Clickable link text**: don't use a transparent `Button` -- the implicit dark-theme style
  overwrites `Foreground`. Use `TextBlock` + `<MouseBinding MouseAction="LeftClick" Command="..."/>`.

## Logging
- `Services/AppLogger` is a DI singleton -- inject it; never use `Debug.WriteLine` or `Console.Write`.
- Levels: `Debug` (verbose/polling), `Info` (state transitions), `Warn` (handled anomalies), `Error` (exceptions).
- Log files: `%APPDATA%\ApsDesktopApp\logs\app-YYYY-MM-DD.log`; 7-day retention purged at startup.
- `AppLogger.FileLogLevel` (default `Debug`) gates file output independently of the in-memory viewer.
- `Views/LogViewerWindow.xaml` -- non-modal viewer. Each tool code-behind opens it via a `_logWindow`
  field + `ShowLogs_Click`. Non-DI windows (like LogViewer itself) resolve deps via
  `App.Services.GetRequiredService<T>()`.

