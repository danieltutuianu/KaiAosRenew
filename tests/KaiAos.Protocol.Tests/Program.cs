using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
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
            Run("NotifyMessage roundtrip", TestMessageRoundtrip);
            Run("Activity/register sync payloads", TestActivityRegisterSyncPayloads);
            Run("Codec edges + AosSync", TestCodecEdgesAndAosSync);
            Run("History/export/register contracts", TestHistoryExportRegisterContracts);
            Run("Golden wire fixtures", TestGoldenWire);
            Run("Loopback no-pairing", TestLoopbackNoPairing);
            Run("Loopback pairing OK", TestLoopbackPairingOk);
            Run("Loopback pairing reject", TestLoopbackPairingReject);

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
    }
}
