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
    }
}
