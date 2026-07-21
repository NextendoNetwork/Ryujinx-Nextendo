using CommunityToolkit.Mvvm.ComponentModel;
using Ryujinx.Ava.Common.Models;
using Ryujinx.Ava.Systems.AppLibrary;
using System.Linq;

namespace Ryujinx.Ava.UI.ViewModels
{
    public partial class DlcSelectViewModel : BaseModel
    {
        [ObservableProperty]
        public partial DownloadableContentModel[] Dlcs { get; set; }
#nullable enable
        [ObservableProperty]
        public partial DownloadableContentModel? SelectedDlc { get; set; }
#nullable disable

        public DlcSelectViewModel(ulong titleId, ApplicationLibrary appLibrary)
        {
            Dlcs = appLibrary.FindDlcsFor(titleId)
                .OrderBy(it => it.IsBundled ? 0 : 1)
                .ThenBy(it => it.TitleId)
                .ToArray();
        }
    }
}
