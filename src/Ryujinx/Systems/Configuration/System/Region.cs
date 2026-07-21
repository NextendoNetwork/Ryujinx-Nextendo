using Ryujinx.HLE.HOS.SystemState;
using System.Text.Json.Serialization;

namespace Ryujinx.Ava.Systems.Configuration.System
{
    [JsonConverter(typeof(JsonStringEnumConverter<Region>))]
    public enum Region
    {
        Japan,
        USA,
        Europe,
        Australia,
        China,
        Korea,
        Taiwan,
    }

    public static class RegionEnumHelper
    {
        extension(RegionCode hle)
        {
            public Region Ui => (Region)hle;
        }

        extension(Region ui)
        {
            public RegionCode Horizon => (RegionCode)ui;
        }
    }
}
