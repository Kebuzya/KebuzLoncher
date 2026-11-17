using System;
using System.IO;
using System.Text;

namespace KebuzLoncher
{
    internal static class LauncherData
    {
        private static readonly object sync = new object();
        private static bool initialized;
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kebuz", "KebuzLoncher");
        public static string Backups => Path.Combine(Root, "backups");
        public static string Logs => Path.Combine(Root, "logs");

        public static void Initialize()
        {
            lock (sync)
            {
                if (initialized) return;
                Migrate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KebuzLoncher"), Root);
                initialized = true;
            }
        }

        internal static void Migrate(string legacy, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string folder in new[] { "cache", "backups", "logs", Path.Combine("logs", "crashes") })
                Directory.CreateDirectory(Path.Combine(destination, folder));
            string marker = Path.Combine(destination, ".migration-v1");
            if (File.Exists(marker)) return;
            if (Directory.Exists(legacy)) CopyMissing(legacy, destination);
            File.WriteAllText(marker, "1", new UTF8Encoding(false));
        }

        private static void CopyMissing(string source, string destination, bool root = true)
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(ModMessages.Get("MigrationLinkUnsupported"));
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException(ModMessages.Get("MigrationLinkUnsupported"));
                string target = Path.Combine(destination, Path.GetFileName(file));
                if (root && Path.GetFileName(file) == "launcher.log") target = Path.Combine(destination, "logs", "launcher.log");
                if (File.Exists(target)) continue;
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(file, temporary);
                    File.Move(temporary, target);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            foreach (string folder in Directory.GetDirectories(source))
                CopyMissing(folder, Path.Combine(destination, Path.GetFileName(folder)), false);
        }

        public static void WriteCrash(Exception error)
        {
            try
            {
                string folder = Path.Combine(Logs, "crashes");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".log"), error.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
