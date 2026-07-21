using System.Reflection;

using NUnit.Framework;

using Ryujinx.Cpu;
using Ryujinx.HLE.HOS.Services.Mii;
using Ryujinx.HLE.HOS.Services.Mii.StaticService;
using Ryujinx.HLE.HOS.Services.Mii.Types;

namespace Ryujinx.Tests.HLE
{
    public class MiiDatabaseTests
    {
        [Test]
        public void UpdateLatestReturnsStoredCharInfo()
        {
            DatabaseImpl database = new();
            StoreData storedData = StoreData.BuildDefault(new UtilityImpl(new TickSource(19200000)), 0);
            MiiDatabaseManager databaseManager = GetDatabaseManager(database);

            NintendoFigurineDatabase figurineDatabase = new();
            figurineDatabase.Format();
            figurineDatabase.Add(storedData);
            SetFigurineDatabase(databaseManager, figurineDatabase);

            TestDatabaseService service = new(database);

            CharInfo oldCharInfo = new();
            oldCharInfo.SetFromStoreData(storedData);
            oldCharInfo.Height--;

            ResultCode result = service.UpdateLatestForTest(oldCharInfo, SourceFlag.Database, out CharInfo newCharInfo);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ResultCode.Success));
                Assert.That(newCharInfo.CreateId, Is.EqualTo(oldCharInfo.CreateId));
                Assert.That(newCharInfo.Height, Is.EqualTo(storedData.CoreData.Height));
                Assert.That(newCharInfo.IsValid(), Is.True);
            });

        }

        [Test]
        public void AppendAddsRegularCharInfoToDatabase()
        {
            DatabaseImpl database = new();
            UtilityImpl utilityImpl = new(new TickSource(19200000));
            SetUtilityImpl(database, utilityImpl);
            MiiDatabaseManager databaseManager = GetDatabaseManager(database);
            SetFigurineDatabase(databaseManager, CreateFormattedDatabase());

            StoreData defaultStoreData = StoreData.BuildDefault(utilityImpl, 0);
            Assert.Multiple(() =>
            {
                Assert.That(defaultStoreData.CoreData.IsValid(), Is.True);
                Assert.That(defaultStoreData.IsValidDataCrc(), Is.True);
                Assert.That(defaultStoreData.IsValidDeviceCrc(), Is.True);
                Assert.That(defaultStoreData.IsValid(), Is.True);
            });

            CharInfo charInfo = new();
            charInfo.SetFromStoreData(defaultStoreData);

            DatabaseSessionMetadata metadata = database.CreateSessionMetadata(new SpecialMiiKeyCode());

            ResultCode result = databaseManager.Append(metadata, utilityImpl, charInfo);

            int count = databaseManager.GetCount(metadata);
            databaseManager.Get(metadata, 0, out StoreData storedData);

            CoreData expectedCoreData = new();
            expectedCoreData.SetFromCharInfo(charInfo);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ResultCode.Success));
                Assert.That(count, Is.EqualTo(1));
                Assert.That(storedData.IsValid(), Is.True);
                Assert.That(storedData.CreateId, Is.Not.EqualTo(charInfo.CreateId));
                Assert.That(storedData.CoreData, Is.EqualTo(expectedCoreData));
            });
        }

        private sealed class TestDatabaseService(DatabaseImpl database) : DatabaseServiceImpl(database, true, new SpecialMiiKeyCode())
        {
            public ResultCode UpdateLatestForTest(CharInfo oldCharInfo, SourceFlag flag, out CharInfo newCharInfo)
            {
                return UpdateLatest(oldCharInfo, flag, out newCharInfo);
            }
        }

        private static MiiDatabaseManager GetDatabaseManager(DatabaseImpl database)
        {
            return (MiiDatabaseManager)typeof(DatabaseImpl)
                .GetField("_miiDatabase", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(database);
        }

        private static void SetFigurineDatabase(MiiDatabaseManager databaseManager, NintendoFigurineDatabase figurineDatabase)
        {
            typeof(MiiDatabaseManager)
                .GetField("_database", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(databaseManager, figurineDatabase);
        }

        private static NintendoFigurineDatabase CreateFormattedDatabase()
        {
            NintendoFigurineDatabase figurineDatabase = new();
            figurineDatabase.Format();

            return figurineDatabase;
        }

        private static void SetUtilityImpl(DatabaseImpl database, UtilityImpl utilityImpl)
        {
            typeof(DatabaseImpl)
                .GetField("_utilityImpl", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(database, utilityImpl);
        }
    }
}
