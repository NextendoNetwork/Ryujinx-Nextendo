using System.Text.Json.Serialization;

namespace Ryujinx.Ava.Systems.Configuration.UI
{
    [JsonConverter(typeof(JsonStringEnumConverter<UpdaterType>))]
    public enum UpdaterType
    {
        Off,
        PromptAtStartup,
        CheckInBackground
    }
}
