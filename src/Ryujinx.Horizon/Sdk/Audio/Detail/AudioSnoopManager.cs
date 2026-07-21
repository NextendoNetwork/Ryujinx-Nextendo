using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Sf;
using Ryujinx.Horizon.Sdk.Sf.Hipc;
using System;

namespace Ryujinx.Horizon.Sdk.Audio.Detail
{
    partial class AudioSnoopManager : IAudioSnoopManager
    {
        private byte[] _dspStatisticsParameter;
        
        // Note: The interface changed completely on firmware 17.0.0, this implementation is for older firmware.

        [CmifCommand(0)] // [6.0.0-16.1.0]
        public Result EnableDspUsageMeasurement()
        {
            return Result.Success;
        }

        [CmifCommand(1)] // [6.0.0-16.1.0]
        public Result DisableDspUsageMeasurement()
        {
            return Result.Success;
        }
        
        [CmifCommand(6)] // [6.0.0-16.1.0]
        public Result GetDspUsage(out uint usage)
        {
            usage = 0;

            return Result.Success;
        }

        [CmifCommand(0)] // 17.0.0+
        public Result GetDspStatistics(out uint statistics) => GetDspUsage(out statistics);

        [CmifCommand(1)] // 20.0.0+
        public Result GetAppletStateSummaries([Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<byte> summaries)
        {
            // Since we do not have any real applets, return empty state summaries.
            summaries.Clear();

            return Result.Success;
        }

        [CmifCommand(2)] // 20.0.0+
        public Result SetDspStatisticsParameter([Buffer(HipcBufferFlags.In | HipcBufferFlags.MapAlias)] ReadOnlySpan<byte> parameter)
        {
            _dspStatisticsParameter = null;
            _dspStatisticsParameter = new byte[0x100];
            parameter.CopyTo(_dspStatisticsParameter);

            return Result.Success;
        }

        [CmifCommand(3)] // 20.0.0+
        public Result GetDspStatisticsParameter([Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<byte> parameter)
        {
            _dspStatisticsParameter.CopyTo(parameter);

            return Result.Success;
        }
    }
}
