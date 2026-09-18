using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Insomnia
{
    internal interface IStartupManager
    {
        bool IsStartupEnabled();
        void SetStartup(bool enable);
    }

    internal sealed class RegistryStartupManager : IStartupManager
    {
        private readonly string runKeyPath;
        private readonly string appName;
        private readonly string exePath;

        public RegistryStartupManager(string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
                                     string appName = "Insomnia",
                                     string exePath = null)
        {
            this.runKeyPath = runKeyPath;
            this.appName = appName;
            this.exePath = exePath ?? Application.ExecutablePath;
        }

        public bool IsStartupEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(runKeyPath, false))
                {
                    if (key == null) return false;
                    var val = key.GetValue(appName) as string;
                    if (string.IsNullOrEmpty(val)) return false;
                    string unquoted = val.Trim().Trim('\"');
                    return string.Equals(unquoted, exePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public void SetStartup(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(runKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
                {
                    if (key == null) throw new InvalidOperationException("Nie można otworzyć klucza rejestru Run.");
                    if (enable)
                    {
                        key.SetValue(appName, $"\"{exePath}\"");
                    }
                    else
                    {
                        if (key.GetValue(appName) != null)
                        {
                            key.DeleteValue(appName, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Nie udało się zaktualizować wpisu autostartu: " + ex.Message, ex);
            }
        }
    }

    internal static class StartupManager
    {
        private static IStartupManager instance = new RegistryStartupManager();

        public static IStartupManager Instance
        {
            get { return instance; }
            set { instance = value ?? new RegistryStartupManager(); }
        }

        public static bool IsStartupEnabled() { return Instance.IsStartupEnabled(); }
        public static void SetStartup(bool enable) { Instance.SetStartup(enable); }
    }

    internal sealed class MemoryStartupManager : IStartupManager
    {
        public bool Enabled { get; set; }
        public bool Fail { get; set; }

        public MemoryStartupManager(bool initial = false) { Enabled = initial; }

        public bool IsStartupEnabled() => Enabled;

        public void SetStartup(bool enable)
        {
            if (Fail) throw new InvalidOperationException("Test: registry unavailable");
            Enabled = enable;
        }
    }
}
