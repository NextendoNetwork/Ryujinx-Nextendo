using System;
using System.IO;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// Nextendo Network linked account. Written by the in-app "Connexion Nextendo
    /// Network" dialog, read by the Account service (ManagerServer) so the NEX
    /// login presents the account's PERSISTENT principal id (PID) instead of the
    /// 0xcafe stub — this is what makes "log in with your account = play online as
    /// you" work. Stored as a tiny key=value file (no JSON, trimming/AOT-safe).
    /// </summary>
    public static class NextendoAccount
    {
        private static readonly object _lock = new();
        private static bool _loaded;

        public static ulong Pid { get; private set; }
        public static string Username { get; private set; } = "";
        public static string FriendCode { get; private set; } = "";
        public static string NexToken { get; private set; } = "";

        private static bool _isGuest;

        /// <summary>True when the linked identity is a no-account GUEST profile (created by
        /// the beta quick-start via /api/guest) rather than a full registered account. A guest
        /// has a real persistent PID/friend code (online, friends and sync all work) but no
        /// e-mail/password; it can be renamed freely and later upgraded to a full account.</summary>
        public static bool IsGuest
        {
            get { EnsureLoaded(); return _isGuest; }
        }

        private static string _profileUserId = "";

        /// <summary>The Ryujinx user-profile UserId bound to this Nextendo account, so
        /// we reuse the same local profile instead of creating duplicates on each login.</summary>
        public static string ProfileUserId
        {
            get { EnsureLoaded(); return _profileUserId; }
        }

        private static string _miiData = "";

        /// <summary>Base64 of the account's Mii (Switch StoreData, 0x44 bytes), mirrored
        /// locally so the exact same Mii can be removed from the Mii database on logout.</summary>
        public static string MiiData
        {
            get { EnsureLoaded(); return _miiData; }
        }

        public static bool IsLinked
        {
            get { EnsureLoaded(); return Pid != 0; }
        }

        /// <summary>Runtime-only (not persisted): set true at game launch when the running
        /// game is a Nextendo title on an UNSUPPORTED version. While true, the NEX login
        /// presents the anonymous stub instead of the account PID, so the gated server
        /// refuses online — enforcing the required game version.</summary>
        public static bool OnlineBlocked { get; set; }

        private static string FilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_account.txt");

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_loaded)
                {
                    return;
                }

                _loaded = true;

                try
                {
                    if (!File.Exists(FilePath))
                    {
                        return;
                    }

                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0)
                        {
                            continue;
                        }

                        string key = line[..eq].Trim();
                        string val = line[(eq + 1)..].Trim();

                        switch (key)
                        {
                            case "pid": ulong.TryParse(val, out ulong p); Pid = p; break;
                            case "username": Username = val; break;
                            case "friend_code": FriendCode = val; break;
                            case "nex_token": NexToken = val; break;
                            case "profile_user_id": _profileUserId = val; break;
                            case "mii_data": _miiData = val; break;
                            case "is_guest": _isGuest = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        }
                    }
                }
                catch
                {
                    // Corrupt/unreadable file -> treat as not linked.
                    Pid = 0;
                }
            }
        }

        public static void Save(ulong pid, string username, string friendCode, string nexToken, bool isGuest = false)
        {
            lock (_lock)
            {
                EnsureLoaded(); // preserve an existing profile binding for the same account
                if (pid != Pid)
                {
                    // different account -> unbind the previous local profile + Mii
                    _profileUserId = "";
                    _miiData = "";
                }
                Pid = pid;
                Username = username ?? "";
                FriendCode = friendCode ?? "";
                NexToken = nexToken ?? "";
                _isGuest = isGuest;
                _loaded = true;
                WriteFileLocked();
            }
        }

        /// <summary>Binds the Ryujinx user-profile created/used for this account.</summary>
        public static void SetProfileUserId(string userId)
        {
            lock (_lock)
            {
                EnsureLoaded();
                _profileUserId = userId ?? "";
                WriteFileLocked();
            }
        }

        /// <summary>Mirrors the account's Mii (base64 StoreData) locally for logout removal.</summary>
        public static void SetMiiData(string miiData)
        {
            lock (_lock)
            {
                EnsureLoaded();
                _miiData = miiData ?? "";
                WriteFileLocked();
            }
        }

        private static void WriteFileLocked()
        {
            try
            {
                File.WriteAllText(FilePath,
                    $"pid={Pid}\nusername={Username}\nfriend_code={FriendCode}\nnex_token={NexToken}\nprofile_user_id={_profileUserId}\nmii_data={_miiData}\nis_guest={(_isGuest ? "1" : "0")}\n");
            }
            catch
            {
                // Best effort; the in-memory values still apply this session.
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                Pid = 0;
                Username = "";
                FriendCode = "";
                NexToken = "";
                _profileUserId = "";
                _miiData = "";
                _isGuest = false;
                _loaded = true;

                try
                {
                    if (File.Exists(FilePath))
                    {
                        File.Delete(FilePath);
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}
