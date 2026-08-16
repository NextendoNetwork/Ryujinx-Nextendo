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

        public FriendService(IEmulatorAccountManager accountManager, FriendsServicePermissionLevel permissionLevel)
        {
            _accountManager = accountManager;

            Os.CreateSystemEvent(out _completionEvent, EventClearMode.ManualClear, interProcess: true).AbortOnFailure();
            Os.SignalSystemEvent(ref _completionEvent); // TODO: Figure out where we are supposed to signal this.
        }

        // [Nextendo] Splatoon 3 passe par NPLN, qui nomme chaque ami par son NSA. Gate explicite par
        // titre : tout autre jeu (Mario Kart 8, Splatoon 2...) garde le comportement PID d'origine.
        private const string Splatoon3TitleId = "0100c2500fc20000";

        private static bool WantsNsaIds()
            => string.Equals(NextendoFriends.CurrentTitleId, Splatoon3TitleId, StringComparison.OrdinalIgnoreCase);

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
            completionEventHandle = Os.GetReadableHandleOfSystemEvent(ref _completionEvent);

            return Result.Success;
        }

        [CmifCommand(1)]
        public Result Cancel()
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend);

            return Result.Success;
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

            if (userId.IsNull)
            {
                return FriendResult.InvalidArgument;
            }

            // [Nextendo] serve the player's real Nextendo friends (page 0 only).
            if (offset == 0)
            {
                bool wantNsa = WantsNsaIds();

                // Splatoon 3 ne demande sa liste qu'UNE fois, tot : si le cache n'est pas encore
                // rempli il repart definitivement avec zero ami. On lui accorde une attente courte
                // et plafonnee ; les jeux NEX, qui reinterrogent en boucle, gardent le chemin non
                // bloquant (les bloquer casse leurs acquittements PRUDP).
                IReadOnlyList<NextendoFriends.Entry> friends =
                    wantNsa ? NextendoFriends.GetWarm(2000) : NextendoFriends.Get();
                int n = Math.Min(friends.Count, friendIds.Length);
                // QUEL identifiant ce jeu attend-il dans sa liste locale ? Mario Kart 8 (NEX)
                // redemande ses amis PAR LE PID. Splatoon 3 (NPLN) les nomme par NSA : mesure,
                // 125 appels sur 125 a UpdateFriendInfo portaient un id de 16 chiffres hexa alors
                // que nous rendions ici des PID de 8. Les deux listes vivaient dans des espaces
                // d'identifiants DISJOINTS : l'intersection etait vide, donc l'ecran aussi.
                // Remplir la fiche (UpdateFriendInfo) ne suffisait pas — c'est ici que se decide
                // l'APPARTENANCE a la liste.
                for (int i = 0; i < n; i++)
                {
                    ulong id = wantNsa && friends[i].Nsa != 0 ? friends[i].Nsa : friends[i].Pid;
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

            if (userId.IsNull)
            {
                return FriendResult.InvalidArgument;
            }

            // [Nextendo] serve the player's real Nextendo friends (page 0 only).
            if (offset == 0)
            {
                IReadOnlyList<NextendoFriends.Entry> friends = NextendoFriends.Get();
                bool wantNsa = WantsNsaIds();
                int n = Math.Min(friends.Count, friendList.Length);
                for (int i = 0; i < n; i++)
                {
                    // Meme espace d'identifiants que GetFriendListIds, sinon Splatoon 3 recevrait
                    // ici des fiches estampillees d'un PID qu'il ne sait pas rapprocher de la liste
                    // que NPLN lui a donnee. Les autres jeux gardent le PID.
                    ulong lid = wantNsa && friends[i].Nsa != 0 ? friends[i].Nsa : friends[i].Pid;
                    friendList[i] = MakeNextendoFriend(lid, friends[i].Name, PresenceStatus.OnlinePlay, friends[i].AppField);
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

            Logger.Info?.Print(LogClass.ServiceFriend, $"[Nextendo] GetFriendList -> count={count}");

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
            IReadOnlyList<NextendoFriends.Entry> known = NextendoFriends.Get();

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
                        PresenceStatus st = e.Status > 0 ? PresenceStatus.OnlinePlay : PresenceStatus.Offline;
                        info[i] = MakeNextendoFriend(wanted, e.Name, st, e.AppField, sameApp: e.Status > 0);

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

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendId });

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
            string friendIdList = string.Join(", ", friendIds.ToArray());

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, friendIdList });

            return Result.Success;
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
        public Result GetProfileImageUrl(out Url imageUrl, Url url, int arg2)
        {
            imageUrl = default;

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { url, arg2 });

            return Result.Success;
        }

        [CmifCommand(20100)]
        public Result GetFriendCount(out int count, Uid userId, SizedFriendFilter filter, ulong pidPlaceholder, [ClientProcessId] ulong pid)
        {
            count = NextendoFriends.Get().Count; // [Nextendo] real friend count

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
            count = 0;
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
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, requestId });

            return Result.Success;
        }

        [CmifCommand(30203)]
        public Result AcceptFriendRequest(Uid userId, RequestId requestId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, requestId });

            return Result.Success;
        }

        [CmifCommand(30204)]
        public Result RejectFriendRequest(Uid userId, RequestId requestId)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { userId, requestId });

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
