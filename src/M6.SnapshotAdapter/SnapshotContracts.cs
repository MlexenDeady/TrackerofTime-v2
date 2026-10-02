using TrackerOfTime.V2.M6.GameState;using TrackerOfTime.V2.M6.CheckState;using TrackerOfTime.V2.M6.RandomizerState;
using RuntimeCheckState = TrackerOfTime.V2.M6.CheckState.CheckState;
namespace TrackerOfTime.V2.M6.SnapshotAdapter;
public enum SnapshotEvidence:byte{Proven,Partial,Inferred,Unknown}
public sealed record SnapshotField<T>(T Value,SnapshotEvidence Evidence,string Source);
public sealed record InventorySlotSnapshot(int Slot,byte ItemId,string ItemName,byte Quantity,bool Owned);
public sealed record InventorySnapshot(IReadOnlyList<InventorySlotSnapshot> Slots,IReadOnlyList<uint> RawItems,IReadOnlyList<uint> RawQuantities);
public sealed record EquipmentEntrySnapshot(int Bit,string Id,bool Owned);public sealed record EquipmentSnapshot(IReadOnlyList<EquipmentEntrySnapshot> Entries,uint RawEquipment,uint RawBiggoronCheck,string BiggoronDisplay);
public sealed record UpgradeStateSnapshot(string Id,int Level);public sealed record UpgradesSnapshot(IReadOnlyList<UpgradeStateSnapshot> Entries,uint RawUpgrades);
public sealed record QuestItemSnapshot(int Bit,string Id,bool Owned);public sealed record QuestItemsSnapshot(IReadOnlyList<QuestItemSnapshot> Entries,uint RawQuestItems);
public sealed record SongSnapshot(int Bit,string Id,bool Owned);public sealed record SongsSnapshot(IReadOnlyList<SongSnapshot> Entries);
public sealed record DungeonItemEntrySnapshot(int DungeonIndex,bool? Map,bool? Compass,bool? BossKey);public sealed record DungeonItemsSnapshot(IReadOnlyList<DungeonItemEntrySnapshot> Entries,IReadOnlyList<uint> RawBlocks,byte TriforceCount);
public sealed record DungeonKeySnapshot(IReadOnlyList<byte> Keys,uint RawForest,uint RawTemples,uint RawWellAndTraining,uint RawCastle);
public sealed record MqSnapshot(bool SourceAvailable,bool EncodingValid,IReadOnlyList<bool> Flags,string RawBits);
public sealed record EntranceRandomizerSnapshot(byte? OverworldMemoryFlag,byte? DungeonMemoryFlag,string Note);
public sealed record PlayerPositionSnapshot(double X,double Y,double Z,int? Rotation);
public sealed record GameSnapshotAdapterResult(DateTimeOffset CapturedAt,string Provider,SnapshotField<OoTGameStateSnapshot> Game,SnapshotField<byte?> CurrentRoom,SnapshotField<MapSnapshot> Map,SnapshotField<InventorySnapshot> Inventory,SnapshotField<EquipmentSnapshot> Equipment,SnapshotField<UpgradesSnapshot> Upgrades,SnapshotField<QuestItemsSnapshot> QuestItems,SnapshotField<SongsSnapshot> Songs,SnapshotField<DungeonItemsSnapshot> DungeonItems,SnapshotField<DungeonKeySnapshot> DungeonKeys,SnapshotField<IReadOnlyDictionary<string,RuntimeCheckState>> Checks,SnapshotField<MqSnapshot> MasterQuest,SnapshotField<EntranceRandomizerSnapshot> EntranceRandomizer,SnapshotField<OoTR9136RuntimeSnapshot> RandomizerRuntime,SnapshotField<PlayerPositionSnapshot?> PlayerPosition);
