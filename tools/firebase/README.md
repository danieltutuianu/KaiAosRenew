# Firebase Realtime Database — Peer Push relay

KaiAosRenew can sync AOS credentials across workshop sites using **Firebase Realtime Database** (Spark free tier) with near real-time push (SSE).

Payloads stay **AES-256 + HMAC encrypted** in the app. Firebase only stores ciphertext under a path derived from your shared secret.

## 1. Create a Firebase project

1. Open [Firebase Console](https://console.firebase.google.com/) and create a project (Spark / free is enough).
2. Build → **Realtime Database** → Create database.
3. Pick a region, start in **locked mode** (you will paste rules next).

## 2. Paste security rules

In Realtime Database → **Rules**, replace with the contents of [`database.rules.json`](database.rules.json) and publish.

Access is gated by a secret path segment (`/kai-aos-peer/{authTag}/…`). Anyone who knows the shared secret (and thus the auth tag) can read/write that subtree. Do not share the secret outside your workshops.

## 3. Copy the database URL

Examples:

- `https://YOUR-PROJECT-default-rtdb.europe-west1.firebasedatabase.app`
- `https://YOUR-PROJECT.firebaseio.com`

## 4. Configure KaiAosRenew

On **each** server (Settings → **Firebase push (multi-site)**), or via the notify client **Server Settings**:

1. Enable **Firebase push**.
2. Paste the Database URL.
3. Set the same **Shared secret** on every site (Generate once, Copy to the others).
4. Set **This server name** (shown on the peer dashboard).
5. Click **Test connection**, then **Push now** after a successful AOS Register if peers need the current password.


## Free tier notes

Spark typically allows ~100 simultaneous connections and generous transfer for a few workshop servers. If you exceed quotas on Spark, the database stops accepting traffic until the next period (no surprise bill unless you upgrade to Blaze).

## Data layout

```
/kai-aos-peer/{authTag}/
  meta/            seq, ackSeq, cmdSeq, peersSeq
  latestCreds/     encrypted credential event
  latestCommand/   encrypted updateAll command
  acks/{ackId}/    encrypted acks
  peers/{serverId}/ presence + encrypted status
```
