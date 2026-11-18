using System;
using System.Reflection;

namespace KebuzLoncher
{
    internal static class LauncherVersion
    {
        public const string Number = "1.2.1";
        public static Version Current => Normalize(Assembly.GetExecutingAssembly().GetName().Version);
        public static string Display => Current.Revision == 0 ? Current.ToString(3) : Current.ToString(4);

        public static bool TryParse(string value, out Version version)
        {
            version = null;
            value = value?.Trim();
            if (string.IsNullOrEmpty(value)) return false;
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            if (!Version.TryParse(value, out var parsed)) return false;
            version = Normalize(parsed);
            return true;
        }

        public static bool ShouldNotify(string available, string dismissed)
        {
            if (!TryParse(available, out var next) || next <= Current) return false;
            // A hidden version and any older versions stay hidden, even if the feed rolls back.
            return !TryParse(dismissed, out var hidden) || next > hidden;
        }

        private static Version Normalize(Version version) =>
            new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }
}
