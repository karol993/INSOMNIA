using System;
using System.Windows.Forms;

namespace Insomnia
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { using (var context = new TrayApplicationContext()) Application.Run(context); }
            catch (Exception ex) when (ex is System.Configuration.ConfigurationErrorsException ||
                ex is ArgumentException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show("Nie można odczytać konfiguracji. Nie została nadpisana.\n" + ex.Message,
                    "Insomnia", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
