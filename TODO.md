# ApsDesktopApp - Next Steps

Status as of 2026-05-27: Phases 1-2 working end-to-end. OAuth + PKCE sign-in,
hub/project listing, and the project directory browser (folder tree + file
details grid) are confirmed against a provisioned ACC test environment.
Project lives at `C:\Users\sdraak\source\repos\ApsDesktopApp`.

## Immediate (you)
- [x] Finish `ApsAuthService.EnsureValidAccessTokenAsync` (guarded refresh w/
      SemaphoreSlim, double-check, force-refresh param, cancellation-safe catch).
- [x] Add `offline_access` to Scopes -- REQUIRED for APS to issue a refresh_token
      at all. Without it, refresh always fails -> SignOut after ~1h.
- [ ] ACTION (you): disconnect + reconnect once so APS issues a refresh-capable
      token under the new scope (the stored token predates offline_access).
- [x] Register an APS app at https://aps.autodesk.com as a
      "Desktop, Mobile, Single-Page App", callback `http://localhost:8080/callback`.
- [x] Run the app, open APS > Settings, paste the Client ID, click Connect,
      and verify the green status dot + name.
- [x] Provision the app on an ACC/BIM 360 account (Account Admin > Settings >
      Custom Integrations, add the Client ID). Done in a dedicated test
      environment; folder/file reads confirmed there. (Reminder: an empty hub
      list with HTTP 200 = not provisioned, NOT a code bug.)

## Verify auth end-to-end
- [x] "Test connection" replaced by a live hub/project listing (the recognizable
      project names are the proof the connection works).
- [x] Token auto-loads on restart via `InitializeFromStoredTokenAsync`
      (no re-login until expiry; full refresh still pending - see above).

## Phase 2 - Data Management tree
- [x] Hub + project listing: `GetHubsAsync` / `GetProjectsAsync` on ApsAuthService
      (GET /project/v1/hubs, .../hubs/{id}/projects), rendered in a TreeView
      (Hubs -> Projects) in the connected view.
- [x] Project directory browser + file metadata listing.
      - Folder tree (Hub -> Project -> Folders) via GET .../topFolders and
        GET .../folders/{id}/contents. ApsAuthService.GetTopFoldersAsync /
        GetFolderContentsAsync; DTOs in Models/FolderContents.cs.
      - Selecting a folder lists its files in a DataGrid: name, type, version,
        size, last-modified, modified-by. File metadata is joined from the
        contents response's "included" tip-version resources.
      - UI: split TreeView + DataGrid in MainWindow.xaml (GridSplitter between).
        Tree expand/select bridged to the VM in MainWindow.xaml.cs.
- [x] Lazy-load folders per node rather than eagerly (placeholder-child trick;
      project top folders + each folder's subfolders load on first expand).
- [x] Route every Data Management call through `EnsureValidAccessTokenAsync` via
      an `ApsAuthHandler : DelegatingHandler` on a dedicated data HttpClient
      (Services/ApsAuthHandler.cs). It injects the bearer token and retries once
      on 401 (force-refresh). Token endpoints use a separate plain HttpClient to
      avoid handler recursion; both wired in App.xaml.cs. Data methods no longer
      touch tokens.
- [ ] NEXT: Refactor data calls (GetHubs/GetProjects/GetTopFolders/
      GetFolderContents + the Extract* helpers) out of ApsAuthService into a
      dedicated `ApsDataService` (Services/, WPF-free), so auth and data concerns
      are separated. The data HttpClient (with ApsAuthHandler) moves to that
      service; ApsAuthService keeps only the plain token client.

## Phase 3 - Metadata & naming conventions
- [ ] File metadata inspector panel (item versions, custom attributes/properties).
- [ ] Naming-convention rule engine (pluggable rules; report violations).

## Phase 4 - Model Derivative
- [ ] Trigger translation jobs, poll manifest status.
- [ ] Pass the `region` header here (Model Derivative IS region-routed, unlike the
      Data Management hub/project endpoints). The `AppSettings.Region` value
      (default EMEA) is already stored for this.

## Phase 5 - Distribution
- [ ] MSIX or ClickOnce packaging for colleagues.
- [ ] One shared APS app (one Client ID) for all colleagues; the account admin
      provisions that single Client ID per ACC account.

## Notes / gotchas (see CLAUDE.md for full list)
- .cs files must be ASCII-only (PostToolUse hook blocks non-ASCII bytes).
- Pinned to net8.0-windows LTS despite .NET 9 SDK being installed.
- Stop the running app before deleting bin/obj (file locks).
- Keep all APS logic in Services/ with zero WPF dependencies.
- Data Management hub/project endpoints are NOT region-routed; an empty hub list
  with HTTP 200 means the app isn't provisioned on the account.