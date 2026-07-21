using CommunityToolkit.Mvvm.ComponentModel;
using Ryujinx.HLE.HOS.Services.Account.Acc;
using System.Collections.ObjectModel;

namespace Ryujinx.Ava.UI.ViewModels
{
    public partial class ProfileSelectorDialogViewModel : BaseModel
    {

        [ObservableProperty]
        public partial UserId SelectedUserId { get; set; }

        [ObservableProperty]
        public partial ObservableCollection<BaseModel> Profiles { get; set; } = [];
    }
}
