# Shared logic (Server + Client)

Pure helpers compiled into both apps (and Logic/Protocol tests). Prefer adding
new cross-app business rules here instead of duplicating them inside `MainWindow`.

| File | Role |
|------|------|
| `NotifyMessage*.cs` | LAN JSON-line protocol |
| `NotifyPairing.cs` | HMAC pairing challenge |
| `TcpUtf8LineReader.cs` | UTF-8 framed line reader |
| `ActivityRegisterLogic.cs` | Activity / Register filters |
| `PeerSyncCrypto.cs` / `PeerSyncLogic.cs` / `PeerSyncFirebaseParse.cs` | Firebase `latestCreds` sync (no multi-site dashboard traffic) |
| `RenewFlowLogic.cs` | AOS helpers + `ClipboardText.NormalizePassword` |
| `KeysExportLogic.cs` | Keys column sort + CSV/TXT export + `ColumnHeaderKey` |
| `DiagBundleLogic.cs` | Redacted diagnostic zip helpers + `HealthAlertLogic` |
| `DpapiString.cs` | DPAPI CurrentUser string protect/unprotect |
| `Themes/KaiTheme.xaml` (+ `.Dark`) | Shared.Ui ResourceDictionaries (linked as `Themes\…`) |
| `ThemeManager.cs` | Day/night theme switch (`Configure` AUMID + mode provider) |
| `TrayTipWindow.cs` | In-app tray tip (Server + Client) |
| `BrowserPassResultWindow.cs` | Browser password result dialog (labels injected) |
| `WpfFocusHelpers.cs` | Shared WPF focus helpers |

Edit theme XAML only under `Shared/Themes/`. Call `ThemeManager.Configure` once at process start.
