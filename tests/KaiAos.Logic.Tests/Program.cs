using System;
using System.Collections.Generic;
using KaiAosRenew;

namespace KaiAos.Logic.Tests
{
    internal static class Program
    {
        private static int _failed;

        private static int Main()
        {
            Console.WriteLine("KaiAos logic regression tests");
            Console.WriteLine("=============================");

            Run("Classify register history", TestClassify);
            Run("Activity UI noise", TestNoise);
            Run("InferLogLevel", TestInferLogLevel);
            Run("Retention window", TestRetention);
            Run("MatchesLogSearch", TestSearch);
            Run("TryParseExportDay", TestExportDay);
            Run("FilterRegisterHistory", TestFilterRegisterHistory);
            Run("PeerSync decisions", TestPeerSyncLogic);
            Run("Firebase parse", TestFirebaseParse);

            Console.WriteLine();
            if (_failed == 0)
            {
                Console.WriteLine("ALL PASS");
                return 0;
            }
            Console.WriteLine("FAILED: " + _failed);
            return 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL  " + name);
                Console.WriteLine("      " + ex.Message);
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void AssertEqual(int expected, int actual, string label)
        {
            if (expected != actual)
                throw new Exception(label + ": expected " + expected + ", got " + actual);
        }

        private static void AssertEqual(string expected, string actual, string label)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new Exception(label + ": expected '" + expected + "', got '" + actual + "'");
        }

        private static void TestClassify()
        {
            int kind;
            AssertTrue(ActivityRegisterLogic.TryClassifyRegisterHistory("Registration OK", out kind), "reg ok");
            AssertEqual(1, kind, "reg ok kind");
            AssertTrue(ActivityRegisterLogic.TryClassifyRegisterHistory("OK: Register OK", out kind), "ok prefix");
            AssertEqual(1, kind, "ok prefix kind");

            AssertTrue(ActivityRegisterLogic.TryClassifyRegisterHistory("Register failed: x", out kind), "fail");
            AssertEqual(0, kind, "fail kind");
            AssertTrue(ActivityRegisterLogic.TryClassifyRegisterHistory("Register rejected", out kind), "rejected");
            AssertEqual(0, kind, "rejected kind");

            AssertTrue(ActivityRegisterLogic.TryClassifyRegisterHistory("Force Register queued", out kind), "info");
            AssertEqual(2, kind, "info kind");

            AssertTrue(!ActivityRegisterLogic.TryClassifyRegisterHistory("Register not required", out kind), "noise not required");
            AssertTrue(!ActivityRegisterLogic.TryClassifyRegisterHistory("Checking WebEAM token", out kind), "noise token");
            AssertTrue(!ActivityRegisterLogic.TryClassifyRegisterHistory("Save + Register automatic", out kind), "startup EN");
            AssertTrue(!ActivityRegisterLogic.TryClassifyRegisterHistory("Salvare + Register automat", out kind), "startup RO");
            AssertTrue(!ActivityRegisterLogic.TryClassifyRegisterHistory("Speichern + Register automatisch", out kind), "startup DE");
            AssertTrue(!ActivityRegisterLogic.TryClassifyRegisterHistory("hello world", out kind), "no register");
        }

        private static void TestNoise()
        {
            AssertTrue(ActivityRegisterLogic.IsActivityUiNoise("Notify client connected: 1"), "connected");
            AssertTrue(ActivityRegisterLogic.IsActivityUiNoise("Notify client disconnected"), "disconnected");
            AssertTrue(ActivityRegisterLogic.IsActivityUiNoise("Notify client socket error"), "socket");
            AssertTrue(ActivityRegisterLogic.IsActivityUiNoise("Notify client replaced"), "replaced");
            AssertTrue(!ActivityRegisterLogic.IsActivityUiNoise("Registration OK"), "not noise");
            AssertTrue(!ActivityRegisterLogic.IsActivityUiNoise(""), "empty");
        }

        private static void TestInferLogLevel()
        {
            AssertEqual((int)ActivityRegisterLogic.ActivityLogLevel.Error,
                (int)ActivityRegisterLogic.InferLogLevel("FAIL: x", false), "FAIL");
            AssertEqual((int)ActivityRegisterLogic.ActivityLogLevel.Success,
                (int)ActivityRegisterLogic.InferLogLevel("OK: done", false), "OK");
            AssertEqual((int)ActivityRegisterLogic.ActivityLogLevel.Success,
                (int)ActivityRegisterLogic.InferLogLevel("Last success at …", false), "last success");
            AssertEqual((int)ActivityRegisterLogic.ActivityLogLevel.Error,
                (int)ActivityRegisterLogic.InferLogLevel("Balloon tip: Error", false), "balloon err");
            AssertEqual((int)ActivityRegisterLogic.ActivityLogLevel.Warning,
                (int)ActivityRegisterLogic.InferLogLevel("Balloon tip: Warning", false), "balloon warn");
            AssertEqual((int)ActivityRegisterLogic.ActivityLogLevel.Warning,
                (int)ActivityRegisterLogic.InferLogLevel("File does not exist", false), "missing");
            AssertEqual((int)ActivityRegisterLogic.ActivityLogLevel.Verbose,
                (int)ActivityRegisterLogic.InferLogLevel("anything", true), "verboseOnly");
            AssertEqual((int)ActivityRegisterLogic.ActivityLogLevel.Info,
                (int)ActivityRegisterLogic.InferLogLevel("hello", false), "info");
        }

        private static void TestRetention()
        {
            var cutoff = ActivityRegisterLogic.LogRetentionCutoffLocal();
            AssertEqual(DateTime.Now.Date.AddDays(-(ActivityRegisterLogic.LogRetentionCalendarDays - 1)),
                cutoff, "cutoff");
            AssertTrue(ActivityRegisterLogic.IsWithinLogRetention(DateTime.Now), "today in");
            AssertTrue(ActivityRegisterLogic.IsWithinLogRetention(cutoff), "cutoff in");
            AssertTrue(!ActivityRegisterLogic.IsWithinLogRetention(cutoff.AddDays(-1)), "before cutoff out");
            AssertTrue(!ActivityRegisterLogic.IsWithinLogRetention(DateTime.Now.Date.AddDays(-3)), "today-3 out");
        }

        private static void AssertEqual(DateTime expected, DateTime actual, string label)
        {
            if (expected != actual)
                throw new Exception(label + ": expected " + expected + ", got " + actual);
        }

        private static void TestSearch()
        {
            AssertTrue(ActivityRegisterLogic.MatchesLogSearch("Hello", ""), "empty needle");
            AssertTrue(ActivityRegisterLogic.MatchesLogSearch("Hello", null), "null needle");
            AssertTrue(ActivityRegisterLogic.MatchesLogSearch("AbcTokenX", "token"), "case insensitive");
            AssertTrue(!ActivityRegisterLogic.MatchesLogSearch("Abc", "zz"), "miss");
            AssertTrue(!ActivityRegisterLogic.MatchesLogSearch("", "x"), "empty text");
            AssertTrue(ActivityRegisterLogic.IsRegistrationFilterMatch("Registration OK"), "reg filter");
            AssertTrue(ActivityRegisterLogic.IsRegistrationFilterMatch("Register failed"), "register filter");
            AssertTrue(!ActivityRegisterLogic.IsRegistrationFilterMatch("KeyReader: x"), "not registration");
        }

        private static void TestExportDay()
        {
            DateTime day;
            AssertTrue(ActivityRegisterLogic.TryParseExportDay("2026-11-18", out day), "iso");
            AssertEqual(new DateTime(2026, 11, 18), day, "iso day");
            AssertTrue(!ActivityRegisterLogic.TryParseExportDay("", out day), "empty");
            AssertTrue(!ActivityRegisterLogic.TryParseExportDay("not-a-date", out day), "garbage");
        }

        private static void TestFilterRegisterHistory()
        {
            var rows = new List<ActivityRegisterLogic.RegisterHistoryFilterRow>
            {
                new ActivityRegisterLogic.RegisterHistoryFilterRow { At = DateTime.Now, Kind = 1, Text = "Registration OK", ResultText = "OK" },
                new ActivityRegisterLogic.RegisterHistoryFilterRow { At = DateTime.Now, Kind = 0, Text = "Register failed cfg", ResultText = "Fail" },
                new ActivityRegisterLogic.RegisterHistoryFilterRow { At = DateTime.Now, Kind = 2, Text = "Force Register", ResultText = "Info" },
                new ActivityRegisterLogic.RegisterHistoryFilterRow { At = DateTime.Now, Kind = 0, Text = "other fail", ResultText = "Fail" }
            };

            AssertTrue(ActivityRegisterLogic.FilterRegisterHistory(rows, "all", "") == null, "all -> null");
            AssertTrue(ActivityRegisterLogic.FilterRegisterHistory(rows, "", null) == null, "empty -> null");

            var ok = ActivityRegisterLogic.FilterRegisterHistory(rows, "ok", "");
            AssertEqual(1, ok.Count, "ok count");
            AssertEqual(1, ok[0].Kind, "ok kind");

            var fail = ActivityRegisterLogic.FilterRegisterHistory(rows, "fail", "cfg");
            AssertEqual(1, fail.Count, "fail+needle");
            AssertTrue(fail[0].Text.IndexOf("cfg", StringComparison.OrdinalIgnoreCase) >= 0, "fail text");

            var info = ActivityRegisterLogic.FilterRegisterHistory(rows, "info", "");
            AssertEqual(1, info.Count, "info count");
            AssertEqual(2, info[0].Kind, "info kind");

            var unknown = ActivityRegisterLogic.FilterRegisterHistory(rows, "nope", "");
            AssertEqual(0, unknown.Count, "unknown empty");
        }

        private static void TestPeerSyncLogic()
        {
            AssertTrue(PeerSyncLogic.ShouldIgnoreOwnEvent("abc", "ABC"), "own event ignore");
            AssertTrue(!PeerSyncLogic.ShouldIgnoreOwnEvent("abc", "xyz"), "foreign event");

            AssertTrue(PeerSyncLogic.IsNewerSeq(5, 4), "newer seq");
            AssertTrue(!PeerSyncLogic.IsNewerSeq(4, 4), "same seq");
            AssertTrue(PeerSyncLogic.IsDuplicateEvent("e1", "e1"), "dup event");
            AssertTrue(!PeerSyncLogic.IsDuplicateEvent("e1", "e2"), "diff event");

            AssertTrue(PeerSyncLogic.ShouldCommitCredentials(true, true, false, false), "commit ok");
            AssertTrue(!PeerSyncLogic.ShouldCommitCredentials(true, true, true, false), "auth rejected");
            AssertTrue(!PeerSyncLogic.ShouldCommitCredentials(true, false, false, true), "skipped");
            AssertTrue(!PeerSyncLogic.ShouldCommitCredentials(false, true, false, false), "fail");
            AssertTrue(PeerSyncLogic.ShouldPublishAfterLocalRegister(true, true, false, false), "publish");

            AssertTrue(PeerSyncLogic.CredentialsDiffer("a@b.c", "1", "a@b.c", "2"), "pass change");
            AssertTrue(!PeerSyncLogic.CredentialsDiffer("A@B.C", "x", "a@b.c", "x"), "same creds");

            AssertEqual(1, PeerSyncLogic.ClampPollMinutes(0), "poll min");
            AssertEqual(1440, PeerSyncLogic.ClampPollMinutes(9999), "poll max");
            AssertEqual(60, PeerSyncLogic.ClampPollMinutes(60), "poll 60");

            var ev = new PeerCredsEvent
            {
                Seq = 3,
                EventId = "evt1",
                OriginServerId = "peer-1",
                PayloadEnc = "blob"
            };
            AssertTrue(PeerSyncLogic.ShouldProcessInboundCreds(ev, "local-1", 2, "old"), "inbound process");
            AssertTrue(!PeerSyncLogic.ShouldProcessInboundCreds(ev, "peer-1", 2, "old"), "skip own");
            AssertTrue(!PeerSyncLogic.ShouldProcessInboundCreds(ev, "local-1", 3, "old"), "skip old seq");
            AssertTrue(!PeerSyncLogic.ShouldProcessInboundCreds(ev, "local-1", 2, "evt1"), "skip dup");

            var ack = new PeerAckEvent
            {
                AckSeq = 9,
                TargetServerId = "local-1",
                PayloadEnc = "blob"
            };
            AssertTrue(PeerSyncLogic.ShouldProcessInboundAck(ack, "local-1", 8), "ack process");
            AssertTrue(!PeerSyncLogic.ShouldProcessInboundAck(ack, "other", 8), "ack wrong target");
            AssertTrue(!PeerSyncLogic.ShouldProcessInboundAck(ack, "local-1", 9), "ack old");

            var cmd = new PeerCommandEvent
            {
                CmdSeq = 4,
                CmdId = "c1",
                OriginServerId = "peer-1",
                PayloadEnc = "blob"
            };
            AssertTrue(PeerSyncLogic.ShouldProcessInboundCommand(cmd, "local-1", 3, "old"), "cmd process");
            AssertTrue(!PeerSyncLogic.ShouldProcessInboundCommand(cmd, "peer-1", 3, "old"), "cmd skip own");
            AssertTrue(!PeerSyncLogic.ShouldProcessInboundCommand(cmd, "local-1", 4, "old"), "cmd skip old");
            AssertTrue(!PeerSyncLogic.ShouldProcessInboundCommand(cmd, "local-1", 3, "c1"), "cmd skip dup");

            var now = DateTime.UtcNow;
            AssertEqual(PeerSyncLogic.HealthOnline,
                PeerSyncLogic.ClassifyPeerHealth(now.ToString("o"), 60, now), "health online");
            AssertEqual(PeerSyncLogic.HealthStale,
                PeerSyncLogic.ClassifyPeerHealth(now.AddHours(-3).ToString("o"), 60, now), "health stale");
            AssertEqual(PeerSyncLogic.HealthOffline,
                PeerSyncLogic.ClassifyPeerHealth(now.AddDays(-2).ToString("o"), 60, now), "health offline");
            AssertEqual(PeerSyncLogic.HealthOffline,
                PeerSyncLogic.ClassifyPeerHealth("", 60, now), "health empty");

            var wire = new List<PeerDashboardWire>
            {
                new PeerDashboardWire { ServerId = "a", ServerName = "Alpha", Health = "Online", TokenStatus = "OK" }
            };
            var enc = PeerDashboardWire.Encode(wire);
            var parsed = PeerDashboardWire.Parse(enc);
            AssertEqual(1, parsed.Count, "wire count");
            AssertEqual("Alpha", parsed[0].ServerName, "wire name");
            AssertEqual("Online", parsed[0].Health, "wire health");
        }

        private static void TestFirebaseParse()
        {
            AssertTrue(PeerSyncFirebaseParse.IsFirebaseDatabaseUrl(
                "https://demo-default-rtdb.europe-west1.firebasedatabase.app"), "rtdb url");
            AssertTrue(PeerSyncFirebaseParse.IsFirebaseDatabaseUrl(
                "https://demo.firebaseio.com/"), "legacy url");
            AssertTrue(!PeerSyncFirebaseParse.IsFirebaseDatabaseUrl(
                "https://script.google.com/macros/s/xxx/exec"), "apps script not firebase");
            AssertTrue(!PeerSyncFirebaseParse.IsFirebaseDatabaseUrl(""), "empty url");

            var tag = PeerSyncFirebaseParse.AuthTagToPathSegment("ab+c/d==");
            AssertEqual("ab-c_d", tag, "auth tag path");

            var norm = PeerSyncFirebaseParse.NormalizeDatabaseUrl(
                "https://demo.firebaseio.com/foo.json/");
            AssertEqual("https://demo.firebaseio.com/foo", norm, "normalize db url");

            var root = PeerSyncFirebaseParse.BuildRelayRootUrl(
                "https://demo.firebaseio.com", "secret-xyz");
            AssertTrue(root.StartsWith("https://demo.firebaseio.com/kai-aos-peer/"), "root prefix");
            AssertTrue(root.Length > "https://demo.firebaseio.com/kai-aos-peer/".Length, "root tag");

            var json = @"{
  ""meta"": { ""seq"": 2, ""ackSeq"": 1, ""cmdSeq"": 3, ""peersSeq"": 4 },
  ""latestCreds"": {
    ""seq"": 2, ""eventId"": ""e1"", ""originServerId"": ""s1"",
    ""originName"": ""A"", ""createdUtc"": ""2020-01-01T00:00:00Z"",
    ""payloadEnc"": ""blob""
  },
  ""latestCommand"": {
    ""cmdSeq"": 3, ""cmdId"": ""c1"", ""type"": ""updateAll"",
    ""originServerId"": ""s1"", ""originName"": ""A"",
    ""createdUtc"": ""2020-01-01T00:00:00Z"", ""payloadEnc"": ""cmdblob""
  },
  ""acks"": {
    ""a1"": {
      ""ackSeq"": 1, ""ackId"": ""a1"", ""targetServerId"": ""local"",
      ""forEventId"": ""e0"", ""responderServerId"": ""s2"",
      ""responderName"": ""B"", ""createdUtc"": ""2020-01-01T00:00:00Z"",
      ""payloadEnc"": ""ackblob""
    }
  },
  ""peers"": {
    ""s1"": { ""serverId"": ""s1"", ""lastSeenUtc"": ""2020-01-01T00:00:00Z"", ""statusEnc"": ""st"" }
  }
}";
            var snap = PeerSyncFirebaseParse.ParseRootJson(json);
            AssertEqual(2, (int)snap.Seq, "snap seq");
            AssertEqual(1, (int)snap.AckSeq, "snap ackSeq");
            AssertEqual(3, (int)snap.CmdSeq, "snap cmdSeq");
            AssertEqual(4, (int)snap.PeersSeq, "snap peersSeq");
            AssertTrue(snap.LatestCreds != null && snap.LatestCreds.EventId == "e1", "snap creds");
            AssertTrue(snap.LatestCommand != null && snap.LatestCommand.CmdId == "c1", "snap cmd");
            AssertEqual(1, snap.Acks.Count, "snap acks");
            AssertEqual(1, snap.Peers.Count, "snap peers");

            string path;
            Newtonsoft.Json.Linq.JToken data;
            AssertTrue(PeerSyncFirebaseParse.TryParseSseData(
                "{\"path\":\"/latestCreds\",\"data\":{\"seq\":2}}", out path, out data), "sse parse");
            AssertEqual("latestCreds", path, "sse path");
            AssertTrue(PeerSyncFirebaseParse.SsePathNeedsSnapshot(path), "sse needs snapshot");
            AssertTrue(!PeerSyncFirebaseParse.SsePathNeedsSnapshot("other/x"), "sse ignore other");
            AssertTrue(PeerSyncFirebaseParse.SsePathNeedsSnapshot(""), "sse root");
            AssertTrue(!PeerSyncFirebaseParse.SsePathNeedsSnapshot("peers/s1"), "sse ignore peers");
            AssertTrue(!PeerSyncFirebaseParse.SsePathNeedsSnapshot("acks/a1"), "sse ignore acks");
            AssertTrue(!PeerSyncFirebaseParse.SsePathNeedsSnapshot("latestCommand"), "sse ignore commands");
            AssertTrue(PeerSyncFirebaseParse.SsePathNeedsSnapshot("meta/seq"), "sse meta.seq");

            PeerCredsEvent sseCreds;
            bool needsSnap;
            AssertTrue(PeerSyncFirebaseParse.TryInterpretLatestCredsSse(
                "{\"path\":\"/\",\"data\":{\"seq\":5,\"eventId\":\"e2\",\"originServerId\":\"s1\"," +
                "\"originName\":\"A\",\"createdUtc\":\"2020-01-01T00:00:00Z\",\"payloadEnc\":\"blob\"}}",
                out sseCreds, out needsSnap), "sse full creds");
            AssertTrue(sseCreds != null && sseCreds.EventId == "e2" && !needsSnap, "sse direct apply");
            AssertTrue(PeerSyncFirebaseParse.TryInterpretLatestCredsSse(
                "{\"path\":\"/seq\",\"data\":6}", out sseCreds, out needsSnap), "sse partial");
            AssertTrue(sseCreds == null && needsSnap, "sse partial needs snapshot");
            AssertTrue(!PeerSyncFirebaseParse.TryInterpretLatestCredsSse(
                "{\"path\":\"/\",\"data\":null}", out sseCreds, out needsSnap), "sse null ignore");

            AssertEqual(5, (int)PeerSyncFirebaseParse.ParseSeqJson("5"), "seq bare");
            AssertEqual(7, (int)PeerSyncFirebaseParse.ParseSeqJson("\"7\""), "seq quoted");
            AssertEqual(0, (int)PeerSyncFirebaseParse.ParseSeqJson("null"), "seq null");
            AssertEqual(0, (int)PeerSyncFirebaseParse.ParseSeqJson(""), "seq empty");
            AssertEqual(9, (int)PeerSyncFirebaseParse.ParseSeqJson("9\n"), "seq newline");

            AssertEqual(1, PeerSyncLogic.ClampPollMinutes(0), "poll clamp low");
            AssertEqual(360, PeerSyncLogic.ClampPollMinutes(360), "poll clamp mid");
            AssertEqual(1440, PeerSyncLogic.ClampPollMinutes(9999), "poll clamp high");

            AssertEqual(PeerSyncLogic.SyncModeOnDemand, PeerSyncLogic.NormalizeFirebasePushSyncMode(""), "mode empty");
            AssertEqual(PeerSyncLogic.SyncModeOnDemand, PeerSyncLogic.NormalizeFirebasePushSyncMode("bogus"), "mode invalid");
            AssertEqual(PeerSyncLogic.SyncModePoll, PeerSyncLogic.NormalizeFirebasePushSyncMode("POLL"), "mode poll");
            AssertEqual(PeerSyncLogic.SyncModeSse, PeerSyncLogic.NormalizeFirebasePushSyncMode("sse"), "mode sse");
            AssertEqual(PeerSyncLogic.SyncModeOnDemand, PeerSyncLogic.NormalizeFirebasePushSyncMode("ondemand"), "mode ondemand");

            var h1 = PeerSyncLogic.ComputeCredsHash("A@B.com", "secret");
            var h2 = PeerSyncLogic.ComputeCredsHash("a@b.com", "secret");
            var h3 = PeerSyncLogic.ComputeCredsHash("a@b.com", "other");
            AssertTrue(!string.IsNullOrEmpty(h1) && h1.Length == 64, "hash length");
            AssertEqual(h1, h2, "hash email case");
            AssertTrue(h1 != h3, "hash password differs");
            AssertTrue(PeerSyncLogic.CredsHashMatches("a@b.com", "secret", h1), "hash match");
            AssertTrue(!PeerSyncLogic.CredsHashMatches("a@b.com", "secret", ""), "hash empty no match");
        }
    }
}
