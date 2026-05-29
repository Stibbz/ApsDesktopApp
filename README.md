# ApsDesktopApp

A WPF desktop platform for connecting to **Autodesk Platform Services (APS)** and building coordination tools on top of it. The application provides a shell that handles authentication and project selection, and exposes a tool system for adding new APS-backed features one at a time.

Current tools: **Data Browser** (browse ACC files, inspect version history, trigger format conversion) and **Issues Manager** (load, search, export, and import ACC construction issues).

---

## Building & Running

```
cd ApsDesktopApp         # solution root (contains the .sln)
dotnet build
dotnet run --project ApsDesktopApp
```

Target framework: **net8.0-windows** (do not upgrade to net9.0).

Settings and tokens are stored in `%APPDATA%\ApsDesktopApp`. Configure your APS application credentials via the **APS > Settings** menu on first launch.

---

## Authentication

Two OAuth flows run side by side:

| Flow | Used for | Config |
|------|----------|--------|
| 3-legged PKCE (public client) | Data Management, Issues, user identity | Client ID in Settings |
| 2-legged client credentials | Model Derivative translations | Separate server-side app Client ID + secret in Settings |

Both tokens are DPAPI-encrypted per Windows user and survive restarts.

---

## Project Structure

```
ApsDesktopApp/
|
+-- Models/                     DTOs for JSON deserialization (no WPF)
|   +-- TokenInfo.cs            OAuth token + expiry helpers
|   +-- UserProfile.cs          OIDC userinfo response
|   +-- Hub.cs, Project.cs      Data Management envelope models
|   +-- FolderContents.cs       JSON:API envelope + friendly record projections
|   +-- ManifestStatus.cs       Model Derivative job/derivative status
|   +-- AccIssue.cs             ACC Issues API response DTOs
|   +-- AccProjectUser.cs       ACC project member DTOs
|
+-- Services/                   Business logic and API calls (no WPF)
|   +-- AppPaths.cs             Centralizes %APPDATA%\ApsDesktopApp paths
|   +-- AppSettings.cs          Plain JSON settings (load/save, no DI)
|   +-- AppLogger.cs            In-memory + file logger (DI singleton)
|   +-- TokenStorage.cs         DPAPI-encrypted token persistence
|   +-- SecretStorage.cs        DPAPI-encrypted Model Derivative secret
|   +-- PkceHelper.cs           PKCE code verifier/challenge/state
|   +-- OAuthCallbackServer.cs  HttpListener for OAuth redirect capture
|   +-- ApsAuthService.cs       3-legged OAuth orchestration + refresh
|   +-- ApsAuthHandler.cs       HttpClient handler: inject 3-legged token, retry 401
|   +-- TwoLeggedTokenService.cs  Client credentials token cache
|   +-- TwoLeggedAuthHandler.cs   HttpClient handler: inject 2-legged token, retry 401
|   +-- ApsDataService.cs       Data Management API (hubs, projects, folders, files)
|   +-- ModelDerivativeService.cs  Translation jobs, manifest polling, download
|   +-- AccIssuesService.cs     ACC Issues API (paginated load, PATCH)
|   +-- AccMembersService.cs    ACC project members lookup (id -> name)
|   +-- Naming/
|       +-- INamingRule.cs      Pluggable naming-rule interface
|       +-- NamingRuleEngine.cs Runs all rules over a file list
|       +-- SegmentNamingRule.cs  ISO 19650 segment validation
|
+-- ViewModels/                 MVVM ViewModels + display-model classes
|   +-- MainViewModel.cs        Shell state machine (Disconnected/Connecting/Connected)
|   +-- ProjectContextViewModel.cs  Shared hub/project picker (singleton)
|   +-- DataBrowserViewModel.cs Folder tree, file grid, version history
|   +-- FileConverterViewModel.cs  Model Derivative conversion + polling
|   +-- IssuesViewModel.cs      Issues grid, Excel export/import
|   +-- IssueRow.cs             Immutable row shown in Issues DataGrid
|   +-- FileRow.cs              Row in file/folder grid (folder or file)
|   +-- VersionRow.cs           Row in version history grid
|   +-- FolderNode.cs           Lazy-loading tree node
|   +-- HubNode.cs, ProjectNode.cs  Hub/project tree nodes
|   +-- ProjectEntry.cs         Hub+project pair for picker ComboBox
|   +-- ToolDescriptor.cs       Tool metadata (name, badge, ViewModel)
|   +-- IToolLifecycle.cs       ActivateAsync() + Reset() contract for tools
|
+-- Views/                      WPF UserControls and Windows
|   +-- HomeView.xaml           Tool card grid (shown when no tool is open)
|   +-- DataBrowserView.xaml    Folder tree (left) + file grid + version grid (right)
|   +-- IssuesView.xaml         Issues DataGrid with search, export, import
|   +-- FileConverterView.xaml  Format picker, convert, polling spinner, download
|   +-- ConvertFileWindow.xaml  Modal window wrapper for FileConverterView
|   +-- LogViewerWindow.xaml    Non-modal real-time log viewer
|   +-- Converters/             WPF IValueConverter implementations
|       +-- InverseBoolConverter.cs
|       +-- InverseBoolToVisibilityConverter.cs
|       +-- StringToVisibilityConverter.cs
|
+-- Styles/
|   +-- AppStyles.xaml          Dark theme ResourceDictionary (merged at startup)
|
+-- App.xaml.cs                 DI container setup, resource merging, startup
+-- MainWindow.xaml             Shell: menu bar, project picker, content area, status bar
+-- SettingsWindow.xaml         OAuth credentials dialog
```

---

## Startup & Code Flow

The flowchart below traces the path from process start through each major feature.

```
 ENTRY POINT
 App.xaml.cs  OnStartup()
      |
      |-- Merge AppStyles.xaml into Application.Resources
      |-- Register static resource converters (BoolToVisibility, etc.)
      |-- Build DI container (services, ViewModels, MainWindow)
      |
      v
 MainWindow (shown)
      |
      |-- Binds to MainViewModel
      |       |
      |       |-- On load: if stored token exists, auto-load user profile
      |       |-- ProjectContextViewModel: loads hubs and projects
      |       |
      |       |-- [DISCONNECTED] shows "Connect" splash
      |       |-- [CONNECTED]    shows HomeView with tool cards
      |
      v
 CONNECT FLOW  (user clicks "Connect" or auto-connect on startup)
      |
      ApsAuthService.SignInAsync()
            |
            |-- PkceHelper: generate code verifier + challenge + state
            |-- Launch browser -> APS authorization endpoint
            |-- OAuthCallbackServer: listen on localhost:8080/callback
            |-- Browser redirects back with ?code=...&state=...
            |-- Exchange code for tokens (POST /authentication/v2/token)
            |-- TokenStorage.Save()  (DPAPI encrypted to disk)
            |-- ApsDataService.GetUserProfileAsync()
            |
            v
      ProjectContextViewModel.LoadAsync()
            |-- GetHubsAsync() -> GetProjectsAsync() for each hub (parallel)
            |-- Restore last-used project from AppSettings
            |
            v
      MainWindow: project picker populated, tool cards visible

      All subsequent API calls go through keyed HttpClients:
        "data"            -> ApsAuthHandler (3-legged token + refresh on 401)
        "modelderivative" -> TwoLeggedAuthHandler (2-legged token + refresh on 401)


 TOOL: DATA BROWSER
      |
      User clicks Data Browser card
            |-- DataBrowserViewModel.ActivateAsync()
            |-- LoadTopFoldersAsync()  (ApsDataService.GetTopFoldersAsync)
            |
            |   [USER EXPANDS FOLDER]
            |   DataBrowserView: ExpandedEvent -> LoadSubFoldersAsync(FolderNode)
            |       -> ApsDataService.GetFolderContentsAsync()
            |       -> FolderNode.Children updated (lazy load)
            |
            |   [USER CLICKS FOLDER in tree or double-clicks row]
            |   ShowFolderContentsAsync / NavigateIntoFolderAsync
            |       -> NavigationPath (breadcrumb) updated
            |       -> Files collection refreshed
            |
            |   [USER SELECTS FILE]
            |   LoadVersionsAsync -> ApsDataService.GetItemVersionsAsync()
            |       -> SelectedFileVersions populated
            |
            |   [RIGHT-CLICK "Convert file..."]
            |   OpenConverterCommand
            |       -> FileConverterViewModel.Reset()
            |       -> ConvertFileWindow (modal) opened

 TOOL: FILE CONVERTER  (opened from Data Browser)
      |
      User picks format, clicks "Convert"
            |-- Check existing manifest (GetManifestAsync)
            |       -> If success derivative exists: ask "use existing?"
            |-- StartTranslationAsync (POST /modelderivative/v2/designdata/job)
            |       x-ads-force: true, x-ads-region: from AppSettings
            |-- PollManifestLoopAsync: every 5s until success/fail/15min timeout
            |
            User clicks "Download"
            |-- SaveFileDialog
            |-- DownloadDerivativeAsync -> File.WriteAllBytesAsync


 TOOL: ISSUES MANAGER
      |
      User selects project, clicks "Load Issues"
            |
            |-- Task.WhenAll:
            |       AccIssuesService.GetAllIssuesAsync()  (paginated, limit=100)
            |       AccMembersService.GetMemberLookupAsync()  (builds id->name dict)
            |
            |-- IssueRow.FromApi() for each issue (resolves user IDs to names)
            |-- IssuesView (ICollectionView) wired with search filter
            |
            |   [EXPORT]  WriteExcel -> ClosedXML workbook -> SaveFileDialog
            |
            |   [IMPORT]  OpenFileDialog -> ReadExcelPatches (diff vs originals)
            |                 -> AccIssuesService.PatchIssueAsync() per changed row
            |                 -> Reload grid on completion
```

---

## Adding a New Tool

1. **Service** - add a class in `Services/` that calls the APS API. Inject the `"data"` keyed `HttpClient` (3-legged) or `"modelderivative"` (2-legged).
2. **ViewModel** - add a class in `ViewModels/` implementing `IToolLifecycle`. Use `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm.
3. **View** - add a `UserControl` in `Views/`.
4. **Wire up** - in `App.xaml.cs`, register the service and ViewModel as singletons. In `MainViewModel`, add a `ToolDescriptor` entry and a `DataTemplate` in `MainWindow.xaml`.

The `ProjectContextViewModel` singleton is injected into every tool that needs the active project. Subscribe to its `SelectedProject` property change to react to project switches.
