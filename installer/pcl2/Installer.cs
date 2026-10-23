using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("PCL2 Setup")]
[assembly: System.Reflection.AssemblyVersion("2.13.1.1")]
[assembly: System.Reflection.AssemblyFileVersion("2.13.1.1")]

namespace PclSetup
{
    public sealed class SetupForm : Form
    {
        readonly TextBox path = new TextBox();
        readonly CheckBox desktop = new CheckBox(), menu = new CheckBox(), launch = new CheckBox();
        readonly ProgressBar progress = new ProgressBar();
        readonly Label status = new Label();
        readonly Button install = new Button(), browse = new Button();
        bool busy, complete;
        public SetupForm()
        {
            Text = "Plain Craft Launcher 2 Setup";
            ClientSize = new Size(620, 410); Font = new Font("Segoe UI", 10);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; BackColor = Color.White;
            Label title = new Label { Text = "Install Plain Craft Launcher 2", Font = new Font("Segoe UI", 19, FontStyle.Bold), AutoSize = true, Location = new Point(25, 22) };
            Label subtitle = new Label { Text = "Version 2.13.1.1  |  Installs for your Windows account", AutoSize = true, Location = new Point(27, 66) };
            Label pathLabel = new Label { Text = "Installation folder", AutoSize = true, Location = new Point(27, 112) };
            path.SetBounds(28, 139, 463, 28);
            path.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PCL2");
            browse.Text = "Browse..."; browse.SetBounds(501, 137, 90, 30);
            browse.Click += delegate { using (FolderBrowserDialog d = new FolderBrowserDialog { Description = "Select an empty installation folder", SelectedPath = path.Text }) if (d.ShowDialog(this) == DialogResult.OK) path.Text = d.SelectedPath; };
            desktop.Text = "Create a Desktop shortcut"; desktop.Checked = true; desktop.SetBounds(28, 184, 550, 26);
            menu.Text = "Create a Start Menu shortcut"; menu.Checked = true; menu.SetBounds(28, 215, 550, 26);
            launch.Text = "Launch PCL2 when I click Finish"; launch.Checked = true; launch.SetBounds(28, 246, 550, 26);
            progress.SetBounds(28, 291, 563, 18); status.SetBounds(28, 318, 563, 29); status.Text = "Ready. Please choose an empty installation folder.";
            install.Text = "Install"; install.SetBounds(476, 360, 115, 32); install.Click += InstallClick;
            Controls.AddRange(new Control[] { title, subtitle, pathLabel, path, browse, desktop, menu, launch, progress, status, install });
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            AcceptButton = install;
        }
        void InstallClick(object sender, EventArgs e)
        {
            if (complete)
            {
                if (launch.Checked)
                {
                    try { Process.Start(new ProcessStartInfo(Path.Combine(path.Text, "Plain Craft Launcher 2.exe")) { WorkingDirectory = path.Text, UseShellExecute = true }); }
                    catch (Exception ex) { MessageBox.Show(this, "Installed successfully, but PCL2 could not be opened.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
                Close(); return;
            }
            string root = path.Text; bool desk = desktop.Checked, start = menu.Checked;
            busy = true; install.Enabled = path.Enabled = browse.Enabled = desktop.Enabled = menu.Enabled = launch.Enabled = false;
            BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += delegate { Engine.Install(root, desk, start, new WindowsIntegration(), delegate(int value, string text) { worker.ReportProgress(value, text); }); };
            worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs args) { progress.Value = args.ProgressPercentage; status.Text = (string)args.UserState; };
            worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs args)
            {
                busy = false; install.Enabled = launch.Enabled = true;
                if (args.Error != null)
                {
                    path.Enabled = browse.Enabled = desktop.Enabled = menu.Enabled = true; progress.Value = 0; status.Text = "Installation did not complete.";
                    MessageBox.Show(this, args.Error.Message, "Unable to install PCL2", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else { complete = true; path.Text = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); install.Text = "Finish"; progress.Value = 100; status.Text = "PCL2 is installed. Click Finish to continue."; }
                worker.Dispose();
            };
            worker.RunWorkerAsync();
        }
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new SetupForm());
        }
    }
}
