using System;
using System.Collections.Generic;
using System.Linq;

namespace Insomnia
{
    internal enum InactiveReason { None, Manual, Wifi, WifiMissing, Schedule }

    internal sealed class AppStatus
    {
        public bool IsActive { get; private set; }
        public InactiveReason Reason { get; private set; }
        public string DetectedSsid { get; private set; }

        public static AppStatus Calculate(AppConfiguration configuration, IEnumerable<string> availableSsids, DateTime localNow)
        {
            if (!configuration.ManuallyEnabled) return New(false, InactiveReason.Manual, null);

            var matching = availableSsids.Where(network =>
                configuration.ExcludedSsids.Contains(network, StringComparer.OrdinalIgnoreCase)).ToList();

            if (configuration.WifiMode == WifiRuleMode.BlockOnMatching)
            {
                if (matching.Count > 0) return New(false, InactiveReason.Wifi, matching[0]);
            }
            else
            {
                if (configuration.ExcludedSsids.Count > 0 && matching.Count == 0)
                    return New(false, InactiveReason.WifiMissing, null);
            }

            if (configuration.ScheduleEnabled && !IsInSchedule(localNow, configuration.ScheduleStart, configuration.ScheduleEnd, configuration.ScheduleDays))
                return New(false, InactiveReason.Schedule, null);

            return New(true, InactiveReason.None, null);
        }

        private static AppStatus New(bool active, InactiveReason reason, string ssid)
        {
            return new AppStatus { IsActive = active, Reason = reason, DetectedSsid = ssid };
        }

        internal static bool IsInSchedule(TimeSpan now, TimeSpan start, TimeSpan end)
        {
            if (start == end) return true;
            return start < end ? now >= start && now < end : now >= start || now < end;
        }

        internal static bool IsInSchedule(DateTime localNow, TimeSpan start, TimeSpan end, ScheduleDays days)
        {
            if (days == ScheduleDays.None) return false;
            TimeSpan nowTime = localNow.TimeOfDay;
            DayOfWeek today = localNow.DayOfWeek;

            if (start == end) return days.ContainsDay(today);

            if (start < end)
                return days.ContainsDay(today) && nowTime >= start && nowTime < end;

            // Overnight schedule (e.g. 22:00 -> 06:00)
            if (nowTime >= start)
                return days.ContainsDay(today);
            if (nowTime < end)
            {
                DayOfWeek yesterday = localNow.AddDays(-1).DayOfWeek;
                return days.ContainsDay(yesterday);
            }

            return false;
        }

        public string Description
        {
            get
            {
                if (IsActive) return "Aktywny";
                if (Reason == InactiveReason.Manual) return "Nieaktywny: wyłączony ręcznie";
                if (Reason == InactiveReason.Wifi) return "Nieaktywny: wykryto wykluczoną sieć Wi-Fi " + DetectedSsid;
                if (Reason == InactiveReason.WifiMissing) return "Nieaktywny: brak dozwolonej sieci Wi-Fi w zasięgu";
                return "Nieaktywny: poza harmonogramem";
            }
        }
    }
}
