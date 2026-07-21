using Ryujinx.Ava.Common.Locale;
using static Ryujinx.Common.Utilities.XCIFileTrimmer;

namespace Ryujinx.Ava.UI.Helpers
{
    public static class XCIFileTrimmerOperationOutcomeExtensions
    {
        extension(OperationOutcome opOutcome)
        {
            public string LocalizedText => opOutcome switch
            {
                OperationOutcome.NoTrimNecessary => LocaleManager.Instance[LocaleKeys.Dialog_XCITrimmer_NoTrimNecessaryMessage],
                OperationOutcome.NoUntrimPossible => LocaleManager.Instance[LocaleKeys.Dialog_XCITrimmer_NoUntrimPossibleMessage],
                OperationOutcome.ReadOnlyFileCannotFix => LocaleManager.Instance[LocaleKeys.Dialog_XCITrimmer_ReadOnlyFileCannotFixMessage],
                OperationOutcome.FreeSpaceCheckFailed => LocaleManager.Instance[LocaleKeys.Dialog_XCITrimmer_FreeSpaceCheckFailedMessage],
                OperationOutcome.InvalidXCIFile => LocaleManager.Instance[LocaleKeys.Dialog_XCITrimmer_InvalidDataMessage],
                OperationOutcome.FileIOWriteError => LocaleManager.Instance[LocaleKeys.Dialog_XCITrimmer_WriteErrorMessage],
                OperationOutcome.FileSizeChanged => LocaleManager.Instance[LocaleKeys.Dialog_XCITrimmer_SizeChangedMessage],
                OperationOutcome.Cancelled => LocaleManager.Instance[LocaleKeys.Dialog_XCITrimmer_TrimCancelledMessage],
                OperationOutcome.Undetermined => LocaleManager.Instance[LocaleKeys.Dialog_XCITrimmer_NoOperationPerformedMessage],
                _ => null
            };
        }
    }
}
