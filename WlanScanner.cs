using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Insomnia
{
    internal interface IWifiSession : IDisposable
    {
        WlanInterfaceInfo[] Enumerate();
        bool RadioOff(Guid id);
        Task<uint> ScanAsync(Guid id, CancellationToken cancellation);
        string[] Networks(Guid id);
    }

    internal sealed class WlanScanner
    {
        private readonly object sync = new object();
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly Func<IWifiSession> factory;
        private Task<WifiScanResult> active;
        private bool stopping;
        internal WlanScanner(Func<IWifiSession> factory = null) { this.factory = factory ?? (() => new NativeWifiSession()); }

        public Task<WifiScanResult> ScanAsync()
        {
            lock (sync)
            {
                if (stopping) return Task.FromCanceled<WifiScanResult>(new CancellationToken(true));
                if (active != null && !active.IsCompleted) return active;
                active = Task.Run(() => ScanCore(shutdown.Token));
                return active;
            }
        }
        public async Task StopAsync()
        {
            Task pending;
            lock (sync) { stopping = true; shutdown.Cancel(); pending = active; }
            if (pending != null)
            {
                try { await pending.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            }
            // Native session is disposed by ScanCore, after all adapter operations have finished.
        }
        private async Task<WifiScanResult> ScanCore(CancellationToken token)
        {
            try
            {
                using (var session = factory())
                {
                    token.ThrowIfCancellationRequested();
                    var interfaces = session.Enumerate();
                    if (interfaces.Length == 0) return new WifiScanResult { Outcome = ScanOutcome.NoAdapter };
                    var adapters = await Task.WhenAll(interfaces.Select(info => ScanAdapter(session, info, token))).ConfigureAwait(false);
                    return new WifiScanResult { Adapters = adapters.ToList() };
                }
            }
            catch (Win32Exception ex) { return new WifiScanResult { Outcome = Classify((uint)ex.NativeErrorCode), Error = (uint)ex.NativeErrorCode }; }
            catch (DllNotFoundException) { return new WifiScanResult { Outcome = ScanOutcome.Failed, Error = 126 }; }
        }
        private static async Task<AdapterScanResult> ScanAdapter(IWifiSession session, WlanInterfaceInfo info, CancellationToken token)
        {
            var result = new AdapterScanResult { Id = info.Id, Name = info.Description };
            try
            {
                token.ThrowIfCancellationRequested();
                if (info.State == 0 || session.RadioOff(info.Id)) { result.Outcome = ScanOutcome.RadioOff; return result; }
                uint error = await session.ScanAsync(info.Id, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (error != 0) { result.Outcome = Classify(error); result.Error = error; return result; }
                result.Ssids = session.Networks(info.Id);
                result.Outcome = ScanOutcome.Success;
            }
            catch (TimeoutException) { result.Outcome = ScanOutcome.Timeout; }
            catch (Win32Exception ex) { result.Error = (uint)ex.NativeErrorCode; result.Outcome = Classify(result.Error); }
            return result;
        }
        internal static ScanOutcome Classify(uint error)
        {
            return error == 5 ? ScanOutcome.AccessDenied : error == 0x80342002 ? ScanOutcome.RadioOff : ScanOutcome.Failed;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WlanInterfaceInfo
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public uint State;
    }

    // Native structure has no pointers: sizeof is 628 on both x86 and x64.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    internal struct WlanAvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public Dot11Ssid Ssid;
        public uint BssType;
        public uint NumberOfBssids;
        [MarshalAs(UnmanagedType.Bool)] public bool NetworkConnectable;
        public uint NotConnectableReason;
        public uint NumberOfPhyTypes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] PhyTypes;
        [MarshalAs(UnmanagedType.Bool)] public bool MorePhyTypes;
        public uint SignalQuality;
        [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
        public uint DefaultAuthAlgorithm;
        public uint DefaultCipherAlgorithm;
        public uint Flags;
        public uint Reserved;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct Dot11Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes;
        public string Decode()
        {
            if (Length > 32) throw new InvalidOperationException("Nieprawidłowa długość SSID.");
            // SSID is bytes, not guaranteed Unicode. Do not silently match lossy replacement characters.
            try { return new UTF8Encoding(false, true).GetString(Bytes, 0, (int)Length); }
            catch (DecoderFallbackException) { return null; }
        }
    }

    internal sealed class NativeWifiSession : IWifiSession
    {
        private IntPtr handle;
        private readonly NotificationCallback callback;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<uint>> pending = new ConcurrentDictionary<Guid, TaskCompletionSource<uint>>();
        private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(12);

        public NativeWifiSession()
        {
            uint version;
            Check(WlanOpenHandle(2, IntPtr.Zero, out version, out handle));
            callback = OnNotification;
            uint previous;
            uint error = WlanRegisterNotification(handle, 8, false, callback, IntPtr.Zero, IntPtr.Zero, out previous);
            if (error != 0) { WlanCloseHandle(handle, IntPtr.Zero); handle = IntPtr.Zero; Check(error); }
        }
        public WlanInterfaceInfo[] Enumerate()
        {
            IntPtr data = IntPtr.Zero;
            try
            {
                Check(WlanEnumInterfaces(handle, IntPtr.Zero, out data));
                int count = Marshal.ReadInt32(data);
                var result = new WlanInterfaceInfo[count];
                int size = Marshal.SizeOf(typeof(WlanInterfaceInfo));
                for (int i = 0; i < count; i++)
                    result[i] = (WlanInterfaceInfo)Marshal.PtrToStructure(IntPtr.Add(data, 8 + i * size), typeof(WlanInterfaceInfo));
                return result;
            }
            finally { if (data != IntPtr.Zero) WlanFreeMemory(data); }
        }
        public bool RadioOff(Guid id)
        {
            IntPtr data = IntPtr.Zero;
            try
            {
                uint size, opcodeType;
                uint error = WlanQueryInterface(handle, ref id, 4, IntPtr.Zero, out size, out data, out opcodeType);
                Check(error);
                if (size < 4) return false;
                int count = Math.Min(Marshal.ReadInt32(data), 64);
                if (count == 0 || size < 4 + count * 12) return false;
                for (int i = 0; i < count; i++)
                {
                    // WLAN_PHY_RADIO_STATE: index, software state, hardware state; ON = 1.
                    if (Marshal.ReadInt32(data, 8 + i * 12) == 1 && Marshal.ReadInt32(data, 12 + i * 12) == 1)
                        return false;
                }
                return true;
            }
            finally { if (data != IntPtr.Zero) WlanFreeMemory(data); }
        }
        public async Task<uint> ScanAsync(Guid id, CancellationToken cancellation)
        {
            var completion = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pending.TryAdd(id, completion)) throw new InvalidOperationException("Skan interfejsu już trwa.");
            try
            {
                cancellation.ThrowIfCancellationRequested();
                uint error = WlanScan(handle, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (error != 0) return error;
                return await WaitForScan(completion.Task, ScanTimeout, cancellation).ConfigureAwait(false);
            }
            finally { TaskCompletionSource<uint> ignored; pending.TryRemove(id, out ignored); }
        }
        internal static async Task<uint> WaitForScan(Task<uint> completion, TimeSpan timeout, CancellationToken cancellation)
        {
            using (var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                var delay = Task.Delay(timeout, timer.Token);
                var finished = await Task.WhenAny(completion, delay).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (finished != completion) throw new TimeoutException();
                timer.Cancel();
                return await completion.ConfigureAwait(false);
            }
        }
        private void OnNotification(ref Notification data, IntPtr context)
        {
            // Callback must be short; never call native APIs, UI, unregister or Dispose here.
            if ((data.Source & 8) == 0 || (data.Code != 7 && data.Code != 8)) return;
            TaskCompletionSource<uint> completion;
            if (!pending.TryGetValue(data.InterfaceGuid, out completion)) return;
            uint error = data.Code == 7 ? 0u :
                data.Data != IntPtr.Zero && data.Size >= 4 ? unchecked((uint)Marshal.ReadInt32(data.Data)) : 31u;
            if (data.Code == 8 && error == 0) error = 31;
            completion.TrySetResult(error);
        }
        public string[] Networks(Guid id)
        {
            return Names(GetNetworkRecords(id));
        }
        internal WlanAvailableNetwork[] GetNetworkRecords(Guid id)
        {
            IntPtr data = IntPtr.Zero;
            try
            {
                // flags=0 excludes out-of-range profiles; NumberOfBssids filters cached profile-only entries.
                Check(WlanGetAvailableNetworkList(handle, ref id, 0, IntPtr.Zero, out data));
                return ParseRecords(data);
            }
            finally { if (data != IntPtr.Zero) WlanFreeMemory(data); }
        }
        internal static string[] ParseNetworks(IntPtr data)
        {
            return Names(ParseRecords(data));
        }
        private static WlanAvailableNetwork[] ParseRecords(IntPtr data)
        {
            int count = Marshal.ReadInt32(data);
            int size = Marshal.SizeOf(typeof(WlanAvailableNetwork));
            var result = new WlanAvailableNetwork[count];
            for (int i = 0; i < count; i++)
                result[i] = (WlanAvailableNetwork)Marshal.PtrToStructure(IntPtr.Add(data, 8 + i * size), typeof(WlanAvailableNetwork));
            return result;
        }
        private static string[] Names(IEnumerable<WlanAvailableNetwork> records)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in records)
            {
                if (item.NumberOfBssids == 0 || item.Ssid.Length == 0) continue;
                string ssid = item.Ssid.Decode();
                if (ssid != null) result.Add(ssid);
            }
            return result.ToArray();
        }
        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            uint previous;
            WlanRegisterNotification(handle, 0, false, null, IntPtr.Zero, IntPtr.Zero, out previous);
            WlanCloseHandle(handle, IntPtr.Zero);
            handle = IntPtr.Zero;
            GC.KeepAlive(callback);
        }
        private static void Check(uint error) { if (error != 0) throw new Win32Exception(unchecked((int)error)); }

        [StructLayout(LayoutKind.Sequential)]
        private struct Notification
        {
            public uint Source, Code;
            public Guid InterfaceGuid;
            public uint Size;
            public IntPtr Data;
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void NotificationCallback(ref Notification data, IntPtr context);
        [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr data);
        [DllImport("wlanapi.dll")] private static extern uint WlanScan(IntPtr handle, ref Guid id, IntPtr ssid, IntPtr ies, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanGetAvailableNetworkList(IntPtr handle, ref Guid id, uint flags, IntPtr reserved, out IntPtr data);
        [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, uint opcode, IntPtr reserved, out uint size, out IntPtr data, out uint opcodeType);
        [DllImport("wlanapi.dll")] private static extern uint WlanRegisterNotification(IntPtr handle, uint source, [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate, NotificationCallback callback, IntPtr context, IntPtr reserved, out uint previous);
        [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr data);
    }
}
