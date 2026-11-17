using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KebuzLoncher
{
    // Accessed from the UI thread; this cache owns every shared Image.
    internal sealed class ThumbnailCache : IDisposable
    {
        private readonly Dictionary<string, Task<Image>> pending = new Dictionary<string, Task<Image>>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim downloads = new SemaphoreSlim(4);
        private readonly HttpClient client;
        private readonly string directory;
        private bool disposed;

        public ThumbnailCache(HttpClient client, string directory)
        {
            this.client = client;
            this.directory = directory;
        }

        public Task<Image> GetAsync(string url)
        {
            if (disposed || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                return Task.FromResult<Image>(null);
            if (!pending.TryGetValue(url, out var task))
            {
                task = LoadAsync(url, lifetime.Token);
                pending.Add(url, task);
            }
            else if (task.Status == TaskStatus.RanToCompletion && task.Result == null)
            {
                // Retry transient failures when the catalog is refreshed.
                task = LoadAsync(url, lifetime.Token);
                pending[url] = task;
            }
            return task;
        }

        private async Task<Image> LoadAsync(string url, CancellationToken token)
        {
            string path;
            using (var hash = SHA256.Create())
                path = Path.Combine(directory, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "") + ".png");

            Image image = null;
            try
            {
                image = await Task.Run(() =>
                {
                    try
                    {
                        using (var cached = Image.FromFile(path))
                            return (Image)new Bitmap(cached);
                    }
                    catch { return null; } // Missing or damaged cache: fetch again.
                }, token);

                if (image == null)
                {
                    await downloads.WaitAsync(token);
                    try
                    {
                        using (var response = await client.GetAsync(url, token))
                        {
                            response.EnsureSuccessStatusCode();
                            var data = await response.Content.ReadAsByteArrayAsync();
                            image = await Task.Run(() => DecodeThumbnail(data), token);
                        }
                        if (!token.IsCancellationRequested)
                        {
                            var thumbnail = image;
                            await Task.Run(() =>
                            {
                                try
                                {
                                    Directory.CreateDirectory(directory);
                                    thumbnail.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                                }
                                catch { } // Disk cache is optional.
                            });
                        }
                    }
                    finally { downloads.Release(); }
                }
                if (disposed || token.IsCancellationRequested)
                {
                    image?.Dispose();
                    return null;
                }
                return image;
            }
            catch
            {
                image?.Dispose();
                return null;
            }
        }

        private static Image DecodeThumbnail(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            using (var original = Image.FromStream(stream))
            {
                double scale = Math.Min(1, Math.Min(460.0 / original.Width, 220.0 / original.Height));
                var thumbnail = new Bitmap(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)));
                using (var graphics = Graphics.FromImage(thumbnail))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(original, 0, 0, thumbnail.Width, thumbnail.Height);
                }
                return thumbnail; // Independent of the stream and the original image.
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            foreach (var task in pending.Values)
                if (task.Status == TaskStatus.RanToCompletion) task.Result?.Dispose();
            pending.Clear();
            lifetime.Dispose();
        }
    }
}
