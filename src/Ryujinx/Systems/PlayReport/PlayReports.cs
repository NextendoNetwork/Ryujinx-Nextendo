using System;

namespace Ryujinx.Ava.Systems.PlayReport
{
    public static partial class PlayReports
    {
        public static void Initialize()
        {
            // init lazy value
            _ = Analyzer;
        }

        public static Analyzer Analyzer => _analyzerLazy.Value;

        private static readonly Lazy<Analyzer> _analyzerLazy = new(() => new Analyzer()
            .AddSpec(
                "01007ef00011e000", // Breath of the Wild
                spec => spec
                    .WithDescription("based on being in Master Mode.")
                    .AddValueFormatter("IsHardMode", BreathOfTheWild_MasterMode)
                    // reset to normal status when switching between normal & master mode in title screen
                    .AddValueFormatter("AoCVer", FormattedValue.SingleAlwaysResets)
            )
            .AddSpec(
                "0100f2c0115b6000", // Tears of the Kingdom
                spec => spec
                    .WithDescription("based on where you are in Hyrule (Depths, Surface, Sky).")
                    .AddValueFormatter("PlayerPosY", TearsOfTheKingdom_CurrentField))
            .AddSpec(
                "01002da013484000", // Skyward Sword
                spec => spec
                    .WithDescription("based on how many Rupees you have.")
                    .AddValueFormatter("rupees", SkywardSwordHD_Rupees))

            .AddSpec(
                "01008cf01baac000", // Echoes of Wisdom
                spec => spec
                    .WithDescription("based on where you've warped.")
                    .AddValueFormatter("dest_index", EchoesOfWisdom_Warp)
            )

            .AddSpec(
                "010049900f546000", // Super Mario 3D All Stars
                spec => spec
                    .WithDescription("based on what album and track you're listening to.")
                    .AddMultiValueFormatter(["app_id","song_id"], SuperMario3DAllStars_MainMenu)
            )
            .AddSpec(
                ["010049900f546001", "010049900f546002", "010049900F546003"], // Super Mario 3D All Stars
                spec => spec
                    .WithDescription("based on which game you've selected to play in the collection.")
                    .AddValueFormatter("program_id", SuperMario3DAllStars)
            )
            .AddSpec(
                "0100000000010000", // Super Mario Odyssey
                spec => spec
                    .WithDescription("based on what kingdom you're in.")
                    .AddValueFormatter("stage_name", SuperMarioOdyssey)
                )
            .AddSpec(
                "010028600ebda000", // Super Mario 3D World + Bowser's Fury
                spec => spec
                    .WithDescription("based on being in either Super Mario 3D World or Bowser's Fury.")
                    .AddValueFormatter("mode", SuperMario3DWorldOrBowsersFury)
            )
            .AddSpec(
                ["010049900f546000", "010049900f546001", "010049900f546002", "010049900F546003"],
                spec => spec
                .WithDescription("based on which game you've selected to play in the collection.")
                .AddValueFormatter("program_id", SuperMario3DAllStars)
            )
            .AddSpec(
                "010015100b514000", // Super Mario Bros. Wonder
                spec => spec
                .WithDescription("based on what world and course you're in.")
                .AddValueFormatter("stage_info", SuperMarioWonder)
            )
            .AddSpec( // Global & China IDs
                ["0100152000022000", "010075100e8ec000"], // Mario Kart 8 Deluxe
                spec => spec
                    .WithDescription(
                        "based on what modes you're selecting in the menu & whether or not you're in a race.")
                    .AddValueFormatter("To", MarioKart8Deluxe_Mode)
            )
            .AddSpec(
                ["0100a3d008c5c000", "01008f6008c5e000"], // Pokemon Scarlet/Violet
                spec => spec
                    .WithDescription("based on if you're playing alone or in a group and what area of Paldea you're exploring.")
                    .AddMultiValueFormatter(["team_circle", "area_no"], PokemonSV)
            )
            .AddSpec(
                "01006a800016e000", // Super Smash Bros. Ultimate
                spec => spec
                    .WithDescription("based on what mode you're playing, who won, and what characters were present.")
                    .AddSparseMultiValueFormatter(
                        [
                            // Metadata to figure out what PlayReport we have.
                            "match_mode", "match_submode", "anniversary", "fighter", "reason", "challenge_count",
                            "adv_slot", "is_created",
                            // List of Fighters
                            "player_1_fighter", "player_2_fighter", "player_3_fighter", "player_4_fighter",
                            "player_5_fighter", "player_6_fighter", "player_7_fighter", "player_8_fighter",
                            // List of rankings/placements
                            "player_1_rank", "player_2_rank", "player_3_rank", "player_4_rank", "player_5_rank",
                            "player_6_rank", "player_7_rank", "player_8_rank"
                        ],
                        SuperSmashBrosUltimate_Mode
                    )
            )
            .AddSpec(
                [
                    "0100B4E00444C000", "0100d870045b6000", "01008d300c50c000", "0100c62011050000", "010012f017576000",
                    /*Famicom*/         /*NES*/             /*SNES*/            /*GBC*/             /*GBA*/
                    "0100b3c014bda000", "0100c9a00ece6000", "0100e0601c632000", "0100bfc01d976000"
                    /*SEGA Genesis*/    /*N64*/             /*N64 MATURE*/      /*Virtual Boy*/
                ],
                spec => spec
                    .WithDescription(
                        "based on what game you first launch.\n\nNSO emulators do not print any Play Report information past the first game launch so it's all we got.")
                    .AddValueFormatter("launch_title_id", NsoEmulator_LaunchedGame)
            )
            .AddSpec(
                [ "010051f0207b2000", "0100ca502552a000" ], // Tomodachi Life: Living the Dream + Demo
                spec => spec
                    .WithDescription(
                        "based on your total Mii count and island level.")
                    .AddValueFormatter("Common", TomodachiLifeLTD_Status)
            )
            .AddSpec(
                "01006f8002326000", // Animal Crossing New Horizons
                spec => spec
                    .WithDescription("based on your island name.")
                    .AddValueFormatter("AppCmn", AnimalCrossingNewHorizons_AppCommon)
            )
            .AddSpec(
                "01003da010e8a000", // Miitopia 01003da010e8a000
                spec => spec
                .WithDescription("based on gold count, report info only in the mii selector, and gamestage (progression)")
                .AddSparseMultiValueFormatter(["gold", "secret", "stage"], MiitopiaRPC)
            )
            .AddSpec(
                "0100ea80032ea000", // New Super Mario Bros U Deluxe
                spec => spec
                    .WithDescription("based on world map return info.")
                    .AddSparseMultiValueFormatter(["WorldNo", "CourseNo", "RlId", "TotalPlayTime", "GameModeType"], NsmbudRpc)
                )
        );

        private static string Playing(string game) => $"Playing {game}";
    }
}
