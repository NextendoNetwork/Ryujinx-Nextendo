using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;

namespace Ryujinx.Ava.Common.Models
{
    public record XCITrimmerFileModel(
        string Name,
        string Path,
        bool Trimmable,
        bool Untrimmable,
        long PotentialSavingsB,
        long CurrentSavingsB,
        long OriginalSizeB,
        int? PercentageProgress,
        XCIFileTrimmer.OperationOutcome ProcessingOutcome)
    {
        public static XCITrimmerFileModel FromApplicationData(ApplicationData applicationData, XCIFileTrimmerLog logger)
        {
            XCIFileTrimmer trimmer = new(applicationData.Path, logger);

            return new XCITrimmerFileModel(
                applicationData.Name,
                applicationData.Path,
                trimmer.CanBeTrimmed,
                trimmer.CanBeUntrimmed,
                trimmer.DiskSpaceSavingsB,
                trimmer.DiskSpaceSavedB,
                applicationData.FileSize,
                null,
                XCIFileTrimmer.OperationOutcome.Undetermined
            );
        }

        public bool IsFailed =>
            ProcessingOutcome is not XCIFileTrimmer.OperationOutcome.Undetermined
            and not XCIFileTrimmer.OperationOutcome.Successful;

        public string StatusText
        {
            get
            {
                if (IsFailed)
                    return LocaleManager.Instance[LocaleKeys.XCITrimmer_FailedLabel];

                return ProcessingOutcome switch
                {
                    XCIFileTrimmer.OperationOutcome.Successful =>
                        CurrentSavingsB > 0
                            ? LocaleManager.Instance[LocaleKeys.XCITrimmer_UntrimmedLabel]
                            : LocaleManager.Instance[LocaleKeys.XCITrimmer_TrimmedLabel],

                    XCIFileTrimmer.OperationOutcome.Undetermined =>
                        Trimmable && Untrimmable
                            ? LocaleManager.Instance[LocaleKeys.XCITrimmer_PartialLabel]

                        : Trimmable
                            ? LocaleManager.Instance[LocaleKeys.XCITrimmer_UntrimmedLabel]

                        : Untrimmable
                            ? LocaleManager.Instance[LocaleKeys.XCITrimmer_TrimmedLabel]

                        : LocaleManager.Instance[LocaleKeys.XCITrimmer_UnknownLabel],

                    _ => LocaleManager.Instance[LocaleKeys.XCITrimmer_UnknownLabel]
                };
            }
        }

        public bool HasStatusDetail =>
            ProcessingOutcome != XCIFileTrimmer.OperationOutcome.Undetermined;

        public virtual bool Equals(XCITrimmerFileModel obj)
        {
            if (obj is null)
                return false;

            return Path == obj.Path;
        }
        public override int GetHashCode() => Path.GetHashCode();
    }
}