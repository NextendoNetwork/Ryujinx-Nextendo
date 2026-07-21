using Gommon;
using Humanizer;
using MsgPack;
using Ryujinx.Common;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Ryujinx.Ava.Systems.PlayReport
{
    public partial class PlayReports
    {
        private static FormattedValue BreathOfTheWild_MasterMode(SingleValue value)
            => value.Matched.BoxedValue is 1 ? "Playing Master Mode" : FormattedValue.ForceReset;

        private static FormattedValue TearsOfTheKingdom_CurrentField(SingleValue value) =>
            value.Matched.DoubleValue switch
            {
                > 800d => "Exploring the Sky Islands",
                < -201d => "Exploring the Depths",
                _ => "Roaming Hyrule"
            };

        private static FormattedValue SkywardSwordHD_Rupees(SingleValue value)
            => "rupee".ToQuantity(value.Matched.IntValue);

        private static FormattedValue EchoesOfWisdom_Warp(SingleValue value)
        {
            FormattedValue locations = value.Matched.IntValue switch
            {
                // Hyrule Field
                23 => "Hyrule Field: Kakariko Village",
                43 => "Hyrule Field: West of Hyrule Ranch",
                45 => "Hyrule Field: North of Hyrule Ranch",
                25 => "Hyrule Field: Hyrule Ranch",
                26 => "Hyrule Field: West of Hyrule Castle",
                48 => "Hyrule Field: Haunted Grove",
                24 => "Hyrule Field: Hyrule Castle",
                27 => "Hyrule Field: Northern Sanctuary",
                28 => "Eastern Hyrule Field: Eastern Temple",
                41 => "Eastern Hyrule Field: Dampé Studio",
                22 => "Lake Hylia: Great Fairy Shrine",
                // Eternal Forest
                47 => "Eternal Forest: Entrance",
                46 => "Eternal Forest: Great Deku Tree",
                752 => "Eternal Forest: Stilled Ancient Ruins (Halfway Point)",
                753 => "Eternal Forest: Stilled Ancient Ruins (Null)",
                // Suthorn
                33 => "Suthorn Prairie: Lueburry's House",
                20 => "Suthorn Prairie: Suthorn Village",
                21 => "Suthorn Forest: Suthorn Ruins",
                // Faron Wetlands
                13 => "Faron Wetlands: Entrance",
                15 => "Faron Wetlands: Scrubton",
                18 => "Faron Wetlands: Blossu's House",
                17 => "Faron Wetlands: Heart Lake",
                852 => "Faron Wetlands: Stilled Faron Wetlands",
                601 => "Faron Wetlands: Faron Temple 3F",
                602 => "Faron Wetlands: Faron Temple 2F (Underwater Entrance)",
                603 => "Faron Wetlands: Faron Temple 2F (West Entrance)",
                604 => "Faron Wetlands: Faron Temple 2F (Cliff Entrance)",
                605 => "Faron Wetlands: Faron Temple 1F (Diababa)",
                606 => "Faron Wetlands: Faron Temple 1F (Gohma)",
                // Jabul Waters
                11 => "Jabul Waters: River Zora Village",
                9 => "Jabul Waters: Crossflows Plaza",
                8 => "Jabul Waters: Seesyde Village",
                12 => "Jabul Waters: Sea Zora Village",
                10 => "Jabul Waters: Lord Jabu-Jabu's Den",
                201 => "Jabul Waters: Jabul Ruins 1F (Entrance)",
                202 => "Jabul Waters: Jabul Ruins 1F (Vocavor)",
                // Gerudo Desert
                40 => "Gerudo Desert: Entrance",
                29 => "Gerudo Desert: Oasis",
                32 => "Gerudo Desert: Ancestor's Cave Of Rest",
                30 => "Gerudo Desert: Gerudo Town",
                31 => "Gerudo Desert: Gerudo Sanctum",
                351 => "Gerudo Desert: Stilled Gerudo Sanctum",
                303 => "Gerudo Desert: Gerudo Sanctum 1F (West Entrance)",
                304 => "Gerudo Desert: Gerudo Sanctum 1F (East Entrance)",
                301 => "Gerudo Desert: Gerudo Sanctum 2F (The Key)",
                302 => "Gerudo Desert: Gerudo Sanctum 2F (The Elephant Room)",
                305 => "Gerudo Desert: Gerudo Sanctum 2F (Mogryph)",
                // Eldin Volcano
                4 => "Eldin Volcano: Eldin Volcano Trail",
                44 => "Eldin Volcano: Lava Lake",
                3 => "Eldin Volcano: Goron City",
                5 => "Eldin Volcano: Rock Roast Volcano",
                49 => "Eldin Volcano: Crater Shortcut",
                552 => "Eldin Volcano: Stilled Eldin Volcano",
                501 => "Eldin Volcano: Eldin Temple 1F",
                503 => "Eldin Volcano: Eldin Temple 2F",
                502 => "Eldin Volcano: Eldin Temple 3F",
                // Hebra Mountain
                34 => "Hebra Mountain: Hebra Mountain Passage (1)",
                35 => "Hebra Mountain: Sheltered Hot Spring",
                36 => "Hebra Mountain: Condé's House",
                38 => "Hebra Mountain: Hebra Mountain Passage (2)",
                37 => "Hebra Mountain: Hebra Mountain Passage (3)",
                39 => "Hebra Mountain: Summit",
                652 => "Hebra Mountain: Stilled Holy Mount Lanayru",
                801 => "Hebra Mountain: Lanayru Temple 1F",
                802 => "Hebra Mountain: Lanayru Temple B2",
                803 => "Hebra Mountain: Lanayru Temple B4",
                _ => FormattedValue.ForceReset
            };

            return locations.Reset
                ? FormattedValue.ForceReset
                : $"Warped to {locations}";
        }
        
        private static FormattedValue SuperMario3DAllStars(SingleValue value)
        {
            // TODO: Is this really necessary?
            FormattedValue title = value.Matched.IntValue switch
            {
                1 => "Super Mario 64",
                2 => "Super Mario Sunshine",
                3 => "Super Mario Galaxy",
                _ => FormattedValue.ForceReset
            };

            return title.Reset
                ? FormattedValue.ForceReset
                : $"Playing {title}";
        }

        private static FormattedValue SuperMario3DAllStars_MainMenu(MultiValue value)
        {
            int albumId = value.Matched[0].IntValue;
            int songId = value.Matched[1].IntValue;

            string album = value.Matched[0].IntValue switch
            {
                1 => "Super Mario 64 OST",
                2 => "Super Mario Sunshine OST",
                3 => "Super Mario Galaxy OST",
                _ => "Listening to Super Mario 3D All-Stars"
            };

            string song = (albumId, songId) switch
            {
                // Super Mario 64
                (1, 0) => "It's a Me, Mario!",
                (1, 1) => "Title Theme",
                (1, 2) => "Peach's Message",
                (1, 3) => "Opening",
                (1, 4) => "Super Mario 64 Main Theme",
                (1, 5) => "Slider",
                (1, 6) => "Inside the Castle Walls",
                (1, 7) => "Looping Steps",
                (1, 8) => "Dire, Dire Docks",
                (1, 9) => "Lethal Lava Land",
                (1, 10) => "Snow Mountain",
                (1, 11) => "Haunted House",
                (1, 12) => "Merry-Go-Round",
                (1, 13) => "Cave Dungeon",
                (1, 14) => "Piranha Plant's Lullaby",
                (1, 15) => "Powerful Mario",
                (1, 16) => "Metallic Mario",
                (1, 17) => "File Select",
                (1, 18) => "Correct Solution",
                (1, 19) => "Toad's Message",
                (1, 20) => "Power Star",
                (1, 21) => "Race Fanfare",
                (1, 22) => "Star Catch Fanfare",
                (1, 23) => "Game Start",
                (1, 24) => "Course Clear",
                (1, 25) => "Game Over",
                (1, 26) => "Stage Boss",
                (1, 27) => "Koopa's Message",
                (1, 28) => "Koopa's Road",
                (1, 29) => "Koopa's Theme",
                (1, 30) => "Koopa Clear",
                (1, 31) => "Ultimate Koopa",
                (1, 32) => "Ultimate Koopa Clear",
                (1, 33) => "Ending Demo",
                (1, 34) => "Staff Roll",
                (1, 35) => "Piranha Plant's Lullaby - Piano",

                // Super Mario Sunshine
                (2, 0) => "Isle Delfino",
                (2, 1) => "Delfino Airstrip",
                (2, 2) => "Bianco Hills",
                (2, 3) => "Ricco Harbor",
                (2, 4) => "Gelato Beach",
                (2, 5) => "Pinna Beach",
                (2, 6) => "Pinna Park",
                (2, 7) => "Sirena Beach",
                (2, 8) => "Hotel Delfino",
                (2, 9) => "Casino",
                (2, 10) => "Noki Bay",
                (2, 11) => "Noki Depths",
                (2, 12) => "Pianta Village",
                (2, 13) => "Pianta Hot Spring",
                (2, 14) => "Pianta Rescue",
                (2, 15) => "Pianta Village - Fluff Festival",
                (2, 16) => "Underground",
                (2, 17) => "Secret Course",
                (2, 18) => "Secret Course - Sky and Sea",
                (2, 19) => "Corona Mountain",
                (2, 20) => "Mid-Boss",
                (2, 21) => "Proto Piranha",
                (2, 22) => "Phantamanta",
                (2, 23) => "Boss Battle",
                (2, 24) => "Gooper Blooper Intro",
                (2, 25) => "Wiggler Intro",
                (2, 26) => "Mecha-Bowser",
                (2, 27) => "Bowser",
                (2, 28) => "Shadow Mario",
                (2, 29) => "Racing Il Piantissimo",
                (2, 30) => "Event",
                (2, 31) => "Timed Event",
                (2, 32) => "Yoshi-Go-Round",
                (2, 33) => "Title Screen",
                (2, 34) => "Opening Demo",
                (2, 35) => "Select Data",
                (2, 36) => "Select Scenario",
                (2, 37) => "Course Intro",
                (2, 38) => "Course Intro - Shadow Mario",
                (2, 39) => "A Shine Sprite Appears",
                (2, 40) => "Shine!",
                (2, 41) => "Race Fanfare",
                (2, 42) => "Casino Fanfare",
                (2, 43) => "Too Bad!",
                (2, 44) => "Game Over",
                (2, 45) => "Welcome to Isle Delfino (Movie)",
                (2, 46) => "Icky Goop (Movie)",
                (2, 47) => "Mario on Trial (Movie)",
                (2, 48) => "How to Use FLUDD (Movie)",
                (2, 49) => "Shadow Mario Appears (Movie)",
                (2, 50) => "The Kidnapping of Princess Peach (Movie)",
                (2, 51) => "Mecha-Bowser Rises (Movie)",
                (2, 52) => "Meet Bowser Jr. (Movie)",
                (2, 53) => "FLUDD Theft (Movie)",
                (2, 54) => "Hot Tub Intrusion (Movie)",
                (2, 55) => "Epilogue (Movie)",
                (2, 56) => "Staff Credits",
                (2, 57) => "Have a Relaxing Vacation!",

                // Super Mario Galaxy
                (3, 0) => "Overture",
                (3, 1) => "The Star Festival",
                (3, 2) => "Attack of the Airships",
                (3, 3) => "Catastrophe",
                (3, 4) => "Peach's Castle Stolen",
                (3, 5) => "Enter the Galaxy",
                (3, 6) => "Egg Planet",
                (3, 7) => "Rosaline in the Observatory 1",
                (3, 8) => "The Honeyhive",
                (3, 9) => "Space Junk Road",
                (3, 10) => "Battlerock Galaxy",
                (3, 11) => "Beach Bowl Galaxy",
                (3, 12) => "Rosalina in the Observatory 2",
                (3, 13) => "Enter Bowser Jr.!",
                (3, 14) => "Waltz of the Boos",
                (3, 15) => "Buoy Base Galaxy",
                (3, 16) => "Gusty Garden Galaxy",
                (3, 17) => "Rosaline in the Observatory 3",
                (3, 18) => "King Bowser",
                (3, 19) => "Melty Molten Galaxy",
                (3, 20) => "The Galaxy Reactor",
                (3, 21) => "Final Battle with Bowser",
                (3, 22) => "A New Dawn",
                (3, 23) => "Birth",
                (3, 24) => "Super Mario Galaxy",
                (3, 25) => "Purple Comet",
                (3, 26) => "Blue Sky Athletic",
                (3, 27) => "Super Mario 2007",
                (3, 28) => "File Select",
                (3, 29) => "Luma",
                (3, 30) => "Gateway Galaxy",
                (3, 31) => "Stolen Grand Star",
                (3, 32) => "To the Observatory Grounds 1",
                (3, 33) => "Observation Dome",
                (3, 34) => "Course Select",
                (3, 35) => "Dino Piranha",
                (3, 36) => "A Chance to Grab a Star!",
                (3, 37) => "A Tense Moment",
                (3, 38) => "Big Bad Bugaboom",
                (3, 39) => "King Kaliente",
                (3, 40) => "The Toad Brigade",
                (3, 41) => "Airship Armada",
                (3, 42) => "Aquatic Race",
                (3, 43) => "Space Fantasy",
                (3, 44) => "Megaleg",
                (3, 45) => "To The Observatory Grounds 2",
                (3, 46) => "Space Athletic",
                (3, 47) => "Speedy Comet",
                (3, 48) => "Beach Bowl Galaxy - Undersea",
                (3, 49) => "Interlude",
                (3, 50) => "Bowser's Stronghold Appears",
                (3, 51) => "The Fiery Stronghold",
                (3, 52) => "The Big Staircase",
                (3, 53) => "Bowser Appears",
                (3, 54) => "Star Ball",
                (3, 55) => "The Library",
                (3, 56) => "Buoy Base Galaxy - Undersea",
                (3, 57) => "Rainbow Mario",
                (3, 58) => "Chase the Bunnies",
                (3, 59) => "Help!",
                (3, 60) => "Major Burrows",
                (3, 61) => "Pipe Interior",
                (3, 62) => "Cosmic Comet",
                (3, 63) => "Drip Drop Galaxy",
                (3, 64) => "Kingfin",
                (3, 65) => "Boo Race",
                (3, 66) => "Ice Mountain",
                (3, 67) => "Ice Mario",
                (3, 68) => "Lava Path",
                (3, 69) => "Fire Mario",
                (3, 70) => "Dusty Dune Galaxy",
                (3, 71) => "Heavy Metal Mecha-Bowser",
                (3, 72) => "A-wa-wa-wa!",
                (3, 73) => "Deep Dark Galaxy",
                (3, 74) => "Kamella",
                (3, 75) => "Star Ball 2",
                (3, 76) => "Sad Girl",
                (3, 77) => "Flying Mario",
                (3, 78) => "Star Child",
                (3, 79) => "A Wish",
                (3, 80) => "Family",
                _ => ""
            };
            
            return string.IsNullOrEmpty(song) ? FormattedValue.ForceReset : $"{album} - {song}";
        }

        private static FormattedValue SuperMarioOdyssey(SingleValue value)
            => value.Matched.LongValue switch
            {
                // TODO: Needs updated for sub-areas.
                2973331007 => "Cap Kingdom: Bonneton",
                2661781375 => "Cascade Kingdom: Fossil Falls",
                512560049 => "Sand Kingdom: Tostarena",
                3079659402 => "Wooded Kingdom: Steam Gardens",
                1941286268 => "Lake Kingdom: Lake Lamode",
                3098209122 => "Cloud Kingdom: Nimbus Arena",
                4088050842 => "Lost Kingdom: Forgotten Isle",
                53003352 => "Metro Kingdom: New Donk City",
                4265839612 => "Seaside Kingdom: Bubblaine",
                3288863344 => "Snow Kingdom: Shiveria",
                3180104973 => "Luncheon Kingdom: Mount Volbono",
                2284558980 => "Ruined Kingdom: Crumbleden",
                3024139598 => "Bowser's Kingdom: Bowser's Castle",
                1351608174 => "Moon Kingdom: Honeylune Ridge",
                1698750149 => "Dark Side: Rabbit Ridge",
                3206301958 => "Darker Side: Culmina Crater",
                3963002526 => "Mushroom Kingdom: Peach's Castle",
                _ => FormattedValue.ForceReset
            };

        private static FormattedValue SuperMario3DWorldOrBowsersFury(SingleValue value)
            => value.Matched.BoxedValue is 0 ? "Playing Super Mario 3D World" : "Playing Bowser's Fury";

        private static FormattedValue SuperMarioWonder(SingleValue value)
        {
            // TODO: Needs updated for course names.
            MessagePackObject messagePackObject = value.Matched.PackedValue;
            MessagePackObjectDictionary messagePackObjectDictionary = messagePackObject.AsDictionary();

            int worldNumber = messagePackObjectDictionary["world_no"].AsInt32();
            int courseNumber = 0;

            if (messagePackObjectDictionary.TryGetValue("course_no", out MessagePackObject courseNumberVariable))
            {
                courseNumber = courseNumberVariable.AsInt32();
            }

            FormattedValue world = worldNumber switch
            {
                1 => "Pipe-Rock Plateau",
                2 => "Petal Isles",
                3 => "Fluff-Puff Peaks",
                4 => "Shining Falls",
                5 => "Sunbaked Desert",
                6 => "Fungi Mines",
                7 => "Deep Magma Bog",
                9 => "Special World",
                _ => FormattedValue.ForceReset
            };

            if (courseNumber == 0)
            {
                return FormattedValue.ForceReset;
            }

            return world.Reset
                ? FormattedValue.ForceReset
                : $"{world}: {worldNumber}-{courseNumber}";
        }

        private static FormattedValue MarioKart8Deluxe_Mode(SingleValue value)
            => value.Matched.StringValue switch
            {
                // Single Player
                "Single" => "Single Player",
                // Multiplayer
                "Multi-2players" => "Multiplayer: 2 Players",
                "Multi-3players" => "Multiplayer: 3 Players",
                "Multi-4players" => "Multiplayer: 4 Players",
                // Wireless/LAN Play
                "Local-Single" => "Wireless/LAN Play",
                "Local-2players" => "Wireless/LAN Play 2 Players",
                // CC Classes
                "50cc" => "50cc",
                "100cc" => "100cc",
                "150cc" => "150cc",
                "Mirror" => "Mirror (150cc)",
                "200cc" => "200cc",
                // Modes
                "GrandPrix" => "Grand Prix",
                "TimeAttack" => "Time Trials",
                "VS" => "VS Races",
                "Battle" => "Battle Mode",
                "RaceStart" => "Selecting a Course",
                "Race" => "Racing",
                _ => FormattedValue.ForceReset
            };

        private static FormattedValue PokemonSV(MultiValue values)
        {
            string region = PokemonSV_Region(values.Matched[1].ToString());
            string union = values.Matched[0].BoxedValue is 0 ? "" : " with friends";
            string academyName = PokemonSV_AcademyName(values.Application.Title);

            FormattedValue locations = values.Matched[1].ToString() switch
            {
                // Base Game Locations
                "a_w01" => "South Area One",
                "a_w02" => "Mesagoza",
                "a_w03" => "The Pokemon League",
                "a_w04" => "South Area Two",
                "a_w05" => "South Area Four",
                "a_w06" => "South Area Six",
                "a_w07" => "South Area Five",
                "a_w08" => "South Area Three",
                "a_w09" => "West Area One",
                "a_w10" => "Asado Desert",
                "a_w11" => "West Area Two",
                "a_w12" => "Medali",
                "a_w13" => "Tagtree Thicket",
                "a_w14" => "East Area Three",
                "a_w15" => "Artazon",
                "a_w16" => "East Area Two",
                "a_w18" => "Casseroya Lake",
                "a_w19" => "Glaseado Mountain",
                "a_w20" => "North Area Three",
                "a_w21" => "North Area One",
                "a_w22" => "North Area Two",
                "a_w23" => "Area Zero: The Great Crater of Paldea",
                "a_w24" => "South Paldean Sea",
                "a_w25" => "West Paldean Sea",
                "a_w26" => "East Paldean Sea",
                "a_w27" => "North Paldean Sea",
                // Naranja / Uva Academy
                "a_sch_entrance01" => $"{academyName} Academy: Entrance",
                "a_sch_cafe01" => $"{academyName} Academy: Cafeteria",
                "a_sch_shop01" => $"{academyName} Academy: School Store",
                "a_sch_room01" => $"{academyName} Academy: Home Ec Room",
                "a_sch_room02" => $"{academyName} Academy: Art Room",
                "a_sch_room03" => $"{academyName} Academy: Biology Lab",
                "a_sch_room04" => $"{academyName} Academy: Staff Room",
                "a_sch_office01" => $"{academyName} Academy: Director's Office",
                "a_sch_office03" => $"{academyName} Academy: Nurse's Office",
                "a_sch_ground01" => $"{academyName} Academy: School Yard",
                "a_sch_class1a" => $"{academyName} Academy: Classroom 1-A",
                "a_sch_class1d" => $"{academyName} Academy: Classroom 1-D",
                "a_sch_class2g" => $"{academyName} Academy: Classroom 2-G",
                "a_sch_dorm01" => $"{academyName} Academy: Dorm Room (Trainer)",
                "a_sch_dorm02" => $"{academyName} Academy: Dorm Room (Nemona)",
                "a_sch_dorm03" => $"{academyName} Academy: Dorm Room (Arven)",
                "a_sch_dorm04" => $"{academyName} Academy: Dorm Room (Penny)",
                // DLC
                // Kitakami
                "a_su0101" => "Mossui Town",
                "a_su0102" => "Loyalty Plaza",
                "a_su0103" => "Kitakami Hall",
                "a_su0104" => "Oni Mountain",
                "a_su0105" => "Infernal Pass",
                "a_su0106" => "Crystal Pool",
                "a_su0107" => "Wistful Fields",
                "a_su0108" => "Mossfell Confluence",
                "a_su0109" => "Fellhorn Gorge",
                "a_su0110" => "Paradise Barrens",
                "a_su0111" => "Timeless Woods",
                // Blueberry Academy: School
                "a_sch_2_entrance0" => "Blueberry Academy: Entrance",
                "a_sch_2_clubroom" => "Blueberry Academy: League Clubroom",
                "a_sch_2_class1" => "Blueberry Academy: Classroom 1-4",
                "a_sch_2_class2" => "Blueberry Academy: Classroom 3-2",
                "a_sch_2_shop01" => "Blueberry Academy: School Store",
                "a_sch_2_cafe01" => "Blueberry Academy: Cafeteria",
                "a_sch_2_dorm01" => "Blueberry Academy: Dorm Room (Trainer)",
                "a_sch_2_dorm02" => "Blueberry Academy: Dorm Room (Carmine)",
                // Blueberry Academy: Terrarium
                "a_su0201" => "Savanna Biome",
                "a_su0202" => "Coastal Biome",
                "a_su0203" => "Canyon Biome",
                "a_su0204" => "Polar Biome",
                _ => FormattedValue.ForceReset
            };

            return locations.Reset
                ? FormattedValue.ForceReset
                : $"Exploring {region}{union} | {locations}";
        }

        private static string PokemonSV_Region(string location)
        {
            if (location.Contains("a_su02") || location.Contains("a_sch_2")) return "Unova";
            if (location.Contains("a_su01")) return "Kitakami";
            return "Paldea";
        }

        private static string PokemonSV_AcademyName(string title)
        {
            // TODO: Is this even necessary?
            if (
                title.Contains("Scarlet")
                || title.Contains("Escarlata")
                || title.Contains("Écarlate")
                || title.Contains("Karmesin")
                || title.Contains("Scarlatto")
                || title.Contains("スカーレット")
                || title.Contains("스칼렛")
                || title.Contains("朱")

            ) { return "Naranja"; }
            return "Uva";
        }

        private static FormattedValue SuperSmashBrosUltimate_Mode(SparseMultiValue values)
        {
            // Check if the PlayReport is for a challenger approach or an achievement.
            if (values.Matched.TryGetValue("fighter", out Value fighter) && values.Matched.ContainsKey("reason"))
            {
                return $"Challenger Approaches - {SuperSmashBrosUltimate_Character(fighter)}";
            }

            if (values.Matched.TryGetValue("fighter", out fighter) && values.Matched.ContainsKey("challenge_count"))
            {
                return $"Fighter Unlocked - {SuperSmashBrosUltimate_Character(fighter)}";
            }

            if (values.Matched.TryGetValue("anniversary", out Value anniversary))
            {
                return $"Achievement Unlocked - ID: {anniversary}";
            }

            if (values.Matched.ContainsKey("is_created"))
            {
                return "Edited a Custom Stage!";
            }

            if (values.Matched.ContainsKey("adv_slot"))
            {
                return
                    "Playing Adventure Mode"; // Doing this as it can be a placeholder until we can grab the character.
            }

            // Check if we have a match_mode at this point, if not, go to default.
            if (!values.Matched.TryGetValue("match_mode", out Value matchMode))
            {
                return "Smashing";
            }

            return matchMode.BoxedValue switch
            {
                0 when values.Matched.TryGetValue("player_1_fighter", out Value player) &&
                       values.Matched.TryGetValue("player_2_fighter", out Value challenger)
                    => $"Last Smashed: {SuperSmashBrosUltimate_Character(challenger)}'s Fighter Challenge - {SuperSmashBrosUltimate_Character(player)}",
                1 => $"Last Smashed: Normal Battle - {SuperSmashBrosUltimate_PlayerListing(values)}",
                2 when values.Matched.TryGetValue("player_1_rank", out Value team)
                    => team.BoxedValue is 0
                        ? "Last Smashed: Squad Strike - Red Team Wins"
                        : "Last Smashed: Squad Strike - Blue Team Wins",
                3 => $"Last Smashed: Custom Smash - {SuperSmashBrosUltimate_PlayerListing(values)}",
                4 => $"Last Smashed: Super Sudden Death - {SuperSmashBrosUltimate_PlayerListing(values)}",
                5 => $"Last Smashed: Smashdown - {SuperSmashBrosUltimate_PlayerListing(values)}",
                6 => $"Last Smashed: Tourney Battle - {SuperSmashBrosUltimate_PlayerListing(values)}",
                7 when values.Matched.TryGetValue("player_1_fighter", out Value player)
                    => $"Last Smashed: Spirit Board Battle as {SuperSmashBrosUltimate_Character(player)}",
                8 when values.Matched.TryGetValue("player_1_fighter", out Value player)
                    => $"Playing Adventure Mode as {SuperSmashBrosUltimate_Character(player)}",
                10 when values.Matched.TryGetValue("match_submode", out Value battle) &&
                        values.Matched.TryGetValue("player_1_fighter", out Value player)
                    => $"Last Smashed: Classic Mode, Battle {(int)battle.BoxedValue + 1}/8 as {SuperSmashBrosUltimate_Character(player)}",
                12 => $"Last Smashed: Century Smash - {SuperSmashBrosUltimate_PlayerListing(values)}",
                13 => $"Last Smashed: All-Star Smash - {SuperSmashBrosUltimate_PlayerListing(values)}",
                14 => $"Last Smashed: Cruel Smash - {SuperSmashBrosUltimate_PlayerListing(values)}",
                15 when values.Matched.TryGetValue("player_1_fighter", out Value player)
                    => $"Last Smashed: Home-Run Contest - {SuperSmashBrosUltimate_Character(player)}",
                16 when values.Matched.TryGetValue("player_1_fighter", out Value player1) &&
                        values.Matched.TryGetValue("player_2_fighter", out Value player2)
                    => $"Last Smashed: Home-Run Content (Co-op) - {SuperSmashBrosUltimate_Character(player1)} and {SuperSmashBrosUltimate_Character(player2)}",
                17 => $"Last Smashed: Home-Run Contest (Versus) - {SuperSmashBrosUltimate_PlayerListing(values)}",
                18 when values.Matched.TryGetValue("player_1_fighter", out Value player1) &&
                        values.Matched.TryGetValue("player_2_fighter", out Value player2)
                    => $"Fresh out of Training mode - {SuperSmashBrosUltimate_Character(player1)} with {SuperSmashBrosUltimate_Character(player2)}",
                58 => $"Last Smashed: LDN Battle - {SuperSmashBrosUltimate_PlayerListing(values)}",
                63 when values.Matched.TryGetValue("player_1_fighter", out Value player)
                    => $"Last Smashed: DLC Spirit Board Battle as {SuperSmashBrosUltimate_Character(player)}",
                _ => "Smashing"
            };
        }

        private static string SuperSmashBrosUltimate_Character(Value value) =>
            BinaryPrimitives.ReverseEndianness(
                    BitConverter.ToInt64(((MsgPack.MessagePackExtendedTypeObject)value.BoxedValue).GetBody(), 0)) switch
            {
                0x0 => "Mario",
                0x1 => "Donkey Kong",
                0x2 => "Link",
                0x3 => "Samus",
                0x4 => "Dark Samus",
                0x5 => "Yoshi",
                0x6 => "Kirby",
                0x7 => "Fox",
                0x8 => "Pikachu",
                0x9 => "Luigi",
                0xA => "Ness",
                0xB => "Captain Falcon",
                0xC => "Jigglypuff",
                0xD => "Peach",
                0xE => "Daisy",
                0xF => "Bowser",
                0x10 => "Ice Climbers",
                0x11 => "Sheik",
                0x12 => "Zelda",
                0x13 => "Dr. Mario",
                0x14 => "Pichu",
                0x15 => "Falco",
                0x16 => "Marth",
                0x17 => "Lucina",
                0x18 => "Young Link",
                0x19 => "Ganondorf",
                0x1A => "Mewtwo",
                0x1B => "Roy",
                0x1C => "Chrom",
                0x1D => "Mr Game & Watch",
                0x1E => "Meta Knight",
                0x1F => "Pit",
                0x20 => "Dark Pit",
                0x21 => "Zero Suit Samus",
                0x22 => "Wario",
                0x23 => "Snake",
                0x24 => "Ike",
                0x25 => "Pokémon Trainer",
                0x26 => "Diddy Kong",
                0x27 => "Lucas",
                0x28 => "Sonic",
                0x29 => "King Dedede",
                0x2A => "Olimar",
                0x2B => "Lucario",
                0x2C => "R.O.B.",
                0x2D => "Toon Link",
                0x2E => "Wolf",
                0x2F => "Villager",
                0x30 => "Mega Man",
                0x31 => "Wii Fit Trainer",
                0x32 => "Rosalina & Luma",
                0x33 => "Little Mac",
                0x34 => "Greninja",
                0x35 => "Palutena",
                0x36 => "Pac-Man",
                0x37 => "Robin",
                0x38 => "Shulk",
                0x39 => "Bowser Jr.",
                0x3A => "Duck Hunt",
                0x3B => "Ryu",
                0x3C => "Ken",
                0x3D => "Cloud",
                0x3E => "Corrin",
                0x3F => "Bayonetta",
                0x40 => "Richter",
                0x41 => "Inkling",
                0x42 => "Ridley",
                0x43 => "King K. Rool",
                0x44 => "Simon",
                0x45 => "Isabelle",
                0x46 => "Incineroar",
                0x47 => "Mii Brawler",
                0x48 => "Mii Swordfighter",
                0x49 => "Mii Gunner",
                0x4A => "Piranha Plant",
                0x4B => "Joker",
                0x4C => "Hero",
                0x4D => "Banjo",
                0x4E => "Terry",
                0x4F => "Byleth",
                0x50 => "Min Min",
                0x51 => "Steve",
                0x52 => "Sephiroth",
                0x53 => "Pyra/Mythra",
                0x54 => "Kazuya",
                0x55 => "Sora",
                0xFE => "Random",
                0xFF => "Scripted Entity",
                _ => "Unknown"
            };

        private static string SuperSmashBrosUltimate_PlayerListing(SparseMultiValue values)
        {
            List<(string Character, int PlayerNumber, int? Rank)> players = [];

            foreach (KeyValuePair<string, Value> player in values.Matched)
            {
                if (player.Key.StartsWith("player_") && player.Key.EndsWith("_fighter") &&
                    player.Value.BoxedValue is not null)
                {
                    if (!int.TryParse(player.Key.Split('_')[1], out int playerNumber))
                        continue;

                    string character = SuperSmashBrosUltimate_Character(player.Value);
                    int? rank = values.Matched.TryGetValue($"player_{playerNumber}_rank", out Value rankValue)
                        ? rankValue.IntValue
                        : null;

                    players.Add((character, playerNumber, rank));
                }
            }

            players = players.OrderBy(p => p.Rank ?? int.MaxValue).ToList();

            return players.Count > 4
                ? $"{players.Count} Players - {players.Take(3)
                        .Select(p => $"{p.Character}({p.PlayerNumber}){RankMedal(p.Rank)}")
                        .JoinToString(", ")}"
                : players
                    .Select(p => $"{p.Character}({p.PlayerNumber}){RankMedal(p.Rank)}")
                    .JoinToString(", ");

            static string RankMedal(int? rank) => rank switch
            {
                0 => "🥇",
                1 => "🥈",
                2 => "🥉",
                _ => ""
            };
        }

        private static FormattedValue NsoEmulator_LaunchedGame(SingleValue value) => value.Matched.StringValue switch
        {
            #region SEGA Genesis

            "m_0054_e" => Playing("Alien Soldier"),
            "m_3978_e" => Playing("Alien Storm"),
            "m_5234_e" => Playing("ALISIA DRAGOON"),
            "m_5003_e" => Playing("Streets of Rage 2"),
            "m_4843_e" => Playing("Kid Chameleon"),
            "m_2874_e" => Playing("Columns"),
            "m_3167_e" => Playing("Comix Zone"),
            "m_5007_e" => Playing("Contra: Hard Corps"),
            "m_0865_e" => Playing("Ghouls 'n Ghosts"),
            "m_0935_e" => Playing("Dynamite Headdy"),
            "m_8314_e" => Playing("Earthworm Jim"),
            "m_5012_e" => Playing("Ecco the Dolphin"),
            "m_2207_e" => Playing("Flicky"),
            "m_9432_e" => Playing("Golden Axe II"),
            "m_5015_e" => Playing("Golden Axe"),
            "m_5017_e" => Playing("Gunstar Heroes"),
            "m_0732_e" => Playing("Altered Beast"),
            "m_2245_e" or "m_2245_pd" or "m_2245_pf" => Playing("Landstalker"),
            "m_1654_e" => Playing("Target Earth"),
            "m_7050_e" => Playing("Light Crusader"),
            "m_5027_e" => Playing("M.U.S.H.A."),
            "m_5028_e" => Playing("Phantasy Star IV"),
            "m_9155_e" => Playing("Pulseman"),
            "m_5030_e" => Playing("Dr. Robotnik's Mean Bean Machine"),
            "m_0098_e" => Playing("Crusader of Centy"),
            "m_0098_k" => Playing("신창세기 라그나센티"),
            "m_0098_pd" or "m_0098_pf" or "m_0098_ps" => Playing("Soleil"),
            "m_5033_e" => Playing("Ristar"),
            "m_1987_e" => Playing("MEGA MAN: THE WILY WARS"),
            "m_2609_e" => Playing("WOLF OF THE BATTLEFIELD: MERCS"),
            "m_3353_e" => Playing("Shining Force II"),
            "m_5036_e" => Playing("Shining Force"),
            "m_9866_e" => Playing("Sonic The Hedgehog Spinball"),
            "m_5041_e" => Playing("Sonic The Hedgehog 2"),
            "m_5523_e" => Playing("Space Harrier II"),
            "m_0041_e" => Playing("STREET FIGHTER II' : SPECIAL CHAMPION EDITION"),
            "m_5044_e" => Playing("STRIDER"),
            "m_6353_e" => Playing("Super Fantasy Zone"),
            "m_9569_e" => Playing("Beyond Oasis"),
            "m_9569_k" => Playing("스토리 오브 도어"),
            "m_9569_pd" or "m_9569_ps" => Playing("The Story of Thor"),
            "m_9569_pf" => Playing("La Légende de Thor"),
            "m_5049_e" => Playing("Shinobi III: Return of the Ninja Master"),
            "m_6811_e" => Playing("The Revenge of Shinobi"),
            "m_4372_e" => Playing("Thunder Force II"),
            "m_1535_e" => Playing("ToeJam & Earl in Panic on Funkotron"),
            "m_0432_e" => Playing("ToeJam & Earl"),
            "m_5052_e" => Playing("Castlevania: BLOODLINES"),
            "m_3626_e" => Playing("VectorMan"),
            "m_7955_e" => Playing("Sword of Vermilion"),
            "m_0394_e" => Playing("Virtua Fighter 2"),
            "m_9417_e" => Playing("Zero Wing"),

            #endregion

            #region Nintendo 64

            "n_1653_e" or "n_1653_p" => Playing("1080º ™ Snowboarding"),
            "n_4868_e" or "n_4868_p" => Playing("Banjo Kazooie™"),
            "n_1226_e" or "n_1226_p" => Playing("Banjo-Tooie™"),
            "n_3083_e" or "n_3083_p" => Playing("Blast Corps"),
            "n_3007_e" => Playing("Dr. Mario™ 64"),
            "n_4238_e" => Playing("Excitebike™ 64"),
            "n_1870_e" => Playing("Extreme G"),
            "n_2456_e" => Playing("F-Zero™ X"),
            "n_4631_e" => Playing("GoldenEye 007"),
            "n_1635_e" => Playing("Harvest Moon 64"),
            "n_2225_e" => Playing("Iggy’s Reckin’ Balls"),
            "n_1625_e" or "n_1625_p" => Playing("JET FORCE GEMINI™"),
            "n_3052_e" => Playing("Kirby 64™: The Crystal Shards"),
            "n_4371_e" => Playing("Mario Golf™"),
            "n_3013_e" => Playing("Mario Kart™ 64"),
            "n_1053_e" or "n_1053_p" => Playing("Mario Party™ 2"),
            "n_2965_e" or "n_2965_p" => Playing("Mario Party™ 3"),
            "n_4737_e" or "n_4737_p" => Playing("Mario Party™"),
            "n_3017_e" => Playing("Mario Tennis™"),
            "n_2992_e" or "n_2992_p" => Playing("Paper Mario™"),
            "n_3783_e" or "n_3783_p" => Playing("Pilotwings™ 64"),
            "n_1848_e" or "n_1848_pd" or "n_1848_pf" => Playing("Pokémon™ Puzzle League"),
            "n_3240_e" or "n_3240_pd" or "n_3240_pf" or "n_3240_pi" or "n_3240_ps" => Playing("Pokémon Snap™"),
            "n_4590_e" or "n_4590_pd" or "n_4590_pf" or "n_4590_pi" or "n_4590_ps" => Playing("Pokémon Stadium™"),
            "n_3309_e" or "n_3309_pd" or "n_3309_pf" or "n_3309_pi" or "n_3309_ps" => Playing("Pokémon Stadium 2™"),
            "n_3029_e" => Playing("Sin & Punishment™"),
            "n_3030_e" => Playing("Star Fox™ 64"),
            "n_3030_p" => Playing("Lylat Wars™"),
            "n_3031_e" or "n_3031_p" => Playing("Super Mario 64™"),
            "n_4813_e" or "n_4813_p" => Playing("Wave Race™ 64"),
            "n_3034_e" => Playing("WIN BACK: COVERT OPERATIONS"),
            "n_3034_p" => Playing("OPERATION: WIN BACK"),
            "n_3036_e" or "n_3036_p" => Playing("Yoshi's Story™"),
            "n_1407_e" or "n_1407_p" => Playing("The Legend of Zelda™: Majora's Mask™"),
            "n_3038_e" or "n_3038_p" => Playing("The Legend of Zelda™: Ocarina of Time™"),

            #endregion

            #region NES

            "clv_p_naaae" => Playing("Super Mario Bros.™"),
            "clv_p_naabe" => Playing("Super Mario Bros.™: The Lost Levels"),
            "clv_p_naace" or "clv_p_naace_sp1" => Playing("Super Mario Bros.™ 3"),
            "clv_p_naade" => Playing("Super Mario Bros.™ 2"),
            "clv_p_naaee" => Playing("Donkey Kong™"),
            "clv_p_naafe" => Playing("Donkey Kong Jr.™"),
            "clv_p_naage" => Playing("Donkey Kong™ 3"),
            "clv_p_naahe" => Playing("Excitebike™"),
            "clv_p_naaje" => Playing("EarthBound Beginnings"),
            "clv_p_naame" => Playing("NES™ Open Tournament Golf"),
            "clv_p_naane" or "clv_p_naane_sp1" => Playing("The Legend of Zelda™"),
            "clv_p_naape" or "clv_p_naape_sp1" => Playing("Kirby's Adventure™"),
            "clv_p_naaqe" or "clv_p_naaqe_sp1" or "clv_p_naaqe_sp2" => Playing("Metroid™"),
            "clv_p_naare" => Playing("Balloon Fight™"),
            "clv_p_naase" or "clv_p_naase_sp1" => Playing("Zelda II - The Adventure of Link™"),
            "clv_p_naate" => Playing("Punch-Out!!™ Featuring Mr. Dream"),
            "clv_p_naaue" => Playing("Ice Climber™"),
            "clv_p_naave" or "clv_p_naave_sp1" => Playing("Kid Icarus™"),
            "clv_p_naawe" => Playing("Mario Bros.™"),
            "clv_p_naaxe" or "clv_p_naaxe_sp1" => Playing("Dr. Mario™"),
            "clv_p_naaye" => Playing("Yoshi™"),
            "clv_p_naaze" => Playing("StarTropics™"),
            "clv_p_nabce" or "clv_p_nabce_sp1" => Playing("Ghosts'n Goblins™"),
            "clv_p_nabre" or "clv_p_nabre_sp1" or "clv_p_nabre_sp2" => Playing("Gradius"),
            "clv_p_nacbe" or "clv_p_nacbe_sp1" => Playing("Ninja Gaiden"),
            "clv_p_nacce" => Playing("Solomon's Key"),
            "clv_p_nacde" => Playing("Tecmo Bowl"),
            "clv_p_nacfe" => Playing("Double Dragon"),
            "clv_p_nache" => Playing("Double Dragon II: The Revenge"),
            "clv_p_nacje" => Playing("River City Ransom"),
            "clv_p_nacke" => Playing("Super Dodge Ball"),
            "clv_p_nacle" => Playing("Downtown Nekketsu March Super-Awesome Field Day!"),
            "clv_p_nacpe" => Playing("The Mystery of Atlantis"),
            "clv_p_nacre" => Playing("Soccer"),
            "clv_p_nacse" or "clv_p_nacse_sp1" => Playing("Ninja JaJaMaru-kun"),
            "clv_p_nacte" => Playing("Ice Hockey"),
            "clv_p_nacue" or "clv_p_nacue_sp1" => Playing("Blaster Master"),
            "clv_p_nacwe" => Playing("ADVENTURES OF LOLO"),
            "clv_p_nacxe" => Playing("Wario's Woods™"),
            "clv_p_nacye" => Playing("Tennis"),
            "clv_p_nacze" => Playing("Wrecking Crew™"),
            "clv_p_nadbe" => Playing("Joy Mech Fight™"),
            "clv_p_nadde" or "clv_p_nadde_sp1" => Playing("Star Soldier"),
            "clv_p_nadke" => Playing("Tetris®"),
            "clv_p_nadle" => Playing("Pro Wrestling"),
            "clv_p_nadpe" => Playing("Baseball"),
            "clv_p_nadte" or "clv_p_nadte_sp1" => Playing("TwinBee"),
            "clv_p_nadue" or "clv_p_nadue_sp1" => Playing("Mighty Bomb Jack"),
            "clv_p_nadve" => Playing("Kung-Fu Heroes"),
            "clv_p_nadxe" => Playing("City Connection"),
            "clv_p_nadye" => Playing("Rygar"),
            "clv_p_naeae" => Playing("Crystalis"),
            "clv_p_naece" => Playing("Vice: Project Doom"),
            "clv_p_naehe" => Playing("Clu Clu Land™"),
            "clv_p_naeie" => Playing("VS. Excitebike™"),
            "clv_p_naeje" => Playing("Volleyball™"),
            "clv_p_naeke" => Playing("JOURNEY TO SILIUS"),
            "clv_p_naele" => Playing("S.C.A.T.: Special Cybernetic Attack Team"),
            "clv_p_naeme" => Playing("Shadow of the Ninja"),
            "clv_p_naene" => Playing("Nightshade"),
            "clv_p_naepe" => Playing("The Immortal"),
            "clv_p_naeqe" => Playing("Eliminator Boat Duel"),
            "clv_p_naere" => Playing("Fire 'n Ice"),
            "clv_p_nafce" => Playing("XEVIOUS"),
            "clv_p_nagpe" => Playing("DAIVA STORY 6 IMPERIAL OF NIRSARTIA"),
            "clv_p_nagqe" => Playing("DIG DUGⅡ"),
            "clv_p_nague" => Playing("MAPPY-LAND"),
            "clv_p_nahhe" => Playing("Mach Rider™"),
            "clv_p_nahje" => Playing("Pinball"),
            "clv_p_nahre" => Playing("Mystery Tower"),
            "clv_p_nahte" => Playing("Urban Champion™"),
            "clv_p_nahue" => Playing("Donkey Kong Jr.™ Math"),
            "clv_p_nahve" => Playing("The Mysterious Murasame Castle"),
            "clv_p_najae" => Playing("DEVIL WORLD™"),
            "clv_p_najbe" => Playing("Golf"),
            "clv_p_najpe" => Playing("R.C. PRO-AM™"),
            "clv_p_najre" => Playing("COBRA TRIANGLE™"),
            "clv_p_najse" => Playing("SNAKE RATTLE N ROLL™"),
            "clv_p_najte" => Playing("SOLAR® JETMAN"),

            #endregion

            #region SNES

            "s_2180_e" => Playing("BATTLETOADS™ DOUBLE DRAGON™"),
            "s_2179_e" => Playing("BATTLETOADS™ IN BATTLEMANIACS"),
            "s_2182_e" => Playing("BIG RUN"),
            "s_2156_e" => Playing("Bombuzal"),
            "s_2002_e" => Playing("BRAWL BROTHERS"),
            "s_2025_e" => Playing("Breath of Fire II"),
            "s_2003_e" => Playing("Breath Of Fire"),
            "s_2163_e" => Playing("Claymates"),
            "s_2150_e" => Playing("Congo's Caper"),
            "s_2171_e" => Playing("COSMO GANG THE PUZZLE"),
            "s_2004_e" => Playing("Demon's Crest"),
            "s_2026_e" => Playing("Kunio-kun no Dodgeball da yo Zen'in Shūgō!"),
            "s_2060_e" => Playing("Donkey Kong Country 2: Diddy's Kong Quest"),
            "s_2061_e" => Playing("Donkey Kong Country 3: Dixie Kong's Double Trouble!"),
            "s_2055_e" => Playing("Donkey Kong Country"),
            "s_2139_e" => Playing("DOOMSDAY WARRIOR"),
            "s_2051_e" => Playing("EarthBound"),
            "s_2162_e" => Playing("Earthworm Jim™ 2"),
            "s_2005_e" => Playing("F-ZERO™"),
            "s_2183_e" => Playing("FATAL FURY 2"),
            "s_2174_e" => Playing("Fighter's History"),
            "s_2037_e" => Playing("Harvest Moon"),
            "s_2161_e" => Playing("Jelly Boy"),
            "s_2006_e" => Playing("Joe & Mac 2: Lost in the Tropics"),
            "s_2169_e" => Playing("Caveman Ninja"),
            "s_2181_e" => Playing("KILLER INSTINCT™"),
            "s_2029_e" or "s_2029_e_sp1" => Playing("Kirby Super Star™"),
            "s_2121_e" => Playing("Kirby's Avalanche™"),
            "s_2007_e" or "s_2007_e_sp1" => Playing("Kirby's Dream Course™"),
            "s_2008_e" or "s_2008_e_sp1" => Playing("Kirby's Dream Land™ 3"),
            "s_2172_e" => Playing("Kirby’s Star Stacker™"),
            "s_2151_e" => Playing("Magical Drop2"),
            "s_2044_e" => Playing("Mario's Super Picross"),
            "s_2038_e" => Playing("Natsume Championship Wrestling"),
            "s_2140_e" => Playing("Operation Logic Bomb"),
            "s_2034_e" => Playing("Panel de Pon"),
            "s_2009_e" => Playing("Pilotwings™"),
            "s_2010_e" => Playing("Pop'n TwinBee"),
            "s_2157_e" => Playing("Prehistorik Man"),
            "s_2145_e" => Playing("Psycho Dream"),
            "s_2141_e" => Playing("Rival Turf!"),
            "s_2152_e" => Playing("SIDE POCKET"),
            "s_2158_e" => Playing("Spanky’s™ Quest"),
            "s_2031_e" => Playing("Star Fox™ 2"),
            "s_2011_e" => Playing("Star Fox™"),
            "s_2012_e" => Playing("Stunt Race FX™"),
            "s_2032_e" => Playing("Amazing Hebereke"),
            "s_2159_e" => Playing("Super Baseball Simulator 1.000"),
            "s_2013_e" => Playing("SUPER E.D.F. EARTH DEFENSE FORCE"),
            "s_2014_e" => Playing("Smash Tennis"),
            "s_2015_e" => Playing("Super Ghouls'n Ghosts™"),
            "s_2033_e" => Playing("Super Mario All-Stars™"),
            "s_2016_e" or "s_2016_e_sp1" => Playing("Super Mario Kart™"),
            "s_2017_e" or "s_2017_e_sp1" => Playing("Super Mario World™"),
            "s_2018_e" or "s_2018_e_sp1" => Playing("Super Metroid™"),
            "s_2184_e" => Playing("Super Ninja Boy"),
            "s_2019_e" or "s_2019_e_sp1" => Playing("Super Punch-Out!!™"),
            "s_2020_e" => Playing("Super Puyo Puyo 2"),
            "s_2133_e" => Playing("SUPER R-TYPE"),
            "s_2021_e" => Playing("Super Soccer"),
            "s_2022_e" => Playing("Super Tennis"),
            "s_2136_e" => Playing("Sutte Hakkun"),
            "s_2142_e" => Playing("The Ignition Factor"),
            "s_2143_e" => Playing("The Peace Keepers"),
            "s_2146_e" => Playing("Tuff E Nuff"),
            "s_2144_e" => Playing("SUPER VALIS Ⅳ"),
            "s_2049_e" => Playing("Wild Guns"),
            "s_2096_e" => Playing("Wrecking Crew™ '98"),
            "s_2023_e" => Playing("Super Mario World™ 2: Yoshi's Island™"),
            "s_2024_e" => Playing("The Legend of Zelda™: A Link to the Past™"),

            #endregion

            #region GameBoy

            "c_7224_e" or "c_7224_p" => Playing("Alone in the Dark: The New Nightmare"),
            "c_5022_e" => Playing("Blaster Master: Enemy Below"),
            "c_3381_e" => Playing("Game & Watch™ Gallery 3"),
            "c_0282_e" => Playing("Kirby Tilt ‘n’ Tumble™"),
            "c_4471_e" or "c_4471_p" => Playing("Mario Golf™"),
            "c_9947_e" => Playing("Mario Tennis™"),
            "c_3191_e" or "c_3191_p" or "c_3191_x" => Playing("Pokémon™ Trading Card Game"),
            "c_8914_e" or "c_8914_p" => Playing("Quest for Camelot™"),
            "c_2648_e" => Playing("Tetris® DX"),
            "c_5928_e" => Playing("Wario Land™ 3"),
            "c_3996_e" or "c_3996_pd" or "c_3996_pf" => Playing("The Legend of Zelda™: Link's Awakening DX™"),
            "c_8852_e" or "c_8852_p" => Playing("The Legend of Zelda™: Oracle of Ages™"),
            "c_9130_e" or "c_9130_p" => Playing("The Legend of Zelda™: Oracle of Seasons™"),
            "d_6879_e" => Playing("Alleyway™"),
            "d_7618_e" => Playing("Baseball"),
            "d_6005_e" => Playing("BurgerTime Deluxe"),
            "d_7120_e" => Playing("Castlevania Legends"),
            "d_2744_e" => Playing("Dr. Mario™"),
            "d_1593_e" => Playing("Donkey Kong Land 2™"),
            "d_7216_e" => Playing("Donkey Kong Land III™"),
            "d_4971_e" => Playing("Donkey Kong Land™"),
            "d_7984_e" => Playing("GARGOYLE'S QUEST"),
            "d_8212_e" => Playing("Kirby's Dream Land™ 2"),
            "d_5661_e" => Playing("Kirby's Dream Land™"),
            "d_3837_e" => Playing("MEGA MAN II"),
            "d_1965_e" => Playing("MEGA MAN III"),
            "d_0194_e" => Playing("MEGA MAN IV"),
            "d_1425_e" => Playing("MEGA MAN V"),
            "d_9324_e" => Playing("MEGA MAN: DR. WILY'S REVENGE"),
            "d_1577_e" => Playing("Metroid™ II - Return of Samus™"),
            "d_5124_e" => Playing("Super Mario Land™ 2 - 6 Golden Coins™"),
            "d_7970_e" => Playing("Super Mario Land™"),
            "d_8484_e" => Playing("Tetris®"),

            #endregion

            #region GameBoy Advance

            "a_9694_e" => Playing("Densetsu no Starfy 1"),
            "a_5600_e" => Playing("Densetsu no Starfy 2"),
            "a_7565_e" => Playing("Densetsu no Starfy 3"),
            "a_6553_e" => Playing("F-ZERO CLIMAX"),
            "a_7842_e" or "a_7842_p" => Playing("F-Zero™- GP Legend"),
            "a_9283_e" => Playing("F-Zero™ Maximum Velocity"),
            "a_3744_e" or "a_3744_x" or "a_3744_y" => Playing("Fire Emblem™"),
            "a_8978_d" or "a_8978_e" or "a_8978_f" or "a_8978_i" or "a_8978_s" => Playing("Golden Sun™: The Lost Age"),
            "a_3108_d" or "a_3108_e" or "a_3108_f" or "a_3108_i" or "a_3108_s" => Playing("Golden Sun™"),
            "a_3654_e" or "a_3654_p" => Playing("Kirby™ & The Amazing Mirror"),
            "a_7279_p" => Playing("Kuru Kuru Kururin™"),
            "a_7311_e" or "a_7311_p" => Playing("Mario & Luigi™: Superstar Saga"),
            "a_6845_e" => Playing("Mario Kart™: Super Circuit™"),
            "a_4139_e" or "a_4139_p" => Playing("Metroid™ Fusion"),
            "a_6834_e" or "a_6834_p" => Playing("Metroid™: Zero Mission"),
            "a_8989_e" or "a_8989_p" => Playing("Pokémon™ Mystery Dungeon: Red Rescue Team"),
            "a_9444_e" => Playing("Super Mario™ Advance"),
            "a_9901_e" or "a_9901_p" => Playing("Super Mario™ Advance 4: Super Mario Bros.™ 3"),
            "a_2939_e" => Playing("Super Mario World™: Super Mario Advance 2"),
            "a_2939_p" => Playing("Super Mario World™: Super Mario Advance 2™"),
            "a_1302_e" => Playing("WarioWare™, Inc.: Mega Microgame$!"),
            "a_1302_p" => Playing("WarioWare™, Inc.: Minigame Mania."),
            "a_6960_e" or "a_6960_p" => Playing("Yoshi's Island™: Super Mario™ Advance 3"),
            "a_5190_e" or "a_5190_p" => Playing("The Legend of Zelda™: A Link to the Past™ Four Swords"),
            "a_8665_e" or "a_8665_p" => Playing("The Legend of Zelda™: The Minish Cap"),

            #endregion

            _ => FormattedValue.ForceReset
        };
        private static FormattedValue TomodachiLifeLTD_Status(SingleValue value)
        {
            MessagePackObject messagePackObject = value.Matched.PackedValue;
            MessagePackObjectDictionary messagePackObjectDictionary = messagePackObject.AsDictionary();
            
            int miiCount = messagePackObjectDictionary["MiiNum"].AsInt32();
            int fountainLevel = messagePackObjectDictionary["FountainLevel"].AsInt32();

            // Fountain Level should be kept consistent throughout code, so I basically made sure of it
            return $"Looking after {"Mii".ToQuantity(miiCount)}, with a fountain level of {fountainLevel}";
        }
        
        private static FormattedValue AnimalCrossingNewHorizons_AppCommon(SingleValue value)
        {
            MessagePackObject messagePackObject = value.Matched.PackedValue;
            MessagePackObjectDictionary messagePackObjectDictionary = messagePackObject.AsDictionary();

            return $"Living on {messagePackObjectDictionary["LandName"].AsString()} Island";
        }

        private static FormattedValue MiitopiaRPC(SparseMultiValue values)
        {
            if (values.Matched.TryGetValue("gold", out Value gold) && values.Matched.TryGetValue("stage", out Value location))
            {
                return $"{LocFinal(location.ToString())} with {gold} gold";
            }

            if (values.Matched.TryGetValue("secret", out Value secret)) // Yes "secret" is unused, but it only appears in the MII selector.
            {
                return $"In the MII selector";
            }
            
            return $"At the main menu";
            
            static string LocFinal(string location) => location switch
            {
                "0" => "Somewhere in Miitopia",
                "1" => "Wandering around Greenhorne",
                "2" => "Trodding through Neksdor",
                "3" => "Exploring The Realm of the Fey",
                "4" => "Burning their feet at Karkaton",
                "5" => "Soaring in the skies of Miitopia",
                "6" => "Fighting up The Sky Scraper",
                "7" => "Traveling Miitopia",
                _ => "Wandering"
            };
        }

        private static FormattedValue NsmbudRpc(SparseMultiValue values)
        {
            if (values.Matched.TryGetValue("WorldNo", out Value world) && values.Matched.TryGetValue("CourseNo", out Value course) | values.Matched.TryGetValue("GameModeType", out Value gamemode))
            {
                string worldstr = world.ToString();
                string coursestr = course.ToString();
                int courseint = Int32.Parse(coursestr);
                string gamemodestr = gamemode.ToString();
                
                try
                {
                    Dictionary<string, Dictionary<string, Dictionary<string, string>>> output;
                    string data;
                    data = EmbeddedResources.ReadAllText("Ryujinx/Assets/PlayReports/nsmbud.json");
                    output = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(data);
                    if (SpecialMapNames(courseint) == "Hazard")
                    {
                        return $"Last Played: Course {worldstr}-Hazard";
                    }
                    string outputloc = output[MarioOrLuigiGamemode(gamemodestr)][worldstr][coursestr];
                    return $"Last Played: Course {worldstr}-{SpecialMapNames(courseint)} | {outputloc}";
                }
                catch
                {
                    return FormattedValue.ForceReset;
                }
            }
            
            if (values.Matched.TryGetValue("RlId", out Value RlId) | values.Matched.TryGetValue("TotalPlayTime", out Value TotalPlayTime))
            {
                return "At the main menu";
            }
            
            return "";
            
            static string MarioOrLuigiGamemode(string gamemode) => gamemode switch
            {
                "0" => "mario",
                "1" => "luigi",
                "4" => "mario",
                "5" => "mario",
                _ => gamemode
            };
            
            // static string OtherGameMode(string gamemode) => gamemode switch
            // {
            //     "2" => "Boost Rush",
            //     "3" => "Challenges",
            //     "4" => "Coin Battle",
            //     "5" => "Coin Battle Editor",
            //     _ => ""
            // };

            static string SpecialMapNames(int? course) => course switch
            {
                >= 1 and <= 9 => course.ToString(),
                13 => "Shortcut",
                14 => "Shortcut",
                15 => "Shortcut",
                16 => "Shortcut",
                17 => "Shortcut",
                20 => "Ghost",
                21 => "Tower",
                22 => "Tower",
                23 => "Castle",
                37 => "Airship",
                42 => "Castle",
                43 => "Castle",
                _ => "Hazard"
            };
            
            // For future reference
            // Tower course = 21, Castle course = 23,Haunted Mansion/ship = 20
            // Tower course 2 (rock candy) = 22
            // Peach castle 1 = 42, Peach final battle = 43
            // airship = 37, jungle beetles = 17
            // Glacier seals = 16, water leaf = 15
            // desert ice = 14, acorn squid = 13
            // all other course numbers are to be considered a hazard
        }
    }
}
