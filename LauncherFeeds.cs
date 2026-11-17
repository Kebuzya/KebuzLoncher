using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KebuzLoncher
{
    internal sealed class LauncherFeeds
    {
        internal const string CatalogUrl = "https://raw.githubusercontent.com/Kebuzya/KebuzLoncher/catalog/config.json";
        internal const string ManifestUrl = "https://raw.githubusercontent.com/Kebuzya/KebuzLoncher/catalog/manifest.json";

        private readonly HttpClient client;
        private readonly string cacheDirectory;

        public LauncherFeeds(HttpClient client, string cacheDirectory)
        {
            this.client = client;
            this.cacheDirectory = cacheDirectory;
        }

        public async Task<GameConfig> ReadCachedCatalogAsync(CancellationToken token)
        {
            var cached = await ReadCacheAsync<GameConfig>("games_config.cache", token);
            return IsValidCatalog(cached) ? cached : null;
        }

        public async Task<GameConfig> LoadCatalogAsync(CancellationToken token)
        {
            var catalog = await ReadRemoteAsync<GameConfig>(CatalogUrl, token);
            if (IsValidCatalog(catalog))
            {
                await SaveCacheAsync("games_config.cache", catalog, token);
                return catalog;
            }
            return await ReadCachedCatalogAsync(token);
        }

        public async Task<LauncherManifest> LoadManifestAsync(CancellationToken token)
        {
            var manifest = await ReadRemoteAsync<LauncherManifest>(ManifestUrl, token);
            if (IsValidManifest(manifest))
            {
                await SaveCacheAsync("launcher_manifest.cache", manifest, token);
                return manifest;
            }
            var cached = await ReadCacheAsync<LauncherManifest>("launcher_manifest.cache", token);
            return IsValidManifest(cached) ? cached : null;
        }

        private static bool IsValidCatalog(GameConfig catalog) =>
            catalog?.games?.Length > 0 && Array.TrueForAll(catalog.games, game => game != null && !string.IsNullOrWhiteSpace(game.id));

        private static bool IsValidManifest(LauncherManifest manifest) =>
            LauncherVersion.TryParse(manifest?.launcher_version, out _) &&
            Uri.TryCreate(manifest?.launcher_download_url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

        private async Task<T> ReadRemoteAsync<T>(string url, CancellationToken token) where T : class
        {
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    using (var response = await client.GetAsync(url, timeout.Token))
                    {
                        response.EnsureSuccessStatusCode();
                        return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync());
                    }
                }
            }
            catch (Exception) when (!token.IsCancellationRequested) { return null; }
        }

        private async Task<T> ReadCacheAsync<T>(string name, CancellationToken token) where T : class
        {
            try
            {
                return await Task.Run(() => JsonConvert.DeserializeObject<T>(File.ReadAllText(Path.Combine(cacheDirectory, name), Encoding.UTF8)), token);
            }
            catch (Exception) when (!token.IsCancellationRequested) { return null; }
        }

        private async Task SaveCacheAsync<T>(string name, T value, CancellationToken token)
        {
            try
            {
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(cacheDirectory);
                    string path = Path.Combine(cacheDirectory, name);
                    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(temporary, JsonConvert.SerializeObject(value), new UTF8Encoding(false));
                        token.ThrowIfCancellationRequested();
                        if (File.Exists(path)) File.Replace(temporary, path, null);
                        else File.Move(temporary, path);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }, token);
            }
            catch (Exception) when (!token.IsCancellationRequested) { }
        }
    }
}
