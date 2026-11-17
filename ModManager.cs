using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace KebuzLoncher
{
    public class SafeModManager
    {
        private readonly string[] foldersToClean = { "BepInEx", "_state" };
        private readonly string[] filesToClean = { "mods.yml", "doorstop_config.ini", "winhttp.dll" };
        private readonly string backupsDir;
        private readonly HttpClient httpClient;

        public SafeModManager()
        {
            // Используем правильные API
            LauncherData.Initialize();
            backupsDir = LauncherData.Backups;

            Directory.CreateDirectory(backupsDir);

            // Безопасный HttpClient
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "KebuzGameLauncher/1.0");
            httpClient.Timeout = TimeSpan.FromSeconds(45);
        }

        public async Task InstallMods(Game game, string gamePath, Action<string> updateStatus,
            Action<bool> setProgressVisible, Action<int> updateProgress = null)
        {
            if (string.IsNullOrEmpty(game?.mod_pack_url))
            {
                throw new InvalidOperationException(ModMessages.Get("FullArchiveMissing"));
            }

            string backupPath = null;
            try
            {
                setProgressVisible(true);
                updateProgress?.Invoke(0);
                backupPath = await PerformFullInstall(game, gamePath, updateStatus, updateProgress);
                setProgressVisible(false);
                MessageBox.Show($"Моды для {game.name} установлены!", "Успех");
            }
            catch
            {
                if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                    await RestoreBackup(backupPath, gamePath);
                setProgressVisible(false);
                updateProgress?.Invoke(0);
                throw;
            }
        }

        // Downloads and installs the full archive, returning its backup path.
        // Restores the backup and propagates installation failures.
        private async Task<string> PerformFullInstall(Game game, string gamePath,
            Action<string> updateStatus, Action<int> updateProgress)
        {
            updateStatus("Начинаем установку...");
            updateStatus("Скачивание модпака...");
            string tempFile = await DownloadModPack(game.mod_pack_url, updateStatus, updateProgress);
            string backupPath = null;
            try
            {
                backupPath = await CreateBackup(gamePath, game.id);
                updateStatus("Очистка старых файлов...");
                await CleanInstallation(gamePath);
                updateProgress?.Invoke(100);
                updateStatus("Распаковка...");
                await ExtractModPack(tempFile, gamePath);
                VerifyInstallation(gamePath);
                return backupPath;
            }
            catch
            {
                if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                    await RestoreBackup(backupPath, gamePath);
                throw;
            }
            finally { if (File.Exists(tempFile)) File.Delete(tempFile); }
        }

        private async Task<string> DownloadModPack(string url, Action<string> updateStatus, Action<int> updateProgress)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "KebuzMods_" + Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempFile, FileMode.Create))
                    {
                        var buffer = new byte[8192];
                        int bytesRead;
                        long totalRead = 0;
                        long? totalLength = response.Content.Headers.ContentLength;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalLength.HasValue)
                            {
                                int percentage = (int)((double)totalRead / totalLength.Value * 100);
                                updateStatus($"Скачивание: {percentage}% ({totalRead / 1024.0 / 1024:0.0} МБ)");
                                updateProgress?.Invoke(percentage);
                            }
                            else
                            {
                                updateStatus($"Скачивание: {totalRead / 1024.0 / 1024:0.0} МБ...");
                            }
                        }
                    }
                }

                return tempFile;
            }
            catch
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                throw;
            }
        }

        private async Task ExtractModPack(string zipPath, string extractPath)
        {
            try
            {
                using (var zipStream = new FileStream(zipPath, FileMode.Open))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string fullPath = Path.GetFullPath(Path.Combine(extractPath, entry.FullName));

                        // Защита от path traversal
                        if (!fullPath.StartsWith(Path.GetFullPath(extractPath)))
                            continue;

                        if (string.IsNullOrEmpty(entry.Name)) // Папка
                        {
                            Directory.CreateDirectory(fullPath);
                        }
                        else // Файл
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

                            using (var entryStream = entry.Open())
                            using (var fileStream = new FileStream(fullPath, FileMode.Create))
                            {
                                await entryStream.CopyToAsync(fileStream);
                            }
                        }

                        await Task.Delay(1); // Маленькая задержка
                    }
                }
            }
            finally
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
            }
        }

        private async Task CleanInstallation(string gamePath)
        {
            // Удаляем папки
            foreach (var folder in foldersToClean)
            {
                string fullPath = Path.Combine(gamePath, folder);
                if (Directory.Exists(fullPath))
                {
                    await SafeDeleteDirectory(fullPath);
                }
            }

            // Удаляем файлы
            foreach (var file in filesToClean)
            {
                string fullPath = Path.Combine(gamePath, file);
                if (File.Exists(fullPath))
                {
                    await SafeDeleteFile(fullPath);
                }
            }
        }

        private async Task SafeDeleteDirectory(string path)
        {
            try
            {
                // Рекурсивно удаляем содержимое
                foreach (var file in Directory.GetFiles(path))
                {
                    await SafeDeleteFile(file);
                }

                foreach (var dir in Directory.GetDirectories(path))
                {
                    await SafeDeleteDirectory(dir);
                }

                await Task.Delay(5);
                Directory.Delete(path);
            }
            catch (Exception ex)
            {
                throw new Exception($"Не удалось удалить папку {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        private async Task SafeDeleteFile(string path)
        {
            try
            {
                await Task.Delay(2);
                File.Delete(path);
            }
            catch (Exception ex)
            {
                throw new Exception($"Не удалось удалить файл {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        private async Task<string> CreateBackup(string gamePath, string gameId = null)
        {
            string prefix = string.IsNullOrEmpty(gameId) ? "backup" : $"{gameId}_backup";
            string backupPath = Path.Combine(backupsDir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}");

            try
            {
                Directory.CreateDirectory(backupPath);

                // Копируем только нужные папки
                foreach (var folder in foldersToClean)
                {
                    string sourcePath = Path.Combine(gamePath, folder);
                    string destPath = Path.Combine(backupPath, folder);

                    if (Directory.Exists(sourcePath))
                    {
                        await CopyDirectoryAsync(sourcePath, destPath);
                    }
                }

                return backupPath;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Бэкап не создан: {ex.Message}", "Предупреждение");
                return null;
            }
        }

        private async Task CopyDirectoryAsync(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(source))
            {
                string destFile = Path.Combine(destination, Path.GetFileName(file));
                using (var sourceStream = new FileStream(file, FileMode.Open))
                using (var destStream = new FileStream(destFile, FileMode.Create))
                {
                    await sourceStream.CopyToAsync(destStream);
                }
                await Task.Delay(1);
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                string destDir = Path.Combine(destination, Path.GetFileName(dir));
                await CopyDirectoryAsync(dir, destDir);
            }
        }

        private async Task RestoreBackup(string backupPath, string gamePath)
        {
            if (!Directory.Exists(backupPath)) return;

            try
            {
                // Очищаем текущую установку
                await CleanInstallation(gamePath);

                // Восстанавливаем из бэкапа
                foreach (var folder in foldersToClean)
                {
                    string sourcePath = Path.Combine(backupPath, folder);
                    string destPath = Path.Combine(gamePath, folder);

                    if (Directory.Exists(sourcePath))
                    {
                        await CopyDirectoryAsync(sourcePath, destPath);
                    }
                }

                MessageBox.Show("Восстановление из бэкапа завершено", "Восстановление");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка восстановления: {ex.Message}", "Ошибка");
            }
        }

        private void VerifyInstallation(string gamePath)
        {
            // Проверяем ключевые папки
            foreach (var folder in foldersToClean)
            {
                if (!Directory.Exists(Path.Combine(gamePath, folder)))
                {
                    throw new Exception($"Папка {folder} не найдена после установки");
                }
            }
        }

        public async Task InstallOrPatch(Game game, string gamePath, string installedVersion,
            Action<string> updateStatus, Action<bool> setProgressVisible, Action<int> updateProgress)
        {
            var chain = BuildPatchChain(game.patches, installedVersion, game.mod_pack_version);

            if (chain != null && chain.Length > 0)
            {
                updateStatus($"Режим: патч ({chain[0].from} → {chain[chain.Length - 1].to})...");
                setProgressVisible(true);
                updateProgress?.Invoke(0);
                string backupPath = await CreateBackup(gamePath, game.id);
                try
                {
                    for (int i = 0; i < chain.Length; i++)
                    {
                        updateStatus($"Патч {i + 1}/{chain.Length}: {chain[i].from} → {chain[i].to}");
                        updateProgress?.Invoke(0);
                        await ApplyPatch(chain[i].url, gamePath, updateStatus, updateProgress, chain[i].from, chain[i].to);
                    }
                    setProgressVisible(false);
                    MessageBox.Show($"Моды для {game.name} обновлены через патч!", "Готово");
                }
                catch (Exception ex)
                {
                    if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                        await RestoreBackup(backupPath, gamePath);
                    setProgressVisible(false);
                    updateProgress?.Invoke(0);
                    throw new Exception($"Ошибка при применении патча: {ex.Message}", ex);
                }
            }
            else
            {
                // Полная установка. Если есть патчи — применяем их сразу, чтобы
                // пользователь получил актуальную версию, а не старый базовый архив.
                PatchEntry[] postChain = null;
                if (game.patches != null && game.patches.Length > 0)
                {
                    string baseVersion = game.patches[0].from;
                    postChain = BuildPatchChain(game.patches, baseVersion, game.mod_pack_version);
                    if (postChain == null) throw new InvalidOperationException(ModMessages.Get("PatchChainBroken"));
                }

                if (postChain != null && postChain.Length > 0)
                {
                    setProgressVisible(true);
                    updateProgress?.Invoke(0);
                    string backupPath = null;
                    try
                    {
                        backupPath = await PerformFullInstall(game, gamePath, updateStatus, updateProgress);

                        for (int i = 0; i < postChain.Length; i++)
                        {
                            updateStatus($"Патч {i + 1}/{postChain.Length}: {postChain[i].from} → {postChain[i].to}");
                            updateProgress?.Invoke(0);
                            await ApplyPatch(postChain[i].url, gamePath, updateStatus, updateProgress, postChain[i].from, postChain[i].to);
                        }

                        setProgressVisible(false);
                        MessageBox.Show($"Моды для {game.name} установлены и обновлены до {game.mod_pack_version}!", "Успех");
                    }
                    catch
                    {
                        if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                            await RestoreBackup(backupPath, gamePath);
                        setProgressVisible(false);
                        updateProgress?.Invoke(0);
                        throw;
                    }
                }
                else
                {
                    await InstallMods(game, gamePath, updateStatus, setProgressVisible, updateProgress);
                }
            }
        }

        public async Task ApplyPatch(string patchUrl, string gamePath,
            Action<string> updateStatus, Action<int> updateProgress, string expectedFrom = null, string expectedTo = null)
        {
            string tempFile = await DownloadModPack(patchUrl, updateStatus, updateProgress);
            try
            {
                PatchManifest manifest;
                using (var archive = ZipFile.OpenRead(tempFile))
                {
                    var manifestEntry = archive.GetEntry("manifest.json");
                    if (manifestEntry == null) throw new InvalidDataException(ModMessages.Get("PatchManifestInvalid"));
                    using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
                        manifest = JsonConvert.DeserializeObject<PatchManifest>(reader.ReadToEnd());
                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.from) || string.IsNullOrWhiteSpace(manifest.to) || manifest.checksums == null)
                        throw new InvalidDataException(ModMessages.Get("PatchManifestInvalid"));
                    if ((expectedFrom != null && manifest.from != expectedFrom) || (expectedTo != null && manifest.to != expectedTo))
                        throw new InvalidDataException(ModMessages.Get("PatchVersionMismatch"));
                    var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in archive.Entries)
                    {
                        string target = ResolvePatchPath(gamePath, entry.FullName);
                        if (!entries.Add(target)) throw new InvalidDataException(ModMessages.Get("PatchManifestInvalid"));
                        if (entry.FullName != "manifest.json" && !string.IsNullOrEmpty(entry.Name) && !manifest.checksums.ContainsKey(entry.FullName))
                            throw new InvalidDataException(ModMessages.Get("PatchManifestInvalid"));
                    }
                    foreach (string relative in manifest.deleted ?? new string[0]) ResolvePatchPath(gamePath, relative);
                    foreach (var checksum in manifest.checksums)
                    {
                        ResolvePatchPath(gamePath, checksum.Key);
                        var entry = archive.GetEntry(checksum.Key);
                        if (entry == null) throw new InvalidDataException(ModMessages.Get("PatchChecksumMismatch", checksum.Key));
                        using (var stream = entry.Open())
                            if (!string.Equals(ComputeSha256(stream), checksum.Value, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException(ModMessages.Get("PatchChecksumMismatch", checksum.Key));
                    }
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName == "manifest.json") continue;
                        string target = ResolvePatchPath(gamePath, entry.FullName);
                        if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(target);
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            using (var source = entry.Open())
                            using (var destination = File.Create(target))
                                await source.CopyToAsync(destination);
                        }
                    }
                }
                foreach (string rel in manifest.deleted ?? new string[0])
                {
                    string target = ResolvePatchPath(gamePath, rel);
                    if (File.Exists(target)) File.Delete(target);
                }
                foreach (var kv in manifest.checksums ?? new Dictionary<string, string>())
                {
                    string target = ResolvePatchPath(gamePath, kv.Key);
                    if (!File.Exists(target) || !string.Equals(ComputeSha256(target), kv.Value, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(ModMessages.Get("PatchChecksumMismatch", kv.Key));
                }
            }
            finally { if (File.Exists(tempFile)) File.Delete(tempFile); }
        }

        private static string ResolvePatchPath(string gamePath, string relative)
        {
            string root = Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(ModMessages.Get("PatchPathInvalid", relative));
            return target;
        }

        private static string ComputeSha256(string filePath)
        {
            using (var fs = File.OpenRead(filePath))
                return ComputeSha256(fs);
        }

        private static string ComputeSha256(Stream stream)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private PatchEntry[] BuildPatchChain(PatchEntry[] patches, string from, string to)
        {
            if (patches == null || patches.Length == 0 || from == null || from == to)
                return null;

            var chain = new List<PatchEntry>();
            string current = from;

            while (current != to)
            {
                PatchEntry next = null;
                foreach (var p in patches)
                {
                    if (p.from == current) { next = p; break; }
                }
                if (next == null) return null; // цепочка прервана -> полная установка
                chain.Add(next);
                current = next.to;
                if (chain.Count > 20) return null; // защита от цикла
            }

            return chain.ToArray();
        }

        public void Dispose()
        {
            httpClient?.Dispose();
        }
    }
}
