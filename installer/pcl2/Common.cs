using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace PclSetup
{
    public interface IIntegration
    {
        bool IsRegistered { get; }
        void Register(string root);
        void Unregister(string root);
        void CreateShortcut(bool desktop, string root);
        void RemoveShortcut(bool desktop, string root);
    }

    public sealed class WindowsIntegration : IIntegration
    {
        public const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PCL2";
        public bool IsRegistered { get { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegistryPath)) return k != null; } }
        public void Register(string root)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                k.SetValue("DisplayName", "Plain Craft Launcher 2");
                k.SetValue("DisplayVersion", "2.13.1.1");
                k.SetValue("InstallLocation", root);
                k.SetValue("UninstallString", Quote(Path.Combine(root, "Uninstall.exe")));
                k.SetValue("DisplayIcon", Path.Combine(root, "Plain Craft Launcher 2.exe"));
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", 6200, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
        public void Unregister(string root)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegistryPath))
            {
                if (k == null || !String.Equals(k.GetValue("InstallLocation") as string, root, StringComparison.OrdinalIgnoreCase)) return;
            }
            Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
        }
        public static string Quote(string value) { return "\"" + value + "\""; }
        private static string ShortcutPath(bool desktop)
        {
            return Path.Combine(Environment.GetFolderPath(desktop ? Environment.SpecialFolder.DesktopDirectory : Environment.SpecialFolder.Programs), "Plain Craft Launcher 2.lnk");
        }
        public void CreateShortcut(bool desktop, string root)
        {
            CreateShortcutAt(ShortcutPath(desktop), root);
        }
        internal static void CreateShortcutAt(string path, string root)
        {
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException("A shortcut already exists: " + path);
            // Reserve the name so an existing shortcut is never overwritten.
            using (File.Create(path)) { }
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type t = shortcut.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(root, "Plain Craft Launcher 2.exe") });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { root });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(root, "Plain Craft Launcher 2.exe") + ",0" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { File.Delete(path); throw; }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }
        public void RemoveShortcut(bool desktop, string root)
        {
            RemoveShortcutAt(ShortcutPath(desktop), root);
        }
        internal static void RemoveShortcutAt(string path, string root)
        {
            if (!File.Exists(path)) return;
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                string target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
                if (String.Equals(target, Path.Combine(root, "Plain Craft Launcher 2.exe"), StringComparison.OrdinalIgnoreCase)) File.Delete(path);
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    public static class Engine
    {
        public const string StateFile = "pcl2-install.state";
        public static readonly string[] Files = { "Plain Craft Launcher 2.exe", "PCL/JavaWrapper.jar", "PCL/libjlw-x86_64-1.4.4.dll", "PCL/libwebp.dll", "PCL/Setup.ini" };
        public static readonly string[] Dirs = { "PCL/", "PCL/Musics/", "PCL/Pictures/" };
        public static string ValidateRoot(string root)
        {
            if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root) || root.IndexOf('"') >= 0) throw new IOException("Choose a full local installation path.");
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            if (root.StartsWith(@"\\") || root.Length <= 3) throw new IOException("Choose a folder on a local drive, not the drive root.");
            for (string p = root; !String.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
                if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Installation paths containing symbolic links or junctions are not supported.");
            return root;
        }
        private static string Local(string root, string relative) { return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)); }
        private static void RemoveEmptyDirectories(string root)
        {
            foreach (string d in Dirs.Reverse())
            {
                string path = Local(root, d.TrimEnd('/'));
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
            }
            if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
        public static void Install(string destination, bool desktop, bool menu, IIntegration integration, Action<int, string> progress)
        {
            string root = ValidateRoot(destination);
            if (integration.IsRegistered) throw new IOException("PCL2 is already registered for this user. Uninstall it from Windows Settings first.");
            if (File.Exists(root) || (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()))
                throw new IOException("Choose an empty folder. Existing launcher files and accounts will not be overwritten.");
            List<string> created = new List<string>();
            bool registered = false, deskCreated = false, menuCreated = false;
            Directory.CreateDirectory(root);
            try
            {
                using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("PCL2.Payload.zip"))
                using (ZipArchive zip = new ZipArchive(payload, ZipArchiveMode.Read))
                {
                    var expected = new HashSet<string>(Files.Concat(Dirs), StringComparer.Ordinal);
                    if (zip.Entries.Count != expected.Count || zip.Entries.Any(e => !expected.Remove(e.FullName)) || expected.Count != 0)
                        throw new InvalidDataException("The embedded payload is invalid.");
                    foreach (string d in Dirs) Directory.CreateDirectory(Local(root, d));
                    for (int i = 0; i < Files.Length; i++)
                    {
                        string name = Files[i], path = Local(root, name);
                        progress(10 + i * 12, "Installing " + Path.GetFileName(path));
                        using (Stream src = zip.GetEntry(name).Open())
                        using (FileStream dst = new FileStream(path, FileMode.CreateNew)) { created.Add(path); src.CopyTo(dst); }
                        using (SHA256 sha = SHA256.Create())
                        using (Stream file = File.OpenRead(path))
                            if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant() != PayloadHashes.Values[name])
                                throw new InvalidDataException("Payload integrity check failed: " + name);
                    }
                }
                string uninstall = Path.Combine(root, "Uninstall.exe");
                using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream("PCL2.Uninstall.exe"))
                using (FileStream dst = new FileStream(uninstall, FileMode.CreateNew)) { created.Add(uninstall); src.CopyTo(dst); }
                progress(78, "Creating shortcuts");
                if (desktop) { integration.CreateShortcut(true, root); deskCreated = true; }
                if (menu) { integration.CreateShortcut(false, root); menuCreated = true; }
                string state = Path.Combine(root, StateFile);
                using (FileStream f = new FileStream(state, FileMode.CreateNew))
                using (StreamWriter w = new StreamWriter(f))
                { created.Add(state); w.Write("PCL2-INSTALL-1\n" + (desktop ? "1" : "0") + "\n" + (menu ? "1" : "0") + "\n"); }
                progress(90, "Registering uninstaller");
                registered = true; integration.Register(root);
                progress(100, "Installation complete");
            }
            catch
            {
                if (registered) integration.Unregister(root);
                if (menuCreated) integration.RemoveShortcut(false, root);
                if (deskCreated) integration.RemoveShortcut(true, root);
                foreach (string path in created.AsEnumerable().Reverse()) if (File.Exists(path)) File.Delete(path);
                RemoveEmptyDirectories(root);
                throw;
            }
        }
        public static bool Uninstall(string destination, IIntegration integration)
        {
            string root = ValidateRoot(destination);
            string[] state = File.ReadAllLines(Path.Combine(root, StateFile));
            if (state.Length != 3 || state[0] != "PCL2-INSTALL-1" || (state[1] != "0" && state[1] != "1") || (state[2] != "0" && state[2] != "1"))
                throw new IOException("The PCL2 installation record is missing or invalid.");
            foreach (string d in Dirs) ValidateRoot(Local(root, d));
            // Check all installed files for locks before changing registration or removing files.
            foreach (string file in Files.Concat(new[] { "Uninstall.exe", StateFile }))
            {
                string path = Local(root, file);
                if (File.Exists(path)) using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            if (state[1] == "1") integration.RemoveShortcut(true, root);
            if (state[2] == "1") integration.RemoveShortcut(false, root);
            foreach (string file in Files.Concat(new[] { "Uninstall.exe" }))
            {
                string path = Local(root, file);
                if (File.Exists(path)) File.Delete(path);
            }
            integration.Unregister(root);
            File.Delete(Path.Combine(root, StateFile));
            RemoveEmptyDirectories(root);
            return Directory.Exists(root);
        }
    }
}
