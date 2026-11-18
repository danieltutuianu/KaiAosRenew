# KaiAosRenew peer sync — Google Apps Script relay

One shared Web App for all servers (2, 4, …) in different networks.  
AOS email/password never leave KaiAosRenew in plaintext — only AES-encrypted blobs are stored in Drive.

## 1. Create the script

1. Open [https://script.google.com](https://script.google.com) (signed into your Google account).
2. **New project** → delete the default code.
3. Paste the contents of `KaiAosPeerRelay.gs`.
4. Save (Ctrl+S), name the project e.g. `KaiAosPeerRelay`.

## 2. Bind the shared secret (one-time)

Goal: tell Google the same secret you use in KaiAosRenew, so only your servers can talk to the Web App.

1. In **KaiAosRenew → Settings → Peer sync**, click **Generate**, then **Copy** the secret.
2. In the Apps Script editor, find the function `RunSetup` at the bottom of `KaiAosPeerRelay.gs`.
3. Paste your secret between the quotes, for example:
   ```js
   function RunSetup() {
     Logger.log(SetupAuthTag('AbCdEf1234....your-copied-secret....'));
   }
   ```
4. At the top of the editor, choose function **`RunSetup`** from the dropdown, then click **Run**.
5. Google will ask for permissions the first time → click **Review permissions** → choose your Google account → **Allow**.
6. When it finishes, you are done. You can leave `RunSetup` in the file (or put a placeholder secret back). You only re-run it if you generate a **new** secret later.

What this does: it saves a hash of the secret in Script Properties (`PEER_AUTH_TAG`). The script never stores and cannot decrypt AOS passwords.

## 3. Deploy as Web App

1. **Deploy → New deployment**
2. Type: **Web app**
3. **Execute as**: Me
4. **Who has access**: Anyone  
   (data stays protected by the secret + end-to-end encryption)
5. Deploy → copy the **Web app URL**  
   (`https://script.google.com/macros/s/…/exec`)

## 4. Configure each KaiAosRenew server

In **Settings → Peer sync** (and optionally from a client **Server Settings**):

| Field | Value |
|-------|--------|
| Enable peer sync | On |
| Web App URL | the URL from step 3 |
| Secret | same secret used in `SetupAuthTag` |
| Server name | e.g. `Atelier-1` |
| Poll interval (minutes) | `60` (default = 1 hour) |

Use **Test connection**, then **Sync now** after changing credentials on another site.  
**Push now** republishes current credentials without changing the password.  
**Update all peers** tells other sites to self-update via Velopack.

## 5. Adding more servers later

Install KaiAosRenew on the new PC and enter the **same URL + same secret**.  
No changes are needed on existing servers or on the Apps Script deployment.

## 6. Updating an existing relay

If you already deployed an older `KaiAosPeerRelay.gs`:

1. Paste the new script contents over the old project.
2. **Deploy → Manage deployments → Edit → New version** (keep the same Web App URL).
3. No re-run of `SetupAuthTag` is needed unless you change the shared secret.

New fields in Drive JSON (`peers`, `cmdSeq`, `latestCommand`) are created automatically on the next write.

## Notes

- Drive file created automatically: `KaiAosPeerSync.json` in the Google account’s Drive root.
- `doGet` uses `CacheService` so idle polls (no new creds/acks/cmds/peers) avoid reading Drive every hour.
- Peer presence (`heartbeat`) is encrypted end-to-end; the script only stores `statusEnc` blobs.
- After editing the `.gs` file, create a **New deployment** (or a new version of the existing deployment) so servers see the update.
