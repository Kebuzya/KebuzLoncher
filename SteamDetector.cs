using Microsoft.Win32;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace KebuzLoncher
{
    public static class SteamDetector
    {
        public static string FindGamePath(int appId, string executable)
        {
            if (appId <= 0 || string.IsNullOrEmpty(executable))
                return null;

            // 1. Реестр: HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {appId}
            string fromRegistry = CheckUninstallRegistry(appId, executable);
            if (fromRegistry != null) return fromRegistry;

            // 2. libraryfolders.vdf из пути Steam
            string fromLibraries = SearchSteamLibraries(appId, executable);
            if (fromLibraries != null) return fromLibraries;

            // 3. Стандартные пути
            return CheckDefaultPaths(appId, executable);
        }

        private static string CheckUninstallRegistry(int appId, string executable)
        {
            try
            {
                string regPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {appId}";
                using (var key = Registry.LocalMachine.OpenSubKey(regPath))
                {
                    if (key == null) return null;
                    string installPath = key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(installPath) && File.Exists(Path.Combine(installPath, executable)))
                        return installPath;
                }
            }
            catch { }
            return null;
        }

        private static string SearchSteamLibraries(int appId, string executable)
        {
            try
            {
                string steamPath = GetSteamPath();
                if (steamPath == null) return null;

                string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdfPath)) return null;

                string content = File.ReadAllText(vdfPath);

                // Парсим пути из VDF: "path" "C:\\Steam"
                var pathMatches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                foreach (Match match in pathMatches)
                {
                    string libPath = match.Groups[1].Value.Replace(@"\\", @"\");
                    string manifestPath = Path.Combine(libPath, "steamapps", $"appmanifest_{appId}.acf");

                    if (!File.Exists(manifestPath)) continue;

                    // Читаем имя папки из манифеста
                    string gameFolderName = GetGameFolderFromManifest(manifestPath);
                    if (gameFolderName == null) continue;

                    string gamePath = Path.Combine(libPath, "steamapps", "common", gameFolderName);
                    if (File.Exists(Path.Combine(gamePath, executable)))
                        return gamePath;
                }
            }
            catch { }
            return null;
        }

        private static string GetSteamPath()
        {
            try
            {
                // 64-bit Windows хранит Steam в WOW6432Node
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    string path = key?.GetValue("InstallPath") as string;
                    if (path != null) return path;
                }
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                {
                    return key?.GetValue("InstallPath") as string;
                }
            }
            catch { }
            return null;
        }

        private static string GetGameFolderFromManifest(string manifestPath)
        {
            try
            {
                string content = File.ReadAllText(manifestPath);
                var match = Regex.Match(content, @"""installdir""\s+""([^""]+)""");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch { }
            return null;
        }

        private static string CheckDefaultPaths(int appId, string executable)
        {
            string[] drives = { "C", "D", "E", "F" };
            string[] steamSubPaths = {
                @"Program Files (x86)\Steam\steamapps\common",
                @"Steam\steamapps\common",
                @"Games\Steam\steamapps\common",
            };

            foreach (string drive in drives)
            {
                foreach (string sub in steamSubPaths)
                {
                    string commonPath = $@"{drive}:\{sub}";
                    if (!Directory.Exists(commonPath)) continue;

                    // Ищем среди всех папок в common — та, где есть нужный exe
                    try
                    {
                        foreach (string dir in Directory.GetDirectories(commonPath))
                        {
                            if (File.Exists(Path.Combine(dir, executable)))
                                return dir;
                        }
                    }
                    catch { }
                }
            }
            return null;
        }
    }
}
