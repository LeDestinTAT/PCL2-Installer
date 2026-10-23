using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("PCL2 Uninstaller")]
[assembly: System.Reflection.AssemblyVersion("2.13.1.1")]
namespace PclSetup
{
    static class Uninstaller
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            bool helper = args.Length == 3 && args[0] == "/cleanup";
            try
            {
                if (helper)
                {
                    int pid = Int32.Parse(args[2]);
                    try { using (Process parent = Process.GetProcessById(pid)) if (!parent.WaitForExit(60000)) throw new IOException("The uninstaller is still running. Please try again."); } catch (ArgumentException) { }
                    bool remains = Engine.Uninstall(args[1], new WindowsIntegration());
                    MessageBox.Show("PCL2 has been uninstalled." + (remains ? "\n\nGames and other files you added remain in:\n" + args[1] : ""), "PCL2 Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    string root = Engine.ValidateRoot(Application.StartupPath);
                    if (!File.Exists(Path.Combine(root, Engine.StateFile))) throw new IOException("Run Uninstall.exe from your PCL2 installation folder.");
                    if (MessageBox.Show("Uninstall Plain Craft Launcher 2?\n\nClose PCL2 before continuing. Games and other files added after installation will be kept.", "PCL2 Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    string temporary = Path.Combine(Path.GetTempPath(), "PCL2-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                    File.Copy(Application.ExecutablePath, temporary, false);
                    Process.Start(new ProcessStartInfo(temporary, "/cleanup " + WindowsIntegration.Quote(root) + " " + Process.GetCurrentProcess().Id) { UseShellExecute = false });
                }
            }
            catch (Exception ex) { MessageBox.Show("Unable to uninstall PCL2. Close the launcher and try again.\n\n" + ex.Message, "PCL2 Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally
            {
                if (helper)
                {
                    // A hidden, single PowerShell process removes only this temporary helper after exit.
                    string self = Application.ExecutablePath;
                    string prefix = Path.Combine(Path.GetTempPath(), "PCL2-Uninstall-");
                    if (self.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string command = "$ErrorActionPreference='SilentlyContinue'; Wait-Process -Id " + Process.GetCurrentProcess().Id + "; Remove-Item -LiteralPath '" + self.Replace("'", "''") + "' -Force";
                        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command))) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                    }
                }
            }
        }
    }
}
