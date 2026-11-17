using System.Collections.Generic;
using Newtonsoft.Json;

namespace KebuzLoncher
{
    public class GameConfig
    {
        public Game[] games { get; set; } = new Game[0];
    }

    public class LauncherManifest
    {
        public string launcher_version { get; set; }
        public string launcher_download_url { get; set; }
    }

    public class Game
    {
        public string id { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public string image_url { get; set; }
        public string mod_pack_version { get; set; }
        public string mod_pack_url { get; set; }
        public string mod_pack_status { get; set; }
        public string mod_pack_warning { get; set; }
        public int steam_app_id { get; set; }
        public string executable { get; set; }
        public PatchEntry[] patches { get; set; } = new PatchEntry[0];
        public Mod[] mods { get; set; } = new Mod[0];
    }

    public class PatchEntry
    {
        public string from { get; set; }
        public string to   { get; set; }
        public string url  { get; set; }
    }

    public class Mod
    {
        public string id { get; set; }
        public string name { get; set; }
        public string version { get; set; }
        public bool required { get; set; }
        public string download_url { get; set; }
    }

    public class PatchManifest
    {
        public string from      { get; set; }
        public string to        { get; set; }
        public string[] deleted { get; set; }
        public Dictionary<string, string> checksums { get; set; }
    }

    public class LauncherSettings
    {
        public Dictionary<string, string> GamePaths { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> InstalledVersions { get; set; } = new Dictionary<string, string>();
        public string Theme { get; set; } = "Classic";
        public string DismissedLauncherVersion { get; set; }
    }
}
