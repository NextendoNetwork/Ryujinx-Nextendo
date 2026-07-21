using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using DynamicData.Binding;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Models;
using Ryujinx.HLE.HOS.Services.Account.Acc;
using System.Collections.ObjectModel;

namespace Ryujinx.Ava.UI.ViewModels
{
    public partial class UserSaveManagerViewModel : BaseModel
    {
        [ObservableProperty]
        public partial int SortIndex { get; set; }

        [ObservableProperty]
        public partial int OrderIndex { get; set; }

        [ObservableProperty]
        public partial string Search { get; set; }

        [ObservableProperty]
        public partial ObservableCollection<SaveModel> Saves { get; set; } = [];

        [ObservableProperty]
        public partial ObservableCollection<SaveModel> Views { get; set; } = [];

        private readonly AccountManager _accountManager;

        public string SaveManagerHeading => LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.SaveManagerHeading, _accountManager.LastOpenedUser.Name, _accountManager.LastOpenedUser.UserId);

        public UserSaveManagerViewModel(AccountManager accountManager)
        {
            _accountManager = accountManager;
            PropertyChanged += (_, evt) =>
            {
                if (evt.PropertyName is
                    nameof(SortIndex) or
                    nameof(OrderIndex) or
                    nameof(Search) or
                    nameof(Saves))
                {
                    Sort();
                }
            };
        }

        public void Sort()
        {
            Saves.AsObservableChangeSet()
                .Filter(Filter)
                .Sort(GetComparer())
                .Bind(out ReadOnlyObservableCollection<SaveModel> view).AsObservableList();

            Views.Clear();
            Views.AddRange(view);
            OnPropertyChanged(nameof(Views));
        }

        private bool Filter(object arg)
        {
            if (arg is SaveModel save)
            {
                return string.IsNullOrWhiteSpace(Search) || save.Title.Contains(Search, System.StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private SortExpressionComparer<SaveModel> GetComparer()
        {
            return SortIndex switch
            {
                0 => OrderIndex == 0
                    ? SortExpressionComparer<SaveModel>.Ascending(save => save.Title)
                    : SortExpressionComparer<SaveModel>.Descending(save => save.Title),
                1 => OrderIndex == 0
                    ? SortExpressionComparer<SaveModel>.Ascending(save => save.Size)
                    : SortExpressionComparer<SaveModel>.Descending(save => save.Size),
                _ => null,
            };
        }
    }
}
