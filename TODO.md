# ApsDesktopApp - Next Steps

Status as of 2026-05-27: Phase 1 (authenticated WPF shell) complete. OAuth + PKCE
sign-in works end-to-end; the app lists hubs and their projects in a TreeView.
Project lives at `C:\Users\sdraak\source\repos\ApsDesktopApp`.

## Immediate (you)
- [ ] Implement `ApsAuthService.EnsureValidAccessTokenAsync` (Services/ApsAuthService.cs).
      Decided behaviour: return token if valid; refresh if expired; on refresh
      failure call `SignOut()` and return null. Guidance comment is in the file.
      This clears the CS1998 warning.
- [x] Register an APS app at https://aps.autodesk.com as a
      "Desktop, Mobile, Single-Page App", callback `http://localhost:8080/callback`.
- [x] Run the app, open APS > Settings, paste the Client ID, click Connect,
      and verify the green status dot + name.
- [ ] Provision the app on an ACC/BIM 360 account so hubs/projects are visible:
      Account Admin > Settings > Custom Integrations, add this app's Client ID.
      (An empty hub list = not provisioned, NOT a code bug.) For dev, spin up a
      free ACC trial where you are the account admin and add the Client ID there.

## Verify auth end-to-end
- [x] "Test connection" replaced by a live hub/project listing (the recognizable
      project names are the proof the connection works).
- [x] Token auto-loads on restart via `InitializeFromStoredTokenAsync`
      (no re-login until expiry; full refresh still pending - see above).

## Phase 2 - Data Management tree
- [x] Hub + project listing: `GetHubsAsync` / `GetProjectsAsync` on ApsAuthService
      (GET /project/v1/hubs, .../hubs/{id}/projects), rendered in a TreeView
      (Hubs -> Projects) in the connected view.
- [ ] NEXT: Project directory browser + file metadata listing.
      - Browse a selected project's folder tree (top folders, then folder
        contents on demand): GET .../projects/{id}/topFolders, then
        GET .../folders/{folder_id}/contents.
      - Let the user select a directory (folder) in the tree.
      - List every file (item) in that directory with its metadata: name, file
        type, latest version number, size, last-modified date, and who modified
        it. (Item attributes come from the contents response; deeper props via
        GET .../items/{item_id} and item versions if needed.)
      - Display as a details list/grid next to (or below) the folder tree.
- [ ] Lazy-load folders/files per node rather than eagerly (large projects).
- [ ] Refactor data calls out of ApsAuthService into a dedicated `ApsDataService`
      (Services/, WPF-free) so auth and data concerns are separated.
- [ ] Route every Data Management call through `EnsureValidAccessTokenAsync` for
      the bearer token (currently they read `CurrentToken.AccessToken` directly).

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