using Ryujinx.Ava.Common.Locale;
using Ryujinx.Common.UI;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Helpers
{
    internal class UserErrorDialog
    {
        private static string GetErrorCode(UserError error)
        {
            return $"RYU-{(uint)error:X4}";
        }

        private static string GetErrorTitle(UserError error) =>
            error switch
            {
                UserError.NoKeys => LocaleManager.Instance[LocaleKeys.Error_NoKeysFound],
                UserError.NoFirmware => LocaleManager.Instance[LocaleKeys.Error_NoFirmwareFound],
                UserError.FirmwareParsingFailed => LocaleManager.Instance[LocaleKeys.Error_FirmwareParsingFailed],
                UserError.ApplicationNotFound => LocaleManager.Instance[LocaleKeys.UserErrorApplicationNotFound],
                UserError.Unknown => LocaleManager.Instance[LocaleKeys.UserErrorUnknown],
                _ => LocaleManager.Instance[LocaleKeys.UserErrorUndefined],
            };

        private static string GetErrorDescription(UserError error) =>
            error switch
            {
                UserError.NoKeys => LocaleManager.Instance[LocaleKeys.Error_NoKeysFoundDescription],
                UserError.NoFirmware => LocaleManager.Instance[LocaleKeys.Error_NoFirmwareFoundDescription],
                UserError.FirmwareParsingFailed => LocaleManager.Instance[LocaleKeys.Error_FirmwareParsingFailedDescription],
                UserError.ApplicationNotFound => LocaleManager.Instance[LocaleKeys.UserErrorApplicationNotFoundDescription],
                UserError.Unknown => LocaleManager.Instance[LocaleKeys.UserErrorUnknownDescription],
                _ => LocaleManager.Instance[LocaleKeys.UserErrorUndefinedDescription],
            };

        public static async Task ShowUserErrorDialog(UserError error)
        {
            string errorCode = GetErrorCode(error);

            await ContentDialogHelper.CreateInfoDialog(
                LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.DialogUserErrorDialogMessage, errorCode, GetErrorTitle(error)),
                GetErrorDescription(error),
                string.Empty,
                LocaleManager.Instance[LocaleKeys.InputDialogOk],
                LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.DialogUserErrorDialogTitle, errorCode));
        }
    }
}
