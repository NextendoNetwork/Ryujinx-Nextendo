using System.Text.Json.Serialization;

namespace Ryujinx.HLE.HOS.Services.Account.Acc
{
    [JsonConverter(typeof(JsonStringEnumConverter<AccountState>))]
    public enum AccountState
    {
        Closed,
        Open,
    }
}
