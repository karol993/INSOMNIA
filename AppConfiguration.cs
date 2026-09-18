using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Insomnia
{
    internal sealed class AppConfiguration
    {
        public bool ManuallyEnabled { get; set; } = true;
        public bool ScheduleEnabled { get; set; }
        public TimeSpan ScheduleStart { get; set; } = TimeSpan.FromHours(8);
        public TimeSpan ScheduleEnd { get; set; } = TimeSpan.FromHours(17);
        public List<string> ExcludedSsids { get; set; } = new List<string>();

        public AppConfiguration Clone()
        {
            return new AppConfiguration { ManuallyEnabled = ManuallyEnabled, ScheduleEnabled = ScheduleEnabled,
                ScheduleStart = ScheduleStart, ScheduleEnd = ScheduleEnd, ExcludedSsids = new List<string>(ExcludedSsids) };
        }

        public void Validate()
        {
            if (ScheduleStart < TimeSpan.Zero || ScheduleStart >= TimeSpan.FromDays(1) ||
                ScheduleEnd < TimeSpan.Zero || ScheduleEnd >= TimeSpan.FromDays(1))
                throw new ArgumentException("Godziny muszą mieścić się w jednej dobie.");
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
            var value = new AppConfiguration {
                ManuallyEnabled = settings.ManuallyEnabled, ScheduleEnabled = settings.ScheduleEnabled,
                ScheduleStart = Minute(settings.ScheduleStart), ScheduleEnd = Minute(settings.ScheduleEnd),
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
