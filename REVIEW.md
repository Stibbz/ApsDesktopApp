# Codebase Review — ApsDesktopApp

Date: 2026-06-10. Scope: full codebase (~6,800 lines). Lenses: correctness,
security, platform architecture, and an API-contract crosscheck against the
official SDK source at `github.com/autodesk-platform-services/aps-sdk-net`
(generated `Http/*.gen.cs` files, fetched from `main`).

All file references are relative to the repo root.

**Status 2026-06-10 (same day):** all findings except L2 have been fixed and
removed from this file per the CLAUDE.md convention. Resolved: H1 (signedcookies
download flow), H2 (token-response validation), M1 (`region` header + AUS),
M2 (state validated before browser page), M3 (timeout -> OCE + friendly message),
M4 (`TaskExtensions.LogFaults`), M5 (latest-call-wins CTS per load family),
M6 (`MemberLookupResult` surfaced in Issues status), M7 (`links.next` /
`totalResults` termination), L1, L3–L10.

## Open findings

### L2. Fixed callback port 8080 (distribution risk)

`ApsDesktopApp/Services/AppSettings.cs`. Port 8080 is popular (dev servers,
Docker, other tools) and the value must match the APS portal's registered
redirect URI exactly, so "let the OS pick a port" is *not* an option here.
Mitigations for distribution: register several redirect URIs in the portal
(e.g. 8080/8081/8082) and try them in order, and/or document the Settings
override prominently in colleague onboarding. Also note two app instances
signing in simultaneously will collide. Action belongs with the distribution
work (portal config + onboarding docs), not a code-only fix.

---

## Architecture assessment

### The tool-hub pattern is sound — its registration is the only part that won't scale

The shell pattern (singleton tool ViewModels + `ToolDescriptor` +
`ContentControl`/`DataTemplate` view resolution + `IToolLifecycle`) is a good
fit for this app and genuinely cheap per tool. The friction is that adding a
tool touches **four places**: DI registration (`App.xaml.cs`), a
`MainViewModel` constructor parameter, a `Tools.Add(...)` descriptor, and a
`DataTemplate` in `MainWindow.xaml`. At 4 tools this is fine; at 10+ it's a
forgettable-step generator (and the constructor parameter list grows
unboundedly).

Recommended consolidation, worth adopting around tool #6-8 (not now):

- A single registration point, e.g. a static
  `ToolCatalog.Tools: IReadOnlyList<ToolRegistration>` where each entry holds
  name/description/badge + `Type viewModelType` + `Type viewType`.
- `App.OnStartup` iterates it: registers each ViewModel in DI and creates the
  `DataTemplate` programmatically
  (`new DataTemplate { VisualTree = new FrameworkElementFactory(viewType) }`,
  added to `Application.Resources` keyed by `new DataTemplateKey(vmType)`).
- `MainViewModel` takes `IServiceProvider` (or an injected
  `IEnumerable<ToolRegistration>`) and builds `Tools` from the catalog instead
  of one constructor parameter per tool.

That reduces "add a tool" to: write the VM + View, add one catalog line.

### Distribution readiness (the real near-term future)

Per TODO.md the plan is MSIX/ClickOnce + one shared Client ID. Gaps to close
before shipping to a colleague, roughly in order:

1. **First-run experience.** Today a colleague must open Settings and paste a
   Client ID before anything works. With one shared Client ID, bake it in as
   the default (`AppSettings.ClientId` default, or an `appsettings.default.json`
   shipped beside the exe) so first-run is just "Connect". Keep the override in
   Settings.
2. **The Model Derivative client secret is a real design tension.** The
   converter requires a *confidential* client's secret on every machine
   (`SecretStorage`), which contradicts confidential-client semantics — any
   colleague (or malware running as them) can extract it once it's DPAPI-local.
   Acceptable inside a trusting team, but decide deliberately: (a) accept and
   document it, (b) make converter features optional/per-user-provisioned, or
   (c) longer-term, stand up a tiny relay service that holds the secret
   (also your headless beachhead). Don't ship it silently.
3. **Sign-in port conflicts** (L2): document, and consider multi-port fallback.
4. **Diagnostics:** add an app version + a "open logs folder" / "copy
   diagnostics" affordance so remote troubleshooting doesn't require a Teams
   screenshare. The log infrastructure is already good; surface it.
5. **Packaging:** MSIX gives clean install/update but needs signing
   (self-signed cert deployment is painful); ClickOnce is older but trivially
   self-updating from a network share, which fits an internal-team scenario
   well. For a small colleague group, ClickOnce from a share is the pragmatic
   first step.
6. **Updates to CLAUDE.md/README onboarding** (portal registration, scopes,
   provisioning per ACC account) — the "empty 200 = not provisioned" gotcha
   will hit every new account.

### Headless-variant cheapness check

`Services/` is verified WPF-free — the convention is being honored. Two small
couplings worth *knowing about* (not fixing now):

- Services call `AppSettings.Load()` statically, binding them to the
  `%APPDATA%` file layout. A future headless host would want an injected
  settings provider.
- Sign-in (`OAuthCallbackServer` + browser launch) is inherently interactive;
  a headless variant would use 2-legged or device-code flows anyway. No action.

### Testing recommendation

The deliberate "no tests" stance made sense while everything was UI-adjacent.
It no longer holds: there is now meaningful pure logic whose breakage is
silent (pagination truncation, token expiry math, URN encoding, Excel
round-trip field mapping). A minimal `ApsDesktopApp.Tests` xUnit project (no
WPF reference — the Services layer is already WPF-free by convention) covering:

- `NamingRuleEngine` / `SegmentNamingRule` (about to encode your real ISO 19650
  convention — this is business logic colleagues will rely on);
- `TokenInfo.IsExpired` boundary math;
- `ModelDerivativeService.ToBase64Url` (already `public static`);
- `StripPrefix` / project-ID validation in the ACC services;
- `ApsDataService.GetAllPagesAsync` + the JSON:API tip-join (`ExtractFiles`)
  via a fake `HttpMessageHandler` returning canned JSON:API pages.

That last one is the highest-value test in the codebase: it pins both the
pagination contract and the `included[]` join, the two most subtle pieces of
parsing logic. Explicitly out of scope: UI/ViewModel tests.

---

## What's working well (do not refactor away)

- **Auth security posture:** PKCE with S256, cryptographic `state` validated,
  localhost-only listener, DPAPI-at-rest for tokens *and* the MD secret,
  default TLS validation, and — verified by grep — **no token or secret ever
  logged**. Refresh is serialized behind a `SemaphoreSlim` with a correct
  double-check, and cancellation deliberately doesn't sign the user out.
  This is better than most samples.
- **The two-handler / three-client HttpClient topology** cleanly prevents
  refresh-through-handler recursion and is exactly how this should be built.
- **`EnsureSuccessAsync` reading the APS error body** before throwing and
  `ExtractApsError`'s friendly mapping — extend these patterns, don't
  replace them.
- **JSON:API handling** in `ApsDataService` (tip-version join via
  `relationships.tip.data.id` against `included[]`) matches the API's intended
  usage and is defensively null-safe.
- **Services layer is genuinely WPF-free**, which keeps every future option
  (tests, headless, relay service) open at zero ongoing cost.
- The **409-is-success** and **404-is-no-manifest** special cases in
  `ModelDerivativeService` encode hard-won APS behavior; the comments explain
  why. Same for `x-ads-force: true` on job submission.
