using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Diagnostics;

namespace Insomnia
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplicationContext());
        }
    }

    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private readonly System.Windows.Forms.Timer activityTimer;
        private readonly Random rand = new Random();
        private POINT lastPos;
        private int lastPattern = -1;
        private readonly Stopwatch patternStopwatch = new Stopwatch();

        [StructLayout(LayoutKind.Sequential)]
        struct LastInputInfo
        {
            public uint cbSize;
            public uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInputInfo plii);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        // ------------------- POPRAWIONE SENDINPUT -------------------
        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int INPUT_MOUSE = 0;
        private const int MOUSEEVENTF_MOVE = 0x0001;
        private const int MOUSEEVENTF_WHEEL = 0x0800;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        // -------------------------------------------------

        private void SmoothMove(int targetX, int targetY, int steps = 15)
        {
            GetCursorPos(out POINT currentPos);
            double dx = (targetX - currentPos.X) / (double)steps;
            double dy = (targetY - currentPos.Y) / (double)steps;

            for (int i = 0; i < steps; i++)
            {
                int shakeX = rand.Next(-1, 2);
                int shakeY = rand.Next(-1, 2);

                int moveX = (int)(currentPos.X + dx + shakeX);
                int moveY = (int)(currentPos.Y + dy + shakeY);

                SetCursorPos(moveX, moveY);
                Thread.Sleep(rand.Next(10, 25));
            }
        }

        private void SendMouseWheel(int delta)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].U.mi = new MOUSEINPUT
            {
                mouseData = (uint)delta,
                dwFlags = MOUSEEVENTF_WHEEL,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private void SendKeyPress(byte keyCode)
        {
            keybd_event(keyCode, 0, 0, UIntPtr.Zero);
            Thread.Sleep(rand.Next(20, 60));
            keybd_event(keyCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static uint GetIdleTime()
        {
            LastInputInfo lastInputInfo = new LastInputInfo();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);
            GetLastInputInfo(ref lastInputInfo);
            return (uint)Environment.TickCount - lastInputInfo.dwTime;
        }

        public TrayApplicationContext()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = Properties.Resources.insomnia,
                ContextMenuStrip = new ContextMenuStrip(),
                Text = "Insomnia - Ultimate Stealth Mode",
                Visible = true
            };

            trayIcon.ContextMenuStrip.Items.Add("Wyjdź", null, (s, e) => Exit());

            activityTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            activityTimer.Tick += ActivityTimer_Tick;
            activityTimer.Start();
            patternStopwatch.Start();
        }

        private void ActivityTimer_Tick(object sender, EventArgs e)
        {
            uint idleTime = GetIdleTime();
            if (idleTime > 25000)
            {
                PerformStealthActivity();
            }
        }

        private void PerformStealthActivity()
        {
            int action = rand.Next(100);
            
            if (action < 40)
            {
                MicroMouseMovement();
            }
            else if (action < 70)
            {
                ScrollActivity();
            }
            else if (action < 85)
            {
                KeyboardActivity();
            }
            else
            {
                SwitchWindowActivity();
            }
        }

        private void MicroMouseMovement()
        {
            GetCursorPos(out lastPos);

            int pattern;
            do { pattern = rand.Next(5); } while (pattern == lastPattern);
            lastPattern = pattern;

            int radius = rand.Next(5, 25);

            switch (pattern)
            {
                case 0:
                    int dirX = rand.Next(-3, 4);
                    int dirY = rand.Next(-3, 4);
                    for (int i = 0; i < 10; i++)
                    {
                        SmoothMove(lastPos.X + dirX * i, lastPos.Y + dirY * i, 3);
                        Thread.Sleep(rand.Next(30, 100));
                    }
                    break;

                case 1:
                    for (int i = 0; i < 15; i++)
                    {
                        SetCursorPos(lastPos.X + rand.Next(-2, 3), lastPos.Y + rand.Next(-2, 3));
                        Thread.Sleep(rand.Next(20, 60));
                    }
                    break;

                case 2:
                    for (int i = 0; i < 20; i++)
                    {
                        double angle = i * 0.3;
                        int x = lastPos.X + (int)(radius * Math.Cos(angle));
                        int y = lastPos.Y + (int)(radius * Math.Sin(angle * 0.7));
                        SmoothMove(x, y, 2);
                    }
                    break;

                case 3:
                    int offsetX = rand.Next(-20, 20);
                    int offsetY = rand.Next(-20, 20);
                    SmoothMove(lastPos.X + offsetX, lastPos.Y + offsetY);
                    Thread.Sleep(rand.Next(200, 800));
                    SmoothMove(lastPos.X, lastPos.Y);
                    break;

                case 4:
                    for (int i = 0; i < 8; i++)
                    {
                        int targetX = lastPos.X + rand.Next(-8, 8);
                        int targetY = lastPos.Y + rand.Next(-8, 8);
                        SmoothMove(targetX, targetY, 5);
                        Thread.Sleep(rand.Next(50, 150));
                    }
                    break;
            }
        }

        private void ScrollActivity()
        {
            int scrollAmount = rand.Next(1, 4) * 120;
            bool direction = rand.Next(2) == 0;
            SendMouseWheel(direction ? scrollAmount : -scrollAmount);

            if (rand.Next(4) == 0)
            {
                Thread.Sleep(rand.Next(300, 1000));
                SendMouseWheel(direction ? -scrollAmount / 2 : scrollAmount / 2);
            }
        }

        private void KeyboardActivity()
        {
            byte[] safeKeys = {
                0x10, // Shift
                0x11, // Ctrl
                0x12, // Alt
                0x14, // Caps Lock
                0x90, // Num Lock
                0x2D, // Insert
                0x91, // Scroll Lock
                0x1B  // Escape
            };

            byte keyCode = safeKeys[rand.Next(safeKeys.Length)];
            bool isToggleKey = (keyCode == 0x14 || keyCode == 0x90 || keyCode == 0x91 || keyCode == 0x2D);

            SendKeyPress(keyCode);
            
            if (isToggleKey)
            {
                Thread.Sleep(rand.Next(100, 300));
                SendKeyPress(keyCode);
            }
        }

        private void SwitchWindowActivity()
        {
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            Thread.Sleep(rand.Next(50, 150));
            keybd_event(0x09, 0, 0, UIntPtr.Zero);
            Thread.Sleep(rand.Next(50, 150));
            keybd_event(0x09, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(rand.Next(50, 150));
            keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            Thread.Sleep(rand.Next(200, 800));

            if (rand.Next(2) == 0)
            {
                keybd_event(0x12, 0, 0, UIntPtr.Zero);
                Thread.Sleep(rand.Next(50, 150));
                keybd_event(0x09, 0, 0, UIntPtr.Zero);
                Thread.Sleep(rand.Next(50, 150));
                keybd_event(0x09, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(rand.Next(50, 150));
                keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        private void Exit()
        {
            activityTimer.Stop();
            trayIcon.Visible = false;
            Application.Exit();
        }
    }
}