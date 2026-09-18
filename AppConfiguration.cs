using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Insomnia
{
    [Flags]
    internal enum ScheduleDays
    {
        None = 0,
        Monday = 1,
        Tuesday = 2,
        Wednesday = 4,
        Thursday = 8,
        Friday = 16,
        Saturday = 32,
        Sunday = 64,
        Workdays = Monday | Tuesday | Wednesday | Thursday | Friday,
        Weekend = Saturday | Sunday,
        All = Workdays | Weekend
    }

    internal static class ScheduleDaysExtensions
    {
        public static ScheduleDays ToScheduleDay(this DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Monday: return ScheduleDays.Monday;
                case DayOfWeek.Tuesday: return ScheduleDays.Tuesday;
                case DayOfWeek.Wednesday: return ScheduleDays.Wednesday;
                case DayOfWeek.Thursday: return ScheduleDays.Thursday;
                case DayOfWeek.Friday: return ScheduleDays.Friday;
                case DayOfWeek.Saturday: return ScheduleDays.Saturday;
                case DayOfWeek.Sunday: return ScheduleDays.Sunday;
                default: return ScheduleDays.None;
            }
        }
        public static bool ContainsDay(this ScheduleDays days, DayOfWeek day)
        {
            return (days & day.ToScheduleDay()) != 0;
        }
    }

    internal enum WifiRuleMode
    {
        BlockOnMatching = 0,     // Wyłączaj program, gdy wykryto sieć z listy
        AllowOnlyOnMatching = 1  // Działaj tylko wtedy, gdy wykryto sieć z listy
    }

    [Flags]
    internal enum SimulationActions
    {
        None = 0,
        MouseMove = 1,
        MouseWheel = 2,
        F15Key = 4,
        AltTab = 8,
        Default = MouseMove | MouseWheel | F15Key // 7
    }

    internal sealed class AppConfiguration
    {
        public bool ManuallyEnabled { get; set; } = true;
        public bool ScheduleEnabled { get; set; }
        public TimeSpan ScheduleStart { get; set; } = TimeSpan.FromHours(8);
        public TimeSpan ScheduleEnd { get; set; } = TimeSpan.FromHours(17);
        public ScheduleDays ScheduleDays { get; set; } = ScheduleDays.All;
        public WifiRuleMode WifiMode { get; set; } = WifiRuleMode.BlockOnMatching;
        public SimulationActions SimulationActions { get; set; } = SimulationActions.Default;
        public List<string> ExcludedSsids { get; set; } = new List<string>();

        public AppConfiguration Clone()
        {
            return new AppConfiguration { ManuallyEnabled = ManuallyEnabled, ScheduleEnabled = ScheduleEnabled,
                ScheduleStart = ScheduleStart, ScheduleEnd = ScheduleEnd,
                ScheduleDays = ScheduleDays, WifiMode = WifiMode,
                SimulationActions = SimulationActions,
                ExcludedSsids = new List<string>(ExcludedSsids) };
        }

        public void Validate()
        {
            if (ScheduleStart < TimeSpan.Zero || ScheduleStart >= TimeSpan.FromDays(1) ||
                ScheduleEnd < TimeSpan.Zero || ScheduleEnd >= TimeSpan.FromDays(1))
                throw new ArgumentException("Godziny muszą mieścić się w jednej dobie.");
            if (ScheduleEnabled && (ScheduleDays == ScheduleDays.None || (int)ScheduleDays < 0 || (int)ScheduleDays > 127))
                throw new ArgumentException("Wybierz co najmniej jeden dzień tygodnia w harmonogramie.");
            if (SimulationActions == SimulationActions.None || ((int)SimulationActions & ~15) != 0)
                throw new ArgumentException("Wybierz co najmniej jedno działanie symulacji.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ssid in ExcludedSsids)
            {
                if (string.IsNullOrWhiteSpace(ssid)) throw new ArgumentException("Nazwa sieci nie może być pusta.");
                if (System.Text.Encoding.UTF8.GetByteCount(ssid) > 32) throw new ArgumentException("SSID może mieć najwyżej 32 bajty UTF-8.");
                if (!seen.Add(ssid)) throw new ArgumentException("Ta sieć jest już na liście: " + ssid);
            }
        }
    }

    internal interface IConfigurationStore
    {
        AppConfiguration Load();
        void Save(AppConfiguration configuration);
    }

    // A fresh Settings instance avoids leaving the shared singleton dirty after a failed Save.
    internal sealed class ConfigurationStore : IConfigurationStore
    {
        public AppConfiguration Load()
        {
            var settings = new Properties.Settings();
            int days = settings.ScheduleDays;
            if (days <= 0 || days > 127) days = 127;
            int wifiMode = settings.WifiMode;
            if (wifiMode != 0 && wifiMode != 1) wifiMode = 0;
            int actions = settings.SimulationActions;
            if (actions <= 0 || actions > 15) actions = (int)SimulationActions.Default;

            var value = new AppConfiguration {
                ManuallyEnabled = settings.ManuallyEnabled, ScheduleEnabled = settings.ScheduleEnabled,
                ScheduleStart = Minute(settings.ScheduleStart), ScheduleEnd = Minute(settings.ScheduleEnd),
                ScheduleDays = (ScheduleDays)days, WifiMode = (WifiRuleMode)wifiMode,
                SimulationActions = (SimulationActions)actions,
                ExcludedSsids = settings.ExcludedSsids == null ? new List<string>() :
                    settings.ExcludedSsids.Cast<string>().Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
            value.Validate();
            return value;
        }

        private static TimeSpan Minute(TimeSpan value) { return TimeSpan.FromMinutes(Math.Floor(value.TotalMinutes)); }

        public void Save(AppConfiguration configuration)
        {
            configuration.Validate();
            var settings = new Properties.Settings {
                ManuallyEnabled = configuration.ManuallyEnabled, ScheduleEnabled = configuration.ScheduleEnabled,
                ScheduleStart = configuration.ScheduleStart, ScheduleEnd = configuration.ScheduleEnd,
                ScheduleDays = (int)configuration.ScheduleDays, WifiMode = (int)configuration.WifiMode,
                SimulationActions = (int)configuration.SimulationActions,
                ExcludedSsids = new StringCollection()
            };
            settings.ExcludedSsids.AddRange(configuration.ExcludedSsids.ToArray());
            settings.Save(); // Exceptions deliberately propagate to the form. Success is reported only afterwards.
        }
    }

    internal sealed class ConfigurationController
    {
        private readonly IConfigurationStore store;
        public AppConfiguration Current { get; private set; }
        public ConfigurationController(IConfigurationStore store) { this.store = store; Current = store.Load(); }
        public void Apply(AppConfiguration draft)
        {
            var candidate = draft.Clone();
            candidate.ManuallyEnabled = Current.ManuallyEnabled;
            Commit(candidate);
        }
        public void Toggle()
        {
            var candidate = Current.Clone();
            candidate.ManuallyEnabled = !candidate.ManuallyEnabled;
            Commit(candidate);
        }
        private void Commit(AppConfiguration candidate)
        {
            candidate.Validate();
            store.Save(candidate);
            Current = candidate;
        }
    }
}
