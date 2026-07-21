using System.Text.Json.Serialization;

namespace Ryujinx.Ava.Systems.Configuration.UI
{
    [JsonConverter(typeof(JsonStringEnumConverter<FocusLostType>))]
    public enum FocusLostType
    {
        DoNothing,
        BlockInput,
        MuteAudio,
        BlockInputAndMuteAudio,
        PauseEmulation
    }
}
