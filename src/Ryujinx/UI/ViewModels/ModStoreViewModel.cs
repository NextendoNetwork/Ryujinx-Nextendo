using CommunityToolkit.Mvvm.ComponentModel;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.ViewModels
{
    // [Nextendo] Mod Store: lists the admin-curated mods for a title (from the server manifest)
    // and installs/removes them into the game's local mod folder. Only cosmetic/local mods
    // live in the store; downloading is one click, removing one click.
    public partial class ModStoreViewModel : BaseModel
    {
        private readonly ulong _titleId;

        public ObservableCollection<ModStoreItem> Mods { get; } = [];

        [ObservableProperty] private bool _loading;
        [ObservableProperty] private string _status = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ModStoreLoading];

        // Ryujinx mod layout: <ModsBasePath>/contents/<titleId>/<modName>/exefs|romfs/...
        private string ModsContentsDir => Path.Combine(ModLoader.GetModsBasePath(), "contents", _titleId.ToString("x16"));

        public ModStoreViewModel(ulong titleId)
        {
            _titleId = titleId;
            _ = LoadAsync();
        }

        private static string BaseUrl()
        {
            // [Nextendo] Une seule decision, dans NextendoEndpoint : c'est elle qui choisit qui
            // recoit le jeton du compte. Cette logique etait dupliquee ici et acceptait
            // n'importe quelle valeur de NEXTENDO_API.
            return NextendoEndpoint.BaseUrl();
        }

        private static HttpClient NewClient(TimeSpan timeout)
        {
            HttpClient http = new() { Timeout = timeout };
            if (!string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
            }

            return http;
        }

        private bool IsInstalled(string name) => Directory.Exists(Path.Combine(ModsContentsDir, name));

        private async Task LoadAsync()
        {
            Loading = true;
            Status = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ModStoreLoadingCatalog];

            try
            {
                if (!NextendoAccount.IsLinked || string.IsNullOrEmpty(NextendoAccount.NexToken))
                {
                    Status = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ModStoreRequiresLogin];
                    return;
                }

                using HttpClient http = NewClient(TimeSpan.FromSeconds(15));
                HttpResponseMessage resp = await http.GetAsync($"{BaseUrl()}/api/mods/{_titleId:x16}/manifest");

                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Status = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ModStoreRequiresLogin];
                    return;
                }

                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent || !resp.IsSuccessStatusCode)
                {
                    Status = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ModStoreNoModsForGame];
                    return;
                }

                string json = await resp.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);

                Mods.Clear();
                foreach (JsonElement el in doc.RootElement.EnumerateArray())
                {
                    string id = el.GetProperty("id").GetString();
                    string name = el.TryGetProperty("name", out JsonElement n) ? n.GetString() : id;
                    string folder = el.TryGetProperty("folder", out JsonElement fo) ? fo.GetString() : name;
                    string desc = el.TryGetProperty("description", out JsonElement d) ? d.GetString() : "";
                    long size = el.TryGetProperty("size", out JsonElement s) && s.TryGetInt64(out long sz) ? sz : 0;

                    Mods.Add(new ModStoreItem
                    {
                        Id = id,
                        Folder = folder,
                        Name = name,
                        Description = desc,
                        Size = size,
                        Installed = IsInstalled(folder),
                    });
                }

                Status = Mods.Count == 0 ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ModStoreNoMods] : LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ModStoreModsAvailableFormat, Mods.Count);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] mod store load failed: {ex.Message}");
                Status = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ModStoreLoadError];
            }
            finally
            {
                Loading = false;
            }
        }

        public async Task DownloadAsync(ModStoreItem item)
        {
            if (item == null || item.Busy)
            {
                return;
            }

            item.Busy = true;
            Status = LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ModStoreDownloadingFormat, item.Name);
            string tmp = Path.Combine(Path.GetTempPath(), $"nxmod_{item.Id}.zip");

            try
            {
                using HttpClient http = NewClient(TimeSpan.FromMinutes(5));
                byte[] zip = await http.GetByteArrayAsync($"{BaseUrl()}/api/mods/{_titleId:x16}/{item.Id}");
                await File.WriteAllBytesAsync(tmp, zip);

                Directory.CreateDirectory(ModsContentsDir);
                ZipFile.ExtractToDirectory(tmp, ModsContentsDir, overwriteFiles: true);

                item.Installed = IsInstalled(item.Folder);
                Status = item.Installed
                    ? LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ModStoreInstalledSuccessFormat, item.Name, ModsContentsDir)
                    : LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ModStoreInstallFailedFormat, item.Name);
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] mod installed: {item.Folder} -> {ModsContentsDir}");
            }
            catch (Exception ex)
            {
                Status = LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ModStoreDownloadFailedFormat, item.Name);
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] mod download failed: {ex.Message}");
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* best effort */ }
                item.Busy = false;
            }
        }

        public async Task DeleteAsync(ModStoreItem item)
        {
            if (item == null || item.Busy)
            {
                return;
            }

            item.Busy = true;
            Status = LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ModStoreDeletingFormat, item.Name);

            try
            {
                string dir = Path.Combine(ModsContentsDir, item.Folder);
                await Task.Run(() =>
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, true);
                    }
                });

                item.Installed = IsInstalled(item.Folder);
                Status = item.Installed
                    ? LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ModStoreDeleteFailedFormat, item.Name)
                    : LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ModStoreDeletedSuccessFormat, item.Name);
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] mod removed: {item.Folder}");
            }
            catch (Exception ex)
            {
                Status = LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_ModStoreDeleteErrorFormat, item.Name, ex.Message);
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] mod delete failed: {ex.Message}");
            }
            finally
            {
                item.Busy = false;
            }
        }
    }
}
