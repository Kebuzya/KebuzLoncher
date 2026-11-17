using System.Globalization;
using System.Resources;

namespace KebuzLoncher
{
    internal static class ModMessages
    {
        private static readonly ResourceManager russian = new ResourceManager("KebuzLoncher.Properties.Resources.ru", typeof(ModMessages).Assembly);

        public static string Get(string key, params object[] arguments)
        {
            var manager = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? russian : Properties.Resources.ResourceManager;
            return string.Format(CultureInfo.CurrentCulture, manager.GetString(key, CultureInfo.InvariantCulture), arguments);
        }
    }
}
