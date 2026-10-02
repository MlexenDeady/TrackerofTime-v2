namespace TrackerOfTime.V2.Randomizer;
public sealed record TrackerSeed(int SchemaVersion,string RandomizerVersion,string Seed,string SeedFingerprint,string SettingsString,int WorldCount,IReadOnlyList<TrackerSeedWorld> Worlds);
public sealed record TrackerSeedWorld(int WorldId,IReadOnlyList<LocationRecord> Locations,IReadOnlyList<RegionRecord> Regions,IReadOnlyList<EntranceRecord> Entrances,IReadOnlyList<string> MqDungeons);
public sealed record LocationRecord(string StableV2Id,string OoTRName,int WorldId,string? RegionId,string? Type,string? TrackerMappingId);
public sealed record RegionRecord(string StableV2Id,string OoTRName,int WorldId,string? TrackerMappingId);
public sealed record EntranceRecord(string StableV2Id,string OoTRName,string? SourceRegion,string? TargetRegion,string? EntranceType,bool Shuffled,string? TrackerMappingId);
