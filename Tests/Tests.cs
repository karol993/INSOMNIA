using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Insomnia;

internal static class Tests
{
    private static int passed;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0].StartsWith("--persist-")) return PersistenceChild(args[0]);
            if (args.Contains("--ui")) { ShowPreview(); return 0; }
            if (args.Contains("--hardware")) { Hardware(); return 0; }
            Test("schedule and exact boundaries", Schedule);
            Test("native sizes, offsets and consecutive records", NativeLayout);
            Test("configuration survives fresh processes including empty list", Persistence);
            Test("transactional edit, manual toggle and failure", Editing);
            Test("real form edit, apply failure, retry, cancel", FormEditing);
            Test("Wi-Fi priority, expiry and partial adapters", WifiExpiry);
            Test("scan notification timeout and cancellation", () => Waits().GetAwaiter().GetResult());
            Test("multi-adapter, errors, deduplication and shutdown", () => Scanner().GetAwaiter().GetResult());
            Test("live form layout at scale factors 100/125/150/200%", Layout);
            Console.WriteLine("PASS: " + passed + " groups; process " + (IntPtr.Size * 8) + "-bit");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Test(string name, Action test) { SynchronizationContext.SetSynchronizationContext(null); test(); passed++; Console.WriteLine("PASS " + name); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static TimeSpan H(int hour, int minute = 0) { return new TimeSpan(hour, minute, 0); }
    private static void Schedule()
    {
        Assert(AppStatus.IsInSchedule(H(8), H(8), H(17)), "start inclusive");
        Assert(!AppStatus.IsInSchedule(H(17), H(8), H(17)), "end exclusive");
        Assert(!AppStatus.IsInSchedule(H(7,59), H(8), H(17)), "before start");
        Assert(AppStatus.IsInSchedule(H(16,59), H(8), H(17)), "before end");
        Assert(AppStatus.IsInSchedule(H(22), H(22), H(6)), "night start");
        Assert(AppStatus.IsInSchedule(H(0), H(22), H(6)), "midnight");
        Assert(AppStatus.IsInSchedule(H(5,59), H(22), H(6)), "night before end");
        Assert(!AppStatus.IsInSchedule(H(6), H(22), H(6)), "night end");
        Assert(!AppStatus.IsInSchedule(H(21,59), H(22), H(6)), "night before start");
        Assert(AppStatus.IsInSchedule(H(3), H(8), H(8)), "24h");
    }
    private static void NativeLayout()
    {
        Assert(Marshal.SizeOf(typeof(WlanAvailableNetwork)) == 628, "WLAN size");
        Assert(Marshal.SizeOf(typeof(Dot11Ssid)) == 36, "SSID size");
        Assert(Marshal.SizeOf(typeof(WlanInterfaceInfo)) == 532, "interface size");
        var offsets = new Dictionary<string,int> {
            {"Ssid",512}, {"BssType",548}, {"NumberOfBssids",552}, {"NetworkConnectable",556},
            {"NotConnectableReason",560}, {"NumberOfPhyTypes",564}, {"PhyTypes",568},
            {"MorePhyTypes",600}, {"SignalQuality",604}, {"SecurityEnabled",608},
            {"DefaultAuthAlgorithm",612}, {"DefaultCipherAlgorithm",616}, {"Flags",620}, {"Reserved",624}
        };
        foreach (var offset in offsets) Assert(Marshal.OffsetOf(typeof(WlanAvailableNetwork), offset.Key).ToInt32() == offset.Value, offset.Key);
        // Construct raw bytes independently of Marshal.StructureToPtr: catches incorrect record stride.
        var bytes = new byte[8 + 4 * 628];
        Array.Copy(BitConverter.GetBytes(4), bytes, 4);
        string[] names = { "Dom", "Biuro Łódź & <>", "Gość", "Tylko profil" };
        for (int i=0; i<names.Length; i++)
        {
            var name = Encoding.UTF8.GetBytes(names[i]); int record = 8 + i * 628;
            Array.Copy(BitConverter.GetBytes(name.Length), 0, bytes, record + 512, 4);
            Array.Copy(name, 0, bytes, record + 516, name.Length);
            Array.Copy(BitConverter.GetBytes(i == 3 ? 0 : 1), 0, bytes, record + 552, 4);
            // Flags remain zero: neither connected nor saved.
        }
        var memory = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            var result = NativeWifiSession.ParseNetworks(memory);
            Assert(result.SequenceEqual(names.Take(3)), "multiple raw records without connected/profile flags");
            var config = new AppConfiguration(); config.ExcludedSsids.Add("biuro łódź & <>");
            Assert(AppStatus.Calculate(config, result, DateTime.Now).Reason == InactiveReason.Wifi, "unconnected visible SSID blocks");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static void Persistence()
    {
        foreach (var mode in new[] { "--persist-write", "--persist-read", "--persist-clear", "--persist-empty" })
        {
            using (var process = Process.Start(new ProcessStartInfo(typeof(Tests).Assembly.Location, mode) { UseShellExecute = false, CreateNoWindow = true }))
            { Assert(process.WaitForExit(15000) && process.ExitCode == 0, "child " + mode); }
        }
    }
    private static int PersistenceChild(string mode)
    {
        var store = new ConfigurationStore();
        if (mode == "--persist-write")
        {
            store.Save(new AppConfiguration { ManuallyEnabled = false, ScheduleEnabled = true, ScheduleStart = H(22), ScheduleEnd = H(6),
                ExcludedSsids = new List<string>() });
            using (var form = new SettingsForm(new ConfigurationController(store), message => { throw new Exception(message); }))
            {
                foreach (var ssid in new[] { "Biuro Łódź", "A & <B> \"C\"", " Dom " }) Assert(form.AddSsid(ssid, false), "form add");
                Assert(form.TryApply(), "form persisted to disk");
            }
        }
        else if (mode == "--persist-clear") { var config = store.Load(); config.ExcludedSsids.Clear(); store.Save(config); }
        else
        {
            var config = store.Load();
            Assert(!config.ManuallyEnabled && config.ScheduleEnabled && config.ScheduleStart == H(22) && config.ScheduleEnd == H(6), "restored settings");
            if (mode == "--persist-empty") Assert(config.ExcludedSsids.Count == 0, "empty persisted");
            else Assert(config.ExcludedSsids.SequenceEqual(new[] { "Biuro Łódź", "A & <B> \"C\"", " Dom " }), "unicode and special characters persisted");
        }
        return 0;
    }
    private sealed class MemoryStore : IConfigurationStore
    {
        public AppConfiguration Value = new AppConfiguration();
        public bool Fail;
        public AppConfiguration Load() { return Value.Clone(); }
        public void Save(AppConfiguration configuration) { if (Fail) throw new IOException("Test: disk unavailable"); Value = configuration.Clone(); }
    }
    private static void Editing()
    {
        var store = new MemoryStore(); var controller = new ConfigurationController(store);
        var draft = controller.Current.Clone(); draft.ExcludedSsids.Add("Office");
        Assert(controller.Current.ExcludedSsids.Count == 0, "cancel does not modify current");
        controller.Toggle();
        controller.Apply(draft);
        Assert(!controller.Current.ManuallyEnabled, "stale draft cannot override manual switch");
        draft.ExcludedSsids.Add("Second"); store.Fail = true;
        try { controller.Apply(draft); throw new Exception("expected failed save"); } catch (IOException) { }
        Assert(controller.Current.ExcludedSsids.Count == 1 && store.Value.ExcludedSsids.Count == 1, "no success after failed write");
        store.Fail = false; controller.Apply(draft);
        Assert(store.Value.ExcludedSsids.Count == 2, "retry");
    }
    private static void FormEditing()
    {
        var store = new MemoryStore(); var controller = new ConfigurationController(store); var errors = new List<string>();
        using (var form = new SettingsForm(controller, errors.Add))
        {
            Assert(!form.AddSsid(" ", false), "empty validation");
            Assert(form.AddSsid("Biuro", false), "add");
            Assert(!form.AddSsid("biuro", false), "duplicate validation");
            Assert(form.HasChanges, "dirty");
            store.Fail = true;
            Assert(!form.TryApply() && form.HasChanges && controller.Current.ExcludedSsids.Count == 0, "failed form save retains draft");
            store.Fail = false;
            Assert(form.TryApply() && !form.HasChanges && controller.Current.ExcludedSsids.Count == 1, "successful apply");
            form.Show();
            form.AddSsid("Unapplied", false);
            Descendants(form).OfType<Button>().Single(b => b.Text == "Anuluj").PerformClick();
            Application.DoEvents();
            Assert(form.IsDisposed, "Cancel closes without saving");
        }
        Assert(controller.Current.ExcludedSsids.SequenceEqual(new[] { "Biuro" }), "discard since last apply");
        Assert(errors.Count == 3, "readable validation and save errors");
    }
    private static void WifiExpiry()
    {
        var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid(); var state = new WifiState();
        state.Accept(new WifiScanResult { Adapters = new List<AdapterScanResult> { new AdapterScanResult { Id=id, Outcome=ScanOutcome.Success, Ssids=new[] { "OFFICE" } } } }, now);
        var config = new AppConfiguration { ScheduleEnabled = true, ScheduleStart = H(8), ScheduleEnd = H(9) };
        config.ExcludedSsids.Add("office");
        Assert(AppStatus.Calculate(config, state.Networks(now), DateTime.Today.AddHours(12)).Reason == InactiveReason.Wifi, "Wi-Fi before schedule");
        config.ManuallyEnabled = false;
        Assert(AppStatus.Calculate(config, state.Networks(now), DateTime.Now).Reason == InactiveReason.Manual, "manual first");
        state.Accept(new WifiScanResult { Outcome = ScanOutcome.AccessDenied, Error = 5 }, now.AddSeconds(20));
        Assert(state.Networks(now.AddSeconds(119)).Length == 1 && !state.Complete(now), "retain bounded cache after error");
        Assert(state.Networks(now.AddSeconds(120)).Length == 0, "exact TTL");
        Assert(state.NetworkState("office", now.AddSeconds(120)) == "Brak aktualnych danych", "failure not absence");
        config.ManuallyEnabled = true; config.ScheduleEnabled = false;
        Assert(AppStatus.Calculate(config, state.Networks(now.AddSeconds(120)), DateTime.Now).IsActive, "fail-open after expiry");
        state.Accept(new WifiScanResult { Adapters = new List<AdapterScanResult> {
            new AdapterScanResult { Id=id, Outcome=ScanOutcome.Timeout },
            new AdapterScanResult { Id=Guid.NewGuid(), Outcome=ScanOutcome.Success }
        } }, now.AddSeconds(90));
        Assert(state.Networks(now.AddSeconds(121)).Length == 0 && !state.Complete(now.AddSeconds(121)), "other adapter does not refresh stale SSID");
        state.Accept(new WifiScanResult { Adapters = new List<AdapterScanResult> {
            new AdapterScanResult { Id=id, Outcome=ScanOutcome.Success, Ssids=new string[0] }
        } }, now.AddSeconds(130));
        Assert(state.Complete(now.AddSeconds(130)) && state.NetworkState("office", now.AddSeconds(130)) == "Niewykryta",
            "confirmed empty scan replaces old networks");
    }
    private static async Task Waits()
    {
        Assert(await NativeWifiSession.WaitForScan(Task.FromResult(0u), TimeSpan.FromSeconds(1), CancellationToken.None) == 0, "success notification");
        Assert(await NativeWifiSession.WaitForScan(Task.FromResult(5u), TimeSpan.FromSeconds(1), CancellationToken.None) == 5, "failure notification");
        try { await NativeWifiSession.WaitForScan(new TaskCompletionSource<uint>().Task, TimeSpan.FromMilliseconds(20), CancellationToken.None); throw new Exception("expected timeout"); } catch (TimeoutException) { }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { await NativeWifiSession.WaitForScan(new TaskCompletionSource<uint>().Task, TimeSpan.FromSeconds(5), cancellation.Token); throw new Exception("expected cancellation"); } catch (OperationCanceledException) { }
        }
    }
    private sealed class FakeSession : IWifiSession
    {
        public WlanInterfaceInfo[] Interfaces = new[] {
            new WlanInterfaceInfo { Id=Guid.NewGuid(), Description="Adapter A", State=4 },
            new WlanInterfaceInfo { Id=Guid.NewGuid(), Description="Adapter B", State=4 }
        };
        public uint Error;
        public bool Off, Block, Disposed, Empty, TimedOut;
        public int InFlight, Calls;
        public readonly TaskCompletionSource<bool> Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public WlanInterfaceInfo[] Enumerate() { return Interfaces; }
        public bool RadioOff(Guid id) { return Off; }
        public async Task<uint> ScanAsync(Guid id, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); Interlocked.Increment(ref InFlight); Started.TrySetResult(true);
            try { if (TimedOut) throw new TimeoutException(); if (Block) await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); return Error; }
            finally { Interlocked.Decrement(ref InFlight); }
        }
        public string[] Networks(Guid id) { return Empty ? new string[0] : new[] { id == Interfaces[0].Id ? "Dom" : "Biuro" }; }
        public void Dispose() { Assert(InFlight == 0, "closed native handle while in use"); Disposed = true; }
    }
    private static async Task Scanner()
    {
        var fake = new FakeSession(); var scanner = new WlanScanner(() => fake);
        var result = await scanner.ScanAsync();
        Assert(result.Adapters.Count == 2 && result.Adapters[1].Ssids.Contains("Biuro") && fake.Disposed, "all disconnected adapters");
        fake = new FakeSession { Error=5 }; scanner = new WlanScanner(() => fake);
        result = await scanner.ScanAsync(); Assert(result.Adapters.All(a => a.Outcome == ScanOutcome.AccessDenied), "access denied");
        fake = new FakeSession { Off=true }; scanner = new WlanScanner(() => fake);
        result = await scanner.ScanAsync(); Assert(result.Adapters.All(a => a.Outcome == ScanOutcome.RadioOff), "radio off");
        fake = new FakeSession { Interfaces=new WlanInterfaceInfo[0] }; scanner = new WlanScanner(() => fake);
        result = await scanner.ScanAsync(); Assert(result.Outcome == ScanOutcome.NoAdapter, "no adapter");
        fake = new FakeSession { Empty=true }; scanner = new WlanScanner(() => fake);
        result = await scanner.ScanAsync(); Assert(result.Adapters.All(a => a.Outcome == ScanOutcome.Success && a.Ssids.Length == 0), "successful empty scan");
        fake = new FakeSession { TimedOut=true }; scanner = new WlanScanner(() => fake);
        result = await scanner.ScanAsync(); Assert(result.Adapters.All(a => a.Outcome == ScanOutcome.Timeout), "timeout is not successful empty scan");
        scanner = new WlanScanner(() => { throw new Win32Exception(5); });
        result = await scanner.ScanAsync(); Assert(result.Outcome == ScanOutcome.AccessDenied, "session access denied");
        fake = new FakeSession { Block=true }; scanner = new WlanScanner(() => fake);
        var first = scanner.ScanAsync(); await fake.Started.Task;
        Assert(ReferenceEquals(first, scanner.ScanAsync()), "deduplicate concurrent requests");
        await scanner.StopAsync();
        Assert(fake.Disposed && fake.InFlight == 0, "shutdown awaits cancellation and disposal");
    }
    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls) { yield return child; foreach(var nested in Descendants(child)) yield return nested; }
    }
    private static void Layout()
    {
        foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
        using (var form = new SettingsForm(new ConfigurationController(new MemoryStore()), message => { throw new Exception(message); }))
        {
            form.Show(); Application.DoEvents();
            form.AutoScaleMode = AutoScaleMode.None;
            form.Scale(new SizeF(scale, scale));
            form.Size = form.MinimumSize;
            form.PerformLayout(); Application.DoEvents();
            var footer = Descendants(form).Single(c => c.Name == "Footer");
            foreach (var button in Descendants(footer).OfType<Button>())
            {
                var rectangle = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                Assert(button.Visible && form.ClientRectangle.Contains(rectangle), scale + ": footer clipped: " + button.Text);
            }
            Assert(Descendants(form).OfType<DateTimePicker>().All(p => !p.Enabled && p.CustomFormat == "HH:mm"), "time format / enabled");
            form.Close();
        }
    }
    private static void Hardware()
    {
        Console.WriteLine("Administrator token: " + new System.Security.Principal.WindowsPrincipal(
            System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
        var scanner = new WlanScanner();
        var result = scanner.ScanAsync().GetAwaiter().GetResult();
        Console.WriteLine("Outcome: " + result.Outcome + "; error: " + result.Error + "; adapters: " + result.Adapters.Count);
        foreach (var adapter in result.Adapters) Console.WriteLine(adapter.Name + ": " + adapter.Outcome + ", error=" + adapter.Error + ", network count=" + adapter.Ssids.Length);
        scanner.StopAsync().GetAwaiter().GetResult();
        using (var session = new NativeWifiSession())
        {
            int verified = 0;
            foreach (var adapter in session.Enumerate())
            {
                if (adapter.State == 0 || session.RadioOff(adapter.Id)) continue;
                if (session.ScanAsync(adapter.Id, CancellationToken.None).GetAwaiter().GetResult() != 0) continue;
                var records = session.GetNetworkRecords(adapter.Id);
                foreach (var record in records.Where(r => r.NumberOfBssids > 0 && (r.Flags & 1) == 0))
                {
                    string name = record.Ssid.Decode();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var config = new AppConfiguration(); config.ExcludedSsids.Add(name);
                    Assert(AppStatus.Calculate(config, session.Networks(adapter.Id), DateTime.Now).Reason == InactiveReason.Wifi,
                        "real unconnected network must block");
                    verified++;
                }
            }
            Console.WriteLine("Real visible, unconnected WLAN records verified as blocking: " + verified);
        }
    }
    private static void ShowPreview()
    {
        var store = new MemoryStore();
        store.Value.ExcludedSsids.Add("Biuro");
        using (var form = new SettingsForm(new ConfigurationController(store)))
        {
            var state = new WifiState();
            form.UpdateStatus(AppStatus.Calculate(store.Value, new string[0], DateTime.Now), state, DateTimeOffset.UtcNow);
            Application.Run(form);
        }
    }
}
