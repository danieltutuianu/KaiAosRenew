using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using KaiAosNotifyClient;
using KaiAosRenew;

namespace KaiAos.Protocol.Tests
{
    internal static class Program
    {
        private static int _failed;

        private static int Main()
        {
            Console.WriteLine("KaiAos protocol regression tests");
            Console.WriteLine("================================");

            Run("Pairing crypto", TestPairingCrypto);
            Run("PeerSync crypto", TestPeerSyncCrypto);
            Run("NotifyMessage roundtrip", TestMessageRoundtrip);
            Run("Activity/register sync payloads", TestActivityRegisterSyncPayloads);
            Run("Codec edges + AosSync", TestCodecEdgesAndAosSync);
            Run("History/export/register contracts", TestHistoryExportRegisterContracts);
            Run("Golden wire fixtures", TestGoldenWire);
            Run("Loopback no-pairing", TestLoopbackNoPairing);
            Run("Loopback pairing OK", TestLoopbackPairingOk);
            Run("Loopback pairing reject", TestLoopbackPairingReject);
            Run("Loopback pairing code change", TestLoopbackPairingCodeChange);
            Run("NotifyClient watchdog reason", TestNotifyClientWatchdogReason);
            Run("NotifyClient empty pairing stops", TestLoopbackPairingRequiredStops);
            Run("NotifyServer InstanceGuid replace", TestServerInstanceGuidReplacesOld);
            Run("NotifyClient rapid reconnect race", TestClientRapidReconnectRace);
            Run("TcpUtf8LineReader chunks + limit", TestTcpUtf8LineReader);
            Run("UpdateFeedHelper version + timeout", TestUpdateFeedHelperTimeout);
            Run("NotifyServer ApplySettings lifecycle", TestNotifyServerApplySettingsLifecycle);
            Run("ServerSettings KV roundtrip", TestServerSettingsKvRoundtrip);
            Run("LanServerDiscovery TCP verify", TestLanServerDiscoveryTcpVerify);

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

        private static void AssertEqual(string expected, string actual, string label)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new Exception(label + ": expected '" + expected + "', got '" + actual + "'");
        }

        private static void AssertEqual(int expected, int actual, string label)
        {
            if (expected != actual)
                throw new Exception(label + ": expected " + expected + ", got " + actual);
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (port < 1024)
                port = 28000 + (Environment.TickCount % 5000);
            return port;
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            var deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline)
            {
                if (condition()) return true;
                Thread.Sleep(25);
            }
            return condition();
        }

        private static void TestPairingCrypto()
        {
            var code = "ABCD2345";
            var nonce = NotifyPairing.CreateNonce();
            AssertTrue(!string.IsNullOrEmpty(nonce), "nonce empty");
            var response = NotifyPairing.ComputeResponse(code, nonce);
            AssertTrue(NotifyPairing.VerifyResponse(code, nonce, response), "matching code should verify");
            AssertTrue(!NotifyPairing.VerifyResponse("WRONGCOD", nonce, response), "wrong code must reject");
            AssertTrue(!NotifyPairing.VerifyResponse(code, nonce, ""), "empty response must reject");
            AssertTrue(!NotifyPairing.VerifyResponse(code, nonce, null), "null response must reject");
        }

        private static void TestPeerSyncCrypto()
        {
            var secret = "TESTSECRET99";
            var tag = PeerSyncCrypto.ComputeAuthTag(secret);
            AssertTrue(!string.IsNullOrEmpty(tag), "auth tag empty");
            AssertTrue(PeerSyncCrypto.FixedTimeEquals(tag, PeerSyncCrypto.ComputeAuthTag(secret)), "auth tag stable");
            AssertTrue(!PeerSyncCrypto.FixedTimeEquals(tag, PeerSyncCrypto.ComputeAuthTag("OTHER")), "auth tag differs");

            var creds = new PeerCredsPayload
            {
                Email = "user@example.com",
                Password = "p@ss|with\\pipe",
                IntervalHours = 12,
                BlockAfter = 4
            };
            var enc = PeerSyncCrypto.EncryptCreds(secret, creds);
            AssertTrue(!string.IsNullOrEmpty(enc), "encrypt empty");
            PeerCredsPayload round;
            AssertTrue(PeerSyncCrypto.TryDecryptCreds(secret, enc, out round), "decrypt failed");
            AssertEqual(creds.Email, round.Email, "email");
            AssertEqual(creds.Password, round.Password, "password");
            AssertEqual(creds.IntervalHours, round.IntervalHours, "hours");
            AssertEqual(creds.BlockAfter, round.BlockAfter, "block");

            PeerCredsPayload bad;
            AssertTrue(!PeerSyncCrypto.TryDecryptCreds("WRONG", enc, out bad), "wrong secret must fail");
            AssertTrue(!PeerSyncCrypto.TryDecryptCreds(secret, "@@@@", out bad), "garbage must fail");

            // Tamper MAC/ciphertext
            var bytes = Convert.FromBase64String(enc);
            bytes[bytes.Length - 1] ^= 0x5A;
            var tampered = Convert.ToBase64String(bytes);
            AssertTrue(!PeerSyncCrypto.TryDecryptCreds(secret, tampered, out bad), "tampered mac must fail");

            var ack = new PeerAckPayload
            {
                Ok = false,
                RollbackAttempted = true,
                RollbackOk = true,
                Message = "auth fail"
            };
            var ackEnc = PeerSyncCrypto.EncryptAck(secret, ack);
            PeerAckPayload ackRound;
            AssertTrue(PeerSyncCrypto.TryDecryptAck(secret, ackEnc, out ackRound), "ack decrypt");
            AssertTrue(!ackRound.Ok, "ack ok");
            AssertTrue(ackRound.RollbackAttempted, "rollback attempted");
            AssertTrue(ackRound.RollbackOk, "rollback ok");
            AssertEqual("auth fail", ackRound.Message, "ack message");

            var status = new PeerStatusPayload
            {
                ServerName = "Shop-1",
                AppVersion = "1.2.23",
                TokenStatus = "OK",
                TokenExpiryUtc = "2026-11-18T12:00:00Z",
                ClientCount = 2,
                LastSyncOk = true,
                LastSyncUtc = "2026-11-18T11:00:00Z",
                LastSyncMsg = "ok"
            };
            var statusEnc = PeerSyncCrypto.EncryptStatus(secret, status);
            PeerStatusPayload statusRound;
            AssertTrue(PeerSyncCrypto.TryDecryptStatus(secret, statusEnc, out statusRound), "status decrypt");
            AssertEqual("Shop-1", statusRound.ServerName, "status name");
            AssertEqual(2, statusRound.ClientCount, "status clients");
            AssertTrue(statusRound.LastSyncOk, "status last ok");
            PeerStatusPayload statusBad;
            AssertTrue(!PeerSyncCrypto.TryDecryptStatus("WRONG", statusEnc, out statusBad), "status wrong secret");

            var cmd = new PeerCommandPayload
            {
                Type = PeerSyncLogic.CommandTypeUpdateAll,
                TargetVersion = "1.2.23",
                IncludeLocal = true
            };
            var cmdEnc = PeerSyncCrypto.EncryptCommand(secret, cmd);
            PeerCommandPayload cmdRound;
            AssertTrue(PeerSyncCrypto.TryDecryptCommand(secret, cmdEnc, out cmdRound), "cmd decrypt");
            AssertEqual(PeerSyncLogic.CommandTypeUpdateAll, cmdRound.Type, "cmd type");
            AssertEqual("1.2.23", cmdRound.TargetVersion, "cmd ver");
            AssertTrue(cmdRound.IncludeLocal, "cmd include local");
        }

        private static void TestMessageRoundtrip()
        {
            RoundtripKind("authChallenge", "nonce-base64-here");
            RoundtripKind("authResponse", "hmac-response");
            RoundtripKind("authOk", "ok");
            RoundtripKind("authFailed", "Invalid pairing code");
            RoundtripKind("clientHello", "1.2.21");
            RoundtripKind("footer", "ok|Connected|next|token");
            RoundtripKind("ping", "ping");
            RoundtripKind("pong", "pong");
            RoundtripKind("activityHistoryReq", "error");
            RoundtripKind("activityHistory", "at|error|msg");
            RoundtripKind("registerHistoryReq", "ok");
            RoundtripKind("registerHistory", "at|1|ok");
            RoundtripKind("activityClearReq", "");
            RoundtripKind("registerClearReq", "");
            RoundtripKind("activityExportReq", "2026-11-18");
            RoundtripKind("registerExportReq", "2026-11-18");
            RoundtripKind("activityExport", "line1\nline2");
            RoundtripKind("registerExport", "line1\nline2");
            RoundtripKind("register", "Registration OK");
            RoundtripKind("clientUpdate", "1.2.22");
            RoundtripKind("peerStatusSync", "id|Shop|Online|OK|1.2.23|2|seen|sync");
            RoundtripKind("keySearchReq", "WBA");
            RoundtripKind("keySearchSync", "vin|paint");
            RoundtripKind("clientUpdateAllReq", "");

            var withDetail = new NotifyMessage
            {
                V = 1,
                Kind = "activityHistoryReq",
                Level = "info",
                Title = "3",
                Text = "error",
                Detail = "token"
            };
            var detailLine = withDetail.ToJsonLine();
            var detailParsed = NotifyMessage.ParseLine(detailLine);
            AssertTrue(detailParsed != null, "detail parse null");
            AssertEqual("token", detailParsed.Detail, "detail needle");
        }

        private static void TestActivityRegisterSyncPayloads()
        {
            var activity = new List<ActivityWireRow>
            {
                new ActivityWireRow { At = new DateTime(2026, 11, 18, 10, 0, 0), Level = "error", Text = "FAIL: x" },
                new ActivityWireRow { At = new DateTime(2026, 11, 18, 10, 1, 0), Level = "info", Text = "hello|pipe" }
            };
            var aEnc = NotifyMessage.EncodeActivitySync(activity);
            var aParsed = NotifyMessage.ParseActivitySync(aEnc);
            AssertEqual(2, aParsed.Count, "activity rows");
            AssertEqual("error", aParsed[0].Level, "activity level");
            AssertEqual("hello|pipe", aParsed[1].Text, "activity pipe unescape");

            var register = new List<RegisterWireRow>
            {
                new RegisterWireRow { At = new DateTime(2026, 11, 18, 11, 0, 0), Kind = 1, Ok = true, Text = "Registration OK" },
                new RegisterWireRow { At = new DateTime(2026, 11, 18, 11, 1, 0), Kind = 0, Ok = false, Text = "fail|x" }
            };
            var rEnc = NotifyMessage.EncodeRegisterSync(register);
            var rParsed = NotifyMessage.ParseRegisterSync(rEnc);
            AssertEqual(2, rParsed.Count, "register rows");
            AssertEqual(1, rParsed[0].Kind, "register ok kind");
            AssertEqual("fail|x", rParsed[1].Text, "register pipe unescape");

            RoundtripKind("activityHistory", aEnc);
            RoundtripKind("registerSync", rEnc);

            AssertEqual("", NotifyMessage.EncodeActivitySync(null), "activity null encode");
            AssertEqual("", NotifyMessage.EncodeActivitySync(new List<ActivityWireRow>()), "activity empty encode");
            AssertEqual("", NotifyMessage.EncodeRegisterSync(null), "register null encode");
            AssertEqual(0, NotifyMessage.ParseActivitySync("").Count, "activity empty parse");
            AssertEqual(0, NotifyMessage.ParseActivitySync("not-a-row").Count, "activity malformed skip");
        }

        private static void TestCodecEdgesAndAosSync()
        {
            var pipeRow = new List<RegisterWireRow>
            {
                new RegisterWireRow { At = new DateTime(2026, 11, 18, 12, 0, 0), Kind = 2, Ok = false, Text = "a|b\\c" }
            };
            var enc = NotifyMessage.EncodeRegisterSync(pipeRow);
            var parsed = NotifyMessage.ParseRegisterSync(enc);
            AssertEqual(1, parsed.Count, "pipe row count");
            AssertEqual("a|b\\c", parsed[0].Text, "pipe+backslash roundtrip");
            AssertEqual(2, parsed[0].Kind, "kind 2");

            var aliases = NotifyMessage.ParseRegisterSync(
                "2026-11-18T12:00:00|ok|OK1\n2026-11-18T12:00:01|info|INF\n2026-11-18T12:00:02|fail|F");
            AssertEqual(3, aliases.Count, "alias rows");
            AssertEqual(1, aliases[0].Kind, "ok alias");
            AssertEqual(2, aliases[1].Kind, "info alias");
            AssertEqual(0, aliases[2].Kind, "fail alias");

            var fallback = new List<RegisterWireRow>
            {
                new RegisterWireRow { At = DateTime.Now, Kind = 99, Ok = true, Text = "x" }
            };
            var fb = NotifyMessage.ParseRegisterSync(NotifyMessage.EncodeRegisterSync(fallback));
            AssertEqual(1, fb.Count, "kind clamp count");
            AssertEqual(1, fb[0].Kind, "Kind outside 0-2 uses Ok");

            int hours;
            string email, pass;
            bool? force;
            int? block;
            AssertTrue(NotifyMessage.TryParseAosSync("6|a@b.c|secret", out hours, out email, out pass, out force, out block), "aos 3");
            AssertEqual(6, hours, "aos hours");
            AssertEqual("a@b.c", email, "aos email");
            AssertEqual("secret", pass, "aos pass");
            AssertTrue(force == null, "aos force null");

            AssertTrue(NotifyMessage.TryParseAosSync("8|u|p|1", out hours, out email, out pass, out force, out block), "aos 4");
            AssertTrue(force == true, "aos force true");

            AssertTrue(NotifyMessage.TryParseAosSync("4|u|p|0|99", out hours, out email, out pass, out force, out block), "aos 5 clamp");
            AssertTrue(block.HasValue && block.Value == 10, "blockAfter clamp to 10");

            AssertTrue(NotifyMessage.TryParseAosSync("4|u|p|0|0", out hours, out email, out pass, out force, out block), "aos block 0");
            AssertTrue(!block.HasValue, "blockAfter 0 ignored");
            AssertTrue(NotifyMessage.TryParseAosSync("4|u|p|0|3", out hours, out email, out pass, out force, out block), "aos block 3");
            AssertEqual(3, block.GetValueOrDefault(), "blockAfter 3");
        }

        private static void TestHistoryExportRegisterContracts()
        {
            var histReq = new NotifyMessage
            {
                V = 1,
                Kind = "registerHistoryReq",
                Level = "info",
                Title = "7",
                Text = "fail",
                Detail = "cfg"
            };
            var histParsed = NotifyMessage.ParseLine(histReq.ToJsonLine());
            AssertEqual("registerHistoryReq", histParsed.Kind, "reg hist kind");
            AssertEqual("7", histParsed.Title, "reg hist req id");
            AssertEqual("fail", histParsed.Text, "reg hist filter");
            AssertEqual("cfg", histParsed.Detail, "reg hist search");

            var actReq = new NotifyMessage
            {
                V = 1,
                Kind = "activityHistoryReq",
                Title = "2",
                Text = "error",
                Detail = "needle"
            };
            var actParsed = NotifyMessage.ParseLine(actReq.ToJsonLine());
            AssertEqual("needle", actParsed.Detail, "act hist search");

            var append = new NotifyMessage
            {
                V = 1,
                Kind = "register",
                Level = "1",
                Title = "KAI - AOS Renew",
                At = "2026-11-18T15:30:00",
                Text = "Registration OK"
            };
            var ap = NotifyMessage.ParseLine(append.ToJsonLine());
            AssertEqual("register", ap.Kind, "append kind");
            AssertEqual("1", ap.Level, "append level digit");
            AssertEqual("2026-11-18T15:30:00", ap.At, "append at");
            AssertEqual("Registration OK", ap.Text, "append text");

            var exportReq = new NotifyMessage
            {
                Kind = "activityExportReq",
                Text = "2026-11-18"
            };
            AssertEqual("2026-11-18", NotifyMessage.ParseLine(exportReq.ToJsonLine()).Text, "export day");

            var exportReply = new NotifyMessage
            {
                Kind = "registerExport",
                At = "2026-11-18",
                Text = "line\tbody"
            };
            var er = NotifyMessage.ParseLine(exportReply.ToJsonLine());
            AssertEqual("registerExport", er.Kind, "export reply kind");
            AssertEqual("2026-11-18", er.At, "export reply at");
            AssertTrue(er.Text.IndexOf("line", StringComparison.Ordinal) >= 0, "export body");
        }

        private static void RoundtripKind(string kind, string text)
        {
            var msg = new NotifyMessage
            {
                V = 1,
                Kind = kind,
                Level = "info",
                Title = "KAI - AOS Renew",
                Text = text
            };
            var line = msg.ToJsonLine();
            var parsed = NotifyMessage.ParseLine(line);
            AssertTrue(parsed != null, kind + " parse null");
            AssertEqual(1, parsed.V, kind + " V");
            AssertEqual(kind, parsed.Kind, kind + " Kind");
            AssertEqual(text, parsed.Text, kind + " Text");
        }

        private static void TestGoldenWire()
        {
            // Frozen shapes older peers still emit — must keep parsing.
            const string footerLine =
                "{\"v\":1,\"kind\":\"footer\",\"level\":\"info\",\"title\":\"KAI - AOS Renew\",\"text\":\"ok|Connected|—|—\"}";
            const string helloLine =
                "{\"v\":1,\"kind\":\"clientHello\",\"level\":\"info\",\"title\":\"KAI - AOS Renew - Client\",\"text\":\"1.2.19\\u001fguid-here\"}";
            const string challengeLine =
                "{\"v\":1,\"kind\":\"authChallenge\",\"level\":\"info\",\"title\":\"KAI - AOS Renew\",\"text\":\"dGVzdC1ub25jZQ==\"}";

            var footer = NotifyMessage.ParseLine(footerLine);
            AssertTrue(footer != null, "footer golden null");
            AssertEqual("footer", footer.Kind, "footer kind");
            AssertEqual(1, footer.V, "footer v");
            AssertTrue(footer.Text.StartsWith("ok|", StringComparison.Ordinal), "footer text");

            var hello = NotifyMessage.ParseLine(helloLine);
            AssertTrue(hello != null, "hello golden null");
            AssertEqual("clientHello", hello.Kind, "hello kind");
            AssertTrue(hello.Text.IndexOf("1.2.19", StringComparison.Ordinal) >= 0, "hello version");

            var challenge = NotifyMessage.ParseLine(challengeLine);
            AssertTrue(challenge != null, "challenge golden null");
            AssertEqual("authChallenge", challenge.Kind, "challenge kind");
            AssertTrue(!string.IsNullOrEmpty(challenge.Text), "challenge nonce");
        }

        private static void TestLoopbackNoPairing()
        {
            var server = NotifyServer.Instance;
            var port = FreePort();
            NotifyClient client = null;
            var connected = new ManualResetEvent(false);
            try
            {
                server.Stop();
                server.PairingCode = "";
                server.Start(port);
                AssertTrue(server.IsRunning, "server not running on " + port);

                client = new NotifyClient();
                client.Connected += () => connected.Set();
                client.Connect("127.0.0.1", port, "");

                // No-pairing: client raises Connected only after the first non-auth message.
                AssertTrue(WaitUntil(() => server.ClientCount >= 1, 5000), "server ClientCount < 1");
                server.Broadcast(new NotifyMessage
                {
                    Kind = "footer",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = "ok|Connected|—|—"
                });
                AssertTrue(connected.WaitOne(8000), "client Connected timeout (no pairing)");

                var sent = client.Send(new NotifyMessage
                {
                    Kind = "clientHello",
                    Level = "info",
                    Title = "KAI - AOS Renew - Client",
                    Text = "1.2.21\u001ftest-guid-no-pair"
                });
                AssertTrue(sent, "clientHello send failed");
                Thread.Sleep(200);
            }
            finally
            {
                if (client != null)
                {
                    try { client.Disconnect(); } catch { }
                    try { client.Dispose(); } catch { }
                }
                try { server.Stop(); } catch { }
                server.PairingCode = "";
            }
        }

        private static void TestLoopbackPairingOk()
        {
            var server = NotifyServer.Instance;
            var port = FreePort();
            const string code = "TESTPAIR1";
            NotifyClient client = null;
            var connected = new ManualResetEvent(false);
            try
            {
                server.Stop();
                server.PairingCode = code;
                server.Start(port);
                AssertTrue(server.IsRunning, "server not running on " + port);

                client = new NotifyClient();
                client.Connected += () => connected.Set();
                client.Connect("127.0.0.1", port, code);

                AssertTrue(connected.WaitOne(8000), "client Connected timeout (pairing OK)");
                AssertTrue(WaitUntil(() => server.ClientCount >= 1, 3000), "server ClientCount < 1 after pairing");
            }
            finally
            {
                if (client != null)
                {
                    try { client.Disconnect(); } catch { }
                    try { client.Dispose(); } catch { }
                }
                try { server.Stop(); } catch { }
                server.PairingCode = "";
            }
        }

        private static void TestLoopbackPairingReject()
        {
            var server = NotifyServer.Instance;
            var port = FreePort();
            NotifyClient client = null;
            var authFailed = new ManualResetEvent(false);
            string failReason = null;
            try
            {
                server.Stop();
                server.PairingCode = "GOODCODE1";
                server.Start(port);
                AssertTrue(server.IsRunning, "server not running on " + port);

                client = new NotifyClient();
                client.AuthFailed += reason =>
                {
                    failReason = reason;
                    authFailed.Set();
                };
                client.Connect("127.0.0.1", port, "BADCODE99");

                AssertTrue(authFailed.WaitOne(8000), "AuthFailed timeout");
                AssertTrue(!string.IsNullOrEmpty(failReason), "AuthFailed reason empty");
                AssertTrue(!client.WantConnect, "WantConnect should be false after auth fail");
                AssertTrue(WaitUntil(() => server.ClientCount == 0, 3000), "server should have no authenticated clients");
            }
            finally
            {
                if (client != null)
                {
                    try { client.Disconnect(); } catch { }
                    try { client.Dispose(); } catch { }
                }
                try { server.Stop(); } catch { }
                server.PairingCode = "";
            }
        }

        private static void TestLoopbackPairingCodeChange()
        {
            var server = NotifyServer.Instance;
            var port = FreePort();
            const string codeA = "CODEAAAA";
            const string codeB = "CODEBBBB";
            NotifyClient client = null;
            NotifyClient clientNew = null;
            var connected = new ManualResetEvent(false);
            var disconnected = new ManualResetEvent(false);
            var authFailed = new ManualResetEvent(false);
            var reconnected = new ManualResetEvent(false);
            try
            {
                server.Stop();
                server.PairingCode = codeA;
                server.Start(port);
                AssertTrue(server.IsRunning, "server not running on " + port);

                client = new NotifyClient();
                client.Connected += () => connected.Set();
                client.Disconnected += () => disconnected.Set();
                client.Connect("127.0.0.1", port, codeA);
                AssertTrue(connected.WaitOne(8000), "client Connected timeout (code A)");
                AssertTrue(WaitUntil(() => server.ClientCount >= 1, 3000), "server ClientCount < 1");

                // Rotating pairing code must drop existing sessions immediately.
                server.ApplySettings(true, port, codeB);
                AssertTrue(disconnected.WaitOne(5000) || WaitUntil(() => server.ClientCount == 0, 5000),
                    "client should disconnect after pairing code change");
                AssertTrue(WaitUntil(() => server.ClientCount == 0, 3000),
                    "server ClientCount should be 0 after pairing rotate");

                // Old code must fail re-auth.
                client.Dispose();
                client = null;
                clientNew = new NotifyClient();
                clientNew.AuthFailed += _ => authFailed.Set();
                clientNew.Connect("127.0.0.1", port, codeA);
                AssertTrue(authFailed.WaitOne(8000), "old pairing code should AuthFailed");
                AssertTrue(WaitUntil(() => server.ClientCount == 0, 3000),
                    "old code must not authenticate");

                // New code should succeed.
                clientNew.Dispose();
                clientNew = new NotifyClient();
                clientNew.Connected += () => reconnected.Set();
                clientNew.Connect("127.0.0.1", port, codeB);
                AssertTrue(reconnected.WaitOne(8000), "new pairing code should connect");
                AssertTrue(WaitUntil(() => server.ClientCount >= 1, 3000),
                    "server ClientCount < 1 after new code");
            }
            finally
            {
                if (client != null)
                {
                    try { client.Disconnect(); } catch { }
                    try { client.Dispose(); } catch { }
                }
                if (clientNew != null)
                {
                    try { clientNew.Disconnect(); } catch { }
                    try { clientNew.Dispose(); } catch { }
                }
                try { server.Stop(); } catch { }
                server.PairingCode = "";
            }
        }

        private static void TestNotifyClientWatchdogReason()
        {
            var server = NotifyServer.Instance;
            var port = FreePort();
            NotifyClient client = null;
            var connected = new ManualResetEvent(false);
            var reconnected = new ManualResetEvent(false);
            var disconnected = new ManualResetEvent(false);
            var connectCount = 0;
            try
            {
                server.Stop();
                server.PairingCode = "";
                server.Start(port);
                AssertTrue(server.IsRunning, "server not running on " + port);

                client = new NotifyClient();
                client.Connected += () =>
                {
                    var n = Interlocked.Increment(ref connectCount);
                    if (n == 1) connected.Set();
                    else reconnected.Set();
                };
                client.Disconnected += () => disconnected.Set();
                client.Connect("127.0.0.1", port, "");

                AssertTrue(WaitUntil(() => server.ClientCount >= 1, 5000), "server ClientCount < 1");
                server.Broadcast(new NotifyMessage
                {
                    Kind = "footer",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = "ok|Connected|—|—"
                });
                AssertTrue(connected.WaitOne(8000), "client Connected timeout");

                // Ownership-safe watchdog path must preserve the reason (not overwritten by ReadLoop).
                client.TriggerWatchdogDisconnectForTests();
                AssertTrue(disconnected.WaitOne(5000), "watchdog Disconnected timeout");
                AssertEqual("Server ping timeout", client.LastDisconnectReason, "watchdog reason");

                // Replacing the socket (Connect again) must leave a live session —
                // an unwinding old ReadLoop must not tear down the new TCP client.
                disconnected.Reset();
                client.Connect("127.0.0.1", port, "");
                AssertTrue(WaitUntil(() => server.ClientCount >= 1, 8000), "server ClientCount after reconnect");
                server.Broadcast(new NotifyMessage
                {
                    Kind = "footer",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = "ok|Connected|—|—"
                });
                AssertTrue(reconnected.WaitOne(8000), "client reconnected timeout");
                Thread.Sleep(500);
                AssertTrue(client.IsConnected, "new socket must stay connected after old ReadLoop unwind");
                AssertTrue(server.ClientCount >= 1, "server must keep the new session");
            }
            finally
            {
                if (client != null)
                {
                    try { client.Disconnect(); } catch { }
                    try { client.Dispose(); } catch { }
                }
                try { server.Stop(); } catch { }
                server.PairingCode = "";
            }
        }

        private static void TestLoopbackPairingRequiredStops()
        {
            var server = NotifyServer.Instance;
            var port = FreePort();
            NotifyClient client = null;
            var authFailed = new ManualResetEvent(false);
            string failReason = null;
            try
            {
                server.Stop();
                server.PairingCode = "REQCODE1";
                server.Start(port);
                AssertTrue(server.IsRunning, "server not running");

                client = new NotifyClient();
                client.AuthFailed += r => { failReason = r; authFailed.Set(); };
                // Empty pairing code when server requires pairing must stop immediately with PAIRING_REQUIRED.
                client.Connect("127.0.0.1", port, "");

                AssertTrue(authFailed.WaitOne(8000), "PAIRING_REQUIRED timeout");
                AssertEqual("PAIRING_REQUIRED", failReason, "pairing required reason");
                AssertTrue(!client.WantConnect, "WantConnect false on PAIRING_REQUIRED");
                AssertEqual(0, server.ClientCount, "server ClientCount 0");
            }
            finally
            {
                if (client != null)
                {
                    try { client.Disconnect(); } catch { }
                    try { client.Dispose(); } catch { }
                }
                try { server.Stop(); } catch { }
                server.PairingCode = "";
            }
        }

        private static void TestServerInstanceGuidReplacesOld()
        {
            var server = NotifyServer.Instance;
            var port = FreePort();
            NotifyClient c1 = null;
            NotifyClient c2 = null;
            var c1Conn = new ManualResetEvent(false);
            var c2Conn = new ManualResetEvent(false);
            var c1Disc = new ManualResetEvent(false);
            Action<int, NotifyMessage> onMsg = (id, msg) =>
            {
                if (msg != null && string.Equals(msg.Kind, "clientHello", StringComparison.OrdinalIgnoreCase))
                    server.ApplyClientHello(id, msg.Text, msg.Detail);
            };
            try
            {
                server.Stop();
                server.PairingCode = "";
                server.MessageFromClient += onMsg;
                server.Start(port);

                c1 = new NotifyClient();
                c1.Connected += () => c1Conn.Set();
                c1.Disconnected += () => c1Disc.Set();
                c1.Connect("127.0.0.1", port, "");
                AssertTrue(WaitUntil(() => server.ClientCount >= 1, 5000), "c1 accepted");
                server.Broadcast(new NotifyMessage { Kind = "ping", Text = "" });
                AssertTrue(c1Conn.WaitOne(5000), "c1 connected");
                c1.Send(new NotifyMessage
                {
                    Kind = "clientHello",
                    Text = "1.2.21",
                    Detail = "same-guid-123"
                });
                Thread.Sleep(150);

                c2 = new NotifyClient();
                c2.Connected += () => c2Conn.Set();
                c2.Connect("127.0.0.1", port, "");
                AssertTrue(WaitUntil(() => server.ClientCount >= 2, 5000) || c2Conn.WaitOne(5000), "c2 accepted");
                server.Broadcast(new NotifyMessage { Kind = "ping", Text = "" });
                AssertTrue(c2Conn.WaitOne(5000), "c2 connected");
                c2.Send(new NotifyMessage
                {
                    Kind = "clientHello",
                    Text = "1.2.21",
                    Detail = "same-guid-123"
                });

                AssertTrue(c1Disc.WaitOne(5000), "old duplicate client should be disconnected");
                AssertTrue(WaitUntil(() => server.ClientCount == 1, 3000), "server should keep exactly 1 client");
            }
            finally
            {
                server.MessageFromClient -= onMsg;
                if (c1 != null) { try { c1.Disconnect(); } catch { } try { c1.Dispose(); } catch { } }
                if (c2 != null) { try { c2.Disconnect(); } catch { } try { c2.Dispose(); } catch { } }
                try { server.Stop(); } catch { }
            }
        }

        private static void TestClientRapidReconnectRace()
        {
            var server = NotifyServer.Instance;
            var port = FreePort();
            NotifyClient client = null;
            var connected = new ManualResetEvent(false);
            try
            {
                server.Stop();
                server.PairingCode = "RAPID123";
                server.Start(port);

                client = new NotifyClient();
                client.Connect("127.0.0.1", port, "RAPID123");
                Thread.Sleep(50);
                // Rapidly replace socket mid-handshake — must settle connected cleanly.
                client.Connected += () => connected.Set();
                client.Connect("127.0.0.1", port, "RAPID123");

                AssertTrue(connected.WaitOne(8000), "rapid reconnect connected timeout");
                Thread.Sleep(300);
                AssertTrue(client.IsConnected, "client should remain connected after rapid reconnect");
                AssertTrue(WaitUntil(() => server.ClientCount >= 1, 3000), "server ClientCount >= 1");
            }
            finally
            {
                if (client != null) { try { client.Disconnect(); } catch { } try { client.Dispose(); } catch { } }
                try { server.Stop(); } catch { }
                server.PairingCode = "";
            }
        }

        private static void TestTcpUtf8LineReader()
        {
            var reader = new TcpUtf8LineReader();
            var lines = new List<string>();
            // Multi-byte UTF-8 character split across two buffers + CRLF.
            var payload = Encoding.UTF8.GetBytes("salut \u0219i test\r\nline2\n");
            var split = 8;
            AssertTrue(reader.Append(payload, 0, split, l => lines.Add(l)), "chunk 1");
            AssertTrue(reader.Append(payload, split, payload.Length - split, l => lines.Add(l)), "chunk 2");
            AssertEqual(2, lines.Count, "split line count");
            AssertEqual("salut \u0219i test", lines[0], "utf8 line 1");
            AssertEqual("line2", lines[1], "line 2");

            // Oversized line exceeding DefaultMaxLineChars must return false (guard against OOM).
            var readerMax = new TcpUtf8LineReader(1024);
            var big = new byte[1040];
            for (var i = 0; i < big.Length; i++) big[i] = (byte)'A';
            AssertTrue(!readerMax.Append(big, 0, big.Length, _ => { }), "oversized line must fail");
        }

        private static void TestUpdateFeedHelperTimeout()
        {
            AssertTrue(UpdateFeedHelper.IsNewerVersion("1.2.23", "1.2.22"), "newer version");
            AssertTrue(!UpdateFeedHelper.IsNewerVersion("1.2.22", "1.2.22"), "same version");
            AssertTrue(!UpdateFeedHelper.IsNewerVersion("1.2.21", "1.2.22"), "older version");
            AssertTrue(UpdateFeedHelper.IsNewerVersion("1.2.23-beta+1", "1.2.22"), "prerelease stripped");

            // Connect to a silent TcpListener (accepts TCP, never sends HTTP response) -> TimeoutWebClient must abort near 1s.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accepted = new List<TcpClient>();
            var stop = false;
            var acceptThread = new Thread(() =>
            {
                while (!stop)
                {
                    try
                    {
                        if (listener.Pending())
                            accepted.Add(listener.AcceptTcpClient());
                        else
                            Thread.Sleep(20);
                    }
                    catch { break; }
                }
            }) { IsBackground = true };
            acceptThread.Start();

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var threw = false;
                try
                {
                    using (var wc = new UpdateFeedHelper.TimeoutWebClient(1000))
                        wc.DownloadString("http://127.0.0.1:" + port + "/releases.json");
                }
                catch (WebException)
                {
                    threw = true;
                }
                sw.Stop();
                AssertTrue(threw, "TimeoutWebClient should throw WebException on hung server");
                AssertTrue(sw.ElapsedMilliseconds < 8000, "TimeoutWebClient took too long: " + sw.ElapsedMilliseconds + "ms");
            }
            finally
            {
                stop = true;
                try { listener.Stop(); } catch { }
                foreach (var tcp in accepted)
                {
                    try { tcp.Close(); } catch { }
                }
            }
        }

        private static void TestNotifyServerApplySettingsLifecycle()
        {
            var server = NotifyServer.Instance;
            var portA = FreePort();
            var portB = FreePort();
            if (portB == portA) portB = FreePort();
            NotifyClient client = null;
            var connected = new ManualResetEvent(false);
            var disconnected = new ManualResetEvent(false);
            try
            {
                server.Stop();
                server.PairingCode = "";
                server.ApplySettings(true, portA, "");
                AssertTrue(server.IsRunning, "server running after enable");
                AssertEqual(portA, server.Port, "bound port A");

                client = new NotifyClient();
                client.Connected += () => connected.Set();
                client.Disconnected += () => disconnected.Set();
                client.Connect("127.0.0.1", portA, "");
                AssertTrue(WaitUntil(() => server.ClientCount >= 1, 5000), "client authenticated");
                server.Broadcast(new NotifyMessage
                {
                    Kind = "footer",
                    Level = "info",
                    Title = "KAI - AOS Renew",
                    Text = "ok|Connected|—|—"
                });
                AssertTrue(connected.WaitOne(8000), "client Connected");

                AssertTrue(!server.SendTo(99999, new NotifyMessage { Kind = "ping", Text = "x" }),
                    "SendTo unknown clientId returns false");

                // Rebind to new port must drop clients and listen on portB
                server.ApplySettings(true, portB, "REBIND01");
                AssertTrue(disconnected.WaitOne(5000) || WaitUntil(() => server.ClientCount == 0, 5000),
                    "clients disconnected on rebind");
                AssertTrue(server.IsRunning, "still running after rebind");
                AssertEqual(portB, server.Port, "bound port B");
                AssertEqual(0, server.ClientCount, "no clients after rebind");

                // Stop via ApplySettings(false)
                server.ApplySettings(false, portB, "REBIND01");
                AssertTrue(!server.IsRunning, "stopped after ApplySettings(false)");
                AssertTrue(!server.SendTo(1, new NotifyMessage { Kind = "ping", Text = "x" }),
                    "SendTo while stopped returns false");
            }
            finally
            {
                if (client != null)
                {
                    try { client.Disconnect(); } catch { }
                    try { client.Dispose(); } catch { }
                }
                try { server.Stop(); } catch { }
                server.PairingCode = "";
            }
        }

        private static void TestServerSettingsKvRoundtrip()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Email", "a@b.com" },
                { "Password", "p|a\\ss\nequals=x" },
                { "Empty", "" },
                { "Interval", "2" },
                { "Note", "line1\nline2" }
            };
            var enc = NotifyMessage.EncodeKvMap(map);
            var parsed = NotifyMessage.ParseKvMap(enc);
            AssertEqual(map.Count, parsed.Count, "kv count");
            AssertEqual(map["Email"], parsed["Email"], "email");
            AssertEqual(map["Password"], parsed["Password"], "password specials");
            AssertEqual("", parsed["Empty"], "empty value");
            AssertEqual("2", parsed["Interval"], "interval");
            AssertEqual(map["Note"], parsed["Note"], "newline value");

            AssertEqual("", NotifyMessage.EncodeKvMap(null), "null map");
            AssertEqual(0, NotifyMessage.ParseKvMap("").Count, "empty parse");
            AssertEqual(0, NotifyMessage.ParseKvMap("no-pipe-here").Count, "malformed skipped");
        }

        private static void TestLanServerDiscoveryTcpVerify()
        {
            var server = NotifyServer.Instance;
            var port = FreePort();
            try
            {
                server.Stop();
                server.PairingCode = "";
                server.Start(port);
                AssertTrue(server.IsRunning, "server for discovery");

                bool pairingRequired;
                AssertTrue(LanServerDiscovery.TryTcpVerify("127.0.0.1", port, out pairingRequired),
                    "open server should verify");
                AssertTrue(!pairingRequired, "no pairing required");

                // UDP discover response from NotifyServer
                using (var udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = 1500;
                    var payload = Encoding.ASCII.GetBytes(NotifyServer.UdpDiscoverRequest);
                    udp.Send(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, port));
                    IPEndPoint remote = null;
                    var data = udp.Receive(ref remote);
                    var text = Encoding.ASCII.GetString(data ?? new byte[0]);
                    AssertTrue(text.StartsWith(NotifyServer.UdpDiscoverResponsePrefix, StringComparison.Ordinal),
                        "UDP discover response prefix: " + text);
                }

                server.ApplySettings(true, port, "PAIRDISC1");
                AssertTrue(LanServerDiscovery.TryTcpVerify("127.0.0.1", port, out pairingRequired),
                    "pairing server still verifies");
                AssertTrue(pairingRequired, "pairing required flag");

                AssertTrue(!LanServerDiscovery.TryTcpVerify("127.0.0.1", FreePort(), out pairingRequired),
                    "closed port must fail verify");
            }
            finally
            {
                try { server.Stop(); } catch { }
                server.PairingCode = "";
            }
        }
    }
}
