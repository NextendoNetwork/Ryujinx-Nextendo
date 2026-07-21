using Gommon;
using Humanizer;
using LibHac.Ns;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Common.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Models
{
    public record LdnGameModel
    {
        public string Id { get; private init; }
        public bool IsPublic { get; private init; }
        public short PlayerCount { get; private init; }
        public short MaxPlayerCount { get; private init; }
        public TitleTuple Title { get; private init; }
        public ConnectionType ConnectionType { get; private init; }
        public bool IsJoinable { get; private init; }
        public ushort SceneId { get; private init; }
        public string[] Players { get; private init; }

        public string PlayersLabel =>
            LocaleManager.GetFormatted(LocaleKeys.LdnGameListPlayersAndPlayerCount, PlayerCount, MaxPlayerCount);

        public string FormattedPlayers =>
            Players.Chunk(4)
                .Select(x => x.FormatCollection(s => s, prefix: "  ", separator: ", "))
                .JoinToString("\n  ");

        public DateTimeOffset CreatedAt { get; init; }

        public string FormattedCreatedAt 
            => LocaleManager.GetFormatted(LocaleKeys.LdnGameListCreatedAt, CreatedAt.Humanize());

        public string CreatedAtToolTip => CreatedAt.DateTime.ToString(CultureInfo.CurrentUICulture);

        public LocaleKeys ConnectionTypeLocaleKey => ConnectionType switch
        {
            ConnectionType.MasterServerProxy => LocaleKeys.LdnGameListConnectionTypeMasterServerProxy,
            ConnectionType.PeerToPeer => LocaleKeys.LdnGameListConnectionTypeP2P,
            _ => throw new ArgumentOutOfRangeException(nameof(ConnectionType),
                $"Expected either 'P2P' or 'Master Server Proxy' ConnectionType; got '{ConnectionType}'")
        };

        public LocaleKeys ConnectionTypeToolTipLocaleKey => ConnectionType switch
        {
            ConnectionType.MasterServerProxy => LocaleKeys.LdnGameListConnectionTypeMasterServerProxyToolTip,
            ConnectionType.PeerToPeer => LocaleKeys.LdnGameListConnectionTypeP2PToolTip,
            _ => throw new ArgumentOutOfRangeException(nameof(ConnectionType),
                $"Expected either 'P2P' or 'Master Server Proxy' ConnectionType; got '{ConnectionType}'")
        };

        public record struct TitleTuple
        {
            public required string Name { get; init; }
            public required string Id { get; init; }
            public required string Version { get; init; }
        }

        public static Array GetArrayForApp(
            LdnGameModel[] receivedData,
            ref ApplicationControlProperty acp,
            bool onlyJoinable = true,
            bool onlyPublic = true)
        {
            LibHac.Common.FixedArrays.Array8<ulong> communicationId = acp.LocalCommunicationId;

            return new Array(receivedData.Where(game =>
                communicationId.AsReadOnlySpan().Contains(game.Title.Id.ToULong())
            ), onlyJoinable, onlyPublic);
        }

        public class Array : IEnumerable<LdnGameModel>
        {
            private readonly LdnGameModel[] _ldnDatas;

            internal Array(IEnumerable<LdnGameModel> receivedData, bool onlyJoinable = false, bool onlyPublic = false)
            {
                if (onlyJoinable)
                    receivedData = receivedData.Where(x => x.IsJoinable);

                if (onlyPublic)
                    receivedData = receivedData.Where(x => x.IsPublic);

                _ldnDatas = receivedData.ToArray();
            }

            public int PlayerCount => _ldnDatas.Sum(it => it.PlayerCount);
            public int GameCount => _ldnDatas.Length;

            public IEnumerator<LdnGameModel> GetEnumerator() => (_ldnDatas as IEnumerable<LdnGameModel>).GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => _ldnDatas.GetEnumerator();
        }
        
        public static async Task<IEnumerable<LdnGameModel>> GetAllAsync(HttpClient client = null)
            => LdnGameJsonModel.ParseArray(await GetAllAsyncRequestImpl(client))
                .Select(FromJson);

        private static async Task<string> GetAllAsyncRequestImpl(HttpClient client = null)
        {
            string ldnWebHost = ConfigurationState.Instance.Multiplayer.GetLdnWebServer();

            LocaleManager.Associate(LocaleKeys.LdnGameListRefreshToolTip, ldnWebHost);

            try
            {
                if (client != null)
                    return await client.GetStringAsync($"https://{ldnWebHost}/api/public_games");

                using HttpClient httpClient = new();
                return await httpClient.GetStringAsync($"https://{ldnWebHost}/api/public_games");
            }
            catch
            {
                return "[]";
            }
        }

        private static LdnGameModel FromJson(LdnGameJsonModel json) =>
            new()
            {
                Id = json.Id,
                IsPublic = json.IsPublic,
                PlayerCount = json.PlayerCount,
                MaxPlayerCount = json.MaxPlayerCount,
                Title = new TitleTuple { Name = json.TitleName, Id = json.TitleId, Version = json.TitleVersion },
                ConnectionType = json.ConnectionType switch
                {
                    "P2P" => ConnectionType.PeerToPeer,
                    "Master Server Proxy" => ConnectionType.MasterServerProxy,
                    _ => throw new ArgumentOutOfRangeException(nameof(json),
                        $"Expected either 'P2P' or 'Master Server Proxy' ConnectionType; got '{json.ConnectionType}'")
                },
                IsJoinable = json.Joinability is "Joinable",
                SceneId = json.SceneId,
                Players = json.Players,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(json.CreatedAtUnixTimestamp).ToLocalTime()
            };
    }

    public class LdnGameJsonModel
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("is_public")] public bool IsPublic { get; set; }
        [JsonPropertyName("player_count")] public short PlayerCount { get; set; }
        [JsonPropertyName("max_player_count")] public short MaxPlayerCount { get; set; }
        [JsonPropertyName("game_name")] public string TitleName { get; set; }
        [JsonPropertyName("title_id")] public string TitleId { get; set; }
        [JsonPropertyName("title_version")] public string TitleVersion { get; set; }
        [JsonPropertyName("mode")] public string ConnectionType { get; set; }
        [JsonPropertyName("status")] public string Joinability { get; set; }
        [JsonPropertyName("scene_id")] public ushort SceneId { get; set; }
        [JsonPropertyName("players")] public string[] Players { get; set; }
        [JsonPropertyName("created_at")] public long CreatedAtUnixTimestamp { get; set; }

        public static LdnGameJsonModel Parse(string value)
            => JsonHelper.Deserialize(value, LdnGameJsonModelSerializerContext.Default.LdnGameJsonModel);
        
        public static LdnGameJsonModel[] ParseArray(string value)
            => JsonHelper.Deserialize(value, LdnGameJsonModelSerializerContext.Default.LdnGameJsonModelArray);
    }

    public enum ConnectionType
    {
        PeerToPeer,
        MasterServerProxy
    }

    [JsonSerializable(typeof(LdnGameJsonModel[]))]
    partial class LdnGameJsonModelSerializerContext : JsonSerializerContext;
}
