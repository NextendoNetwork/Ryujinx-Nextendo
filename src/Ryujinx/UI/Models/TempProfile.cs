using CommunityToolkit.Mvvm.ComponentModel;
using Ryujinx.Ava.UI.ViewModels;
using Ryujinx.HLE.HOS.Services.Account.Acc;
using System;

namespace Ryujinx.Ava.UI.Models
{
    public partial class TempProfile : BaseModel
    {
        [ObservableProperty]
        public partial byte[] Image { get; set; }

        [ObservableProperty]
        public partial string Name { get; set; } = string.Empty;

        public static uint MaxProfileNameLength => 0x20;

        public UserId UserId
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UserIdString));
            }
        }

        public string UserIdString => UserId.ToString();

        public TempProfile(UserProfile profile)
        {
            if (profile != null)
            {
                Image = profile.Image;
                Name = profile.Name;
                UserId = profile.UserId;
            }
        }
    }
}
