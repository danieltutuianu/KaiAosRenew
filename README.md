# KAI AOS Renew

Reînregistrează agentul BDR la Kai Zentral și rescrie `KAI_Properties.cfg` (ClientId/ClientSecret) la un interval configurabil (implicit 24h).

## Rulare

1. Pornește `bin\Release\KaiAosRenew.exe`
2. Introdu email + parola AOS
3. Setează intervalul (ore)
4. **Salvează** → **Pornește timer**
5. Opțional: „Pornește cu Windows”

La închiderea ferestrei rămâne în **tray**.

One-shot (folosește setările salvate):

```text
KaiAosRenew.exe --once
```

## Fișiere

| Path | Rol |
|------|-----|
| `%AppData%\KaiAosRenew\settings.xml` | setări; parola e protejată DPAPI (user curent) |
| `%AppData%\KaiAosRenew\renew.log` | jurnal |
| `C:\ProgramData\BMW\ISPI\config\KAI\KAI\KAI_Properties.cfg` | credențiale OAuth folosite de KAI |

## Distribuție

ZIP portabil (fără installer):

```text
pack.bat
```

Rezultat: `dist\KaiAosRenew.zip`

### Pe PC-ul țintă

Prerechizite:

- Windows cu **.NET Framework 4.8**
- **BMW KAI / ISPI** instalat (servicii BDR + config KAI)
- Contul Windows al utilizatorului care rulează aplicația (parola AOS e DPAPI per-user)

Pași:

1. Copiază ZIP-ul pe PC
2. Extrage într-un folder (ex. `C:\Tools\KaiAosRenew\`)
3. Rulează `KaiAosRenew.exe`
4. Email + parolă AOS → **Salvează** → **Pornește timer**
5. Opțional: Start with Windows + Minimize to tray

Setările și logurile rămân pe fiecare PC în `%AppData%\KaiAosRenew\` (nu sunt în ZIP).

## Sync multi-site (credențiale AOS)

Sincronizarea între site-uri folosește **Firebase push (multi-site)** — Firebase Realtime Database (SSE), latență tipică ~1–2 s, cu o verificare de siguranță la 60 min.

Setup Firebase: [`tools/firebase/README.md`](tools/firebase/README.md)

Aceleași controale apar și în client, sub **Server Settings**.

## Build

Necesită KAI instalat (DLL-uri BMW) și MSBuild .NET Framework x86:

```text
build.bat
```
