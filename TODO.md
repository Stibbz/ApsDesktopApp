# ApsDesktopApp - Next Steps

Status as of 2026-05-28: Tool-hub architecture complete and building clean.
OAuth + PKCE, hub/project/folder browser, naming-convention checker, and Model
Derivative scaffold are all wired. App navigates Home -> tool -> Home via
ContentControl + DataTemplate shell pattern.

## Data Browser tool
- [x] Hub + project + folder tree (lazy-load on expand).
- [x] File grid with version, size, modified-by (JSON:API joined from included[]).
- [x] Version history inspector panel (select file -> versions DataGrid).
- [x] Naming-convention rule engine (pluggable INamingRule, SegmentNamingRule
      placeholder, "Check naming" button lists violations).
- [ ] Fill `SegmentNamingRule.Fields` with your real convention
      (Services/Naming/SegmentNamingRule.cs) -- currently ISO 19650 placeholders.
- [ ] Custom attributes/properties via ACC custom-attributes API (separate from
      Data Management; confirm the test project has them configured first).

## Model Derivative tool
- [x] Service (StartTranslationAsync + GetManifestAsync), region header, Base64Url URN encoding.
- [x] ViewModel (VersionUrn, TranslateCommand, CheckStatusCommand, Status).
- [x] View (URN input, Translate + Check status buttons, status text).
- [ ] Poll loop / auto-refresh: currently one-shot "Check status" button.
      Consider a timer that polls every N seconds while status is "inprogress".
- [ ] Copy URN from Data Browser selected version into Model Derivative tool
      (either clipboard or a direct command that opens the tool pre-filled).

## New tools to build
- [ ] Coordinate with colleagues on which tools to add next. Ideas:
      - ACC Issues viewer / exporter
      - RFI list
      - Model properties / parameter audit
      - Batch URN translator

## Distribution
- [ ] MSIX or ClickOnce packaging for colleagues.
- [ ] One shared APS app (one Client ID) for all colleagues; the account admin
      provisions that single Client ID per ACC account.

## Notes / gotchas (see CLAUDE.md for full list)
- .cs files must be ASCII-only (PostToolUse hook blocks non-ASCII bytes).
- Pinned to net8.0-windows LTS despite .NET 9 SDK being installed.
- Stop the running app before deleting bin/obj (file locks).
- Keep all APS logic in Services/ with zero WPF dependencies.
- Data Management hub/project endpoints are NOT region-routed; empty hub list
  with HTTP 200 means the app isn't provisioned on the account.
- offline_access scope is required for APS to issue a refresh_token.
