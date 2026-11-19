using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BMW.ISPI.KAI.Decentral.KaiAdminLibData.KaiZentral;
using KaiAosNotifyClient;
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
            Run("VIN upload inherit window", TestVinUploadInheritWindow);
            Run("AppSettings load/save/bak/concurrency", TestAppSettingsLoadSave);
            Run("RenewEngine run + persistSettings", TestRenewEngineRunAndPersist);
            Run("RenewEngine edge cases", TestRenewEngineEdgeCases);
            Run("RenewRunQueue 403 VIN parking", TestRenewRunQueue);
            Run("AosAckDecisionLogic dirty isolation", TestAosAckDecisions);
            Run("KeysAlertLogic + clear cutoff", TestKeysAlertAndClear);
            Run("KeyActivityMonitor parse/inherit/stall", TestKeyActivityMonitorEndToEnd);
            Run("KeySync + ClientKeyHistory + ActivityFilter", TestKeySyncHistoryAndActivityFilter);
            Run("KeyActivityMonitor dedupe/stall/search/403", TestKeyActivityMonitorDedupeStallSearch);
            Run("KeyReaderLogTailer EOF seeding", TestKeyReaderLogTailerEof);
            Run("KeysExportLogic sort/csv", TestKeysExportLogic);
            Run("DiagBundleLogic redact/health", TestDiagBundleAndHealthAlert);

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
  ""peers"": {
    ""s1"": { ""serverId"": ""s1"", ""lastSeenUtc"": ""2020-01-01T00:00:00Z"", ""statusEnc"": ""st"" }
  }
}";
            var snap = PeerSyncFirebaseParse.ParseRootJson(json);
            AssertEqual(2, (int)snap.Seq, "snap seq");
            AssertTrue(snap.LatestCreds != null && snap.LatestCreds.EventId == "e1", "snap creds");
            // Legacy peers/acks/command nodes are ignored — only latestCreds is read.
            AssertTrue(snap.LatestCreds.OriginServerId == "s1", "snap origin");

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
            AssertEqual(PeerSyncLogic.SyncModeOnDemand, PeerSyncLogic.NormalizeFirebasePushSyncMode("sse"), "mode sse→ondemand");
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

        private static void TestVinUploadInheritWindow()
        {
            AssertEqual(3, (int)ActivityRegisterLogic.InheritUploadWindowMinutes, "window minutes");
            var baseLocal = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Local);
            AssertTrue(ActivityRegisterLogic.ShouldInheritVinUploadStatus(baseLocal, baseLocal), "same instant");
            AssertTrue(ActivityRegisterLogic.ShouldInheritVinUploadStatus(
                baseLocal, baseLocal.AddMinutes(3)), "at 3 min edge");
            AssertTrue(ActivityRegisterLogic.ShouldInheritVinUploadStatus(
                baseLocal, baseLocal.AddMinutes(-2.5)), "within 3 min past");
            AssertTrue(!ActivityRegisterLogic.ShouldInheritVinUploadStatus(
                baseLocal, baseLocal.AddMinutes(3.1)), "outside 3 min future");
            AssertTrue(!ActivityRegisterLogic.ShouldInheritVinUploadStatus(
                baseLocal, baseLocal.AddHours(-1)), "hour-apart older read");
            AssertTrue(!ActivityRegisterLogic.ShouldInheritVinUploadStatus(
                baseLocal, baseLocal.AddMinutes(15)), "15 min separate read");
        }

        private static string NewTempDir(string tag)
        {
            var dir = Path.Combine(Path.GetTempPath(), "KaiAosTests_" + tag + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void SafeDeleteDir(string dir)
        {
            try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { }
        }

        private static int StartMockWebEamServer(string jsonBody, int statusCode, out CancellationTokenSource cts, out Task serverTask)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            cts = new CancellationTokenSource();
            var token = cts.Token;
            serverTask = Task.Run(() =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (!listener.Pending())
                        {
                            Thread.Sleep(15);
                            continue;
                        }
                        using (var tcp = listener.AcceptTcpClient())
                        using (var ns = tcp.GetStream())
                        {
                            var buf = new byte[4096];
                            ns.ReadTimeout = 2000;
                            // Drain headers + POST body before responding so HttpWebRequest never gets RST mid-write.
                            var sb = new StringBuilder();
                            var headerEnd = -1;
                            var contentLen = 0;
                            while (true)
                            {
                                var n = 0;
                                try { n = ns.Read(buf, 0, buf.Length); } catch { break; }
                                if (n <= 0) break;
                                sb.Append(Encoding.ASCII.GetString(buf, 0, n));
                                var raw = sb.ToString();
                                if (headerEnd < 0)
                                {
                                    headerEnd = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                                    if (headerEnd >= 0)
                                    {
                                        foreach (var hline in raw.Substring(0, headerEnd).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                                        {
                                            if (hline.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                                int.TryParse(hline.Substring(15).Trim(), out contentLen);
                                        }
                                    }
                                }
                                if (headerEnd >= 0 && raw.Length >= headerEnd + 4 + contentLen)
                                    break;
                            }
                            var payload = Encoding.UTF8.GetBytes(jsonBody ?? "");
                            var statusText = statusCode == 200 ? "OK" : "Unauthorized";
                            var header = "HTTP/1.1 " + statusCode + " " + statusText + "\r\n"
                                + "Content-Type: application/json\r\n"
                                + "Content-Length: " + payload.Length + "\r\n"
                                + "Connection: close\r\n\r\n";
                            var hdrBytes = Encoding.ASCII.GetBytes(header);
                            ns.Write(hdrBytes, 0, hdrBytes.Length);
                            ns.Write(payload, 0, payload.Length);
                            ns.Flush();
                        }
                    }
                }
                catch { }
                finally
                {
                    try { listener.Stop(); } catch { }
                }
            });
            return port;
        }

        private static void TestAppSettingsLoadSave()
        {
            var tmp = NewTempDir("appsettings");
            var prevOverride = AppSettings.StoreDirOverride;
            try
            {
                AppSettings.StoreDirOverride = tmp;

                // 1) Save + Load roundtrip (DPAPI password & secret + clamps).
                var s = new AppSettings
                {
                    Email = "tech@example.com",
                    IntervalHours = 12,
                    NotifyPairingCode = "  PAIR12  ",
                    AuthRegisterBlockAfter = 99
                };
                s.SetPassword("Secret!123");
                s.SetFirebasePushSecret("FbSec!456");
                s.Save();

                AssertTrue(File.Exists(AppSettings.SettingsPath), "settings.xml should exist");
                var loaded = AppSettings.Load();
                AssertEqual("tech@example.com", loaded.Email, "loaded email");
                AssertEqual("Secret!123", loaded.GetPassword(), "loaded dpapi password");
                AssertEqual("FbSec!456", loaded.GetFirebasePushSecret(), "loaded dpapi secret");
                AssertEqual("PAIR12", loaded.GetNotifyPairingCode(), "pairing trimmed (DPAPI)");
                AssertEqual("", loaded.NotifyPairingCode ?? "", "legacy plaintext pairing cleared");
                AssertTrue(File.ReadAllText(AppSettings.SettingsPath).IndexOf("PAIR12", StringComparison.Ordinal) < 0,
                    "pairing code must not be plaintext in settings.xml");
                AssertEqual(10, loaded.AuthRegisterBlockAfter, "blockAfter clamped");

                // 2) Second Save creates .bak; corrupting primary recovers from .bak and repairs primary.
                loaded.IntervalHours = 24;
                loaded.Save();
                AssertTrue(File.Exists(AppSettings.SettingsPath + ".bak"), ".bak should exist after second save");
                File.WriteAllText(AppSettings.SettingsPath, "<corrupt-xml", Encoding.UTF8);

                var recovered = AppSettings.Load();
                AssertEqual("tech@example.com", recovered.Email, "recovered email from .bak");
                AssertEqual("Secret!123", recovered.GetPassword(), "recovered password from .bak");
                var repairedXml = File.ReadAllText(AppSettings.SettingsPath);
                AssertTrue(repairedXml.IndexOf("tech@example.com", StringComparison.Ordinal) >= 0,
                    "primary settings.xml should be repaired from .bak");

                // 3) Legacy XML defaults applied when elements are missing.
                File.WriteAllText(AppSettings.SettingsPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<AppSettings><Email>legacy@example.com</Email></AppSettings>",
                    Encoding.UTF8);
                var legacy = AppSettings.Load();
                AssertEqual("legacy@example.com", legacy.Email, "legacy email");
                AssertTrue(legacy.AutoUpdate, "legacy AutoUpdate default");
                AssertTrue(legacy.MinimizeOnClose, "legacy MinimizeOnClose default");
                AssertEqual(60, legacy.AuthCooldownMinutes, "legacy cooldown default");

                // 4) Concurrent Save + Load stress test under SaveGate.
                legacy.SetPassword("ConcurrentPass");
                legacy.Save();
                var errors = new List<Exception>();
                var tasks = new List<Task>();
                for (var t = 0; t < 4; t++)
                {
                    var worker = t;
                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            for (var i = 0; i < 15; i++)
                            {
                                if (worker % 2 == 0)
                                {
                                    var x = new AppSettings { Email = "w" + worker + "@example.com", IntervalHours = 6 };
                                    x.SetPassword("ConcurrentPass");
                                    x.Save();
                                }
                                else
                                {
                                    var r = AppSettings.Load();
                                    if (string.IsNullOrEmpty(r.Email))
                                        throw new Exception("concurrent Load saw empty Email");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (errors) errors.Add(ex);
                        }
                    }));
                }
                Task.WaitAll(tasks.ToArray());
                AssertEqual(0, errors.Count, "concurrent Save/Load errors");
            }
            finally
            {
                AppSettings.StoreDirOverride = prevOverride;
                SafeDeleteDir(tmp);
            }
        }

        private static void TestRenewEngineRunAndPersist()
        {
            var tmpStore = NewTempDir("renew_store");
            var tmpConfig = NewTempDir("renew_cfg");
            var prevOverride = AppSettings.StoreDirOverride;
            CancellationTokenSource cts = null;
            Task srvTask = null;
            try
            {
                AppSettings.StoreDirOverride = tmpStore;
                KaiZentralUserRegProc.Reset();

                var port = StartMockWebEamServer("{\"access_token\":\"tok-123\",\"expires_in\":3600}", 200, out cts, out srvTask);
                var webEamUrl = "http://127.0.0.1:" + port + "/token";
                KaiZentralUserRegProc.WebEamUrlOverride = webEamUrl;

                // 1) persistSettings=false (PeerSync candidate clone) must NEVER create or overwrite settings.xml.
                var realSettings = new AppSettings { Email = "real@example.com", ConfigPath = tmpConfig };
                realSettings.SetPassword("RealPass");
                realSettings.Save();
                var beforeXml = File.ReadAllText(AppSettings.SettingsPath);

                var probeClone = new AppSettings
                {
                    Email = "probe@example.com",
                    ConfigPath = tmpConfig,
                    WebEamUrl = webEamUrl
                };
                probeClone.SetPassword("ProbePass");
                var probeRes = RenewEngine.Run(probeClone, true, "ProbePass", true, persistSettings: false);
                AssertTrue(probeRes.Ok && probeRes.RegisterAttempted, "probe register should succeed");
                AssertEqual(beforeXml, File.ReadAllText(AppSettings.SettingsPath),
                    "persistSettings=false must not overwrite settings.xml");

                // 2) persistSettings=true DOES update settings.xml.
                var realRes = RenewEngine.Run(realSettings, true, "RealPass", true, persistSettings: true);
                AssertTrue(realRes.Ok, "real register should succeed");
                var afterLoad = AppSettings.Load();
                AssertTrue(afterLoad.LastRegisterOkUtc.HasValue, "LastRegisterOkUtc persisted");

                // 3) allowRegister=false runs HealthOnly against mock WebEAM without calling Register.
                var callsBefore = KaiZentralUserRegProc.RegisterCallCount;
                var healthRes = RenewEngine.Run(realSettings, false, null, allowRegister: false, persistSettings: false);
                AssertTrue(healthRes.Ok && healthRes.SkippedRegister,
                    "HealthOnly via allowRegister=false: " + (healthRes.Message ?? ""));
                AssertEqual(callsBefore, KaiZentralUserRegProc.RegisterCallCount, "allowRegister=false must not call Register");

                // 4) forceRegister + Register 401 auth rejection falls back to existing valid WebEAM token.
                KaiZentralUserRegProc.OnRegister = (uri, em, pw, pr) =>
                {
                    throw new WebException("HTTP 401 Unauthorized");
                };
                var softCallsBefore = KaiZentralUserRegProc.RegisterCallCount;
                var softRes = RenewEngine.Run(realSettings, true, "BadNewPass", true, persistSettings: false);
                AssertTrue(softRes.Ok, "soft fallback should stay Ok");
                AssertTrue(softRes.RegisterAuthRejected, "RegisterAuthRejected flag");
                AssertTrue(softRes.SkippedRegister && softRes.RegisterAttempted, "soft flags");
                AssertEqual(softCallsBefore + 1, KaiZentralUserRegProc.RegisterCallCount,
                    "auth failure must not retry 3 times");
            }
            finally
            {
                if (cts != null) cts.Cancel();
                try { if (srvTask != null) srvTask.Wait(1500); } catch { }
                KaiZentralUserRegProc.Reset();
                AppSettings.StoreDirOverride = prevOverride;
                SafeDeleteDir(tmpStore);
                SafeDeleteDir(tmpConfig);
            }
        }

        private static void TestRenewRunQueue()
        {
            var q = new RenewRunQueueState();
            string activeVin = "WBATEST0000000001";
            string activeReader = "reader-1";

            // Queuing a force/403 run while busy parks the 403 VIN so active run does not consume it.
            q.Enqueue("NewPass", true, true, false, ref activeVin, ref activeReader);
            AssertTrue(activeVin == null && activeReader == null, "active 403 VIN parked");
            AssertTrue(q.HasPending, "queue should have pending");

            // Queuing a second non-lock run preserves LockRegisterAfter=true (OR semantics) and StartTimer.
            q.Enqueue("NewPass2", false, false, true, ref activeVin, ref activeReader);

            PendingRenewWork work;
            AssertTrue(q.TryDequeue(out work), "dequeue should succeed");
            AssertEqual("NewPass2", work.Password, "dequeued password");
            AssertTrue(work.IgnoreAuthGuards, "dequeued ignoreAuthGuards");
            AssertTrue(work.LockRegisterAfter, "dequeued lockRegisterAfter preserved");
            AssertTrue(work.StartTimerOnSuccess, "dequeued startTimer preserved");
            AssertEqual("WBATEST0000000001", work.Retry403Vin, "promoted 403 VIN");
            AssertEqual("reader-1", work.Retry403ReaderId, "promoted 403 reader");
            AssertTrue(!q.HasPending, "queue empty after dequeue");
        }

        private static void TestAosAckDecisions()
        {
            AssertTrue(AosAckDecisionLogic.IsDownloadAckText("getKeyBin|WBA123"), "getKeyBin download");
            AssertTrue(AosAckDecisionLogic.IsDownloadAckText("getZip|logs"), "getZip download");
            AssertTrue(AosAckDecisionLogic.IsDownloadAckText("Bin not found"), "bin not found");
            AssertTrue(!AosAckDecisionLogic.IsDownloadAckText("Invalid password"), "auth error is not download");

            AssertEqual(90, AosAckDecisionLogic.TimeoutSecondsForCmd("fixServices"), "fixServices timeout");
            AssertEqual(90, AosAckDecisionLogic.TimeoutSecondsForCmd("restartServices"), "restartServices timeout");
            AssertEqual(90, AosAckDecisionLogic.TimeoutSecondsForCmd("serverUpdate"), "serverUpdate timeout");
            AssertEqual(10, AosAckDecisionLogic.TimeoutSecondsForCmd("checkToken"), "checkToken timeout");

            // Service/update error or timeout must NEVER dirty _aosDirty.
            AssertTrue(!AosAckDecisionLogic.ResolveDirtyOnAck(false, true, false, false), "non-creds ack error keeps clean");
            AssertTrue(!AosAckDecisionLogic.ResolveDirtyOnTimeout(false, false, false), "non-creds timeout keeps clean");
            AssertTrue(AosAckDecisionLogic.ResolveDirtyOnAck(true, true, false, false), "non-creds ack error preserves existing dirty");

            // Interval-only save error/timeout also leaves _aosDirty untouched.
            AssertTrue(!AosAckDecisionLogic.ResolveDirtyOnAck(false, true, true, true), "interval error keeps clean");
            AssertTrue(!AosAckDecisionLogic.ResolveDirtyOnTimeout(false, true, true), "interval timeout keeps clean");

            // Real credential save (wasCredsSave=true, intervalOnly=false) marks dirty on error/timeout, clears on OK.
            AssertTrue(AosAckDecisionLogic.ResolveDirtyOnAck(false, true, true, false), "creds error marks dirty");
            AssertTrue(AosAckDecisionLogic.ResolveDirtyOnTimeout(false, true, false), "creds timeout marks dirty");
            AssertTrue(!AosAckDecisionLogic.ResolveDirtyOnAck(true, false, true, false), "creds OK clears dirty");
        }

        private static void TestKeysAlertAndClear()
        {
            var clearAt = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Local);
            AssertTrue(!KeysAlertLogic.IsAfterClear(clearAt, clearAt), "equal timestamp hidden after clear");
            AssertTrue(!KeysAlertLogic.IsAfterClear(clearAt.AddSeconds(-1), clearAt), "older event hidden after clear");
            AssertTrue(KeysAlertLogic.IsAfterClear(clearAt.AddSeconds(1), clearAt), "newer event visible after clear");
            AssertTrue(KeysAlertLogic.IsAfterClear(clearAt, null), "null clear shows all");

            var now = new DateTime(2026, 11, 19, 12, 10, 0, DateTimeKind.Local);
            // Failed (4) or Waiting (1) within 30 min -> alert
            AssertTrue(KeysAlertLogic.ShouldAlertNewestKey(true, 4, true, false, true, now.AddMinutes(-5), now, true, 2, 30),
                "failed recent alerts");
            AssertTrue(KeysAlertLogic.ShouldAlertNewestKey(true, 1, false, false, false, now.AddMinutes(-1), now, true, 2, 30),
                "waiting recent alerts");
            // LocalSent without BMW upload beyond stallMinutes -> alert when stallEnabled=true
            AssertTrue(KeysAlertLogic.ShouldAlertNewestKey(true, 2, true, false, false, now.AddMinutes(-3), now, true, 2, 30),
                "stalled alerts");
            AssertTrue(!KeysAlertLogic.ShouldAlertNewestKey(true, 2, true, false, false, now.AddMinutes(-3), now, false, 2, 30),
                "stalled disabled no alert");
            // BmwUploaded clears alert
            AssertTrue(!KeysAlertLogic.ShouldAlertNewestKey(true, 3, true, true, false, now.AddMinutes(-3), now, true, 2, 30),
                "bmw ok no alert");
            // Older than maxAlertMinutes does not alert
            AssertTrue(!KeysAlertLogic.ShouldAlertNewestKey(true, 4, true, false, true, now.AddMinutes(-35), now, true, 2, 30),
                "stale failure no alert");
            // After Clear Keys -> no event passed -> no alert
            AssertTrue(!KeysAlertLogic.ShouldAlertNewestKey(false, 0, false, false, false, DateTime.MinValue, now, true, 2, 30),
                "cleared cache no alert");
        }

        private static void TestKeyActivityMonitorEndToEnd()
        {
            var tmpStore = NewTempDir("keymon_store");
            var pickupDir = NewTempDir("keymon_pickup");
            var upLogDir = NewTempDir("keymon_up");
            var kaiLogDir = NewTempDir("keymon_kai");
            var prevOverride = KeyActivityMonitor.StoreDirOverride;
            try
            {
                KeyActivityMonitor.StoreDirOverride = tmpStore;

                // Write synthetic keyreader.bin (>=20 bytes) containing VIN + 0x84 0x01 tag (Paint A300, Uph KCSW).
                var binBytes = new byte[48];
                var vinBytes = Encoding.ASCII.GetBytes("WBA12345678901234");
                Buffer.BlockCopy(vinBytes, 0, binBytes, 0, vinBytes.Length);
                binBytes[20] = 0x84;
                binBytes[21] = 0x01;
                binBytes[22] = 0x00;
                Buffer.BlockCopy(Encoding.ASCII.GetBytes("A300KCSW"), 0, binBytes, 23, 8);
                var binPath = Path.Combine(pickupDir, "keyreader.bin");
                File.WriteAllBytes(binPath, binBytes);

                var tRecent = DateTime.Now.AddMinutes(-4);
                var tOlderSeparate = tRecent.AddMinutes(-20);
                var tOlderSibling = tRecent.AddMinutes(-1);

                // KeyUploader log: an older read (20m earlier, same VIN) + recent sibling pair (1m apart)
                var upLines = new[]
                {
                    tOlderSeparate.ToString("yyyy-MM-dd HH:mm:ss,fff") + " INFO Upload Url: https://x/keyreaderids/READER0/macaddr/AA",
                    tOlderSeparate.ToString("yyyy-MM-dd HH:mm:ss,fff") + " INFO Picked up binary file keyreader.bin",
                    tOlderSeparate.ToString("yyyy-MM-dd HH:mm:ss,fff") + " INFO 11111111-1111-1111-1111-111111111111 successfully sent to KAI Dezentral",
                    tRecent.ToString("yyyy-MM-dd HH:mm:ss,fff") + " INFO Upload Url: https://x/keyreaderids/READER1/macaddr/BB",
                    tRecent.ToString("yyyy-MM-dd HH:mm:ss,fff") + " INFO Picked up binary file keyreader.bin",
                    tOlderSibling.ToString("yyyy-MM-dd HH:mm:ss,fff") + " INFO 22222222-2222-2222-2222-222222222222 successfully sent to KAI Dezentral"
                };
                File.WriteAllLines(Path.Combine(upLogDir, "KeyUploader.log"), upLines, Encoding.UTF8);

                // KAI log: BMW upload success ONLY for the 20-minute-older read (NOT for recent read!)
                var kaiLines = new[]
                {
                    tOlderSeparate.AddSeconds(5).ToString("yyyy-MM-dd HH:mm:ss,fff") + " INFO Successfully uploaded key data to KAI Central"
                };
                File.WriteAllLines(Path.Combine(kaiLogDir, "KAI.log"), kaiLines, Encoding.UTF8);
                File.SetLastWriteTime(binPath, tRecent);

                using (var mon = new KeyActivityMonitor())
                {
                    mon.SkipDefaultTransferredForTests = true;
                    mon.Configure(pickupDir, 2, upLogDir, kaiLogDir, Path.Combine(tmpStore, "missing.db"));
                    var snap = mon.Refresh();
                    AssertTrue(snap != null && snap.Events != null && snap.Events.Count >= 2, "monitor built events");

                    // Newest event (~4 min ago) must NOT inherit BmwUploaded from the 20-min-older read!
                    var newest = snap.Events[0];
                    AssertEqual("WBA12345678901234", newest.Vin, "newest VIN attached from bin");
                    AssertEqual("A300", newest.Paint, "newest paint parsed");
                    AssertEqual("KCSW", newest.Upholstery, "newest upholstery parsed");
                    AssertTrue(newest.LocalSent, "newest LocalSent inherited from 1-min sibling");
                    AssertTrue(!newest.BmwUploaded, "newest must NOT inherit BmwUploaded from 20-min-older read");
                    AssertEqual("READER1", newest.ReaderId, "reader id extracted");

                    // Also verify persisted key-events.xml exists in redirected store dir.
                    AssertTrue(File.Exists(KeyActivityMonitor.EventsStorePath), "key-events.xml persisted in override dir");
                }
            }
            finally
            {
                KeyActivityMonitor.StoreDirOverride = prevOverride;
                SafeDeleteDir(tmpStore);
                SafeDeleteDir(pickupDir);
                SafeDeleteDir(upLogDir);
                SafeDeleteDir(kaiLogDir);
            }
        }

        private static void TestKeySyncHistoryAndActivityFilter()
        {
            var tmp = NewTempDir("keysync");
            var prevHist = ClientKeyHistory.StoreDirOverride;
            try
            {
                ClientKeyHistory.StoreDirOverride = tmp;

                var t0 = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Local);
                var binPath = Path.Combine(tmp, "exists.bin");
                File.WriteAllBytes(binPath, new byte[] { 1, 2, 3 });

                var rows = new List<KeyActivityEvent>
                {
                    new KeyActivityEvent
                    {
                        DetectedLocal = t0,
                        Vin = "WBAPIPE0000000001",
                        Paint = "A3|00",
                        Upholstery = "KC\\SW",
                        ILevelCurrent = "I001",
                        ReaderId = "R1",
                        LocalSent = true,
                        BmwUploaded = true,
                        Failed = false,
                        Status = KeyEventStatus.BmwOk,
                        Detail = "ok|detail",
                        DebugBinPath = binPath,
                        MileageKm = 12345
                    },
                    new KeyActivityEvent
                    {
                        DetectedLocal = t0.AddMinutes(1),
                        Vin = "WBAWAIT0000000002",
                        LocalSent = false,
                        BmwUploaded = false,
                        Failed = false,
                        Status = KeyEventStatus.Waiting,
                        Detail = "waiting"
                    },
                    new KeyActivityEvent
                    {
                        DetectedLocal = t0.AddMinutes(2),
                        Vin = "WBAFAIL0000000003",
                        LocalSent = true,
                        BmwUploaded = false,
                        Failed = true,
                        Status = KeyEventStatus.Failed,
                        Detail = "fail"
                    },
                    new KeyActivityEvent
                    {
                        DetectedLocal = t0.AddMinutes(3),
                        Vin = "WBALOCAL000000004",
                        LocalSent = true,
                        BmwUploaded = false,
                        Failed = false,
                        Status = KeyEventStatus.LocalOk,
                        Detail = "local"
                    },
                    new KeyActivityEvent
                    {
                        DetectedLocal = t0.AddMinutes(4),
                        Vin = "WBAEXTRA000000005",
                        LocalSent = false,
                        Status = KeyEventStatus.Waiting,
                        Detail = "extra"
                    }
                };

                var encoded = NotifyKeySync.EncodeKeySync(rows);
                AssertTrue(encoded.IndexOf("A3\\p00", StringComparison.Ordinal) >= 0,
                    "paint pipe must be escaped as \\p in wire");

                var parsedAll = ClientKeySync.ParseKeySync(encoded);
                AssertEqual(5, parsedAll.Count, "full parse count");
                AssertEqual("A3|00", parsedAll[0].Paint, "paint unescape");
                AssertEqual("KC\\SW", parsedAll[0].Upholstery, "upholstery unescape");
                AssertEqual("BmwOk", parsedAll[0].Status, "BmwOk status");
                AssertTrue(parsedAll[0].HasBin, "HasBin when DebugBinPath exists");
                AssertEqual(12345, parsedAll[0].MileageKm.GetValueOrDefault(), "MileageKm");
                AssertEqual("Waiting", parsedAll[1].Status, "Waiting status");
                AssertEqual("Failed", parsedAll[2].Status, "Failed status");
                AssertEqual("LocalOk", parsedAll[3].Status, "LocalOk status");

                var capped = ClientKeySync.ParseKeySync(encoded, 2);
                AssertEqual(2, capped.Count, "maxRows cap");

                var msg = new NotifyMessage { Kind = "keyEvent" };
                NotifyKeySync.ApplyKeyEventFields(msg, rows[0]);
                var fromMsg = ClientKeySync.FromNotifyMessage(msg);
                AssertEqual(rows[0].Vin, fromMsg.Vin, "FromNotifyMessage VIN");
                AssertEqual("BmwOk", fromMsg.Status, "FromNotifyMessage status");
                AssertTrue(fromMsg.BmwUploaded && fromMsg.LocalSent, "FromNotifyMessage flags");
                AssertEqual(12345, fromMsg.MileageKm.GetValueOrDefault(), "FromNotifyMessage km");

                var histRows = new List<ClientKeyRow>
                {
                    ClientKeyHistory.Clone(parsedAll[0]),
                    ClientKeyHistory.Clone(parsedAll[1])
                };
                ClientKeyHistory.Save(histRows);
                var loaded = ClientKeyHistory.Load();
                AssertEqual(2, loaded.Count, "history load count");
                AssertEqual(parsedAll[0].Vin, loaded[0].Vin, "history VIN");
                AssertEqual(parsedAll[0].Paint, loaded[0].Paint, "history paint");
                AssertEqual(ClientKeyHistory.RowKey(parsedAll[0]), ClientKeyHistory.RowKey(loaded[0]), "RowKey stable");
                AssertTrue(ClientKeyHistory.Clone(null) == null, "Clone null");

                AssertTrue(ActivityFilterLogic.MatchesActivityRow("all", "info", "anything", ""), "all empty search");
                AssertTrue(ActivityFilterLogic.MatchesActivityRow("error", "error", "FAIL: x", ""), "error level");
                AssertTrue(!ActivityFilterLogic.MatchesActivityRow("error", "info", "hello", ""), "error filter rejects info");
                AssertTrue(ActivityFilterLogic.MatchesActivityRow("keyreader", "info", "KeyReader: read", ""), "keyreader filter");
                AssertTrue(!ActivityFilterLogic.MatchesActivityRow("keyreader", "info", "plain note", ""), "keyreader rejects plain");
                AssertTrue(ActivityFilterLogic.MatchesActivityRow("registration", "info", "Registration OK", ""), "registration filter");
                AssertTrue(ActivityFilterLogic.MatchesActivityRow("registration", "info", "Register failed", "fail"), "registration + search");
                AssertTrue(!ActivityFilterLogic.MatchesActivityRow("all", "info", "hello", "xyz"), "search miss");
            }
            finally
            {
                ClientKeyHistory.StoreDirOverride = prevHist;
                SafeDeleteDir(tmp);
            }
        }

        private static void TestKeyActivityMonitorDedupeStallSearch()
        {
            var tmpStore = NewTempDir("dedupe_store");
            var pickupDir = NewTempDir("dedupe_pickup");
            var prevOverride = KeyActivityMonitor.StoreDirOverride;
            try
            {
                KeyActivityMonitor.StoreDirOverride = tmpStore;
                var now = DateTime.Now;
                const string histVin = "WBASEARCH00000001";

                // Dedupe: same VIN within 3 min keeps richest (BmwUploaded > LocalSent > Failed > Waiting)
                var wait = new KeyActivityEvent
                {
                    DetectedLocal = now.AddMinutes(-2),
                    Vin = "WBADEDUPE00000001",
                    LocalSent = false,
                    BmwUploaded = false,
                    Failed = false,
                    Status = KeyEventStatus.Waiting
                };
                var local = new KeyActivityEvent
                {
                    DetectedLocal = now.AddMinutes(-1),
                    Vin = "WBADEDUPE00000001",
                    LocalSent = true,
                    BmwUploaded = false,
                    Failed = false,
                    Status = KeyEventStatus.LocalOk
                };
                var bmw = new KeyActivityEvent
                {
                    DetectedLocal = now.AddMinutes(-0.5),
                    Vin = "WBADEDUPE00000001",
                    LocalSent = true,
                    BmwUploaded = true,
                    Failed = false,
                    Status = KeyEventStatus.BmwOk
                };
                var farApart = new KeyActivityEvent
                {
                    DetectedLocal = now.AddMinutes(-10),
                    Vin = "WBADEDUPE00000001",
                    LocalSent = true,
                    BmwUploaded = false,
                    Failed = false,
                    Status = KeyEventStatus.LocalOk
                };
                var deduped = KeyActivityMonitor.DeduplicateVinRows(
                    new List<KeyActivityEvent> { wait, local, bmw, farApart });
                AssertEqual(2, deduped.Count, "dedupe keeps far-apart + merged");
                AssertTrue(deduped.Exists(e => e.BmwUploaded), "prefer BmwUploaded in window");
                AssertTrue(deduped.Exists(e => !e.BmwUploaded && Math.Abs((e.DetectedLocal - farApart.DetectedLocal).TotalSeconds) < 1),
                    "far-apart row preserved");

                AssertTrue(KeyActivityMonitor.LooksLikeKaiUploadFail(
                    "PutKeyDataV1 403 Forbidden from KAI Zentral"), "403 Zentral fail");
                AssertTrue(KeyActivityMonitor.LooksLikeKaiUploadFail(
                    "could not upload key data"), "could not upload");
                AssertTrue(!KeyActivityMonitor.LooksLikeKaiUploadFail(
                    "MAINTENANCE : Info unrelated"), "maintenance noise");

                using (var mon = new KeyActivityMonitor())
                {
                    mon.SkipDefaultTransferredForTests = true;
                    mon.Configure(pickupDir, 2, pickupDir, pickupDir, Path.Combine(tmpStore, "missing.db"));

                    var stallEv = new KeyActivityEvent
                    {
                        DetectedLocal = now.AddMinutes(-5),
                        Vin = "WBASTALL000000001",
                        LocalSent = true,
                        BmwUploaded = false,
                        Failed = false,
                        StallNotified = false,
                        Detail = "stalled"
                    };
                    var stalls1 = mon.DetectStallEvents(new List<KeyActivityEvent> { stallEv });
                    AssertEqual(1, stalls1.Count, "stall emitted once");
                    AssertTrue(stallEv.StallNotified, "StallNotified set");
                    var stalls2 = mon.DetectStallEvents(new List<KeyActivityEvent> { stallEv });
                    AssertEqual(0, stalls2.Count, "repeat stall suppressed");

                    var siblingOk = new KeyActivityEvent
                    {
                        DetectedLocal = now.AddMinutes(-1),
                        Vin = "WBASTALL000000001",
                        LocalSent = true,
                        BmwUploaded = true,
                        Failed = false
                    };
                    var stallSibling = new KeyActivityEvent
                    {
                        DetectedLocal = now.AddMinutes(-5),
                        Vin = "WBASTALL000000001",
                        LocalSent = true,
                        BmwUploaded = false,
                        Failed = false,
                        StallNotified = false
                    };
                    AssertEqual(0, mon.DetectStallEvents(new List<KeyActivityEvent> { stallSibling, siblingOk }).Count,
                        "BMW upload for same VIN suppresses stall");

                    var ancient = new KeyActivityEvent
                    {
                        DetectedLocal = now.AddHours(-30),
                        Vin = "WBAOLD00000000001",
                        LocalSent = true,
                        BmwUploaded = false,
                        Failed = false,
                        StallNotified = false
                    };
                    AssertEqual(0, mon.DetectStallEvents(new List<KeyActivityEvent> { ancient }).Count,
                        "events >24h ignored");

                    // Persist a searchable history row + archived debug bin
                    var hist = new List<KeyActivityEvent>
                    {
                        new KeyActivityEvent
                        {
                            DetectedLocal = now.AddMinutes(-3),
                            Vin = histVin,
                            Paint = "A300",
                            Upholstery = "KCSW",
                            Detail = "needle-detail",
                            LocalSent = true,
                            Status = KeyEventStatus.LocalOk
                        }
                    };
                    var ser = new System.Xml.Serialization.XmlSerializer(typeof(List<KeyActivityEvent>));
                    using (var fs = File.Create(KeyActivityMonitor.EventsStorePath))
                        ser.Serialize(fs, hist);

                    var binsDir = Path.Combine(tmpStore, "key-bins");
                    Directory.CreateDirectory(binsDir);
                    var archived = Path.Combine(binsDir, histVin + ".bin");
                    var binBytes = new byte[48];
                    Buffer.BlockCopy(Encoding.ASCII.GetBytes(histVin), 0, binBytes, 0, 17);
                    binBytes[20] = 0x84;
                    binBytes[21] = 0x01;
                    binBytes[22] = 0x00;
                    Buffer.BlockCopy(Encoding.ASCII.GetBytes("A300KCSW"), 0, binBytes, 23, 8);
                    File.WriteAllBytes(archived, binBytes);

                    AssertEqual(archived, KeyActivityMonitor.ResolveDebugBinForVin(histVin),
                        "ResolveDebugBinForVin finds archive");

                    // New monitor instance to force reload from disk
                }

                using (var mon2 = new KeyActivityMonitor())
                {
                    mon2.SkipDefaultTransferredForTests = true;
                    mon2.Configure(pickupDir, 2, pickupDir, pickupDir, Path.Combine(tmpStore, "missing.db"));
                    var hits = mon2.SearchHistory("needle-detail", 10);
                    AssertTrue(hits.Count >= 1, "SearchHistory finds detail");
                    AssertEqual(histVin, hits[0].Vin, "SearchHistory VIN");
                    AssertTrue(!string.IsNullOrEmpty(hits[0].DebugBinPath)
                        || File.Exists(KeyActivityMonitor.ResolveDebugBinForVin(histVin)),
                        "debug bin linked or resolvable");

                    // Archive-only VIN search (not in history)
                    var archiveOnlyVin = "WBAARCHIVE0000001";
                    var archiveOnly = Path.Combine(Path.Combine(tmpStore, "key-bins"), archiveOnlyVin + ".bin");
                    var ab = new byte[48];
                    Buffer.BlockCopy(Encoding.ASCII.GetBytes(archiveOnlyVin), 0, ab, 0, 17);
                    File.WriteAllBytes(archiveOnly, ab);
                    var archHits = mon2.SearchHistory(archiveOnlyVin, 5);
                    AssertTrue(archHits.Exists(e => string.Equals(e.Vin, archiveOnlyVin, StringComparison.OrdinalIgnoreCase)),
                        "SearchHistory finds archived-only VIN");
                }
            }
            finally
            {
                KeyActivityMonitor.StoreDirOverride = prevOverride;
                SafeDeleteDir(tmpStore);
                SafeDeleteDir(pickupDir);
            }
        }

        private static void TestKeyReaderLogTailerEof()
        {
            var logDir = NewTempDir("tailer_log");
            try
            {
                var logPath = Path.Combine(logDir, "KeyReader.log");
                File.WriteAllText(logPath,
                    "2026-11-19 10:00:00,000 INFO pre-existing line should be skipped\r\n"
                    + "2026-11-19 10:00:01,000 INFO another old line\r\n",
                    Encoding.UTF8);

                var emitted = new List<Tuple<string, KeyReaderActivityKind>>();
                using (var tailer = new KeyReaderLogTailer())
                {
                    tailer.Start(logDir, null, (text, kind) =>
                    {
                        lock (emitted) emitted.Add(Tuple.Create(text, kind));
                    }, "KeyReader: ", false);

                    Thread.Sleep(200);
                    lock (emitted) AssertEqual(0, emitted.Count, "EOF seed must skip pre-existing lines");

                    using (var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                    {
                        sw.WriteLine("2026-11-19 10:05:00,000 INFO Successfully uploaded key data to KAI Central");
                        sw.WriteLine("2026-11-19 10:05:01,000 ERROR PutKeyDataV1 403 Forbidden from KAI Zentral");
                        sw.Flush();
                    }

                    // Force drain (watcher debounce may be slow in CI)
                    for (var i = 0; i < 20; i++)
                    {
                        tailer.PollNow();
                        lock (emitted)
                        {
                            if (emitted.Count >= 2) break;
                        }
                        Thread.Sleep(50);
                    }

                    lock (emitted)
                    {
                        AssertTrue(emitted.Count >= 2, "new lines should emit, got " + emitted.Count);
                        AssertTrue(emitted.Exists(e =>
                            e.Item2 == KeyReaderActivityKind.Success
                            && e.Item1.IndexOf("Successfully uploaded", StringComparison.OrdinalIgnoreCase) >= 0),
                            "success kind for upload");
                        AssertTrue(emitted.Exists(e =>
                            e.Item2 == KeyReaderActivityKind.Error
                            && e.Item1.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0),
                            "error kind for 403");
                        AssertTrue(!emitted.Exists(e =>
                            e.Item1.IndexOf("pre-existing", StringComparison.OrdinalIgnoreCase) >= 0),
                            "pre-existing must never emit");
                    }
                }
            }
            finally
            {
                SafeDeleteDir(logDir);
            }
        }

        private static void TestRenewEngineEdgeCases()
        {
            var tmpStore = NewTempDir("renew_edge_store");
            var tmpConfig = NewTempDir("renew_edge_cfg");
            var prevOverride = AppSettings.StoreDirOverride;
            CancellationTokenSource cts = null;
            Task srvTask = null;
            try
            {
                AppSettings.StoreDirOverride = tmpStore;
                KaiZentralUserRegProc.Reset();

                // Missing email/password -> immediate validation failure, no Register call
                var calls0 = KaiZentralUserRegProc.RegisterCallCount;
                var noCreds = new AppSettings { Email = "", ConfigPath = tmpConfig };
                var missingCreds = RenewEngine.Run(noCreds, true, null, true, persistSettings: false);
                AssertTrue(!missingCreds.Ok, "missing creds not Ok");
                AssertTrue((missingCreds.Message ?? "").IndexOf("Email or password", StringComparison.OrdinalIgnoreCase) >= 0,
                    "missing creds message");
                AssertEqual(calls0, KaiZentralUserRegProc.RegisterCallCount, "missing creds must not Register");

                // Pre-cancelled token -> Cancelled immediately
                var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                var withCreds = new AppSettings { Email = "x@y.com", ConfigPath = tmpConfig };
                withCreds.SetPassword("Secret1");
                var cancelRes = RenewEngine.Run(withCreds, true, "Secret1", true, cancelled.Token, persistSettings: false);
                AssertTrue(!cancelRes.Ok, "cancelled not Ok");
                AssertEqual("Cancelled", cancelRes.Message, "Cancelled message");
                AssertEqual(calls0, KaiZentralUserRegProc.RegisterCallCount, "cancelled must not Register");

                // Missing KAI_Properties.cfg -> HealthOnly clean failure
                var healthMissing = RenewEngine.HealthOnly(withCreds, persistSettings: false);
                AssertTrue(!healthMissing.Ok, "missing cfg HealthOnly fails");
                AssertTrue((healthMissing.Message ?? "").IndexOf("KAI_Properties", StringComparison.OrdinalIgnoreCase) >= 0
                    || (healthMissing.Message ?? "").IndexOf("Health failed", StringComparison.OrdinalIgnoreCase) >= 0,
                    "missing cfg message: " + healthMissing.Message);

                // Malformed cfg (too few lines)
                File.WriteAllText(Path.Combine(tmpConfig, "KAI_Properties.cfg"), "only-one-line\n");
                var healthBad = RenewEngine.HealthOnly(withCreds, persistSettings: false);
                AssertTrue(!healthBad.Ok, "malformed cfg HealthOnly fails");
                AssertTrue((healthBad.Message ?? "").IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0
                    || (healthBad.Message ?? "").IndexOf("Health failed", StringComparison.OrdinalIgnoreCase) >= 0,
                    "malformed cfg message: " + healthBad.Message);

                // Valid expires_in updates token fields
                File.WriteAllLines(Path.Combine(tmpConfig, "KAI_Properties.cfg"), new[]
                {
                    "version=1",
                    "lang=en",
                    "ENC:cid-tok",
                    "ENC:csec-tok"
                });
                var port = StartMockWebEamServer("{\"access_token\":\"tok-edge\",\"expires_in\":7200}", 200, out cts, out srvTask);
                withCreds.WebEamUrl = "http://127.0.0.1:" + port + "/token";
                KaiZentralUserRegProc.WebEamUrlOverride = withCreds.WebEamUrl;
                var beforeUtc = DateTime.UtcNow;
                var healthOk = RenewEngine.HealthOnly(withCreds, persistSettings: false);
                AssertTrue(healthOk.Ok && healthOk.SkippedRegister, "HealthOnly with mock token: " + healthOk.Message);
                AssertEqual(7200, withCreds.LastTokenExpiresInSeconds, "LastTokenExpiresInSeconds");
                AssertTrue(withCreds.LastTokenExpiryUtc.HasValue, "LastTokenExpiryUtc set");
                AssertTrue(withCreds.LastTokenExpiryUtc.Value >= beforeUtc.AddSeconds(7100),
                    "expiry ~7200s from now");
            }
            finally
            {
                if (cts != null) cts.Cancel();
                try { if (srvTask != null) srvTask.Wait(1500); } catch { }
                KaiZentralUserRegProc.Reset();
                AppSettings.StoreDirOverride = prevOverride;
                SafeDeleteDir(tmpStore);
                SafeDeleteDir(tmpConfig);
            }
        }

        private static void TestKeysExportLogic()
        {
            var rows = new List<KeysRowView>
            {
                new KeysRowView
                {
                    Time = new DateTime(2026, 11, 19, 10, 0, 0),
                    Vin = "WBA2", Km = 100, Paint = "P1", Upholstery = "U1",
                    ILevel = "I001", Reader = "R1", Local = "OK", Bmw = "OK",
                    Status = "OK", StatusRank = 3, Detail = "Uploaded"
                },
                new KeysRowView
                {
                    Time = new DateTime(2026, 11, 19, 12, 0, 0),
                    Vin = "WBA1", Km = 50, Paint = "P2", Upholstery = "U2",
                    ILevel = "I002", Reader = "R2", Local = "OK", Bmw = "FAIL",
                    Status = "Failed", StatusRank = 4, Detail = "403"
                }
            };
            var sorted = KeysExportLogic.Sort(rows, "vin", true, r => r);
            AssertEqual("WBA1", sorted[0].Vin, "vin asc first");
            AssertEqual("WBA2", sorted[1].Vin, "vin asc second");

            var csv = KeysExportLogic.Build(rows, "out.csv");
            AssertTrue(csv.IndexOf("VIN", StringComparison.Ordinal) >= 0, "csv has VIN header");
            AssertTrue(csv.IndexOf("WBA1", StringComparison.Ordinal) >= 0, "csv has WBA1");
            AssertTrue(KeysExportLogic.IsSortableColumn("time"), "time sortable");
            AssertTrue(!KeysExportLogic.IsSortableColumn("detail"), "detail not sortable");
            AssertTrue(!KeysExportLogic.DefaultAscending("time"), "time default desc");
            AssertTrue(KeysExportLogic.DefaultAscending("vin"), "vin default asc");
        }

        private static void TestDiagBundleAndHealthAlert()
        {
            AssertTrue(DiagBundleLogic.IsSecretElementName("PasswordProtectedBase64"), "password secret");
            AssertTrue(DiagBundleLogic.IsSecretElementName("NotifyPairingCode"), "pairing secret");
            AssertTrue(DiagBundleLogic.IsSecretElementName("FirebasePushSecretProtectedBase64"), "firebase secret");
            AssertEqual("a***@example.com", DiagBundleLogic.MaskEmail("alice@example.com"), "mask email");
            AssertEqual("https://x.firebasedatabase.app",
                DiagBundleLogic.StripUrlSecrets("https://x.firebasedatabase.app?auth=secret"), "strip url");

            var xml = "<?xml version=\"1.0\"?><AppSettings><Email>bob@bmw.com</Email>"
                + "<PasswordProtectedBase64>SECRETBLOB</PasswordProtectedBase64>"
                + "<NotifyPairingCode>PLAIN</NotifyPairingCode>"
                + "<FirebasePushUrl>https://db.firebasedatabase.app?auth=tok</FirebasePushUrl>"
                + "</AppSettings>";
            var redacted = DiagBundleLogic.RedactSettingsXml(xml);
            AssertTrue(redacted.IndexOf("SECRETBLOB", StringComparison.Ordinal) < 0, "password scrubbed");
            AssertTrue(redacted.IndexOf("PLAIN", StringComparison.Ordinal) < 0, "pairing scrubbed");
            AssertTrue(redacted.IndexOf("b***@bmw.com", StringComparison.Ordinal) >= 0, "email masked");
            AssertTrue(redacted.IndexOf("auth=tok", StringComparison.Ordinal) < 0, "url secret scrubbed");
            AssertTrue(redacted.IndexOf(DiagBundleLogic.Redacted, StringComparison.Ordinal) >= 0, "has ***");

            var now = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
            AssertTrue(HealthAlertLogic.ShouldAlertTokenExpiry(
                now, now.AddMinutes(10), 2, false, false, null,
                HealthAlertLogic.TokenExpiryWindow, HealthAlertLogic.AlertThrottle),
                "alert when token soon + failures");
            AssertTrue(!HealthAlertLogic.ShouldAlertTokenExpiry(
                now, now.AddHours(2), 2, false, false, null,
                HealthAlertLogic.TokenExpiryWindow, HealthAlertLogic.AlertThrottle),
                "no alert when token far");
            AssertTrue(!HealthAlertLogic.ShouldAlertTokenExpiry(
                now, now.AddMinutes(10), 0, false, false, null,
                HealthAlertLogic.TokenExpiryWindow, HealthAlertLogic.AlertThrottle),
                "no alert when not struggling");
            AssertEqual("failures=2, cooldown",
                HealthAlertLogic.DescribeReasons(2, true, false), "reasons text");
        }
    }
}
