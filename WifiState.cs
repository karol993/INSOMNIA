using System;
using System.Collections.Generic;
using System.Linq;

namespace Insomnia
{
    internal enum ScanOutcome { Success, NoAdapter, RadioOff, AccessDenied, Timeout, Failed }
    internal sealed class AdapterScanResult
    {
        public Guid Id;
        public string Name;
        public ScanOutcome Outcome;
        public uint Error;
        public string[] Ssids = new string[0];
    }
    internal sealed class WifiScanResult
    {
        public List<AdapterScanResult> Adapters = new List<AdapterScanResult>();
        public ScanOutcome Outcome = ScanOutcome.Success;
        public uint Error;
    }

    // Keep independently timestamped results: a healthy adapter must not renew stale data from another.
    internal sealed class WifiState
    {
        private sealed class Cached { public string[] Ssids; public DateTimeOffset At; }
        private readonly Dictionary<Guid, Cached> cache = new Dictionary<Guid, Cached>();
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
        public WifiScanResult LastAttempt { get; private set; }
        public DateTimeOffset? LastSuccess { get; private set; }
        public bool Scanning { get; set; }

        public void Accept(WifiScanResult result, DateTimeOffset now)
        {
            LastAttempt = result;
            foreach (var adapter in result.Adapters.Where(x => x.Outcome == ScanOutcome.Success))
            {
                cache[adapter.Id] = new Cached { Ssids = adapter.Ssids, At = now };
                LastSuccess = now;
            }
            // An enumeration with a known adapter set removes results of physically removed adapters.
            if (result.Outcome == ScanOutcome.Success || result.Outcome == ScanOutcome.NoAdapter)
            {
                foreach (var id in cache.Keys.Where(id => !result.Adapters.Any(a => a.Id == id)).ToArray())
                    cache.Remove(id);
            }
        }
        public string[] Networks(DateTimeOffset now)
        {
            return cache.Values.Where(x => now >= x.At && now - x.At < Lifetime)
                .SelectMany(x => x.Ssids).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public bool Complete(DateTimeOffset now)
        {
            return LastAttempt != null && LastAttempt.Outcome == ScanOutcome.Success &&
                LastAttempt.Adapters.Count > 0 && LastAttempt.Adapters.All(a =>
                    a.Outcome == ScanOutcome.Success && cache.ContainsKey(a.Id) &&
                    now >= cache[a.Id].At && now - cache[a.Id].At < Lifetime);
        }
        public string NetworkState(string ssid, DateTimeOffset now)
        {
            if (Networks(now).Contains(ssid, StringComparer.OrdinalIgnoreCase)) return "W zasięgu";
            return Complete(now) ? "Niewykryta" : "Brak aktualnych danych";
        }
        public string Describe(DateTimeOffset now)
        {
            string header = Scanning ? "Skanowanie…" : LastAttempt == null ? "Oczekiwanie na pierwszy skan." :
                LastAttempt.Outcome != ScanOutcome.Success ? DescribeOutcome(LastAttempt.Outcome, LastAttempt.Error) :
                string.Join(Environment.NewLine, LastAttempt.Adapters.Select(a => a.Name + ": " +
                    (a.Outcome == ScanOutcome.Success ? (a.Ssids.Length == 0 ? "Brak sieci w zasięgu" : a.Ssids.Length + " sieci") :
                    DescribeOutcome(a.Outcome, a.Error))));
            if (!Complete(now)) header += Environment.NewLine + "Kontrola Wi-Fi niedostępna lub niepełna. Ostatnie poprawne wyniki są ważne przez 2 minuty.";
            return header;
        }
        public static string DescribeOutcome(ScanOutcome outcome, uint error)
        {
            switch (outcome)
            {
                case ScanOutcome.NoAdapter: return "Brak dostępnego adaptera Wi-Fi.";
                case ScanOutcome.RadioOff: return "Radio Wi-Fi jest wyłączone lub adapter nie jest gotowy.";
                case ScanOutcome.AccessDenied: return "Odmowa dostępu. Windows może wymagać zgody na lokalizację.";
                case ScanOutcome.Timeout: return "Timeout: nie otrzymano potwierdzenia zakończenia skanowania.";
                default: return "Błąd skanowania WLAN (kod " + error + ").";
            }
        }
    }
}
