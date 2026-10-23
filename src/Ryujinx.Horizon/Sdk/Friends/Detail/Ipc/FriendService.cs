using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Account;
using Ryujinx.Horizon.Sdk.OsTypes;
using Ryujinx.Horizon.Sdk.Settings;
using Ryujinx.Horizon.Sdk.Sf;
using Ryujinx.Horizon.Sdk.Sf.Hipc;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Ryujinx.Horizon.Sdk.Friends.Detail.Ipc
{
    partial class FriendService : IFriendService, IDisposable
    {
        private readonly IEmulatorAccountManager _accountManager;
        private SystemEventType _completionEvent;
        private bool _isAsyncSession;

        // Retain the bounded Classics lab experiment without changing upstream's
        // default cache wait or the behavior of unrelated titles.
        private static bool WarmClassicsFriendInfo =>
            (Environment.GetEnvironmentVariable("NEXTENDO_GBA_FRIEND_TRACE") == "1" &&
             Environment.GetEnvironmentVariable("NEXTENDO_GBA_FRIEND_WARM") == "1" &&
             string.Equals(NextendoFriends.CurrentTitleId, "010012f017576000", StringComparison.OrdinalIgnoreCase)) ||
            (Environment.GetEnvironmentVariable("NEXTENDO_NES_FRIEND_TRACE") == "1" &&
             string.Equals(NextendoFriends.CurrentTitleId, "0100d870045b6000", StringComparison.OrdinalIgnoreCase)) ||
            (Environment.GetEnvironmentVariable("NEXTENDO_GB_FRIEND_TRACE") == "1" &&
             string.Equals(NextendoFriends.CurrentTitleId, "0100c62011050000", StringComparison.OrdinalIgnoreCase));

        public FriendService(IEmulatorAccountManager accountManager, FriendsServicePermissionLevel permissionLevel)
        {
            _accountManager = accountManager;

            Os.CreateSystemEvent(out _completionEvent, EventClearMode.ManualClear, interProcess: true).AbortOnFailure();
            Os.SignalSystemEvent(ref _completionEvent); // TODO: Figure out where we are supposed to signal this.
        }

        // NPLN games identify friends by NSA ID; NEX games retain their PID mapping.
        private const string Splatoon3TitleId = "0100c2500fc20000";
        private const string WonderTitleId = "010015100b514000";

        // Attente maximale de la liste au premier GetFriendList de Smash Bros. Ultimate apres son
        // lancement (NextendoFriends.GetForSmashLaunch) : seule /api/friends compte (delai HTTP de
        // 8 s), l'historique et la liaison partent en tache de fond.
        private const int SmashListWaitMs = 8000;

        // Animal Crossing: New Horizons aussi : il compare le NSA qu'annonce chaque joueur (session,
        // carte) a Friend::GetAccountId. Une Switch annonce son baasUserID et Ryujinx aussi dans ce
        // jeu (NextendoBaasNsa, ManagerServer) : les amis y sont donc ranges par leur « nsa ».
        // Seulement quand notre propre baasUserID est connu, sinon tout reste au PID (comme avant).
        private static bool WantsNsaIds()
            => string.Equals(NextendoFriends.CurrentTitleId, Splatoon3TitleId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NextendoFriends.CurrentTitleId, WonderTitleId, StringComparison.OrdinalIgnoreCase)
                || (NextendoBaasNsa.TitleUsesBaasNsa(NextendoFriends.CurrentTitleId) && NextendoBaasNsa.Get() != 0);

        // True for the local profile the signed-in Nextendo account is bound to (every profile
        // when none is recorded yet).
        private static bool IsNextendoProfile(Uid userId)
        {
            string bound = NextendoAccount.ProfileUserId;

            return string.IsNullOrEmpty(bound) || string.Equals(bound, userId.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        // The friend record of SDK 20+ (0x220 bytes), as the HOME Menu's friend page reads it.
        // Worked out by writing known bytes and watching the page: the head is the old record
        // (user id, account id at 0x10, name at 0x18), the presence block sits at 0xE8 (its
        // status at 0x100), and the flags close the record: 0x219 favourite, 0x21A new,
        // 0x21F valid. Without the last one the tile stays an anonymous "?".
        private const int FriendRecordV2Size = 0x220;

        private static void WriteNextendoFriendV2(Span<byte> record, ulong netId, NextendoFriends.Entry entry)
        {
            PresenceStatus status = NextendoPresence(entry);

            FriendImpl old = MakeNextendoFriend(netId, entry.Name, status, entry.AppField, sameApp: false);
            ReadOnlySpan<byte> oldBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref old, 1));

            record.Clear();

            oldBytes[..0x40].CopyTo(record);                 // ids and name
            oldBytes.Slice(0x40, 0xE0).CopyTo(record[0xE8..]); // presence
            record.Slice(0xE8 + 0x10, 8).Clear();

            // "last online" of a friend who is offline: when the server last saw them
            if (status == PresenceStatus.Offline && entry.LastSeen > 0)
            {
                BitConverter.TryWriteBytes(record.Slice(0xE8 + 0x10, 8), entry.LastSeen);
            }
            record[0x21F] = 1;

            // The game being played, right before the presence (found by probing: the friend
            // page asks the title card of the id at 0xE0 for its top line).
            if (entry.Status > 0)
            {
                // online without a game (the HOME Menu of a console): the HOME Menu's card, not a
                // card of id 0 ("?", "Loading failed")
                BitConverter.TryWriteBytes(record.Slice(0xE0, 8), entry.AppId != 0 ? entry.AppId : NextendoGames.HomeMenuId);
            }
        }

        // [Nextendo] The presence of a friend as the console tells it: playing only when in a game
        // (its id, or the presence a game left), else just online. A friend on a console without
        // a game was told "playing" a game of id 0: their page showed a "?" card, "Loading failed".
        private static PresenceStatus NextendoPresence(NextendoFriends.Entry entry)
        {
            if (entry.Status <= 0)
            {
                return PresenceStatus.Offline;
            }

            // Online at all: "playing", always with a title (the game, the Nextendo App, or the HOME
            // Menu, written at 0xE0 by WriteNextendoFriendV2), as the user's own presence is
            // (GetUserPresenceViewV2). The page still says "Online" with the title's card; the
            // overlay applet only reads the title of a friend "playing": with "Online" alone, a
            // friend coming online without a game was announced "Online" instead of "HOME Menu".
            // ("Playing" with a title of 0 was the "Loading failed" card: never written any more.)
            return PresenceStatus.OnlinePlay;
        }

        private static FriendImpl MakeNextendoFriend(ulong netId, string nick, PresenceStatus status, byte[] appField = null, bool sameApp = true)
        {
            Array33<byte> nameArr = default;
            byte[] nameBytes = Encoding.UTF8.GetBytes(nick);
            nameBytes.AsSpan(0, Math.Min(nameBytes.Length, 32)).CopyTo(nameArr.AsSpan());

            FriendImpl friend = default;
            friend.UserId = new Uid(netId, 0x1100000000000000UL);
            friend.NetworkUserId = new NetworkServiceAccountId(netId);
            friend.Nickname = new Nickname(nameArr);
            friend.Presence = default;
            friend.Presence.UserId = friend.UserId;
            friend.Presence.Status = status;
            // [Nextendo] Mark the friend as currently in the SAME application (MK8) so the
            // game treats them as "playing this game" and actually queries their playing
            // session (CustomGetSimplePlayingSession) — otherwise it asks for 0 friend PIDs.
            friend.Presence.SamePresenceGroupApplication = sameApp;
            friend.Presence.LastTimeOnlineTimestamp = 0x7FFFFFFFFFFFFFFF;
            friend.IsValid = true;

            // [Nextendo] Copy the friend's live S2 presence AppField (relayed by the account
            // server) into the presence's AppKeyValueStorage (0xC0). This is the nn::friends
            // key-value blob S2 set via UpdateUserPresence (SessionId/Full/Mode/...), so the
            // joiner's S2 sees this friend as in a JOINABLE private battle. Empty = not in a room.
            if (appField != null && appField.Length > 0)
            {
                Span<byte> dst = MemoryMarshal.CreateSpan(
                    ref System.Runtime.CompilerServices.Unsafe.As<UserPresenceImpl.AppKeyValueStorageHolder, byte>(
                        ref friend.Presence.AppKeyValueStorage), 0xC0);
                appField.AsSpan(0, Math.Min(appField.Length, 0xC0)).CopyTo(dst);
            }

            return friend;
        }

        [CmifCommand(0)]
        public Result GetCompletionEvent([CopyHandle] out int completionEventHandle)
        {
            // nnSdk asks for this only on a fresh AsyncContext session, which runs one command on a
            // worker thread and then closes; signalled now, the waiter reads the result before it exists.
            if (!_isAsyncSession)
            {
                _isAsyncSession = true;
                Os.ClearSystemEvent(ref _completionEvent);
            }

            completionEventHandle = Os.GetReadableHandleOfSystemEvent(ref _completionEvent);

            return Result.Success;
        }

        [CmifCommand(1)]
        public Result Cancel()
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend);

            return Result.Success;
        }

        // [Nextendo] The filter the game asks for, applied BEFORE the list is cut to the game's
        // buffer. It was ignored: Portal 2's "Invite Friends" asks { Online, IsSameAppPresenceOnly }
        // with room for 32, received the first 32 of a 293-friend list whatever their presence,
        // and showed the two of those who were online (21/10) - never the friends past the 32nd,
        // and friends in other games among the first 32. A console returns only the friends that
        // match. Games asking no filter (PresenceStatus None, every other game seen in the logs)
        // get the whole list as before. "Played this game" filters are not known here: kept as is.
        private static IReadOnlyList<NextendoFriends.Entry> ApplyFilter(IReadOnlyList<NextendoFriends.Entry> friends, SizedFriendFilter filter)
        {
            bool online = filter.PresenceStatus is PresenceStatusFilter.Online
                or PresenceStatusFilter.OnlinePlay or PresenceStatusFilter.OnlineOrOnlinePlay;

            if (!online && !filter.IsSameAppPresenceOnly && !filter.IsFavoriteOnly)
            {
                return friends;
            }

            List<NextendoFriends.Entry> kept = new(friends.Count);
            foreach (NextendoFriends.Entry entry in friends)
            {
                if (online && entry.Status <= 0)
                {
                    continue;
                }

                if (filter.IsSameAppPresenceOnly && !NextendoFriends.PlaysCurrentGame(entry))
                {
                    continue;
                }

                if (filter.IsFavoriteOnly && !entry.Favorite)
                {
                    continue;
                }

                kept.Add(entry);
            }

            return kept;
        }

        [CmifCommand(10100)]
        public Result GetFriendListIds(
            out int count,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer)] Span<NetworkServiceAccountId> friendIds,
            Uid userId,
            int offset,
            SizedFriendFilter filter,
            ulong pidPlaceholder,
            [ClientProcessId] ulong pid)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, offset, filter, pidPlaceholder, pid });

            if (userId.IsNull || offset < 0)
            {
                return FriendResult.InvalidArgument;
            }

            // NPLN games enumerate the full list using the requested offset.
            if (offset == 0 || WantsNsaIds())
            {
                bool wantNsa = WantsNsaIds();

                // Splatoon 3 ne demande sa liste qu'UNE fois, tot : si le cache n'est pas encore
                // rempli il repart definitivement avec zero ami. On lui accorde une attente courte
                // et plafonnee ; les jeux NEX, qui reinterrogent en boucle, gardent le chemin non
                // bloquant (les bloquer casse leurs acquittements PRUDP).
                IReadOnlyList<NextendoFriends.Entry> friends = ApplyFilter(
                    wantNsa ? NextendoFriends.GetWarm(2000) : NextendoFriends.Get(), filter);
                int n = Math.Min(Math.Max(0, friends.Count - offset), friendIds.Length);
                // QUEL identifiant ce jeu attend-il dans sa liste locale ? Mario Kart 8 (NEX)
                // redemande ses amis PAR LE PID. Splatoon 3 (NPLN) les nomme par NSA : mesure,
                // 125 appels sur 125 a UpdateFriendInfo portaient un id de 16 chiffres hexa alors
                // que nous rendions ici des PID de 8. Les deux listes vivaient dans des espaces
                // d'identifiants DISJOINTS : l'intersection etait vide, donc l'ecran aussi.
                // Remplir la fiche (UpdateFriendInfo) ne suffisait pas — c'est ici que se decide
                // l'APPARTENANCE a la liste.
                for (int i = 0; i < n; i++)
                {
                    var entry = friends[offset + i];
                    ulong id = wantNsa && entry.Nsa != 0 ? entry.Nsa : entry.Pid;
                    friendIds[i] = new NetworkServiceAccountId(id);
                }
                count = n;
            }

            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetFriendListIds -> count={count}");

            return Result.Success;
        }

        [CmifCommand(10101)]
        public Result GetFriendList(
            out int count,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<FriendImpl> friendList,
            Uid userId,
            int offset,
            SizedFriendFilter filter,
            ulong pidPlaceholder,
            [ClientProcessId] ulong pid)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, offset, filter, pidPlaceholder, pid });

            if (userId.IsNull || offset < 0)
            {
                return FriendResult.InvalidArgument;
            }

            // NPLN games enumerate the full list using the requested offset.
            if (offset == 0 || WantsNsaIds())
            {
                // Smash Bros. Ultimate ne lit cette liste qu'au lancement et sur FriendListUpdate,
                // que NotificationService.GetEvent lui signale aussitot : deux appels a 0,6 s d'ecart,
                // puis plus rien. Servie vide, il cherche les arenes d'amis avec zero identifiant pour
                // toute la partie. Le premier appel du lancement attend donc la liste (8 s au plus),
                // en relancant la lecture si celle du lancement a rate ; les suivants, comme les jeux
                // NEX qui interrogent en boucle, gardent le chemin non bloquant. Cette boucle sert
                // tous les clients friends (menu HOME compris) : jamais plus d'une attente par
                // lancement (NextendoFriends.GetForSmashLaunch).
                bool smash = NextendoFriends.ReadsListOnlyOnLaunch(NextendoFriends.CurrentTitleId) && !WantsNsaIds();
                IReadOnlyList<NextendoFriends.Entry> friends = ApplyFilter(WantsNsaIds() ? NextendoFriends.GetWarm(2000)
                    : smash ? NextendoFriends.GetForSmashLaunch(SmashListWaitMs)
                    : NextendoFriends.Get(), filter);
                bool wantNsa = WantsNsaIds();
                int n = Math.Min(Math.Max(0, friends.Count - offset), friendList.Length);
                for (int i = 0; i < n; i++)
                {
                    // Meme espace d'identifiants que GetFriendListIds, sinon Splatoon 3 recevrait
                    // ici des fiches estampillees d'un PID qu'il ne sait pas rapprocher de la liste
                    // que NPLN lui a donnee. Les autres jeux gardent le PID.
                    var entry = friends[offset + i];
                    ulong lid = wantNsa && entry.Nsa != 0 ? entry.Nsa : entry.Pid;
                    // Presence REELLE, comme le fait deja UpdateFriendInfo plus bas. Annoncer
                    // TOUT LE MONDE « en train de jouer a ce jeu » est un mensonge que le jeu
                    // peut verifier : il demande ensuite au serveur la session de chacun de ces
                    // pretendus joueurs, n'en retrouve aucune, et ecarte l'entree.
                    PresenceStatus st = NextendoPresence(entry);
                    friendList[i] = MakeNextendoFriend(lid, entry.Name, st, entry.AppField,
                        sameApp: NextendoFriends.PlaysCurrentGame(entry));
                }
                count = n;

                if (n > 0)
                {
                    System.ReadOnlySpan<byte> b0 = System.Runtime.InteropServices.MemoryMarshal.AsBytes(friendList.Slice(0, 1));
                    Logger.Info?.Print(LogClass.ServiceFriend,
                        $"[NX-DIAG] sizeof=0x{System.Runtime.CompilerServices.Unsafe.SizeOf<FriendImpl>():x} bufLen={friendList.Length} " +
                        $"isValid@0x128={b0[0x128]} fav@0x120={b0[0x120]} nick@0x18='{(char)b0[0x18]}{(char)b0[0x19]}{(char)b0[0x1a]}' " +
                        $"nsa@0x10=0x{System.BitConverter.ToUInt64(b0.Slice(0x10, 8)):x} status@0x40+0x18={b0[0x58]}");
                }
            }

            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetFriendList -> offset={offset} count={count}");

            return Result.Success;
        }

        [CmifCommand(10102)]
        public Result UpdateFriendInfo(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<FriendImpl> info,
            Uid userId,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer)] ReadOnlySpan<NetworkServiceAccountId> friendIds,
            ulong pidPlaceholder,
            [ClientProcessId] ulong pid)
        {
            string friendIdList = string.Join(", ", friendIds.ToArray());

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendIdList, pidPlaceholder, pid });

            // [Nextendo] Resolve each requested id to the real friend. This method returned Success
            // while writing NOTHING into its out buffer, so every game that resolves its friend list
            // this way got the ids and zero details: Splatoon 3's "Amis" screen stayed empty even
            // though GetFriendListIds had handed it the full list. The ids are the INPUT here and the
            // details the OUTPUT, one entry per requested id, in the SAME order — an entry we cannot
            // resolve stays IsValid=false rather than being skipped, otherwise the caller mis-pairs
            // the remaining ids with the wrong friends.
            // A list not loaded yet (the overlay of the system asks right after a friend comes online)
            // is waited for a little.
            bool warmClassics = WarmClassicsFriendInfo;
            IReadOnlyList<NextendoFriends.Entry> known = NextendoFriends.GetWarm(warmClassics ? 3000 : 1500);

            for (int i = 0; i < friendIds.Length && i < info.Length; i++)
            {
                ulong wanted = friendIds[i].Id;
                info[i] = default;

                foreach (NextendoFriends.Entry e in known)
                {
                    // Splatoon 3 nomme l'ami par son NSA (celui que NPLN lui a donne) ; Mario Kart 8,
                    // lui, redemande le PID que GetFriendListIds a rendu. On accepte les deux.
                    if ((e.Nsa != 0 && e.Nsa == wanted) || e.Pid == wanted)
                    {
                        // On renvoie la fiche estampillee de l'identifiant DEMANDE, pas du PID :
                        // l'appelant apparie la reponse a sa requete par cet id. Estampillee du PID
                        // alors que Splatoon 3 avait demande un NSA, la fiche etait correctement
                        // remplie et pourtant jetee — l'ecran restait vide malgre un ami resolu.
                        // Presence REELLE : un ami hors ligne doit apparaitre grise, pas « en train
                        // de jouer ». Annoncer OnlinePlay pour tout le monde pousse le jeu a proposer
                        // de rejoindre des parties qui n'existent pas.
                        PresenceStatus st = NextendoPresence(e);
                        info[i] = MakeNextendoFriend(wanted, e.Name, st, e.AppField, sameApp: NextendoFriends.PlaysCurrentGame(e));

                        // [Nextendo, HOME Menu work] As on a console, the record names the user of
                        // the console whose friend it is: the overlay of the system checks it is a
                        // user here before showing "online" (and asks the picture with it).
                        info[i].UserId = userId;

                        break;
                    }
                }
            }

            return Result.Success;
        }

        [CmifCommand(10110)]
        public Result GetFriendProfileImage(
            out int size,
            Uid userId,
            NetworkServiceAccountId friendId,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<byte> profileImage)
        {
            size = 0;

            // [Nextendo] La photo de profil de l'ami. Cette methode rendait size=0 : la liste
            // s'affichait avec les vrais pseudos mais une vignette « ? » pour chacun. L'ami est
            // nomme ici par le MEME identifiant que dans la liste (NSA pour Splatoon 3, PID
            // ailleurs) ; c'est le PID qui adresse /api/avatar, d'ou la resolution.
            ulong wanted = friendId.Id;
            ulong pid = 0;

            foreach (NextendoFriends.Entry e in NextendoFriends.Get())
            {
                if ((e.Nsa != 0 && e.Nsa == wanted) || e.Pid == wanted)
                {
                    pid = e.Pid;

                    break;
                }
            }

            if (pid == 0)
            {
                Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetFriendProfileImage 0x{wanted:x} -> ami inconnu");

                return Result.Success;
            }

            // Le jeu ne redemande pas : une attente courte vaut mieux qu'une vignette vide definitive.
            byte[] jpeg = NextendoFriends.ProfileImageWarm(pid, 1500);

            if (jpeg == null || jpeg.Length == 0)
            {
                Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetFriendProfileImage pid={pid} -> pas d'image");

                return Result.Success;
            }

            if (jpeg.Length > profileImage.Length)
            {
                Logger.Info?.Print(LogClass.ServiceFriend,
                    $"[Nextendo] GetFriendProfileImage pid={pid} -> {jpeg.Length} o > tampon {profileImage.Length} o, ignoree");

                return Result.Success;
            }

            jpeg.CopyTo(profileImage);
            size = jpeg.Length;

            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetFriendProfileImage pid={pid} -> {size} o (JPEG)");

            return Result.Success;
        }

        [CmifCommand(10120)]
        public Result CheckFriendListAvailability(out bool listAvailable, Uid userId)
        {
            listAvailable = true;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(10121)]
        public Result EnsureFriendListAvailable(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(10200)]
        public Result SendFriendRequestForApplication(
            Uid userId,
            NetworkServiceAccountId friendId,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x48)] in InAppScreenName arg2,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x48)] in InAppScreenName arg3,
            ulong pidPlaceholder,
            [ClientProcessId] ulong pid)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId, arg2, arg3, pidPlaceholder, pid });

            return Result.Success;
        }

        [CmifCommand(10211)]
        public Result AddFacedFriendRequestForApplication(
            Uid userId,
            FacedFriendRequestRegistrationKey key,
            Nickname nickname,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.MapAlias)] ReadOnlySpan<byte> arg3,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x48)] in InAppScreenName arg4,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x48)] in InAppScreenName arg5,
            ulong pidPlaceholder,
            [ClientProcessId] ulong pid)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, key, nickname, arg4, arg5, pidPlaceholder, pid });

            return Result.Success;
        }

        [CmifCommand(10400)]
        public Result GetBlockedUserListIds(
            out int count,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer)] Span<NetworkServiceAccountId> blockedIds,
            Uid userId,
            int offset)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, offset });

            return Result.Success;
        }

        [CmifCommand(10420)]
        public Result CheckBlockedUserListAvailability(out bool listAvailable, Uid userId)
        {
            listAvailable = true;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(10421)]
        public Result EnsureBlockedUserListAvailable(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(10500)]
        public Result GetProfileList(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<ProfileImpl> profileList,
            Uid userId,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer)] ReadOnlySpan<NetworkServiceAccountId> friendIds)
        {
            int count = Math.Min(profileList.Length, friendIds.Length);
            for (int i = 0; i < count; i++)
            {
                profileList[i] = MakeProfile(friendIds[i].Id);
            }

            return Result.Success;
        }

        // Local account, then friends, then any player's public name (a balloon card may show a stranger).
        private static ProfileImpl MakeProfile(ulong id)
        {
            ulong pid = id;
            string name = null;
            // soi-meme : par le PID, ou par le baasUserID annonce dans ACNH (NextendoBaasNsa)
            if (NextendoAccount.IsLinked && (NextendoAccount.Pid == id ||
                (NextendoBaasNsa.TitleUsesBaasNsa(NextendoFriends.CurrentTitleId) && NextendoBaasNsa.Get() == id && id != 0)))
            {
                pid = NextendoAccount.Pid;
                name = NextendoAccount.Username;
            }
            else
            {
                foreach (NextendoFriends.Entry friend in NextendoFriends.Get())
                {
                    if (friend.Pid == id || (friend.Nsa != 0 && friend.Nsa == id))
                    {
                        pid = friend.Pid;
                        name = friend.Name;
                        break;
                    }
                }

                name ??= NextendoFriends.PublicName(id, 1500);
            }

            if (string.IsNullOrEmpty(name))
            {
                return default;
            }

            ProfileImpl profile = default;
            profile.NetworkUserId = new NetworkServiceAccountId(id);

            Array33<byte> nameArr = default;
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            nameBytes.AsSpan(0, Math.Min(nameBytes.Length, 32)).CopyTo(nameArr.AsSpan());
            profile.Nickname = new Nickname(nameArr);

            // The pid query lets Nextendo's CDN find the picture for any player.
            byte[] url = Encoding.ASCII.GetBytes(
                $"https://cdn-image-e0d67c509fb203858ebcb2fe3f88c2aa.baas.nintendo.com/1/pid_{pid}?pid={pid}");
            url.AsSpan(0, Math.Min(url.Length, 0x9F)).CopyTo(profile.ImageUrl.Path);

            profile.IsValid = 1;

            return profile;
        }

        [CmifCommand(10600)]
        public Result DeclareOpenOnlinePlaySession(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            if (userId.IsNull)
            {
                return FriendResult.InvalidArgument;
            }

            _accountManager.OpenUserOnlinePlay(userId);

            return Result.Success;
        }

        [CmifCommand(10601)]
        public Result DeclareCloseOnlinePlaySession(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            if (userId.IsNull)
            {
                return FriendResult.InvalidArgument;
            }

            _accountManager.CloseUserOnlinePlay(userId);

            return Result.Success;
        }

        // [Nextendo] ARMS's raw nn::friends presence status floors at Online (1) even while the
        // player is hosting/joined to a private battle, and ARMS's own friends-list UI only ever
        // consults its JoinMode app-field key when status==2 — so a real, joinable session still
        // shows friends as greyed-out/non-joinable otherwise. Mirrors citron's IsArmsSessionActive
        // fixup (friend.cpp): parse the null-delimited key/value app-field tokens and bump status
        // to OnlinePlay whenever JoinMode indicates an active session.
        private static bool IsArmsSessionActive(ReadOnlySpan<byte> appField)
        {
            int pos = 0;
            while (pos < appField.Length)
            {
                int end = appField[pos..].IndexOf((byte)0);
                int tokenEnd = end < 0 ? appField.Length : pos + end;
                if (tokenEnd == pos)
                {
                    break;
                }
                string key = Encoding.ASCII.GetString(appField[pos..tokenEnd]);
                pos = tokenEnd + 1;
                if (pos >= appField.Length)
                {
                    break;
                }
                int valueEnd = appField[pos..].IndexOf((byte)0);
                int valueTokenEnd = valueEnd < 0 ? appField.Length : pos + valueEnd;
                string value = Encoding.ASCII.GetString(appField[pos..valueTokenEnd]);
                pos = valueTokenEnd + 1;

                if (key == "JoinMode")
                {
                    return value == "1" || value == "2" || value == "3" || value == "4";
                }
            }
            return false;
        }

        [CmifCommand(10610)]
        public Result UpdateUserPresence(
            Uid userId,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0xE0)] in UserPresenceImpl userPresence,
            ulong pidPlaceholder,
            [ClientProcessId] ulong pid)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, userPresence, pidPlaceholder, pid });

            // [Nextendo] Relay this player's presence (nn::friends status + the S2 AppField blob)
            // to the account server so friends see them as online + JOINABLE in a private battle.
            try
            {
                ref UserPresenceImpl up = ref System.Runtime.CompilerServices.Unsafe.AsRef(in userPresence);
                Span<byte> af = MemoryMarshal.CreateSpan(
                    ref System.Runtime.CompilerServices.Unsafe.As<UserPresenceImpl.AppKeyValueStorageHolder, byte>(
                        ref up.AppKeyValueStorage), 0xC0);

                int status = (int)userPresence.Status;
                if (IsArmsSessionActive(af))
                {
                    status = Math.Max(status, (int)PresenceStatus.OnlinePlay);
                    Logger.Info?.Print(LogClass.ServiceFriend,
                        "[Nextendo] ARMS JoinMode indicates an active session; bumping status to OnlinePlay (raw status floors at Online otherwise)");
                }

                NextendoFriends.PublishPresence(status, af.ToArray());
            }
            catch
            {
                // best-effort; never fail the presence update
            }

            return Result.Success;
        }

        [CmifCommand(10700)]
        public Result GetPlayHistoryRegistrationKey(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x40)] out PlayHistoryRegistrationKey registrationKey,
            Uid userId,
            bool arg2)
        {
            if (userId.IsNull)
            {
                registrationKey = default;

                return FriendResult.InvalidArgument;
            }

            // NOTE: Calls nn::friends::detail::service::core::PlayHistoryManager::GetInstance and stores the instance.

            // NOTE: Calls nn::friends::detail::service::core::UuidManager::GetInstance and stores the instance.
            //       Then calls nn::friends::detail::service::core::AccountStorageManager::GetInstance and stores the instance.
            //       Then it checks if an Uuid is already stored for the UserId, if not it generates a random Uuid,
            //       and stores it in the savedata 8000000000000080 in the friends:/uid.bin file.

            /*

            NOTE: The service uses the KeyIndex to get a random key from a keys buffer (since the key index is stored in the returned buffer).
                  We currently don't support play history and online services so we can use a blank key for now.
                  Code for reference:

            byte[] hmacKey = new byte[0x20];

            HMACSHA256 hmacSha256 = new HMACSHA256(hmacKey);
            byte[]     hmacHash   = hmacSha256.ComputeHash(playHistoryRegistrationKeyBuffer);

            */

            Uid randomGuid = new();

            Guid.NewGuid().TryWriteBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref randomGuid, 1)));

            registrationKey = new()
            {
                Type = 0x101,
                KeyIndex = (byte)(Random.Shared.Next() & 7),
                UserIdBool = 0, // TODO: Find it.
                UnknownBool = (byte)(arg2 ? 1 : 0), // TODO: Find it.
                Reserved = new(),
                Uuid = randomGuid,
                HmacHash = new(),
            };

            return Result.Success;
        }

        [CmifCommand(10701)]
        public Result GetPlayHistoryRegistrationKeyWithNetworkServiceAccountId(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x40)] out PlayHistoryRegistrationKey registrationKey,
            NetworkServiceAccountId friendId,
            bool arg2)
        {
            registrationKey = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { friendId, arg2 });

            return Result.Success;
        }

        [CmifCommand(10702)]
        public Result AddPlayHistory(
            Uid userId,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x40)] in PlayHistoryRegistrationKey registrationKey,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x48)] in InAppScreenName arg2,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x48)] in InAppScreenName arg3,
            ulong pidPlaceholder,
            [ClientProcessId] ulong pid)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, registrationKey, arg2, arg3, pidPlaceholder, pid });

            return Result.Success;
        }

        [CmifCommand(11000)]
        public Result GetProfileImageUrl(out Url imageUrl, Url url, int imageSize)
        {
            // Titles decode the picture into a buffer sized for imageSize, so the CDN must serve that size.
            imageUrl = url;
            string sized = url.ToString();
            if (!sized.Contains("size="))
            {
                sized += (sized.Contains('?') ? "&" : "?") + "size=" + imageSize;
            }

            byte[] bytes = Encoding.ASCII.GetBytes(sized);
            if (bytes.Length < 0xA0)
            {
                imageUrl = default;
                bytes.CopyTo(imageUrl.Path);
            }

            return Result.Success;
        }

        [CmifCommand(20100)]
        public Result GetFriendCount(out int count, Uid userId, SizedFriendFilter filter, ulong pidPlaceholder, [ClientProcessId] ulong pid)
        {
            // [Nextendo] The friends of the profile bound to the Nextendo account, and of no other
            // local profile. Asked with the "online" filter (the badge on the HOME Menu), only those
            // who are online count.
            count = 0;

            if (IsNextendoProfile(userId))
            {
                bool onlineOnly = filter.PresenceStatus is PresenceStatusFilter.Online or PresenceStatusFilter.OnlinePlay or PresenceStatusFilter.OnlineOrOnlinePlay;

                foreach (NextendoFriends.Entry friend in NextendoFriends.Get())
                {
                    if (!onlineOnly || friend.Status > 0)
                    {
                        count++;
                    }
                }
            }

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, filter, pidPlaceholder, pid });

            return Result.Success;
        }

        [CmifCommand(20101)]
        public Result GetNewlyFriendCount(out int count, Uid userId)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(20102)]
        public Result GetFriendDetailedInfo(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x800)] out FriendDetailedInfoImpl detailedInfo,
            Uid userId,
            NetworkServiceAccountId friendId)
        {
            detailedInfo = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId });

            return Result.Success;
        }

        [CmifCommand(20103)]
        public Result SyncFriendList(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(20104)]
        public Result RequestSyncFriendList(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        // [Nextendo, HOME Menu work] The page of one friend (SDK 20+): asked when a tile is opened.
        [CmifCommand(20107)]
        public Result GetFriendDetailedInfoV2(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x800)] out FriendDetailedInfoImpl detailedInfo,
            Uid userId,
            NetworkServiceAccountId friendId)
        {
            detailedInfo = default;

            Span<byte> info = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref detailedInfo, 1));

            if (IsNextendoProfile(userId))
            {
                // What is known so far of this record: the head is the friend record's (ids at 0
                // and 0x10, name at 0x18), the address of the picture is at 0x40, and games sit in
                // up to twenty 0x30-byte entries from 0xE0 (the play history), each led by a title id.
                // The account server only tells what is played now: that game opens the list.
                foreach (NextendoFriends.Entry entry in NextendoFriends.Get())
                {
                    if (entry.Pid != friendId.Id && (entry.Nsa == 0 || entry.Nsa != friendId.Id))
                    {
                        continue;
                    }

                    FriendImpl head = MakeNextendoFriend(friendId.Id, entry.Name, PresenceStatus.Offline);

                    // One friend asked alone is a friend page being opened (a page that walks the
                    // whole list asks them all in a row).
                    if (!NextendoFriends.Sweeping)
                    {
                        NextendoActivity.Set("Friend:" + entry.Name);
                    }

                    MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref head, 1))[..0x40].CopyTo(info);

                    // The play history: up to twenty entries of 0x30 bytes, laid out like the
                    // play log the console receives for a friend (application id, presence group
                    // id, times played, minutes played, first and last time played). The game
                    // being played now opens the list when the history does not have it yet.
                    List<NextendoFriends.Played> played = new(NextendoFriends.History(entry.Pid, 2500));

                    // An entry the server could not name has no title card to show.
                    played.RemoveAll(g => string.IsNullOrEmpty(g.Name));

                    if (entry.Status > 0 && entry.AppId != 0 && !played.Exists(g => g.TitleId == entry.AppId))
                    {
                        played.Insert(0, new NextendoFriends.Played(entry.AppId, entry.Game, 0, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                    }

                    for (int g = 0; g < played.Count && g < 20; g++)
                    {
                        Span<byte> slot = info.Slice(0xE0 + g * 0x30, 0x30);

                        // Entry layout, found by probing the page: title id, presence group id,
                        // then at 0x18 the number of times played, at 0x1C the minutes played, at
                        // 0x20 the first time played and at 0x28 the last one. The server does not
                        // know the first time: left empty, the page tells how long the game was
                        // played instead of "first played ... ago".
                        BitConverter.TryWriteBytes(slot.Slice(0x00, 8), played[g].TitleId);
                        BitConverter.TryWriteBytes(slot.Slice(0x08, 8), played[g].TitleId);
                        BitConverter.TryWriteBytes(slot.Slice(0x18, 4), 1);
                        BitConverter.TryWriteBytes(slot.Slice(0x1C, 4), (int)Math.Min(int.MaxValue, played[g].Seconds / 60));
                        BitConverter.TryWriteBytes(slot.Slice(0x28, 8), played[g].LastPlayed);
                    }

                    // Two flags the "Trending With Friends" page reads before the entries (myPage
                    // 22.5.0, 0x4DDC18): the record is valid (0x608) and the play history is
                    // there (0x4A0, the history starting at 0x10). Either one at 0 and the friend's
                    // games were left out: the page stayed empty.
                    info[0x608] = 1;
                    info[0x4A0] = (byte)(played.Count > 0 ? 1 : 0);

                    // The game being played now, for the top line of the page: its id sits at
                    // 0x570 (found by probing: the menu asks the title card of what is there).
                    if (entry.Status > 0 && entry.AppId != 0)
                    {
                        BitConverter.TryWriteBytes(info.Slice(0x570, 8), entry.AppId);
                        BitConverter.TryWriteBytes(info.Slice(0x578, 8), entry.AppId);
                        Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] friend 0x{friendId.Id:X} plays {entry.AppId:X16} ({entry.Game}) on {entry.Platform}");
                    }
                    else if (entry.Status > 0)
                    {
                        // online without a game (the HOME Menu of a console): the page shows a card
                        // for the game all the same, a "?" "Loading failed" for an id of 0; the
                        // HOME Menu's card instead, as on the user's own page
                        BitConverter.TryWriteBytes(info.Slice(0x570, 8), NextendoGames.HomeMenuId);
                        BitConverter.TryWriteBytes(info.Slice(0x578, 8), NextendoGames.HomeMenuId);
                    }

                    // "Friends since", in Unix seconds (found at 0x5D0 by probing).
                    if (entry.Since > 0)
                    {
                        BitConverter.TryWriteBytes(info.Slice(0x5D0, 8), entry.Since);
                    }

                    break;
                }
            }

            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetFriendDetailedInfoV2 friend 0x{friendId.Id:X}");

            if (friendId.Id != 0)
            {
                NextendoFriends.TellFriendDetailsAsked(friendId.Id);
            }

            return Result.Success;
        }

        // [Nextendo, HOME Menu work] LoadFriendSettingV2 (22.0.0+): the settings kept for a
        // friend (best friend, memo), asked by the friend page. The reply has to be exactly
        // 0x80 bytes; its fields are not known yet, an empty one reads as "no setting".
        // (The method keeps its first name: this command was first taken for the presence.
        // The game being played is in the list record, see WriteNextendoFriendV2.)
        [CmifCommand(20111)]
        public Result GetFriendPresenceV2(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, FriendPresenceV2Impl.Size)] out FriendPresenceV2Impl presence,
            Uid userId,
            NetworkServiceAccountId friendId)
        {
            presence = default;

            Span<byte> view = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref presence, 1));

            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetFriendPresenceV2 friend 0x{friendId.Id:X}");

            // asked for the friend whose page is shown
            if (friendId.Id != 0)
            {
                NextendoFriends.TellFriendPageShown(friendId.Id);
            }

            return Result.Success;
        }

        [CmifCommand(20108)]
        public Result GetFriendListForViewerV2(
            out int count,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<FriendImpl> friendList,
            Uid userId,
            int offset,
            SizedFriendFilter filter)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, offset, filter });

            if (userId.IsNull || offset < 0)
            {
                return FriendResult.InvalidArgument;
            }

            // Meme remplissage que GetFriendList : les vrais amis Nextendo avec
            // leur presence reelle. MyPage (SDK 22) ne lit QUE cette version V2 :
            // la laisser vide, c'est une liste d'amis vide a l'ecran.
            if (IsNextendoProfile(userId) && offset == 0)
            {
                NextendoActivity.Set("FriendList");
            }

            if (IsNextendoProfile(userId) && (offset == 0 || WantsNsaIds()))
            {
                IReadOnlyList<NextendoFriends.Entry> friends = WantsNsaIds() ? NextendoFriends.GetWarm(2000) : NextendoFriends.Get();
                bool wantNsa = WantsNsaIds();
                int n = Math.Min(Math.Max(0, friends.Count - offset), friendList.Length);
                for (int i = 0; i < n; i++)
                {
                    var entry = friends[offset + i];
                    ulong lid = wantNsa && entry.Nsa != 0 ? entry.Nsa : entry.Pid;
                    PresenceStatus st = entry.Status > 0 ? PresenceStatus.OnlinePlay : PresenceStatus.Offline;
                    Span<byte> raw = MemoryMarshal.AsBytes(friendList);

                    if ((i + 1) * FriendRecordV2Size <= raw.Length)
                    {
                        WriteNextendoFriendV2(raw.Slice(i * FriendRecordV2Size, FriendRecordV2Size), lid, entry);
                    }
                }
                count = n;
            }

            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetFriendListForViewerV2 -> offset={offset} count={count} buffer={friendList.Length * 0x200} bytes");

            return Result.Success;
        }

        [CmifCommand(20110)]
        public Result LoadFriendSetting(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x40)] out FriendSettingImpl friendSetting,
            Uid userId,
            NetworkServiceAccountId friendId)
        {
            friendSetting = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId });

            return Result.Success;
        }

        [CmifCommand(20200)]
        public Result GetReceivedFriendRequestCount(out int count, out int count2, Uid userId)
        {
            // [Nextendo, HOME Menu work] The requests received by the Nextendo account.
            // The page adds the two (unread, then read): all the requests count once, as unread.
            count = IsNextendoProfile(userId) ? NextendoFriends.Requests().Count : 0;
            count2 = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(20201)]
        public Result GetFriendRequestList(
            out int count,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<FriendRequestImpl> requestList,
            Uid userId,
            int arg3,
            int arg4)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, arg3, arg4 });

            return Result.Success;
        }

        [CmifCommand(20203)] // 22.0.0+
        public Result GetFriendRequestReceivedNotificationCount(out int count, Uid userId)
        {
            count = IsNextendoProfile(userId) ? NextendoFriends.Requests().Count : 0;

            return Result.Success;
        }

        // [Nextendo, HOME Menu work] The list of friend requests (SDK 20+): received or sent,
        // by the type asked. The layout of a record is being worked out: the probe writes known
        // values for the page to show.
        [CmifCommand(20202)]
        public Result GetFriendRequestListV2(
            out int count,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<byte> requestList,
            Uid userId,
            int offset,
            int type)
        {
            count = 0;
            requestList.Clear();

            IReadOnlyList<NextendoFriends.Entry> requests = !IsNextendoProfile(userId) ? []
                : type == 0 ? NextendoFriends.Sent() : NextendoFriends.Requests();

            int size = FriendRequestV2Size;

            for (int i = offset; i < requests.Count && (count + 1) * size <= requestList.Length; i++)
            {
                WriteFriendRequestV2(requestList.Slice(count * size, size), requests[i]);

                count++;
            }

            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetFriendRequestListV2 type={type} offset={offset} buffer=0x{requestList.Length:X} -> {count} request(s)");

            return Result.Success;
        }

        private const int FriendRequestV2Size = 0x200; // 0x200 each: the SDK sizes the buffer as count << 9

        private static void WriteFriendRequestV2(Span<byte> record, NextendoFriends.Entry entry)
        {

            // Found by probing the received list: the request id at 0, the account the request
            // comes from (or goes to) at 0x18: the page asks its profile (GetProfileExtraListV2).
            BitConverter.TryWriteBytes(record.Slice(0x00, 8), entry.Pid);
            BitConverter.TryWriteBytes(record.Slice(0x18, 8), entry.Nsa != 0 ? entry.Nsa : entry.Pid);

            // The nickname at 0x20 (found the same way: the tile shows the text there).
            byte[] nick = System.Text.Encoding.UTF8.GetBytes(entry.Name);
            nick.AsSpan(0, Math.Min(nick.Length, 0x20)).CopyTo(record.Slice(0x20, 0x21));

            // The address of the picture at 0x48: the page downloads it itself.
            // An account with no picture of its own has the default avatar of Nextendo, served by
            // the emulator (the server has no address for it).
            string pictureUrl = entry.HasPicture
                ? $"{NextendoEndpoint.Canonical.TrimEnd('/')}/api/avatar?pid={entry.Pid}"
                : NextendoLocalImages.DefaultAvatarUrl(entry.Pid);
            byte[] picture = System.Text.Encoding.ASCII.GetBytes(pictureUrl);
            picture.AsSpan(0, Math.Min(picture.Length, 0x9F)).CopyTo(record.Slice(0x48, 0xA0));

            // The date of the request at 0x158 (found by probing: one date per free slot, the
            // detail page showed the one of the slot 43).
            BitConverter.TryWriteBytes(record.Slice(0x158, 8), entry.RequestedAt);

            // How the request was made, at 0xEC (found by probing the detail page): 1 a local
            // search, 2 a friend code, 3 a game (its id then at 0xF8). Requests to Nextendo go
            // through friend codes (the site and the console both send by code).
            BitConverter.TryWriteBytes(record.Slice(0xEC, 4), 2);
        }

        [CmifCommand(20300)]
        public Result GetFriendCandidateList(
            out int count,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<FriendCandidateImpl> candidateList,
            Uid userId,
            int arg3)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, arg3 });

            return Result.Success;
        }

        [CmifCommand(20301)]
        public Result GetNintendoNetworkIdInfo(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x38)] out NintendoNetworkIdUserInfo networkIdInfo,
            out int arg1,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<NintendoNetworkIdFriendImpl> friendInfo,
            Uid userId,
            int arg4)
        {
            networkIdInfo = default;
            arg1 = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, arg4 });

            return Result.Success;
        }

        [CmifCommand(20302)]
        public Result GetSnsAccountLinkage(out SnsAccountLinkage accountLinkage, Uid userId)
        {
            accountLinkage = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(20303)]
        public Result GetSnsAccountProfile(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x380)] out SnsAccountProfile accountProfile,
            Uid userId,
            NetworkServiceAccountId friendId,
            int arg3)
        {
            accountProfile = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId, arg3 });

            return Result.Success;
        }

        [CmifCommand(20304)]
        public Result GetSnsAccountFriendList(
            out int count,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<SnsAccountFriendImpl> friendList,
            Uid userId,
            int arg3)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, arg3 });

            return Result.Success;
        }

        [CmifCommand(20400)]
        public Result GetBlockedUserList(
            out int count,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<BlockedUserImpl> blockedUsers,
            Uid userId,
            int arg3)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, arg3 });

            return Result.Success;
        }

        [CmifCommand(20401)]
        public Result SyncBlockedUserList(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(20500)]
        public Result GetProfileExtraList(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<ProfileExtraImpl> extraList,
            Uid userId,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer)] ReadOnlySpan<NetworkServiceAccountId> friendIds)
        {
            string friendIdList = string.Join(", ", friendIds.ToArray());

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendIdList });

            return Result.Success;
        }

        // [Nextendo, HOME Menu work] The profiles of accounts that are not (yet) friends: the
        // senders of friend requests, the people met. Looked up among the requests, then the
        // friends. Each record (its size given by the buffer) is laid out like the head of a
        // friend record: ids, then the nickname, and the "valid" flag in its last byte.
        [CmifCommand(20502)] // 19.0.0+
        public Result GetProfileExtraListV2(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<byte> extraList,
            Uid userId,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer)] ReadOnlySpan<NetworkServiceAccountId> accountIds)
        {
            extraList.Clear();

            int size = accountIds.Length > 0 ? extraList.Length / accountIds.Length : 0;
            int filled = 0;

            for (int i = 0; size >= 0x41 && i < accountIds.Length && (i + 1) * size <= extraList.Length; i++)
            {
                ulong wanted = accountIds[i].Id;
                NextendoFriends.Entry entry = default;
                bool found = false;

                foreach (IReadOnlyList<NextendoFriends.Entry> list in new[] { NextendoFriends.Requests(), NextendoFriends.Sent(), NextendoFriends.Get() })
                {
                    foreach (NextendoFriends.Entry candidate in list)
                    {
                        if (candidate.Pid == wanted || (candidate.Nsa != 0 && candidate.Nsa == wanted))
                        {
                            entry = candidate;
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        break;
                    }
                }

                if (!found)
                {
                    continue;
                }

                Span<byte> record = extraList.Slice(i * size, size);

                // nn::friends ProfileExtra, laid out as the accessors of the HOME Menu read it: the
                // account id at 0 (the page of a request looks its profile up by it), the nickname
                // at 8, the address of the picture at 0x30, twenty played games of 0x30 bytes from
                // 0xD0 (same entries as the friend's history) and "valid" at 0x490.
                BitConverter.TryWriteBytes(record.Slice(0x00, 8), wanted);

                byte[] nickname = Encoding.UTF8.GetBytes(entry.Name ?? "");
                nickname.AsSpan(0, Math.Min(nickname.Length, 0x20)).CopyTo(record.Slice(0x08, 0x21));

                if (size >= 0xD0)
                {
                    string pictureUrl = entry.HasPicture
                        ? $"{NextendoEndpoint.Canonical.TrimEnd('/')}/api/avatar?pid={entry.Pid}"
                        : NextendoLocalImages.DefaultAvatarUrl(entry.Pid);
                    byte[] picture = Encoding.ASCII.GetBytes(pictureUrl);
                    picture.AsSpan(0, Math.Min(picture.Length, 0x9F)).CopyTo(record.Slice(0x30, 0xA0));
                }

                if (size >= 0x490)
                {
                    List<NextendoFriends.Played> played = new(NextendoFriends.History(entry.Pid, 2500, direct: true));

                    played.RemoveAll(g => string.IsNullOrEmpty(g.Name));

                    for (int g = 0; g < played.Count && g < 20; g++)
                    {
                        Span<byte> slot = record.Slice(0xD0 + g * 0x30, 0x30);

                        BitConverter.TryWriteBytes(slot.Slice(0x00, 8), played[g].TitleId);
                        BitConverter.TryWriteBytes(slot.Slice(0x08, 8), played[g].TitleId);
                        BitConverter.TryWriteBytes(slot.Slice(0x18, 4), 1);
                        BitConverter.TryWriteBytes(slot.Slice(0x1C, 4), (int)Math.Min(int.MaxValue, played[g].Seconds / 60));
                        BitConverter.TryWriteBytes(slot.Slice(0x28, 8), played[g].LastPlayed);
                    }
                }

                record[size >= 0x491 ? 0x490 : size - 1] = 1; // "valid", found by bisecting the received list (record of 0x4A8)

                filled++;
            }

            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetProfileExtraListV2 -> {accountIds.Length} ids (first 0x{(accountIds.Length > 0 ? accountIds[0].Id : 0):X}), buffer 0x{extraList.Length:X}, record 0x{size:X}, {filled} filled");

            return Result.Success;
        }

        [CmifCommand(20501)]
        public Result GetRelationship(out Relationship relationship, Uid userId, NetworkServiceAccountId friendId)
        {
            relationship = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId });

            return Result.Success;
        }

        [CmifCommand(20600)]
        public Result GetUserPresenceView([Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0xE0)] out UserPresenceViewImpl userPresenceView, Uid userId)
        {
            userPresenceView = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        // [Nextendo, HOME Menu work] The friend list of SDK 20+ comes in two steps: 20108 gives
        // the list, then this command is handed every friend's id at once (a pointer buffer of
        // 8-byte ids) and returns one 0x220-byte record per id, which is where the name, the
        // picture and the presence shown on each tile are read.
        [CmifCommand(20109)]
        public Result GetFriendDetailListV2(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<byte> records,
            Uid userId,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer)] ReadOnlySpan<NetworkServiceAccountId> friendIds)
        {
            const int RecordSize = FriendRecordV2Size;

            records.Clear();


            IReadOnlyList<NextendoFriends.Entry> friends = IsNextendoProfile(userId) ? NextendoFriends.Get() : [];
            int filled = 0;

            for (int i = 0; i < friendIds.Length && (i + 1) * RecordSize <= records.Length; i++)
            {
                ulong wanted = friendIds[i].Id;

                foreach (NextendoFriends.Entry entry in friends)
                {
                    if (entry.Pid != wanted && (entry.Nsa == 0 || entry.Nsa != wanted))
                    {
                        continue;
                    }

                    WriteNextendoFriendV2(records.Slice(i * RecordSize, RecordSize), wanted, entry);

                    filled++;

                    break;
                }
            }

            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetFriendDetailListV2 -> {friendIds.Length} ids (first 0x{(friendIds.Length > 0 ? friendIds[0].Id : 0):X}), {filled} records, buffer 0x{records.Length:X}");

            return Result.Success;
        }

        [CmifCommand(20601)]
        public Result GetUserPresenceViewV2([Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, UserPresenceViewV2Impl.Size)] out UserPresenceViewV2Impl userPresenceView, Uid userId)
        {
            userPresenceView = default;

            Span<byte> view = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref userPresenceView, 1));

            // The signed-in Nextendo profile is online; any other local profile is not.
            if (IsNextendoProfile(userId) && NextendoAccount.IsLinked)
            {
                // Like a friend's record: the application being played first, then the
                // presence, whose status is 0x18 further (found by probing).
                BitConverter.TryWriteBytes(view[..8], NextendoGames.Playing);
                view[0x20] = (byte)PresenceStatus.OnlinePlay;
            }

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(20700)]
        public Result GetPlayHistoryList(out int count, [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<PlayHistoryImpl> playHistoryList, Uid userId, int arg3)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, arg3 });

            return Result.Success;
        }

        [CmifCommand(20701)]
        public Result GetPlayHistoryStatistics(out PlayHistoryStatistics statistics, Uid userId)
        {
            statistics = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(20800)]
        public Result LoadUserSetting([Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x800)] out UserSettingImpl userSetting, Uid userId)
        {
            userSetting = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(20801)]
        public Result SyncUserSetting(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(20802)]
        public Result LoadUserSettingV2([Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x800)] out UserSettingImpl userSetting, Uid userId)
        {
            userSetting = default;

            Span<byte> setting = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref userSetting, 1));

            if (IsNextendoProfile(userId) && NextendoAccount.IsLinked)
            {
                // the user's own page asks for these settings too (its friend code), not only the
                // page to add a friend: Discord said "Adding a friend" on the profile
                NextendoActivity.Set("Profile");

                // The user's own settings: who they are, then their friend code as text.
                MemoryMarshal.Write(setting, in userId);

                int codeAt = 0x20;
                string code = NextendoAccount.FriendCode ?? "";

                // The menu writes "SW-" itself in front of the code.
                if (code.StartsWith("SW-", StringComparison.OrdinalIgnoreCase))
                {
                    code = code[3..];
                }

                if (code.Length > 0)
                {
                    Encoding.ASCII.GetBytes(code).AsSpan(0, Math.Min(code.Length, 0x1F)).CopyTo(setting.Slice(codeAt, 0x20));
                }

                // The user's own profile, with the games played (the profile page shows them):
                // a ProfileExtra as the friends' ones, at 0x48.
                int extraAt = 0x48;

                if (extraAt + 0x4A8 <= setting.Length)
                {
                    Span<byte> extra = setting.Slice(extraAt, 0x4A8);

                    BitConverter.TryWriteBytes(extra.Slice(0x00, 8), NextendoAccount.Pid);

                    byte[] nickname = Encoding.UTF8.GetBytes(NextendoAccount.Username ?? "");
                    nickname.AsSpan(0, Math.Min(nickname.Length, 0x20)).CopyTo(extra.Slice(0x08, 0x21));

                    byte[] picture = Encoding.ASCII.GetBytes($"{NextendoEndpoint.Canonical.TrimEnd('/')}/api/avatar?pid={NextendoAccount.Pid}");
                    picture.AsSpan(0, Math.Min(picture.Length, 0x9F)).CopyTo(extra.Slice(0x30, 0xA0));

                    List<NextendoFriends.Played> played = new(NextendoFriends.OwnHistory());

                    played.RemoveAll(g => string.IsNullOrEmpty(g.Name));

                    for (int g = 0; g < played.Count && g < 20; g++)
                    {
                        Span<byte> slot = extra.Slice(0xD0 + g * 0x30, 0x30);

                        BitConverter.TryWriteBytes(slot.Slice(0x00, 8), played[g].TitleId);
                        BitConverter.TryWriteBytes(slot.Slice(0x08, 8), played[g].TitleId);
                        BitConverter.TryWriteBytes(slot.Slice(0x18, 4), 1);
                        BitConverter.TryWriteBytes(slot.Slice(0x1C, 4), (int)Math.Min(int.MaxValue, played[g].Seconds / 60));
                        BitConverter.TryWriteBytes(slot.Slice(0x28, 8), played[g].LastPlayed);
                    }

                    extra[0x490] = 1;

                    Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] LoadUserSettingV2: own profile with {Math.Min(played.Count, 20)} game(s) played");
                }
            }

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(20900)]
        public Result RequestListSummaryOverlayNotification()
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend);

            return Result.Success;
        }

        [CmifCommand(21000)]
        public Result GetExternalApplicationCatalog(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x4B8)] out ExternalApplicationCatalog catalog,
            ExternalApplicationCatalogId catalogId,
            LanguageCode language)
        {
            catalog = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { catalogId, language });

            return Result.Success;
        }

        [CmifCommand(22000)]
        public Result GetReceivedFriendInvitationList(
            out int count,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<FriendInvitationForViewerImpl> invitationList,
            Uid userId)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(22001)]
        public Result GetReceivedFriendInvitationDetailedInfo(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias, 0x1400)] out FriendInvitationGroupImpl invicationGroup,
            Uid userId,
            FriendInvitationGroupId groupId)
        {
            invicationGroup = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, groupId });

            return Result.Success;
        }

        [CmifCommand(22010)]
        public Result GetReceivedFriendInvitationCountCache(out int count, Uid userId)
        {
            count = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(30100)]
        public Result DropFriendNewlyFlags(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(30101)]
        public Result DeleteFriend(Uid userId, NetworkServiceAccountId friendId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId });

            return Result.Success;
        }

        [CmifCommand(30110)]
        public Result DropFriendNewlyFlag(Uid userId, NetworkServiceAccountId friendId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId });

            return Result.Success;
        }

        [CmifCommand(30120)]
        public Result ChangeFriendFavoriteFlag(Uid userId, NetworkServiceAccountId friendId, bool favoriteFlag)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId, favoriteFlag });

            return Result.Success;
        }

        [CmifCommand(30121)]
        public Result ChangeFriendOnlineNotificationFlag(Uid userId, NetworkServiceAccountId friendId, bool onlineNotificationFlag)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId, onlineNotificationFlag });

            return Result.Success;
        }

        [CmifCommand(30200)]
        public Result SendFriendRequest(Uid userId, NetworkServiceAccountId friendId, int arg2)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId, arg2 });

            return Result.Success;
        }

        [CmifCommand(30201)]
        public Result SendFriendRequestWithApplicationInfo(
            Uid userId,
            NetworkServiceAccountId friendId,
            int arg2,
            ApplicationInfo applicationInfo,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x48)] in InAppScreenName arg4,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x48)] in InAppScreenName arg5)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId, arg2, applicationInfo, arg4, arg5 });

            return Result.Success;
        }

        [CmifCommand(30202)]
        public Result CancelFriendRequest(Uid userId, RequestId requestId)
        {
            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] cancel friend request to {requestId.Id}: {(IsNextendoProfile(userId) && NextendoFriends.CancelRequest(requestId.Id) ? "done" : "failed")}");

            return Result.Success;
        }

        [CmifCommand(30203)]
        public Result AcceptFriendRequest(Uid userId, RequestId requestId)
        {
            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] accept friend request from {requestId.Id}: {(IsNextendoProfile(userId) && NextendoFriends.Answer(requestId.Id, accept: true) ? "done" : "failed")}");

            return Result.Success;
        }

        [CmifCommand(30204)]
        public Result RejectFriendRequest(Uid userId, RequestId requestId)
        {
            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] decline friend request from {requestId.Id}: {(IsNextendoProfile(userId) && NextendoFriends.Answer(requestId.Id, accept: false) ? "done" : "failed")}");

            return Result.Success;
        }

        [CmifCommand(30205)]
        public Result ReadFriendRequest(Uid userId, RequestId requestId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, requestId });

            return Result.Success;
        }

        [CmifCommand(30210)]
        public Result GetFacedFriendRequestRegistrationKey(out FacedFriendRequestRegistrationKey registrationKey, Uid userId)
        {
            registrationKey = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(30211)]
        public Result AddFacedFriendRequest(
            Uid userId,
            FacedFriendRequestRegistrationKey registrationKey,
            Nickname nickname,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.MapAlias)] ReadOnlySpan<byte> arg3)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, registrationKey, nickname });

            return Result.Success;
        }

        [CmifCommand(30212)]
        public Result CancelFacedFriendRequest(Uid userId, NetworkServiceAccountId friendId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId });

            return Result.Success;
        }

        [CmifCommand(30213)]
        public Result GetFacedFriendRequestProfileImage(
            out int size,
            Uid userId,
            NetworkServiceAccountId friendId,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<byte> profileImage)
        {
            size = 0;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId });

            return Result.Success;
        }

        [CmifCommand(30214)]
        public Result GetFacedFriendRequestProfileImageFromPath(
            out int size,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer)] ReadOnlySpan<byte> path,
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<byte> profileImage)
        {
            size = 0;

            string pathString = Encoding.UTF8.GetString(path);

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { pathString });

            return Result.Success;
        }

        [CmifCommand(30215)]
        public Result SendFriendRequestWithExternalApplicationCatalogId(
            Uid userId,
            NetworkServiceAccountId friendId,
            int arg2,
            ExternalApplicationCatalogId catalogId,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x48)] in InAppScreenName arg4,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x48)] in InAppScreenName arg5)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId, arg2, catalogId, arg4, arg5 });

            return Result.Success;
        }

        [CmifCommand(30216)]
        public Result ResendFacedFriendRequest(Uid userId, NetworkServiceAccountId friendId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId });

            return Result.Success;
        }

        [CmifCommand(30217)]
        public Result SendFriendRequestWithNintendoNetworkIdInfo(
            Uid userId,
            NetworkServiceAccountId friendId,
            int arg2,
            MiiName arg3,
            MiiImageUrlParam arg4,
            MiiName arg5,
            MiiImageUrlParam arg6)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId, arg2, arg3, arg4, arg5, arg6 });

            return Result.Success;
        }

        [CmifCommand(30300)]
        public Result GetSnsAccountLinkPageUrl([Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias, 0x1000)] out WebPageUrl url, Uid userId, int arg2)
        {
            url = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, arg2 });

            return Result.Success;
        }

        [CmifCommand(30301)]
        public Result UnlinkSnsAccount(Uid userId, int arg1)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, arg1 });

            return Result.Success;
        }

        [CmifCommand(30400)]
        public Result BlockUser(Uid userId, NetworkServiceAccountId friendId, int arg2)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId, arg2 });

            return Result.Success;
        }

        [CmifCommand(30401)]
        public Result BlockUserWithApplicationInfo(
            Uid userId,
            NetworkServiceAccountId friendId,
            int arg2,
            ApplicationInfo applicationInfo,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer, 0x48)] in InAppScreenName arg4)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId, arg2, applicationInfo, arg4 });

            return Result.Success;
        }

        [CmifCommand(30402)]
        public Result UnblockUser(Uid userId, NetworkServiceAccountId friendId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId });

            return Result.Success;
        }

        [CmifCommand(30500)]
        public Result GetProfileExtraFromFriendCode(
            [Buffer(HipcBufferFlags.Out | HipcBufferFlags.Pointer, 0x400)] out ProfileExtraImpl profileExtra,
            Uid userId,
            FriendCode friendCode)
        {
            profileExtra = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendCode });

            return Result.Success;
        }

        [CmifCommand(30700)]
        public Result DeletePlayHistory(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(30810)]
        public Result ChangePresencePermission(Uid userId, int permission)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, permission });

            return Result.Success;
        }

        [CmifCommand(30811)]
        public Result ChangeFriendRequestReception(Uid userId, bool reception)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, reception });

            return Result.Success;
        }

        [CmifCommand(30812)]
        public Result ChangePlayLogPermission(Uid userId, int permission)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, permission });

            return Result.Success;
        }

        [CmifCommand(30820)]
        public Result IssueFriendCode(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(30830)]
        public Result ClearPlayLog(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(30900)]
        public Result SendFriendInvitation(
            Uid userId,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer)] ReadOnlySpan<NetworkServiceAccountId> friendIds,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.MapAlias, 0xC00)] in FriendInvitationGameModeDescription description,
            ApplicationInfo applicationInfo,
            [Buffer(HipcBufferFlags.In | HipcBufferFlags.MapAlias)] ReadOnlySpan<byte> arg4,
            bool arg5)
        {
            string friendIdList = string.Join(", ", friendIds.ToArray());

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendIdList, description, applicationInfo, arg5 });

            return Result.Success;
        }

        [CmifCommand(30910)]
        public Result ReadFriendInvitation(Uid userId, [Buffer(HipcBufferFlags.In | HipcBufferFlags.Pointer)] ReadOnlySpan<FriendInvitationId> invitationIds)
        {
            string invitationIdList = string.Join(", ", invitationIds.ToArray());

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, invitationIdList });

            return Result.Success;
        }

        [CmifCommand(30911)]
        public Result ReadAllFriendInvitations(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(40100)]
        public Result DeleteFriendListCache(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(40400)]
        public Result DeleteBlockedUserListCache(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        [CmifCommand(49900)]
        public Result DeleteNetworkServiceAccountCache(Uid userId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId });

            return Result.Success;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // nnSdk closes an async session only after storing its result.
                if (_isAsyncSession)
                {
                    Os.SignalSystemEvent(ref _completionEvent);
                }

                Os.DestroySystemEvent(ref _completionEvent);
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
