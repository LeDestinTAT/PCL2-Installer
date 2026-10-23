using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Forms;
using PclSetup;

sealed class SandboxIntegration : IIntegration
{
    public bool IsRegistered { get; set; }
    public bool Desktop, Menu, FailMenu, FailRegister;
    public string Root;
    public void Register(string root) { IsRegistered = true; Root = root; if (FailRegister) throw new IOException("Simulated registration failure"); }
    public void Unregister(string root) { IsRegistered = false; }
    public void CreateShortcut(bool desktop, string root)
    {
        if (!desktop && FailMenu) throw new IOException("Simulated shortcut failure");
        if (desktop) Desktop = true; else Menu = true;
    }
    public void RemoveShortcut(bool desktop, string root) { if (desktop) Desktop = false; else Menu = false; }
}

static class PclInstallerTests
{
    static int passed;
    static string testRoot;
    static void Assert(bool condition, string text) { if (!condition) throw new Exception(text); }
    static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    static void MustFail(Action action) { bool failed = false; try { action(); } catch (IOException) { failed = true; } catch (InvalidDataException) { failed = true; } Assert(failed, "Expected rejection"); }
    static void Install(string path, SandboxIntegration integration, bool desktop, bool menu)
    { Engine.Install(path, desktop, menu, integration, delegate(int value, string status) { Assert(value >= 0 && value <= 100, "Invalid progress"); }); }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            testRoot = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(testRoot);
            Test("Install: exact files, hashes, empty assets, default INI, progress, shortcuts and registration", delegate
            {
                string root = Path.Combine(testRoot, "clean install");
                SandboxIntegration integration = new SandboxIntegration();
                List<int> progress = new List<int>();
                Engine.Install(root, true, true, integration, delegate(int value, string status) { progress.Add(value); });
                Assert(progress.Last() == 100 && progress.SequenceEqual(progress.OrderBy(x => x)), "Progress did not finish monotonically");
                string[] actual = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(p => p.Substring(root.Length + 1).Replace('\\', '/')).OrderBy(x => x).ToArray();
                string[] expected = Engine.Files.Concat(new[] { "Uninstall.exe", Engine.StateFile }).OrderBy(x => x).ToArray();
                Assert(actual.SequenceEqual(expected), "Unexpected installed files");
                foreach (string file in Engine.Files)
                    using (SHA256 hash = SHA256.Create())
                    using (Stream input = File.OpenRead(Path.Combine(root, file)))
                        Assert(BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant() == PayloadHashes.Values[file], "Installed file mismatch");
                Assert(File.ReadAllText(Path.Combine(root, "PCL/Setup.ini")).Trim() == "SystemSystemUpdate:3", "INI does not disable updates");
                Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(root, "PCL/Musics")).Any(), "Music folder is not empty");
                Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(root, "PCL/Pictures")).Any(), "Pictures folder is not empty");
                Assert(integration.IsRegistered && integration.Desktop && integration.Menu && integration.Root == root, "Integration missing");
                Assert(!Engine.Uninstall(root, integration), "Clean uninstall left a directory");
                Assert(!integration.IsRegistered && !integration.Desktop && !integration.Menu, "Integration was not removed");
            });
            Test("Optional shortcuts disabled and apostrophe / Unicode install path", delegate
            {
                string root = Path.Combine(testRoot, "PCL user's \u6d4b\u8bd5"); SandboxIntegration integration = new SandboxIntegration();
                Install(root, integration, false, false);
                Assert(!integration.Desktop && !integration.Menu, "Unrequested shortcuts created");
                Engine.Uninstall(root, integration);
            });
            Test("Occupied destination rejected without modifying user files", delegate
            {
                string root = Path.Combine(testRoot, "occupied"); Directory.CreateDirectory(root);
                string keep = Path.Combine(root, "keep.txt"); File.WriteAllText(keep, "preserve me");
                MustFail(delegate { Install(root, new SandboxIntegration(), true, true); });
                Assert(File.ReadAllText(keep) == "preserve me" && Directory.GetFiles(root).Length == 1, "Existing data changed");
            });
            Test("Existing registration rejected", delegate
            {
                string root = Path.Combine(testRoot, "registered"); SandboxIntegration integration = new SandboxIntegration { IsRegistered = true };
                MustFail(delegate { Install(root, integration, true, true); });
                Assert(!Directory.Exists(root) && integration.IsRegistered, "Existing registration changed");
            });
            Test("Shortcut failure rolls back files and earlier shortcut", delegate
            {
                string root = Path.Combine(testRoot, "shortcut-failure"); SandboxIntegration integration = new SandboxIntegration { FailMenu = true };
                MustFail(delegate { Install(root, integration, true, true); });
                Assert(!Directory.Exists(root) && !integration.IsRegistered && !integration.Desktop, "Rollback incomplete");
            });
            Test("Registration failure rolls back files and both shortcuts", delegate
            {
                string root = Path.Combine(testRoot, "registry-failure"); SandboxIntegration integration = new SandboxIntegration { FailRegister = true };
                MustFail(delegate { Install(root, integration, true, true); });
                Assert(!Directory.Exists(root) && !integration.IsRegistered && !integration.Desktop && !integration.Menu, "Rollback incomplete");
            });
            Test("Corrupt payload hash detected and extracted files rolled back", delegate
            {
                string root = Path.Combine(testRoot, "integrity-failure"); string name = Engine.Files[0]; string saved = PayloadHashes.Values[name];
                try { PayloadHashes.Values[name] = "invalid"; MustFail(delegate { Install(root, new SandboxIntegration(), false, false); }); }
                finally { PayloadHashes.Values[name] = saved; }
                Assert(!Directory.Exists(root), "Corrupt install left files");
            });
            Test("Uninstall preserves games and user-added files", delegate
            {
                string root = Path.Combine(testRoot, "preserve-data"); SandboxIntegration integration = new SandboxIntegration(); Install(root, integration, true, true);
                Directory.CreateDirectory(Path.Combine(root, ".minecraft/saves"));
                File.WriteAllText(Path.Combine(root, ".minecraft/saves/world.txt"), "world");
                File.WriteAllText(Path.Combine(root, "PCL/Pictures/custom.txt"), "custom");
                Assert(Engine.Uninstall(root, integration), "Expected preserved user data");
                Assert(File.ReadAllText(Path.Combine(root, ".minecraft/saves/world.txt")) == "world", "World deleted");
                Assert(File.ReadAllText(Path.Combine(root, "PCL/Pictures/custom.txt")) == "custom", "Custom asset deleted");
                Assert(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 2, "Installation files left behind");
            });
            Test("Locked launcher prevents partial uninstall", delegate
            {
                string root = Path.Combine(testRoot, "locked"); SandboxIntegration integration = new SandboxIntegration(); Install(root, integration, true, true);
                using (FileStream locked = new FileStream(Path.Combine(root, Engine.Files[0]), FileMode.Open, FileAccess.Read, FileShare.None))
                    MustFail(delegate { Engine.Uninstall(root, integration); });
                Assert(integration.IsRegistered && integration.Desktop && File.Exists(Path.Combine(root, Engine.StateFile)), "Locked uninstall modified installation");
                Engine.Uninstall(root, integration);
            });
            Test("Invalid ownership record prevents uninstall", delegate
            {
                string root = Path.Combine(testRoot, "bad-state"); SandboxIntegration integration = new SandboxIntegration(); Install(root, integration, false, false);
                string state = Path.Combine(root, Engine.StateFile); File.WriteAllText(state, "invalid");
                MustFail(delegate { Engine.Uninstall(root, integration); });
                Assert(File.Exists(Path.Combine(root, Engine.Files[0])) && integration.IsRegistered, "Invalid manifest allowed deletion");
                File.WriteAllText(state, "PCL2-INSTALL-1\n0\n0\n"); Engine.Uninstall(root, integration);
            });
            Test("Unsafe root and relative paths rejected", delegate
            {
                foreach (string root in new[] { @"C:\", "relative", @"\\server\share\PCL2", "" })
                    MustFail(delegate { Engine.ValidateRoot(root); });
            });
            Test("Real Windows shortcut target, working directory, collision protection and removal", delegate
            {
                string root = Path.Combine(testRoot, "shortcut target"); Directory.CreateDirectory(root);
                string shortcutPath = Path.Combine(testRoot, "PCL2-test.lnk");
                WindowsIntegration.CreateShortcutAt(shortcutPath, root);
                object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                object shortcut = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                try
                {
                    Type type = shortcut.GetType();
                    string target = (string)type.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null);
                    string working = (string)type.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.GetProperty, null, shortcut, null);
                    Assert(target == Path.Combine(root, Engine.Files[0]) && working == root, "Shortcut properties incorrect");
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
                MustFail(delegate { WindowsIntegration.CreateShortcutAt(shortcutPath, root); });
                WindowsIntegration.RemoveShortcutAt(shortcutPath, Path.Combine(testRoot, "different-install"));
                Assert(File.Exists(shortcutPath), "Unrelated shortcut removed");
                WindowsIntegration.RemoveShortcutAt(shortcutPath, root);
                Assert(!File.Exists(shortcutPath), "Owned shortcut left behind");
            });
            Test("GUI defaults, required options and layout render", delegate
            {
                Application.EnableVisualStyles();
                using (SetupForm form = new SetupForm())
                {
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-3000, -3000); form.Show(); Application.DoEvents();
                    CheckBox[] options = form.Controls.OfType<CheckBox>().ToArray();
                    Assert(options.Length == 3 && options.All(c => c.Checked), "GUI options missing or defaults wrong");
                    Assert(form.Controls.OfType<TextBox>().Single().Text == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PCL2"), "Default path wrong");
                    Assert(form.Controls.OfType<ProgressBar>().Count() == 1, "Progress missing");
                    foreach (Control control in form.Controls) Assert(form.ClientRectangle.Contains(control.Bounds), "Control outside form");
                    using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(0, 0, form.Width, form.Height)); image.Save(Path.Combine(testRoot, "installer-preview.png")); }
                }
            });
            Console.WriteLine("All " + passed + " tests passed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
