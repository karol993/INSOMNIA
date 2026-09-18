using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Insomnia
{
    internal sealed partial class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem statusItem = new ToolStripMenuItem { Enabled = false };
        private readonly ToolStripMenuItem toggleItem = new ToolStripMenuItem();
        private readonly Timer activityTimer = new Timer { Interval = 30000 };
        private readonly Timer stateTimer = new Timer { Interval = 1000 };
        private readonly Timer scanTimer = new Timer { Interval = 45000 };
        private readonly WlanScanner scanner = new WlanScanner();
        private readonly WifiState wifi = new WifiState();
        private readonly ConfigurationController controller = new ConfigurationController(new ConfigurationStore());
        private SettingsForm settingsForm;
        private bool closing;
        private Task scanTask = Task.CompletedTask;

        public TrayApplicationContext()
        {
            toggleItem.Click += ToggleProgram;
            menu.Items.Add(statusItem);
            menu.Items.Add(toggleItem);
            menu.Items.Add("Ustawienia…", null, OpenSettings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Wyjdź", null, async (s, e) => await ExitAsync());
            trayIcon = new NotifyIcon { Icon = Properties.Resources.insomnia, ContextMenuStrip = menu, Visible = true };
            trayIcon.DoubleClick += OpenSettings;
            activityTimer.Tick += (s, e) => {
                // Re-evaluate at the activity boundary, not just at the status timer tick.
                if (!closing && RefreshStatus().IsActive && GetIdleTime() > 25000) PerformStealthActivity();
            };
            stateTimer.Tick += (s, e) => RefreshStatus();
            scanTimer.Tick += (s, e) => BeginWifiScan();
            activityTimer.Start(); stateTimer.Start(); scanTimer.Start();
            RefreshStatus();
            Application.Idle += FirstIdle;
        }
        private void FirstIdle(object sender, EventArgs e) { Application.Idle -= FirstIdle; BeginWifiScan(); }
        private void ToggleProgram(object sender, EventArgs e)
        {
            try { controller.Toggle(); RefreshStatus(); }
            catch (Exception ex) { MessageBox.Show("Nie zapisano zmiany stanu.\n" + ex.Message, "Insomnia", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void OpenSettings(object sender, EventArgs e)
        {
            if (closing) return;
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
                settingsForm.Activate();
                return;
            }
            settingsForm = new SettingsForm(controller);
            settingsForm.SettingsApplied += (s, args) => { RefreshStatus(); BeginWifiScan(); };
            settingsForm.RefreshRequested += (s, args) => BeginWifiScan();
            settingsForm.FormClosed += (s, args) => { settingsForm.Dispose(); settingsForm = null; };
            RefreshStatus();
            settingsForm.Show();
        }
        private void BeginWifiScan()
        {
            if (closing || !scanTask.IsCompleted) return;
            scanTask = ScanWifiAsync();
        }
        private async Task ScanWifiAsync()
        {
            wifi.Scanning = true;
            RefreshStatus();
            try
            {
                var result = await scanner.ScanAsync();
                if (!closing) wifi.Accept(result, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                if (!closing) wifi.Accept(new WifiScanResult { Outcome = ScanOutcome.Failed, Error = 31 }, DateTimeOffset.UtcNow);
            }
            finally
            {
                wifi.Scanning = false;
                if (!closing) RefreshStatus();
            }
        }
        private AppStatus RefreshStatus()
        {
            var now = DateTimeOffset.UtcNow;
            var status = AppStatus.Calculate(controller.Current, wifi.Networks(now), DateTime.Now);
            statusItem.Text = "Status: " + (status.IsActive ? "Aktywny" : "Nieaktywny");
            statusItem.ToolTipText = status.Description + (wifi.Complete(now) ? "" : "\nKontrola Wi-Fi niedostępna");
            toggleItem.Text = controller.Current.ManuallyEnabled ? "Wyłącz program" : "Włącz program";
            string text = "Insomnia — " + status.Description;
            if (status.IsActive && !wifi.Complete(now)) text += " — Kontrola Wi-Fi niedostępna";
            trayIcon.Text = text.Length <= 63 ? text : text.Substring(0, 62) + "…";
            settingsForm?.UpdateStatus(status, wifi, now);
            return status;
        }
        private async Task ExitAsync()
        {
            if (closing) return;
            if (settingsForm != null)
            {
                settingsForm.Close(); // Its unsaved-change prompt may cancel exit.
                if (settingsForm != null) return;
            }
            closing = true;
            activityTimer.Stop(); stateTimer.Stop(); scanTimer.Stop();
            menu.Enabled = false;
            await scanner.StopAsync();
            await scanTask;
            ExitThread();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Application.Idle -= FirstIdle;
                activityTimer.Dispose(); stateTimer.Dispose(); scanTimer.Dispose();
                trayIcon.Visible = false; trayIcon.Dispose(); menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
