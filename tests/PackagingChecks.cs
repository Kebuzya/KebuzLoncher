using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using KebuzLoncher;
using Newtonsoft.Json;

internal static class PackagingChecks
{
    private static readonly Assembly launcher = typeof(GameConfig).Assembly;

    private static int Main()
    {
        try
        {
            Check(Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory).Length == 2, "Only the launcher and its test harness may exist in the isolated directory.");
            CheckDependencies();
            string[] names = launcher.GetManifestResourceNames();
            Check(names.Count(name => name.StartsWith("KebuzLoncher.Licenses.", StringComparison.Ordinal)) == 5, "Launcher and dependency licenses must be embedded.");
            var legal = launcher.GetType("KebuzLoncher.LegalNotices", true);
            string notices = (string)legal.GetMethod("ReadText", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Check(notices.Contains("Copyright (c) 2025-2026 Kebuzya") && notices.Contains("Newtonsoft") && notices.Contains("Costura"), "Embedded copyright notices are incomplete.");
            foreach (string resource in new[] { "KebuzLoncher.Properties.Resources", "KebuzLoncher.Properties.Resources.ru" })
            {
                var manager = new ResourceManager(resource, launcher);
                Check(!string.IsNullOrWhiteSpace(manager.GetString("LegalNoticesTitle", CultureInfo.InvariantCulture)), "License UI resources must be embedded.");
                Check(!string.IsNullOrWhiteSpace(manager.GetString("DownloadUpdate", CultureInfo.InvariantCulture)), "Update UI resources must be embedded.");
            }
            using (var icon = Icon.ExtractAssociatedIcon(launcher.Location)) Check(icon != null, "The application icon must be embedded.");
            Console.WriteLine("PASS: isolated EXE, embedded JSON dependency, licenses, RU/EN resources and application icon.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void CheckDependencies()
    {
        var config = JsonConvert.DeserializeObject<GameConfig>("{\"games\":[{\"id\":\"fixture\"}]}");
        Check(config.games.Single().id == "fixture", "Embedded JSON dependency failed.");
        Check(string.IsNullOrEmpty(typeof(JsonConvert).Assembly.Location), "JSON must load from the embedded assembly, without a loose DLL.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
