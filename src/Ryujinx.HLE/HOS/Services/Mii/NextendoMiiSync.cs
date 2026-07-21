using LibHac;
using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.Fs.Shim;
using Ryujinx.HLE.HOS.Services.Mii.Types;
using System;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Mii
{
    /// <summary>
    /// [Nextendo] Public bridge that lets the UI sync a Nextendo account's Mii into the
    /// local Switch Mii database (system save 0x8000000000000030) WITHOUT a running game.
    /// A Mii is persisted on the account as raw <see cref="StoreData"/> bytes (0x44); the
    /// fixed Ryujinx device id (Helper.GetDeviceId = "RyuMiiNx") makes those bytes valid
    /// on every Ryujinx install, so the Mii follows the account across machines.
    /// All operations are best-effort and never throw to the caller.
    /// </summary>
    public static class NextendoMiiSync
    {
        private static readonly U8String MountName = new("mii");

        /// <summary>Build a fresh Mii and return its StoreData bytes (length StoreData.Size), or null.</summary>
        public static byte[] CreateMii()
        {
            try
            {
                uint index = (uint)Random.Shared.Next(0, DefaultMii.TableLength);

                return ToBytes(StoreData.BuildNextendo(index));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Add (or replace) the given Mii in the local database. Returns true on success.</summary>
        public static bool InjectMii(HorizonClient client, byte[] storeDataBytes)
        {
            if (!IsUsable(client, storeDataBytes))
            {
                return false;
            }

            try
            {
                StoreData storeData = FromBytes(storeDataBytes);
                storeData.UpdateCrc();

                if (!storeData.IsValid())
                {
                    return false;
                }

                return WithDatabase(client, (db, metadata) => db.AddOrReplace(metadata, storeData) == ResultCode.Success);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Remove the given Mii (matched by its CreateId) from the local database.</summary>
        public static bool RemoveMii(HorizonClient client, byte[] storeDataBytes)
        {
            if (!IsUsable(client, storeDataBytes))
            {
                return false;
            }

            try
            {
                StoreData storeData = FromBytes(storeDataBytes);

                return WithDatabase(client, (db, metadata) => db.Delete(metadata, storeData.CreateId) == ResultCode.Success);
            }
            catch
            {
                return false;
            }
        }

        private static bool WithDatabase(HorizonClient client, Func<MiiDatabaseManager, DatabaseSessionMetadata, bool> op)
        {
            // The UI reuses one long-lived client, so clear any stale "mii" mount first.
            try { client.Fs.Unmount(MountName); } catch { /* not mounted */ }

            try
            {
                MiiDatabaseManager database = new();
                database.InitializeDatabase(client);
                database.LoadFromFile(out _);

                DatabaseSessionMetadata metadata = database.CreateSessionMetadata(new SpecialMiiKeyCode());

                bool ok = op(database, metadata);

                if (ok)
                {
                    database.SaveDatabase();
                }

                return ok;
            }
            finally
            {
                try { client.Fs.Unmount(MountName); } catch { /* already unmounted */ }
            }
        }

        private static bool IsUsable(HorizonClient client, byte[] bytes)
        {
            return client != null && bytes != null && bytes.Length == StoreData.Size;
        }

        private static byte[] ToBytes(StoreData storeData)
        {
            Span<StoreData> span = MemoryMarshal.CreateSpan(ref storeData, 1);

            return MemoryMarshal.AsBytes(span).ToArray();
        }

        private static StoreData FromBytes(byte[] bytes)
        {
            return MemoryMarshal.Read<StoreData>(bytes);
        }
    }
}
