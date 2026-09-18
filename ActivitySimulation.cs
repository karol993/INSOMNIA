using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Insomnia
{
    internal sealed partial class TrayApplicationContext
    {
        private readonly Random random = new Random();
        [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint cbSize; public uint dwTime; }
        [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X; public int Y; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo value);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointNative value);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [StructLayout(LayoutKind.Sequential)] private struct Input { public int Type; public InputUnion Union; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X; public int Y; public uint Data; public uint Flags; public uint Time; public IntPtr Extra; }

        private static uint GetIdleTime() { var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf(typeof(LastInputInfo)) }; GetLastInputInfo(ref info); return (uint)Environment.TickCount - info.dwTime; }
        private void PerformStealthActivity()
        {
            var actions = controller.Current.SimulationActions;
            var list = new List<Action>();
            if ((actions & SimulationActions.MouseMove) != 0) list.Add(MicroMouseMovement);
            if ((actions & SimulationActions.MouseWheel) != 0) list.Add(() => SendMouseWheel(random.Next(2) == 0 ? 120 : -120));
            if ((actions & SimulationActions.F15Key) != 0) list.Add(() => SendKeyPress(0x7E));
            if ((actions & SimulationActions.AltTab) != 0) list.Add(SwitchWindowActivity);
            if (list.Count == 0) list.Add(MicroMouseMovement);
            list[random.Next(list.Count)]();
        }
        private void MicroMouseMovement()
        {
            PointNative point; GetCursorPos(out point); int x = point.X + random.Next(-12, 13); int y = point.Y + random.Next(-12, 13);
            for (int i = 1; i <= 5; i++) { SetCursorPos(point.X + (x - point.X) * i / 5, point.Y + (y - point.Y) * i / 5); Thread.Sleep(random.Next(12, 30)); }
        }
        private void SendMouseWheel(int delta) { SendInput(1, new[] { new Input { Type = 0, Union = new InputUnion { Mouse = new MouseInput { Data = (uint)delta, Flags = 0x0800 } } } }, Marshal.SizeOf(typeof(Input))); }
        private void SendKeyPress(byte key) { keybd_event(key, 0, 0, UIntPtr.Zero); Thread.Sleep(random.Next(20, 50)); keybd_event(key, 0, 2, UIntPtr.Zero); }
        private void SwitchWindowActivity() { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x09, 0, 0, UIntPtr.Zero); Thread.Sleep(60); keybd_event(0x09, 0, 2, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); }
    }
}
