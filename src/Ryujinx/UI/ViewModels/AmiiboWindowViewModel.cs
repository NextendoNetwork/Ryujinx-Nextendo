using Avalonia;
using Avalonia.Collections;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Path = System.IO.Path;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Common.Models.Amiibo;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Ava.Utilities;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.ViewModels
{
    public partial class AmiiboWindowViewModel : BaseModel, IDisposable
    {
        public enum AmiiboSortField
        {
            Name,
            
        }

        private int _amiiboSelectedIndex;
        private int _seriesSelectedIndex;
        private bool _showAllAmiibo;

        // ReSharper disable once InconsistentNaming
        private static bool _cachedUseRandomUuid;
        public bool IsSortedByName => _sortField == AmiiboSortField.Name;
        private const string DefaultJson = "{ \"amiibo\": [] }";
        private const float AmiiboImageSize = 350f;
        public string TitleId { get; set; }
        public string LastScannedAmiiboId { get; set; }
        public string SortingFieldName => LocaleManager.Instance[LocaleKeys.Common_Sort_NameLabel];
        private readonly string _amiiboJsonPath;
        private readonly byte[] _amiiboLogoBytes;
        private readonly HttpClient _httpClient;
        private readonly AmiiboWindow _owner;
        private List<AmiiboApi> _amiiboList;
        private AvaloniaList<AmiiboApi> _amiibos;
        private ObservableCollection<string> _amiiboSeries;
        private CancellationTokenSource _imageCts = new();
        private static readonly AmiiboJsonSerializerContext _serializerContext = new(JsonHelper.GetDefaultSerializerOptions());

        public AmiiboWindowViewModel(AmiiboWindow owner, string lastScannedAmiiboId, string titleId)
        {
            _owner = owner;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            LastScannedAmiiboId = lastScannedAmiiboId;
            TitleId = titleId;

            Directory.CreateDirectory(Path.Join(AppDataManager.BaseDirPath, "system", "amiibo"));

            _amiiboJsonPath = Path.Join(AppDataManager.BaseDirPath, "system", "amiibo", "Amiibo.json");
            _amiiboList = [];
            _amiiboSeries = [];
            _amiibos = [];

            _amiiboLogoBytes = EmbeddedResources.Read("Ryujinx/Assets/UIImages/Logo_Amiibo.png");

            _ = LoadContentAsync();
        }

        public AmiiboWindowViewModel() { }

        public UserResult Response { get; private set; }

        public bool UseRandomUuid
        {
            get;
            set
            {
                _cachedUseRandomUuid = field = value;

                OnPropertyChanged();
            }
        } = _cachedUseRandomUuid;

        public bool ShowAllAmiibo
        {
            get => _showAllAmiibo;
            set
            {
                _showAllAmiibo = value;

                ParseAmiiboData();

                OnPropertyChanged();
            }
        }

        public AvaloniaList<AmiiboApi> AmiiboList
        {
            get => _amiibos;
            set
            {
                _amiibos = value;

                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> AmiiboSeries
        {
            get => _amiiboSeries;
            set
            {
                _amiiboSeries = value;
                OnPropertyChanged();
            }
        }

        public int SeriesSelectedIndex
        {
            get => _seriesSelectedIndex;
            set
            {
                _seriesSelectedIndex = value;

                FilterAmiibo();

                OnPropertyChanged();
            }
        }

        public int AmiiboSelectedIndex
        {
            get => _amiiboSelectedIndex;
            set
            {
                _amiiboSelectedIndex = value;

                EnableScanning = _amiiboSelectedIndex >= 0 && _amiiboSelectedIndex < _amiibos.Count;

                SetAmiiboDetails();

                OnPropertyChanged();
            }
        }

        public bool PauseEmulationWhileScanningAmiibo
        {
            get => ConfigurationState.Instance.UI.PauseEmulationWhileScanningAmiibo.Value;
            set
            {
                ConfigurationState.Instance.UI.PauseEmulationWhileScanningAmiibo.Value = value;
                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
                OnPropertyChanged();
            }
        }

        private string _searchText = string.Empty;
        private bool _sortingAscending = true;

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                FilterAmiibo();
                OnPropertyChanged();
            }
        }

        public bool SortingAscending
        {
            get => _sortingAscending;
            set
            {
                _sortingAscending = value;
                FilterAmiibo();
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        public partial Bitmap AmiiboImage { get; set; }

        [ObservableProperty]
        public partial string Usage { get; set; }

        [ObservableProperty]
        public partial bool EnableScanning { get; set; }

        public void Scan()
        {
            if (AmiiboSelectedIndex > -1)
            {
                _owner.ScannedAmiibo = AmiiboList[AmiiboSelectedIndex];
                _owner.IsScanned = true;

            }
        }

        public void Cancel()
        {
            _owner.IsScanned = false;

        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            _httpClient.Dispose();
        }

        private static bool TryGetAmiiboJson(string json, out AmiiboJson amiiboJson)
        {
            if (string.IsNullOrEmpty(json))
            {
                amiiboJson = JsonHelper.Deserialize(DefaultJson, _serializerContext.AmiiboJson);

                return false;
            }

            try
            {
                amiiboJson = JsonHelper.Deserialize(json, _serializerContext.AmiiboJson);

                return true;
            }
            catch (JsonException exception)
            {
                Logger.Error?.Print(LogClass.Application, $"Unable to deserialize amiibo data: {exception}");
                amiiboJson = JsonHelper.Deserialize(DefaultJson, _serializerContext.AmiiboJson);

                return false;
            }
        }

        private async Task<AmiiboJson> GetMostRecentAmiiboListOrDefaultJson()
        {
            bool localIsValid = false;
            bool remoteIsValid = false;
            AmiiboJson amiiboJson = new();

            try
            {
                try
                {
                    if (File.Exists(_amiiboJsonPath))
                    {
                        localIsValid = TryGetAmiiboJson(await File.ReadAllTextAsync(_amiiboJsonPath), out amiiboJson);
                    }
                }
                catch (Exception exception)
                {
                    Logger.Warning?.Print(LogClass.Application, $"Unable to read data from '{_amiiboJsonPath}': {exception}");
                    localIsValid = false;
                }

                if (!localIsValid || await NeedsUpdate(amiiboJson.LastUpdated))
                {
                    remoteIsValid = TryGetAmiiboJson(await DownloadAmiiboJson(), out amiiboJson);
                }
            }
            catch (Exception exception)
            {
                if (!(localIsValid || remoteIsValid))
                {
                    Logger.Error?.Print(LogClass.Application, $"Couldn't get valid amiibo data: {exception}");

                    // Neither local or remote files are valid JSON, close window.
                    await ShowInfoDialog();
                    Close();
                }
                else if (!remoteIsValid)
                {
                    Logger.Warning?.Print(LogClass.Application, $"Couldn't update amiibo data: {exception}");

                    // Only the local file is valid, the local one should be used
                    // but the user should be warned.
                    await ShowInfoDialog();
                }
            }

            return amiiboJson;
        }

        private async Task<AmiiboJson?> ReadLocalJsonFileAsync()
        {
            bool isValid = false;
            AmiiboJson amiiboJson = new();

            try
            {
                try
                {
                    if (File.Exists(_amiiboJsonPath))
                    {
                        isValid = TryGetAmiiboJson(await File.ReadAllTextAsync(_amiiboJsonPath), out amiiboJson);
                    }
                }
                catch (Exception exception)
                {
                    Logger.Warning?.Print(LogClass.Application, $"Unable to read data from '{_amiiboJsonPath}': {exception}");
                    isValid = false;
                }

                if (!isValid)
                {
                    return null;
                }
            }
            catch (Exception exception)
            {
                if (!isValid)
                {
                    Logger.Error?.Print(LogClass.Application, $"Couldn't get valid amiibo data: {exception}");

                    // Neither local file is not valid JSON, close window.
                    await ShowInfoDialog();
                    Close();
                }
            }

            return amiiboJson;
        }

        private async Task LoadContentAsync()
        {
            AmiiboJson? amiiboJson;

            if (CommandLineState.OnlyLocalAmiibo)
                amiiboJson = await ReadLocalJsonFileAsync();
            else
                amiiboJson = await GetMostRecentAmiiboListOrDefaultJson();

            if (!amiiboJson.HasValue)
                return;

            _amiiboList = amiiboJson.Value.Amiibo.OrderBy(amiibo => amiibo.AmiiboSeries).ToList();

            ParseAmiiboData();
        }

        private void ParseAmiiboData()
        {
            _amiiboSeries.Clear();

            foreach (AmiiboApi amiibo in _amiiboList)
            {
                if (!_amiiboSeries.Contains(amiibo.AmiiboSeries))
                {
                    if (_showAllAmiibo)
                    {
                        _amiiboSeries.Add(amiibo.AmiiboSeries);
                    }
                    else
                    {
                        bool hasCompatible = amiibo.GamesSwitch.Any(game =>
                            game != null && game.GameId.Contains(TitleId));

                        if (hasCompatible)
                        {
                            _amiiboSeries.Add(amiibo.AmiiboSeries);
                        }
                    }
                }
            }

            if (_showAllAmiibo)
            {
                SeriesSelectedIndex = -1;
            }
            else if (LastScannedAmiiboId != string.Empty)
            {
                SelectLastScannedAmiibo();
            }
            else if (_amiiboSeries.Count > 0)
            {
                SeriesSelectedIndex = 0;
            }
            else
            {
                SeriesSelectedIndex = -1;
            }

            FilterAmiibo();
        }

        private void SelectLastScannedAmiibo()
        {
            AmiiboApi scanned = _amiiboList.FirstOrDefault(amiibo => amiibo.GetId() == LastScannedAmiiboId);

            SeriesSelectedIndex = AmiiboSeries.IndexOf(scanned.AmiiboSeries);
            AmiiboSelectedIndex = AmiiboList.IndexOf(scanned);
        }
        
        private void FilterAmiibo()
        {
            _amiibos.Clear();

            IEnumerable<AmiiboApi> query = _amiiboList.AsEnumerable();

            if (_seriesSelectedIndex >= 0 && _seriesSelectedIndex < _amiiboSeries.Count)
            {
                string selectedSeries = _amiiboSeries[_seriesSelectedIndex];
                query = query.Where(amiibo => amiibo.AmiiboSeries == selectedSeries);
            }

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                query = query.Where(amiibo =>
                    amiibo.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
            }

            if (!_showAllAmiibo)
            {
                query = query.Where(amiibo =>
                    amiibo.GamesSwitch.Any(game => game != null && game.GameId.Contains(TitleId)));
            }

            query = _sortingAscending
                ? query.OrderBy(amiibo => amiibo.Name)
                : query.OrderByDescending(amiibo => amiibo.Name);

            _amiibos.AddRange(query);

            int restoredIndex = -1;
            for (int i = 0; i < _amiibos.Count; i++)
            {
                if (_amiibos[i].GetId() == LastScannedAmiiboId)
                {
                    restoredIndex = i;
                    break;
                }
            }

            AmiiboSelectedIndex = restoredIndex != -1
                ? restoredIndex
                : (_amiibos.Count > 0 ? 0 : -1);

            SetAmiiboDetails();
        }

        private AmiiboSortField _sortField = AmiiboSortField.Name;

        public AmiiboSortField SortingField
        {
            get => _sortField;
            set
            {
                _sortField = value;
                FilterAmiibo();
                OnPropertyChanged(nameof(SortingFieldName));
                OnPropertyChanged(nameof(IsSortedByName));
            }
        }

        private void SetAmiiboDetails()
        {
            ResetAmiiboPreview();
            Usage = string.Empty;

            if (_amiiboSelectedIndex < 0 || _amiibos.Count < 1)
                return;

            AmiiboApi selected = _amiibos[_amiiboSelectedIndex];
            string imageUrl = selected.Image;

            StringBuilder usageStringBuilder = new();
            bool writable = false;

            foreach (AmiiboApiGamesSwitch game in selected.GamesSwitch)
            {
                if (game != null && game.GameId.Contains(TitleId))
                {
                    foreach (AmiiboApiUsage usageItem in game.AmiiboUsage)
                    {
                        usageStringBuilder.Append($"{Environment.NewLine}- {usageItem.Usage.Replace("/", Environment.NewLine + "-")}");
                        if (usageItem.Write)
                            writable = true;
                    }
                }
            }

            string usageLabel = writable
                ? LocaleManager.Instance[LocaleKeys.Amiibo_WritableLabel]
                : LocaleManager.Instance[LocaleKeys.Amiibo_UsageLabel];

            if (usageStringBuilder.Length == 0)
            {
                usageStringBuilder.Append(Environment.NewLine + Environment.NewLine + LocaleManager.Instance[LocaleKeys.Amiibo_UnknownLabel]);
            }
            else
            {
                usageStringBuilder.Replace(Environment.NewLine + "-", Environment.NewLine + Environment.NewLine + "-");
            }

            Usage = usageLabel + usageStringBuilder.ToString();
            _ = UpdateAmiiboPreview(imageUrl);
        }

        private async Task<bool> NeedsUpdate(DateTime oldLastModified)
        {
            try
            {
                HttpResponseMessage response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, SharedConstants.AmiiboTagsUrl));

                if (response.IsSuccessStatusCode)
                {
                    return response.Content.Headers.LastModified != oldLastModified;
                }
            }
            catch (HttpRequestException exception)
            {
                Logger.Error?.Print(LogClass.Application, $"Unable to check for amiibo data updates: {exception}");
            }

            return false;
        }

        private async Task<string> DownloadAmiiboJson()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(SharedConstants.AmiiboTagsUrl);

                if (response.IsSuccessStatusCode)
                {
                    string amiiboJsonString = await response.Content.ReadAsStringAsync();

                    try
                    {
                        using FileStream dlcJsonStream = File.Create(_amiiboJsonPath, 4096, FileOptions.WriteThrough);
                        dlcJsonStream.Write(Encoding.UTF8.GetBytes(amiiboJsonString));
                    }
                    catch (Exception exception)
                    {
                        Logger.Warning?.Print(LogClass.Application, $"Couldn't write amiibo data to file '{_amiiboJsonPath}: {exception}'");
                    }

                    return amiiboJsonString;
                }

                Logger.Error?.Print(LogClass.Application, $"Failed to download amiibo data. Response status code: {response.StatusCode}");
            }
            catch (HttpRequestException exception)
            {
                Logger.Error?.Print(LogClass.Application, $"Failed to request amiibo data: {exception}");
            }

            await ContentDialogHelper.CreateInfoDialog(LocaleManager.Instance[LocaleKeys.Dialog_Amiibo_APITitle],
                LocaleManager.Instance[LocaleKeys.Dialog_Amiibo_APIFailFetchMessage],
                LocaleManager.Instance[LocaleKeys.InputDialogOk],
                string.Empty,
                LocaleManager.Instance[LocaleKeys.RyujinxInfo]);

            return null;
        }

        private void Close()
        {
            Dispatcher.UIThread.Post(_owner.Close);
        }

        private async Task UpdateAmiiboPreview(string imageUrl)
        {
            _imageCts.Cancel();
            _imageCts = new CancellationTokenSource();

            try
            {
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_imageCts.Token);

                HttpResponseMessage response = await _httpClient.GetAsync(imageUrl, linkedCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync(linkedCts.Token);
                    using MemoryStream ms = new(bytes);

                    Bitmap bitmap = new(ms);

                    double ratio = Math.Min(AmiiboImageSize / bitmap.Size.Width, AmiiboImageSize / bitmap.Size.Height);
                    int newWidth = (int)(bitmap.Size.Width * ratio);
                    int newHeight = (int)(bitmap.Size.Height * ratio);

                    AmiiboImage = bitmap.CreateScaledBitmap(new PixelSize(newWidth, newHeight));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Error?.Print(LogClass.Application, $"Failed to load amiibo preview: {ex}");
            }
        }

        private void ResetAmiiboPreview()
        {
            using MemoryStream memoryStream = new(_amiiboLogoBytes);

            Bitmap bitmap = new(memoryStream);

            AmiiboImage = bitmap;
        }

        private static async Task ShowInfoDialog()
        {
            await ContentDialogHelper.CreateInfoDialog(LocaleManager.Instance[LocaleKeys.Dialog_Amiibo_APITitle],
                LocaleManager.Instance[LocaleKeys.Dialog_Amiibo_APIConnectErrorMessage],
                LocaleManager.Instance[LocaleKeys.InputDialogOk],
                string.Empty,
                LocaleManager.Instance[LocaleKeys.RyujinxInfo]);
        }
    }
}
