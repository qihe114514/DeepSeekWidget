using System;
using System.Collections.Generic;

namespace DeepSeekWidget {

    public sealed class CacheHitRate {
        public bool HasValue;
        public bool HasActivity;
        public double Percent;
        public int WindowMinutes;
    }

    public sealed class CacheHitTracker {
        const int RetainedMinutes = 30;

        sealed class Sample {
            public DateTime At;
            public long CacheHitTokens;
            public long CacheMissTokens;
        }

        readonly List<Sample> _samples = new List<Sample>();

        public void Record(DateTime at, long cacheHitTokens, long cacheMissTokens) {
            if (cacheHitTokens < 0 || cacheMissTokens < 0) return;

            if (_samples.Count > 0) {
                Sample last = _samples[_samples.Count - 1];
                bool dateChanged = at.Date != last.At.Date;
                bool countersMovedBack = cacheHitTokens < last.CacheHitTokens
                    || cacheMissTokens < last.CacheMissTokens;
                if (dateChanged || countersMovedBack) _samples.Clear();
                else if (at <= last.At) {
                    last.CacheHitTokens = cacheHitTokens;
                    last.CacheMissTokens = cacheMissTokens;
                    return;
                }
            }

            _samples.Add(new Sample {
                At = at,
                CacheHitTokens = cacheHitTokens,
                CacheMissTokens = cacheMissTokens
            });
            _samples.RemoveAll(s => s.At < at - TimeSpan.FromMinutes(RetainedMinutes));
        }

        public CacheHitRate GetRate(int windowMinutes, DateTime now) {
            if (windowMinutes != 5 && windowMinutes != 10) {
                throw new ArgumentOutOfRangeException(nameof(windowMinutes));
            }

            var result = new CacheHitRate { WindowMinutes = windowMinutes };
            if (_samples.Count < 2) return result;

            int latestIndex = -1;
            for (int i = _samples.Count - 1; i >= 0; i--) {
                if (_samples[i].At <= now) {
                    latestIndex = i;
                    break;
                }
            }
            if (latestIndex < 1) return result;

            Sample latest = _samples[latestIndex];
            if (latest.At.Date != now.Date) return result;

            TimeSpan window = TimeSpan.FromMinutes(windowMinutes);
            DateTime cutoff = now - window;
            Sample baseline = null;
            for (int i = latestIndex - 1; i >= 0; i--) {
                if (_samples[i].At <= cutoff) {
                    baseline = _samples[i];
                    break;
                }
            }
            // 启动不足一个完整窗口时，按已有最早样本估算。
            if (baseline == null) baseline = _samples[0];
            if (baseline.At.Date != latest.At.Date) return result;

            TimeSpan span = latest.At - baseline.At;
            TimeSpan maxSpan = window + window + TimeSpan.FromMinutes(5);
            if (span <= TimeSpan.Zero || span > maxSpan) return result;

            long hit = latest.CacheHitTokens - baseline.CacheHitTokens;
            long miss = latest.CacheMissTokens - baseline.CacheMissTokens;
            if (hit < 0 || miss < 0) return result;

            long total = hit + miss;
            result.HasValue = true;
            result.HasActivity = total > 0;
            if (total > 0) result.Percent = hit * 100.0 / total;
            return result;
        }
    }
}
