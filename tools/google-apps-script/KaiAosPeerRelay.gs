/**
 * KaiAosRenew peer credential sync relay (Google Drive JSON + CacheService).
 *
 * Deploy as Web App:
 *   Execute as: Me
 *   Who has access: Anyone
 *
 * Auth: every request needs query/body fields:
 *   authTag = HMAC-SHA256(secret, "kai-aos-peer-v1") as Base64
 * The script never sees plaintext AOS passwords — only encrypted blobs.
 *
 * After editing this file, Deploy → Manage deployments → Edit → New version.
 */

var FILE_NAME = 'KaiAosPeerSync.json';
var CACHE_SEQ_KEY = 'kai_peer_seq';
var CACHE_ACK_SEQ_KEY = 'kai_peer_ack_seq';
var CACHE_CMD_SEQ_KEY = 'kai_peer_cmd_seq';
var CACHE_PEERS_SEQ_KEY = 'kai_peer_peers_seq';
var CACHE_TTL_SECONDS = 21600; // 6 hours
var MAX_ACKS = 50;
var MAX_PEERS = 20;
var PEER_STALE_DAYS = 30;
var AUTH_MESSAGE = 'kai-aos-peer-v1';

function doGet(e) {
  try {
    e = e || {};
    var p = e.parameter || {};
    if (!verifyAuthTag_(p.authTag)) {
      return json_({ ok: false, error: 'unauthorized' });
    }
    var sinceSeq = toInt_(p.sinceSeq, 0);
    var sinceAckSeq = toInt_(p.sinceAckSeq, 0);
    var sinceCmdSeq = toInt_(p.sinceCmdSeq, 0);
    var sincePeersSeq = toInt_(p.sincePeersSeq, 0);
    var serverId = String(p.serverId || '');

    var cache = CacheService.getScriptCache();
    var cachedSeq = toInt_(cache.get(CACHE_SEQ_KEY), -1);
    var cachedAckSeq = toInt_(cache.get(CACHE_ACK_SEQ_KEY), -1);
    var cachedCmdSeq = toInt_(cache.get(CACHE_CMD_SEQ_KEY), -1);
    var cachedPeersSeq = toInt_(cache.get(CACHE_PEERS_SEQ_KEY), -1);

    // Fast path: no changes since last poll and cache is warm.
    if (cachedSeq >= 0 && cachedAckSeq >= 0 && cachedCmdSeq >= 0 && cachedPeersSeq >= 0
        && sinceSeq >= cachedSeq && sinceAckSeq >= cachedAckSeq
        && sinceCmdSeq >= cachedCmdSeq && sincePeersSeq >= cachedPeersSeq) {
      return json_({
        ok: true,
        unchanged: true,
        seq: cachedSeq,
        ackSeq: cachedAckSeq,
        cmdSeq: cachedCmdSeq,
        peersSeq: cachedPeersSeq,
        latestCreds: null,
        latestCommand: null,
        peers: [],
        acks: []
      });
    }

    var lock = LockService.getScriptLock();
    lock.waitLock(15000);
    try {
      var state = loadState_();
      refreshCache_(state);
      var out = {
        ok: true,
        unchanged: false,
        seq: state.seq,
        ackSeq: state.ackSeq,
        cmdSeq: state.cmdSeq || 0,
        peersSeq: state.peersSeq || 0,
        latestCreds: null,
        latestCommand: null,
        peers: [],
        acks: []
      };
      if (state.latestCreds && state.latestCreds.seq > sinceSeq) {
        out.latestCreds = state.latestCreds;
      }
      if (state.latestCommand && (state.latestCommand.cmdSeq || 0) > sinceCmdSeq) {
        out.latestCommand = state.latestCommand;
      }
      out.acks = filterAcks_(state.acks, sinceAckSeq, serverId);
      if ((state.peersSeq || 0) > sincePeersSeq) {
        out.peers = peersToArray_(state.peers);
      }
      return json_(out);
    } finally {
      lock.releaseLock();
    }
  } catch (err) {
    return json_({ ok: false, error: String(err && err.message ? err.message : err) });
  }
}

function doPost(e) {
  try {
    e = e || {};
    var body = {};
    if (e.postData && e.postData.contents) {
      body = JSON.parse(e.postData.contents);
    }
    if (!verifyAuthTag_(body.authTag)) {
      return json_({ ok: false, error: 'unauthorized' });
    }
    var action = String(body.action || '').toLowerCase();
    var lock = LockService.getScriptLock();
    lock.waitLock(15000);
    try {
      var state = loadState_();
      if (action === 'publish') {
        return json_(handlePublish_(state, body));
      }
      if (action === 'ack') {
        return json_(handleAck_(state, body));
      }
      if (action === 'heartbeat') {
        return json_(handleHeartbeat_(state, body));
      }
      if (action === 'command') {
        return json_(handleCommand_(state, body));
      }
      if (action === 'ping') {
        upsertPeerPresence_(state, body);
        refreshCache_(state);
        return json_({
          ok: true,
          seq: state.seq,
          ackSeq: state.ackSeq,
          cmdSeq: state.cmdSeq || 0,
          peersSeq: state.peersSeq || 0
        });
      }
      return json_({ ok: false, error: 'unknown action' });
    } finally {
      lock.releaseLock();
    }
  } catch (err) {
    return json_({ ok: false, error: String(err && err.message ? err.message : err) });
  }
}

function handlePublish_(state, body) {
  var eventId = String(body.eventId || '').trim();
  var originServerId = String(body.originServerId || '').trim();
  var originName = String(body.originName || '').trim();
  var payloadEnc = String(body.payloadEnc || '').trim();
  if (!eventId || !originServerId || !payloadEnc) {
    return { ok: false, error: 'missing fields' };
  }
  upsertPeerPresence_(state, body);
  // Idempotent: same eventId already stored.
  if (state.latestCreds && state.latestCreds.eventId === eventId) {
    saveState_(state);
    refreshCache_(state);
    return { ok: true, seq: state.seq, eventId: eventId, duplicate: true };
  }
  state.seq = (state.seq || 0) + 1;
  state.latestCreds = {
    seq: state.seq,
    eventId: eventId,
    originServerId: originServerId,
    originName: originName,
    createdUtc: new Date().toISOString(),
    payloadEnc: payloadEnc
  };
  saveState_(state);
  refreshCache_(state);
  return { ok: true, seq: state.seq, eventId: eventId };
}

function handleAck_(state, body) {
  var ackId = String(body.ackId || '').trim();
  var forEventId = String(body.forEventId || '').trim();
  var targetServerId = String(body.targetServerId || '').trim();
  var responderServerId = String(body.responderServerId || '').trim();
  var responderName = String(body.responderName || '').trim();
  var payloadEnc = String(body.payloadEnc || '').trim();
  if (!ackId || !forEventId || !targetServerId || !responderServerId || !payloadEnc) {
    return { ok: false, error: 'missing fields' };
  }
  // Presence from responder
  upsertPeerPresence_(state, {
    serverId: responderServerId,
    originServerId: responderServerId,
    originName: responderName,
    statusEnc: body.statusEnc || ''
  });
  if (!state.acks) state.acks = [];
  for (var i = 0; i < state.acks.length; i++) {
    if (state.acks[i] && state.acks[i].ackId === ackId) {
      saveState_(state);
      refreshCache_(state);
      return { ok: true, ackSeq: state.ackSeq, ackId: ackId, duplicate: true };
    }
  }
  state.ackSeq = (state.ackSeq || 0) + 1;
  state.acks.push({
    ackSeq: state.ackSeq,
    ackId: ackId,
    targetServerId: targetServerId,
    forEventId: forEventId,
    responderServerId: responderServerId,
    responderName: responderName,
    createdUtc: new Date().toISOString(),
    payloadEnc: payloadEnc
  });
  while (state.acks.length > MAX_ACKS) {
    state.acks.shift();
  }
  saveState_(state);
  refreshCache_(state);
  return { ok: true, ackSeq: state.ackSeq, ackId: ackId };
}

function handleHeartbeat_(state, body) {
  var serverId = String(body.serverId || body.originServerId || '').trim();
  if (!serverId) {
    return { ok: false, error: 'missing fields' };
  }
  upsertPeerPresence_(state, body);
  saveState_(state);
  refreshCache_(state);
  return {
    ok: true,
    seq: state.seq,
    ackSeq: state.ackSeq,
    cmdSeq: state.cmdSeq || 0,
    peersSeq: state.peersSeq || 0
  };
}

function handleCommand_(state, body) {
  var cmdId = String(body.cmdId || '').trim();
  var type = String(body.type || '').trim();
  var originServerId = String(body.originServerId || '').trim();
  var originName = String(body.originName || '').trim();
  var payloadEnc = String(body.payloadEnc || '').trim();
  if (!cmdId || !type || !originServerId || !payloadEnc) {
    return { ok: false, error: 'missing fields' };
  }
  upsertPeerPresence_(state, body);
  if (state.latestCommand && state.latestCommand.cmdId === cmdId) {
    saveState_(state);
    refreshCache_(state);
    return {
      ok: true,
      cmdSeq: state.cmdSeq || 0,
      cmdId: cmdId,
      duplicate: true
    };
  }
  state.cmdSeq = (state.cmdSeq || 0) + 1;
  state.latestCommand = {
    cmdSeq: state.cmdSeq,
    cmdId: cmdId,
    type: type,
    originServerId: originServerId,
    originName: originName,
    createdUtc: new Date().toISOString(),
    payloadEnc: payloadEnc
  };
  saveState_(state);
  refreshCache_(state);
  return { ok: true, cmdSeq: state.cmdSeq, cmdId: cmdId };
}

function upsertPeerPresence_(state, body) {
  var serverId = String(body.serverId || body.originServerId || body.responderServerId || '').trim();
  if (!serverId) return;
  if (!state.peers || typeof state.peers !== 'object') state.peers = {};
  var nowIso = new Date().toISOString();
  var prev = state.peers[serverId] || {};
  var statusEnc = String(body.statusEnc || '').trim();
  if (!statusEnc && prev.statusEnc) statusEnc = prev.statusEnc;
  state.peers[state.peers[serverId] ? serverId : serverId] = {
    serverId: serverId,
    lastSeenUtc: nowIso,
    statusEnc: statusEnc
  };
  // Ensure assignment even if weird key reuse
  state.peers[serverId] = {
    serverId: serverId,
    lastSeenUtc: nowIso,
    statusEnc: statusEnc
  };
  prunePeers_(state);
  state.peersSeq = (state.peersSeq || 0) + 1;
}

function prunePeers_(state) {
  if (!state.peers) return;
  var ids = Object.keys(state.peers);
  var cutoff = Date.now() - (PEER_STALE_DAYS * 24 * 60 * 60 * 1000);
  for (var i = 0; i < ids.length; i++) {
    var p = state.peers[ids[i]];
    if (!p) {
      delete state.peers[ids[i]];
      continue;
    }
    var t = Date.parse(p.lastSeenUtc || '');
    if (isNaN(t) || t < cutoff) {
      delete state.peers[ids[i]];
    }
  }
  ids = Object.keys(state.peers);
  if (ids.length <= MAX_PEERS) return;
  ids.sort(function (a, b) {
    var ta = Date.parse((state.peers[a] && state.peers[a].lastSeenUtc) || '') || 0;
    var tb = Date.parse((state.peers[b] && state.peers[b].lastSeenUtc) || '') || 0;
    return ta - tb; // oldest first
  });
  while (ids.length > MAX_PEERS) {
    delete state.peers[ids.shift()];
  }
}

function peersToArray_(peers) {
  var list = [];
  if (!peers || typeof peers !== 'object') return list;
  var ids = Object.keys(peers);
  for (var i = 0; i < ids.length; i++) {
    var p = peers[ids[i]];
    if (!p) continue;
    list.push({
      serverId: p.serverId || ids[i],
      lastSeenUtc: p.lastSeenUtc || '',
      statusEnc: p.statusEnc || ''
    });
  }
  return list;
}

function filterAcks_(acks, sinceAckSeq, serverId) {
  var list = [];
  if (!acks || !acks.length) return list;
  for (var i = 0; i < acks.length; i++) {
    var a = acks[i];
    if (!a) continue;
    if ((a.ackSeq || 0) <= sinceAckSeq) continue;
    if (serverId && a.targetServerId && a.targetServerId !== serverId) continue;
    list.push(a);
  }
  return list;
}

function verifyAuthTag_(authTag) {
  var expected = PropertiesService.getScriptProperties().getProperty('PEER_AUTH_TAG');
  if (!expected) {
    // First-run helper: allow bootstrap when property is empty (set via SetupAuthTag).
    return false;
  }
  return String(authTag || '') === String(expected);
}

/**
 * One-time setup in Apps Script editor:
 *   SetupAuthTag('paste-the-same-PeerSyncSecret-from-KaiAosRenew')
 * Computes HMAC-SHA256(secret, "kai-aos-peer-v1") and stores Base64 in Script Properties.
 */
function SetupAuthTag(secret) {
  secret = String(secret || '');
  if (!secret) throw new Error('secret required');
  var raw = Utilities.computeHmacSha256Signature(AUTH_MESSAGE, secret);
  var tag = Utilities.base64Encode(raw);
  PropertiesService.getScriptProperties().setProperty('PEER_AUTH_TAG', tag);
  return tag;
}

/**
 * One-time: paste the Peer Sync secret from KaiAosRenew Settings, then Run → RunSetup.
 * After it succeeds once, you can leave this as-is or put a placeholder back.
 */
function RunSetup() {
  return SetupAuthTag('PASTE_THE_SAME_SECRET_FROM_KAI_AOS_RENEW');
}

function loadState_() {
  var file = findOrCreateFile_();
  var raw = file.getBlob().getDataAsString() || '';
  if (!raw) {
    return emptyState_();
  }
  var state = JSON.parse(raw);
  if (!state || typeof state !== 'object') {
    return emptyState_();
  }
  if (typeof state.seq !== 'number') state.seq = 0;
  if (typeof state.ackSeq !== 'number') state.ackSeq = 0;
  if (typeof state.cmdSeq !== 'number') state.cmdSeq = 0;
  if (typeof state.peersSeq !== 'number') state.peersSeq = 0;
  if (!state.acks) state.acks = [];
  if (!state.peers || typeof state.peers !== 'object') state.peers = {};
  return state;
}

function emptyState_() {
  return {
    seq: 0,
    ackSeq: 0,
    cmdSeq: 0,
    peersSeq: 0,
    latestCreds: null,
    latestCommand: null,
    peers: {},
    acks: []
  };
}

function saveState_(state) {
  var file = findOrCreateFile_();
  file.setContent(JSON.stringify(state));
}

function refreshCache_(state) {
  var cache = CacheService.getScriptCache();
  cache.put(CACHE_SEQ_KEY, String(state.seq || 0), CACHE_TTL_SECONDS);
  cache.put(CACHE_ACK_SEQ_KEY, String(state.ackSeq || 0), CACHE_TTL_SECONDS);
  cache.put(CACHE_CMD_SEQ_KEY, String(state.cmdSeq || 0), CACHE_TTL_SECONDS);
  cache.put(CACHE_PEERS_SEQ_KEY, String(state.peersSeq || 0), CACHE_TTL_SECONDS);
}

function findOrCreateFile_() {
  var it = DriveApp.getFilesByName(FILE_NAME);
  if (it.hasNext()) return it.next();
  return DriveApp.createFile(FILE_NAME, JSON.stringify(emptyState_()));
}

function toInt_(v, fallback) {
  var n = parseInt(v, 10);
  return isNaN(n) ? fallback : n;
}

function json_(obj) {
  return ContentService
    .createTextOutput(JSON.stringify(obj))
    .setMimeType(ContentService.MimeType.JSON);
}
