# Changelog — KaiAosRenew

Format: Keep a Changelog. GitHub release notes are taken from the matching `## [X.Y.Z]` section.

## [Unreleased]

## [1.2.22] - 2026-11-18

- Settings: “Update all clients” broadcasts `clientUpdate` (server + client Server Settings via `clientUpdateAllReq`); outdated clients silent-apply Velopack, already-current clients ignore
- Activity/Register: double-click copy no longer logs “Copied to clipboard.”

## [1.2.21] - 2026-11-18

- Activity/Register: All capped at 500; filtered views show last 3 days (server disk scan; client history requests)
- Register: Info filter; lighter `register` append + capped `registerSync`; Save Register from disk
- Client: Loading hints; Clear/Save routed to server when connected; text search on Activity/Register
- Tests: shared `ActivityRegisterLogic`; Protocol + Logic suites run automatically from `build.bat`

## [1.2.20] - 2026-11-18

- Keys: parse paint/upholstery for `.bin` variants without `%` prefix and alphanumeric paint codes; merge BinHints; decode SQLite `KeyBinary`; backfill from archived key-bins
- Tray: Debug Mode no longer forces the amber outdated badge (server + client); badge only for real updates

## [1.2.19] - 2026-11-18

- Client: sticky footer “Update server” offer (no flash-and-vanish after Check for updates); skip false UpdateNone when server update is available
- Update feed: merge inbound versions (never clobber with empty); server busy-lock + no blank wipe on updateFeedReq
- Client AssemblyVersion aligned to 1.2.x with Velopack pack/release tags

## [1.2.18] - 2026-11-18

- Notify: 1-click Windows Firewall allow (TCP/UDP); UDP LAN discovery with verified TCP fallback; optional pairing code (HMAC) + client InstanceGuid ghost replace
- Keys: 100-row history sync; Km column + Copy Km; always-archive `{VIN}.bin`; KeyReader Inactive/ENC alerts; reconnect warn clear
- Browser passwords: clipboard watcher + Paste/Save banner; `GetForegroundWindow` guard before SendKeys
- Renew: preventive clamp to token expiry − 10 minutes; Velopack prepare-for-restart; TLS 1.2 on update peek
- Reliability: atomic `settings.xml`; log rotate/append lock; folderZip chunking; ToastWindow dark; ServiceMonitor Pause/StopPending wait
- Architecture: `MainWindow` partials (server + client); shared `NotifyMessage` / UTF-8 line reader; i18n EN baseline backfill for 17 languages

## [1.2.17] - 2026-11-18

- Keys / BMW: probe uses KIS + HwAddress; password tip only on HTTP 403; non-200 BMW calls log full request+response (secrets redacted); Register path → Register tab
- Activity / Register: copy full log text (no truncation)
- Keys: one archived `{VIN}.bin` per VIN; Debug Bin Open (server) / Download (client); hide button when no bin; scrollbar gutter so icons are not clipped
- Settings: Download settings / logs / key-debug as zip (server + client)

## [1.2.16] - 2026-11-18

- Keys: I-level parse for all series families (`[A-Z][0-9A-F]{3}`), ASCII fallback; paint/upholstery prefer match near VIN
- AOS: “Changed — click Save.” clears when email/password are reverted (server + client)
- Settings: Notify This PC IP looks disabled; Open for ENC / KeyUploader / KAI logs; Paths Browse then Open on server
- UX: Settings conflict Reload/Overwrite/Cancel; Connect/Disconnect plug icons; AOS dirty under Save; cooldown until in footer; RO strings polish

## [1.2.15] - 2026-11-16

- Setup.exe (server + client): pack with app.ico so installers show the KAI icon in Explorer

## [1.2.14] - 2026-11-16

- Settings: optional VIN info tip only when BMW upload fails (dependent on existing VIN tip checkbox; local per machine)
- Client: disable server-synced controls (AOS Save, Keys Refresh, Update server, etc.) while disconnected; re-enable on reconnect

## [1.2.13] - 2026-11-16

- Browser password check: also scan `Login Data For Account`; copy WAL/SHM and checkpoint so open Chrome no longer yields empty logins

## [1.2.12] - 2026-11-16

- Register tab (server + client): OK/Fail history with age, All/OK/Fail filter; loads from renew.log and syncs via registerSync
- Browser password check: write diagnostics to `%AppData%\KaiAosRenew\browser-pass-check.log`; scan all Chrome profiles with Login Data

## [1.2.11] - 2026-11-15

- Keys: Clear button on server and client — server clear is local only (no empty keySync); client clear is local + history
- Settings Check for updates: if newer version exists, Yes/No then install and restart (same flow as footer Update)

## [1.2.10] - 2026-11-15

- AOS validation on server and client: empty email/password, `@` / no mid-spaces, interval 1–168, host/port on Connect
- Red border + focus on invalid fields; blocked Save/Force Register tooltips while busy or updating
- Credentials-change confirm before Force Register; aosApply can pass force flag; busy/updating gates
- On reconnect, client requests `aosSyncReq` + `updateFeedReq` (server peeks feed fresh)
- Client LostFocus no longer MessageBox-loops on empty AOS fields (modal only on explicit Save)
- Debug Mode (About version triple-click), sticky register/token balloons, Activity Registration filter + renew.log seed
- Check browser password works with empty email; wider Upholstery column; footer version on row 2

## [1.2.9] - 2026-11-15

- Broadcast `serverUpdating` to all clients before self-update (countdown, Activity log); `serverUpdateFailed` on error
- All clients hide Update server and wait/verify after reconnect; concurrent Update server is gated
- Client Update gated while downloading; `clientUpdating` marks the client in the server notify combo
- Footer buttons show Update client/server v.X; channel tooltips; tray outdated badge (no update balloons)

## [1.2.8] - 2026-11-15

- Shared `updateFeed`: any GitHub poll loads latest server + client versions; server redistributes to all clients immediately
- Footer Update on each side when the shared feed is newer than the local install
- Optional Authenticode signing in `release.bat` via `CODESIGN_PFX` + `CODESIGN_PASSWORD`
- Auto GitHub release notes extracted from this changelog (`tools/extract-changelog.ps1`)
- Skip Velopack Portable.zip (`--noPortable`); versioned Setup filenames only
- Client: after remote Update server, wait for reconnect and verify server version

## [1.2.7] - 2026-11-15

- Server / Client version labels in footer; Update button when a local Velopack update is available
- Client can detect a newer server on GitHub and trigger `serverUpdate` (server self-updates and restarts)
- No Portable assets on GitHub Releases; Setup.exe names include version
- Check for updates at startup and every 6 hours (banner when Auto update is off)

## [Earlier]

- Notify client: Scan LAN manual, recent 3 hosts dropdown, EN/RO; server firewall hint when 0 clients
- Notify server: Settings test tips to All clients or one client (VIN/Info/Warning/Error)
- Notify client: Keys tab (5 VINs), Settings (tips/startup/log/offline), tray status + menu, server ping heartbeat
- Release: `KaiAosRenew.zip` includes `KaiAosNotifyClient\` next to server; also `KaiAosNotifyClient.zip`
- Notify server: Settings enable + port + local IPs; TCP JSON tips to remote KaiAosNotifyClient (incl. Copy VIN)
- Auto: when Health fails, Register at most once per 12 hours (Save/Paste/Fill unlimited; Check Token stays Health-only)
- Keys: hide activity rows with no VIN
- Register only on manual Save/Paste/Fill — never on timer/startup/wake/Keys 403
- Keys: Retry BMW + tab red dot only when newest row is Failed (and .bin exists); block Register after any Register fail until password changes
- Keys: detect BMW upload “Could not upload” / 403 as Failed; Retry BMW (last .bin only) + log HTTP body; Register only if Health fails (60 min cooldown)
- Schedule: Interval (hours) applies live (debounce) and always reschedules next run
- Schedule: changing Interval (hours) starts/reschedules the renew timer automatically
- Settings: removed Register/WebEAM URL fields (shown on KAI tab)
- Keys: new-VIN row highlight lasts 20s and survives list refresh
- Tabs: selected tab color updates immediately on click (hover no longer overrides selected)
- Keys: removed “Copy last VIN” toolbar button (use row icon / tray / Ctrl+C)
- Keys: copy icon ✓ feedback; Today filter; stronger new-VIN highlight; copy icon does not select row; tray shows only last VIN as Copy VIN
- Keys: copy icon before each VIN (hidden if empty); no clipboard copy on row/VIN text click
- Keys: fixed infinite SelectionChanged -> MainTabs_SelectionChanged -> RefreshKeysTab loop on row click; value-based list diffing; Win32 clipboard copy; Input-priority hang watchdog
- Freeze fix: no theme re-apply / log rebuild on every window activation; log capped at 500 lines, async; UI hang watchdog writes %AppData%\KaiAosRenew\ui-hang.log
- Keys: no VIN copy on whole-row click (clipboard was freezing UI); copy via VIN cell / button / Ctrl+C
- Keys: ignore right-click; VIN cache avoids UI lock during refresh; skip no-op list/tray rebuilds
- Keys: removed row right-click / double-click context menu (was freezing)
- Keys: removed bottom detail panel; right-click menu hardened (no list rebuild / freeze)
- Keys: VIN/I-level cells fill again (Loaded highlight); refresh runs off UI thread (no freeze)
- Autostart minimize-to-tray: no window flash (show hidden, no activation)
- Keys: highlight search hits in VIN/I-level; sort by Time/Status column headers
- Settings: Minimize to tray on start (no “Windows”); checkbox always editable
- Keys: search box (VIN / paint / I-level); keep selection after refresh; double-click opens row menu (no VIN copy)
- Keys right-click: open menu deferred so it stays open when the row is already selected
- Keys right-click menu opens reliably (pause list refresh; open menu in code)
- Removed AOS password-rotate reminder banner and Settings interval
- Keys context: Copy I-level / Copy Reader ID; tray last 3 VINs; tray icon on Waiting/Failed
- Startup self-check: tray, single-instance, DataBin, Sqlite (log + warn balloon if missing)
- Settings groups: General / Startup / Auth / Paths / Keys + short tooltips
- Keys row right-click: Copy VIN, Copy paint, Open logs
- Single-instance: invalid HWND (white square) kills zombie and starts fresh
- Settings 1–7: Verbose log, auth cooldown minutes, Minimize on close (X→tray), Register/WebEAM URLs, KeyReader Logging level, KAI data paths, Keys refresh/tip duration
- Keys UX: Copy last VIN, row tooltip, highlight new VIN ~4s; double-click VIN tip → Keys tab + select row
- Settings tab: Export/Import + KAI config path at top; Desktop KeyReader Plus DataBin/Log/ENC (registry); general + Keys options
- About panel: version + changelog only
- Defaults: interval 1h, system proxy off; Save starts timer on OK (no auto-minimize)
- Token footer TTL; About: open logs / settings folder; settings path shown
- Alert blink: red dots on Renew (token fail) and KAI (services); Fix now blinks
- Click alert dot → jump to that tab
- Save preflight: config path + BMW services must exist before renew
- Stronger single-instance bring-to-front from tray
- Service status contrast (Running green / Stopped red)
- Dist ZIP: `dist\KaiAosRenew.zip` (no version in filename)
- Browser password metadata hint (Chrome/Edge, no decrypt); Check browser password
- Open BMW SSO from saved login URL
- Yellow banner when browser password is newer than last successful renew
- Editable AOS password reminder every N hours (default 1)
- Save with changed email/password forces Register (even if token still valid)
- Browser password check: Chrome, Edge, Brave, Opera, Opera GX, Vivaldi, Chromium
- Open browser password-manager page from check result dialog
- DPAPI decrypt (v10/v11) fills AOS password; v20 ABE shows clear unavailable message
- Fill & Save from browser check (compare match/mismatch, force Register)
- Tray warning on password-rotate reminder and newer browser password
- Wake from sleep: renew if due, else health check
- Open passwords: maximize + Ctrl+F / Ctrl+V paste site search
- ABE check: Open passwords is default; after open, focus AOS password field
- Silent browser password check only when app has a password problem (empty or auth fail)
- After Open passwords: compare Current vs Clipboard with Paste / Paste & Save / Dismiss
- Auth cooldown 60 min after hard 401 — blocks Register (Health only) to avoid account lockout
- After wrong-password 401: block auto Register until the user changes the password (timer/retry stay Health-only)
- Keys tab: live reader activity from KeyUploader/KAI logs + SqliteKaiDb; shows local handoff and BMW (KAI Central) upload status
- Keys tab: show VIN extracted from keyreader.bin (and from Sqlite when available)
- Keys tab: paint, upholstery, I-levels; detail panel; filter; today badge; row colors; persisted history
- Copy VIN (button, VIN click, Ctrl+C, tray submenu); tray balloon on new key (optional)
- Stall alert when Local OK without BMW >2 min (optional on/off)
- Keys: hide dense summary strip; richer .bin detail panel on row select
- Keys is the first tab and the default selected tab on startup
- Wider default window (1280) so Keys columns fit without horizontal scroll
- Keys VIN info balloon with Copy button (on/off in Keys tab)
- Tips use in-app balloon only (no Windows toast)
- Keys Detail: hide raw "Picked up binary file" log lines
- VIN info tip fires as soon as .bin is read (not after BMW Server); column renamed BMW Server
- Keys: wider Local/BMW match window; Waiting rows inherit status from same VIN; dedupe duplicates
- Password rotate reminder clears when app password matches browser
- Keys wording: BMW → BMW Server (column, status, detail, stall alert)
- Tray menu: remove Run now (still available in the main window)

## [1.1.0]

- Tabs Renew / KAI; system proxy under Save
- BMW services monitor, Fix now / Stop (UAC), footer Services OK/FAIL
- Token footer OK/FAIL; balloons only on Warning/Error
- Log rotation at 10 MB (`renew.log` → `renew.log.1`)
- About tab (CHANGELOG.md in-app) + tray About; pack.bat ZIP
- Day/Night tab headers aligned and spaced

## [1.0.0]

- Initial KAI AOS renew tray app (WebEAM health + Register, timer, themes, multilang)
