using System;
using System.Collections.Generic;

namespace DeepSeekWidget.Tests {

    static class Program {
        static int _failed;

        static int Main() {
            Run("5 minute window uses the exact cutoff sample", FiveMinuteWindowUsesCutoff);
            Run("10 minute window falls back to the oldest available sample", TenMinuteWindowFallsBack);
            Run("unchanged counters report no activity", UnchangedCountersMeanNoActivity);
            Run("cross-day history resets at midnight", CrossDayResets);
            Run("counter rollback resets the baseline", CounterRollbackResets);
            Run("stale samples caused by sleep are rejected", StaleSamplesRejected);
            Run("only one sample is not enough", OneSampleIsNotEnough);
            Run("invalid window is rejected", InvalidWindowRejected);            Run("theme preference accepts only supported values", ThemePreferenceParsing);
            Run("token-like storage keys outrank generic values", TokenLikeStorageKeyWins);
            Run("duplicate login candidates are collapsed", DuplicateLoginCandidatesCollapse);

            Console.WriteLine(_failed == 0
                ? "PASS: all CacheHitTracker tests passed"
                : "FAIL: " + _failed + " test(s) failed");
            return _failed == 0 ? 0 : 1;
        }

        static void Run(string name, Action test) {
            try {
                test();
                Console.WriteLine("PASS: " + name);
            } catch (Exception ex) {
                _failed++;
                Console.WriteLine("FAIL: " + name + " -> " + ex.Message);
            }
        }

        static CacheHitTracker Tracker(out DateTime now) {
            now = new DateTime(2026, 9, 30, 12, 0, 0);
            return new CacheHitTracker();
        }

        static void FiveMinuteWindowUsesCutoff() {
            DateTime now;
            CacheHitTracker tracker = Tracker(out now);
            tracker.Record(now.AddMinutes(-8), 1000, 500);
            tracker.Record(now.AddMinutes(-5), 1200, 600);
            tracker.Record(now, 1500, 700);

            CacheHitRate rate = tracker.GetRate(5, now);
            Equal(true, rate.HasValue, "HasValue");
            Equal(true, rate.HasActivity, "HasActivity");
            Near(75.0, rate.Percent, 0.0001, "Percent");
        }

        static void TenMinuteWindowFallsBack() {
            DateTime now;
            CacheHitTracker tracker = Tracker(out now);
            tracker.Record(now.AddMinutes(-12), 1000, 500);
            tracker.Record(now.AddMinutes(-6), 1300, 600);
            tracker.Record(now, 1800, 700);

            CacheHitRate rate = tracker.GetRate(10, now);
            Equal(true, rate.HasValue, "HasValue");
            Near(80.0, rate.Percent, 0.0001, "Percent");
        }

        static void UnchangedCountersMeanNoActivity() {
            DateTime now;
            CacheHitTracker tracker = Tracker(out now);
            tracker.Record(now.AddMinutes(-5), 1000, 500);
            tracker.Record(now, 1000, 500);

            CacheHitRate rate = tracker.GetRate(5, now);
            Equal(true, rate.HasValue, "HasValue");
            Equal(false, rate.HasActivity, "HasActivity");
        }

        static void CrossDayResets() {
            DateTime now;
            CacheHitTracker tracker = Tracker(out now);
            tracker.Record(now.Date.AddDays(-1).AddHours(23), 1000, 500);
            tracker.Record(now, 1300, 600);

            CacheHitRate rate = tracker.GetRate(5, now);
            Equal(false, rate.HasValue, "HasValue");
        }

        static void CounterRollbackResets() {
            DateTime now;
            CacheHitTracker tracker = Tracker(out now);
            tracker.Record(now.AddMinutes(-5), 1000, 500);
            tracker.Record(now.AddMinutes(-3), 900, 400);
            tracker.Record(now, 1200, 600);

            CacheHitRate rate = tracker.GetRate(5, now);
            Equal(true, rate.HasValue, "HasValue");
            Near(60.0, rate.Percent, 0.0001, "Percent");
        }

        static void StaleSamplesRejected() {
            DateTime now;
            CacheHitTracker tracker = Tracker(out now);
            tracker.Record(now.AddMinutes(-30), 1000, 500);
            tracker.Record(now, 1600, 700);

            CacheHitRate rate = tracker.GetRate(5, now);
            Equal(false, rate.HasValue, "HasValue");
        }

        static void OneSampleIsNotEnough() {
            DateTime now;
            CacheHitTracker tracker = Tracker(out now);
            tracker.Record(now, 1300, 600);

            CacheHitRate rate = tracker.GetRate(5, now);
            Equal(false, rate.HasValue, "HasValue");
        }

        static void InvalidWindowRejected() {
            DateTime now;
            CacheHitTracker tracker = Tracker(out now);
            Throws<ArgumentOutOfRangeException>(() => tracker.GetRate(7, now));
        }

        static void ThemePreferenceParsing() {
            Equal(ThemePreference.System, ThemePreferenceValues.Parse("system"), "system");
            Equal(ThemePreference.Light, ThemePreferenceValues.Parse("light"), "light");
            Equal(ThemePreference.Dark, ThemePreferenceValues.Parse("dark"), "dark");
            Equal(ThemePreference.System, ThemePreferenceValues.Parse("unexpected"), "fallback");
            Equal("dark", ThemePreferenceValues.ToConfig(ThemePreference.Dark), "serialize");
        }

        static void TokenLikeStorageKeyWins() {
            const string json = "{\"found\":[{\"key\":\"analytics.visitor\",\"value\":\"abcdefghijklmnop\"},{\"key\":\"localStorage.userToken\",\"value\":\"eyJhbGciOiJIUzI1NiJ9.payload.signature\"}],\"keys\":[]}";
            LoginCandidate candidate = LoginCredentialDetector.SelectBest(json);
            Equal("eyJhbGciOiJIUzI1NiJ9.payload.signature", candidate.Token, "Token");
        }

        static void DuplicateLoginCandidatesCollapse() {
            const string json = "{\"found\":[{\"key\":\"localStorage.token\",\"value\":\"same-token-value\"},{\"key\":\"cookie.token\",\"value\":\"same-token-value\"}],\"keys\":[]}";
            var candidates = LoginCredentialDetector.ParseCandidates(json);
            Equal(1, candidates.Count, "Count");
        }

        static void Equal<T>(T expected, T actual, string field) {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) {
                throw new InvalidOperationException(field + ": expected " + expected + ", got " + actual);
            }
        }

        static void Near(double expected, double actual, double tolerance, string field) {
            if (Math.Abs(expected - actual) > tolerance) {
                throw new InvalidOperationException(field + ": expected " + expected + ", got " + actual);
            }
        }

        static void Throws<TException>(Action action) where TException : Exception {
            try {
                action();
            } catch (TException) {
                return;
            } catch (Exception ex) {
                throw new InvalidOperationException("expected " + typeof(TException).Name + ", got " + ex.GetType().Name);
            }
            throw new InvalidOperationException("expected " + typeof(TException).Name + ", but no exception was thrown");
        }
    }
}




