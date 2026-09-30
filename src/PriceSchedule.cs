using System;
using System.Collections.Generic;
using System.Globalization;

namespace DeepSeekWidget {

    // DeepSeek 计费时段：高峰 / 空闲
    // 依据官方定价文档：北京时间周一至周五（不含中国法定节假日）
    // 09:00–12:00、14:00–18:00 为高峰时段；其余时间（含周末与法定节假日全天）为空闲时段。
    public enum PricePeriod {
        OffPeak,
        Peak
    }

    public sealed class PriceStatus {
        public PricePeriod Period;
        public DateTime NowBeijing;
        public DateTime NextSwitchBeijing;
        public TimeSpan Remaining;
        public bool HolidayDataKnown;
    }

    public static class PriceSchedule {

        // 距离下一次峰谷切换剩余不足该阈值时，界面开始按分钟倒计时
        public static readonly TimeSpan CountdownThreshold = TimeSpan.FromHours(2);

        // 高峰时段边界（北京时间当天时刻）
        static readonly TimeSpan PeakMorningStart = TimeSpan.FromHours(9);
        static readonly TimeSpan PeakMorningEnd = TimeSpan.FromHours(12);
        static readonly TimeSpan PeakAfternoonStart = TimeSpan.FromHours(14);
        static readonly TimeSpan PeakAfternoonEnd = TimeSpan.FromHours(18);

        // 中国法定节假日（仅放假日期；调休补班日不在此列，因为官方规则按“周末”判定空闲）
        static readonly HashSet<int> Holidays = BuildHolidays(new[] {
            // 2025
            "2025-01-01",
            "2025-01-28", "2025-01-29", "2025-01-30", "2025-01-31",
            "2025-02-01", "2025-02-02", "2025-02-03", "2025-02-04",
            "2025-04-04", "2025-04-05", "2025-04-06",
            "2025-05-01", "2025-05-02", "2025-05-03", "2025-05-04", "2025-05-05",
            "2025-05-31", "2025-06-01", "2025-06-02",
            "2025-10-01", "2025-10-02", "2025-10-03", "2025-10-04",
            "2025-10-05", "2025-10-06", "2025-10-07", "2025-10-08",
            // 2026
            "2026-01-01", "2026-01-02", "2026-01-03",
            "2026-02-15", "2026-02-16", "2026-02-17", "2026-02-18", "2026-02-19",
            "2026-02-20", "2026-02-21", "2026-02-22", "2026-02-23",
            "2026-04-04", "2026-04-05", "2026-04-06",
            "2026-05-01", "2026-05-02", "2026-05-03", "2026-05-04", "2026-05-05",
            "2026-06-19", "2026-06-20", "2026-06-21",
            "2026-09-25", "2026-09-26", "2026-09-27",
            "2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04",
            "2026-10-05", "2026-10-06", "2026-10-07"
        });

        static readonly int[] HolidayYears = { 2025, 2026 };

        // 北京时间（UTC+8，中国不实行夏令时）
        public static DateTime NowBeijing() {
            return DateTime.UtcNow.AddHours(8);
        }

        public static bool HolidayDataKnown(int year) {
            foreach (int y in HolidayYears) if (y == year) return true;
            return false;
        }

        // 当前年份的法定节假日数据是否已内置；未内置时周末仍正确，但节假日可能被当作工作日
        public static bool HolidayDataCurrent {
            get { return HolidayDataKnown(NowBeijing().Year); }
        }

        public static bool IsHoliday(DateTime beijing) {
            return Holidays.Contains(beijing.Year * 10000 + beijing.Month * 100 + beijing.Day);
        }

        public static bool IsPeak(DateTime beijing) {
            DayOfWeek w = beijing.DayOfWeek;
            if (w == DayOfWeek.Saturday || w == DayOfWeek.Sunday) return false;
            if (IsHoliday(beijing)) return false;
            TimeSpan t = beijing.TimeOfDay;
            return (t >= PeakMorningStart && t < PeakMorningEnd)
                || (t >= PeakAfternoonStart && t < PeakAfternoonEnd);
        }

        public static PriceStatus GetStatus() {
            return GetStatus(NowBeijing());
        }

        public static PriceStatus GetStatus(DateTime beijingNow) {
            bool peak = IsPeak(beijingNow);
            DateTime next = NextSwitch(beijingNow, peak);
            return new PriceStatus {
                Period = peak ? PricePeriod.Peak : PricePeriod.OffPeak,
                NowBeijing = beijingNow,
                NextSwitchBeijing = next,
                Remaining = next - beijingNow,
                HolidayDataKnown = HolidayDataKnown(beijingNow.Year)
            };
        }

        public static string PeriodName(PricePeriod p) {
            return p == PricePeriod.Peak ? "高峰时段" : "空闲时段";
        }

        // 下一切换提示：剩余 >=2 小时显示切换时刻，不足 2 小时显示按分钟倒计时
        public static string SwitchHint(PriceStatus st) {
            string target = st.Period == PricePeriod.Peak ? "空闲" : "高峰";
            if (st.Remaining < CountdownThreshold) {
                int mins = (int)Math.Ceiling(st.Remaining.TotalMinutes);
                if (mins < 0) mins = 0;
                string cd = mins >= 60
                    ? (mins / 60) + " 小时 " + (mins % 60) + " 分"
                    : mins + " 分钟";
                return "距转" + target + " " + cd;
            }
            DateTime ns = st.NextSwitchBeijing;
            string day = ns.Date == st.NowBeijing.Date ? "今天" : "周" + WeekdayCn(ns.DayOfWeek);
            return day + " " + ns.ToString("HH:mm") + " 后转" + target;
        }

        public static string WeekdayCn(DayOfWeek d) {
            switch (d) {
                case DayOfWeek.Monday: return "一";
                case DayOfWeek.Tuesday: return "二";
                case DayOfWeek.Wednesday: return "三";
                case DayOfWeek.Thursday: return "四";
                case DayOfWeek.Friday: return "五";
                case DayOfWeek.Saturday: return "六";
                default: return "日";
            }
        }

        // 下一次时段真正发生切换的时刻（跳过 00:00 这类不改变时段状态的边界）
        static DateTime NextSwitch(DateTime now, bool currentPeak) {
            TimeSpan[] boundaries = {
                TimeSpan.Zero,
                TimeSpan.FromHours(9),
                TimeSpan.FromHours(12),
                TimeSpan.FromHours(14),
                TimeSpan.FromHours(18)
            };
            for (int d = 0; d <= 16; d++) {
                DateTime day = now.Date.AddDays(d);
                foreach (TimeSpan b in boundaries) {
                    DateTime t = day + b;
                    if (t <= now) continue;
                    if (IsPeak(t) != currentPeak) return t;
                }
            }
            return now.Date.AddDays(1);
        }

        static HashSet<int> BuildHolidays(string[] dates) {
            var set = new HashSet<int>();
            foreach (string s in dates) {
                DateTime d;
                if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out d)) {
                    set.Add(d.Year * 10000 + d.Month * 100 + d.Day);
                }
            }
            return set;
        }
    }
}
