using System;
using System.Collections.Generic;
using System.Linq;

namespace Insomnia
{
    internal enum InactiveReason { None, Manual, Wifi, Schedule }

    internal sealed class AppStatus
    {
        public bool IsActive { get; private set; }
        public InactiveReason Reason { get; private set; }
        public string DetectedSsid { get; private set; }

        public static AppStatus Calculate(AppConfiguration configuration, IEnumerable<string> availableSsids, DateTime localNow)
        {
            if (!configuration.ManuallyEnabled) return New(false, InactiveReason.Manual, null);
            string blocked = availableSsids.FirstOrDefault(network =>
                configuration.ExcludedSsids.Contains(network, StringComparer.OrdinalIgnoreCase));
            if (blocked != null) return New(false, InactiveReason.Wifi, blocked);
            if (configuration.ScheduleEnabled && !IsInSchedule(localNow.TimeOfDay, configuration.ScheduleStart, configuration.ScheduleEnd))
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
        public string Description
        {
            get
            {
                if (IsActive) return "Aktywny";
                if (Reason == InactiveReason.Manual) return "Nieaktywny: wyłączony ręcznie";
                if (Reason == InactiveReason.Wifi) return "Nieaktywny: wykryto sieć Wi-Fi " + DetectedSsid;
                return "Nieaktywny: poza harmonogramem";
            }
        }
    }
}
