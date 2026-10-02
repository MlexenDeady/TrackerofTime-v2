using TrackerOfTime.V2.M6.GameState;
namespace TrackerOfTime.V2.M6.SnapshotAdapter;

public enum MapRegionKind { Unknown, Dungeon, Overworld, Interior }
public sealed record TrialSnapshot(bool? Forest,bool? Fire,bool? Water,bool? Spirit,bool? Shadow,bool? Light,SnapshotEvidence Evidence,string Source);
public sealed record MapSnapshot(
    int LocationCode,
    byte? Room,
    byte? AreaIndex,
    string AreaCode,
    MapRegionKind RegionKind,
    string? MinimapAsset,
    int LastMinimap,
    int AutoScanIntervalMs,
    int FastScanIntervalMs,
    TrialSnapshot Trials,
    SnapshotEvidence Evidence,
    string Source,
    bool RetainsPreviousMinimap = false);

public static class GoldenReferenceMapCatalog
{
    public static byte? ZoneToMap(int zone, OoTAge age) => zone switch
    {
        0 or 1 => 10, 2 or 3 or 4 or 8 => 16, 5 or 6 => 11, 7 => 6, 9 => 24,
        197 => 0, 10 => age==OoTAge.Adult?(byte)3:age==OoTAge.Young?(byte)2:null,
        198 or 199 => 1, 11 => 4, 12 or 200 => 5, 13 => 20, >=15 and <=17 => 7,
        18 or 19 => 8, 20 or 21 => 21, >=22 and <=28 => 22, >=29 and <=33 => 23,
        >=34 and <=36 => 9, >=37 and <=39 => 13, 40 or 41 => 14, 42 or 43 => 12,
        44 or 45 or 56 or 57 => 15, 46 or 47 => 18, 48 or 49 or 55 => 19, 50 or 54 => 17,
        51 or 53 => 25, >=60 and <=68 => 26, >=69 and <=78 => 27, >=79 and <=87 => 28,
        >=88 and <=107 => 29, >=108 and <=118 => 30, >=119 and <=131 => 31,
        >=132 and <=149 => 32, >=150 and <=173 => 33, >=174 and <=177 => 34,
        178 or >=201 and <=204 => 35, >=179 and <=192 => 36, >=193 and <=196 => 37, _ => null
    };

    public static byte? LocationToArea(int location, OoTAge age) => location switch
    {
        >=0 and <=9 => (byte)(26 + location),
        10 or 13 => 37,
        11 => 36,
        >=27 and <=29 => 0,
        30 or 31 => 1,
        32 or 33 => 2,
        34 => 3,
        35 or 36 or 37 => 4,
        67 => 5,
        >=81 and <=100 => (byte)(location - 75),
        12 => 18,
        _ => null
    };

    // Form1.vb live area grouping. This is deliberately separate from zone2map():
    // scene/location codes and ER zone ids are different Golden-Reference domains.
    public static string LocationToAreaCode(int location) => location switch
    {
        >=0 and <=9 => $"DUN{location}", 11 => "DUN10", 10 or 13 => "DUN11",
        >=17 and <=24 => $"BOSS{location-17}", 25 => "BOSS8", 79 => "BOSS9", 26 => "ESCAPE",
        38 or 39 or 40 or 41 or 45 or 52 or 85 => "KF", 91 => "LW", 86 => "SFM", 81 => "HF",
        54 or 76 or 99 => "LLR", 16 or 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 43 or 50 or 51 or 53 or 75 or 77 => "MK",
        35 or 36 or 37 or 67 => "TT", 69 or 74 or 95 => "HC", 42 or 48 or 55 or 78 or 80 or 82 => "KV",
        58 or 63 or 64 or 65 or 83 => "GY", 96 => "DMT", 97 => "DMC", 46 or 98 => "GC", 84 => "ZR",
        47 or 88 => "ZD", 89 => "ZF", 56 or 73 or 87 => "LH", 57 or 90 => "GV", 12 or 93 => "GF",
        94 => "HW", 92 => "DC", 100 => "OGC", 62 => "GROTTO", 72 => "GRAVE", _ => "OTHER"
    };

    // Only names explicitly evidenced in the inspected Golden Reference are exposed.
    public static string? ProvenLocationName(int location) => location switch
    {
        40 => "Mido's House", 81 => "Hyrule Field", 85 => "Kokiri Forest", _ => null
    };

    public static MapSnapshot Decode(int location, byte? room, OoTAge age)
    {
        int normalizedLocation = location == 12 ? 93 : location;
        // scanER only refreshes iRoom when scene/locationCode <= 9 (dungeons).
        byte? effectiveRoom = location <= 9 ? room : null;
        string? asset = Asset(location, effectiveRoom);
        byte? area = LocationToArea(normalizedLocation, age);
        string areaCode = LocationToAreaCode(location);
        var kind = location is >=0 and <=11 or 13 ? MapRegionKind.Dungeon : areaCode is "GROTTO" or "GRAVE" or "OTHER" ? MapRegionKind.Interior : MapRegionKind.Overworld;
        bool retain = asset is null;
        int last = retain ? 101 : normalizedLocation;
        int slow = last==94 ? 10000 : 5000, fast = last==94 ? 333 : 1000;
        var trials = new TrialSnapshot(null,null,null,null,null,null,SnapshotEvidence.Unknown,"Golden Reference updateTrials depends on checkLoc scan state, not yet represented in GameSnapshot");
        return new(normalizedLocation,effectiveRoom,area,areaCode,kind,asset,last,slow,fast,trials,asset is null?SnapshotEvidence.Partial:SnapshotEvidence.Proven,"minimap.vb + Form1.vb scene/area grouping; unsupported scene keeps previous iLastMinimap",retain);
    }

    public static string? Asset(int l, byte? r) => l switch
    {
        0 when r is 10 or 12=>"mapDT4.png",0 when r is 1 or 2 or 11=>"mapDT3.png",0 when r==0=>"mapDT2.png",0 when r is >=3 and <=8=>"mapDT1.png",0 when r==9=>"mapDT0.png",
        1 when r is 5 or 6 or 9 or 10 or 12 or 16 or 17 or 18=>"mapDDC1.png",1 when r is <=4 or 7 or 8 or 11 or >=13 and <=15=>"mapDDC0.png",
        2 when r is <=2 or >=4 and <=12=>"mapJB1.png",2 when r is 3 or >=13 and <=16=>"mapJB0.png",
        3 when r is 10 or >=12 and <=14 or 19 or 20 or >=23 and <=26=>"mapFoT3.png",3 when r is <=8 or 11 or 15 or 16 or 18 or 21 or 22=>"mapFoT2.png",3 when r==9=>"mapFoT1.png",3 when r==17=>"mapFoT0.png",
        4 when r is 8 or 30 or 34 or 35=>"mapFiT4.png",4 when r is 7 or >=12 and <=14 or 27 or 32 or 33 or 37=>"mapFiT3.png",4 when r is 5 or 9 or 11 or 16 or >=23 and <=26 or 28 or 31=>"mapFiT2.png",4 when r is 4 or 10 or 36=>"mapFiT1.png",4 when r is <=3 or 15 or >=17 and <=22=>"mapFiT0.png",
        5 when r is 0 or 1 or >=4 and <=7 or 10 or 11 or 13 or 17 or 19 or 20 or 30 or 31 or 43=>"mapWaT3.png",5 when r is 22 or 25 or 29 or 32 or 35 or 39 or 41=>"mapWaT2.png",5 when r is 3 or 8 or 9 or 12 or >=14 and <=16 or 18 or 21 or 23 or 24 or 26 or 28 or 33 or 34 or >=36 and <=38 or 40 or 42=>"mapWaT1.png",5 when r is 2 or 27=>"mapWaT0.png",
        6 when r is 22 or >=24 and <=26 or 31=>"mapSpT3.png",6 when r is >=7 and <=11 or >=16 and <=21 or 23 or 29=>"mapSpT2.png",6 when r is 5 or 6 or 28 or 30=>"mapSpT1.png",6 when r is <=4 or >=12 and <=15 or 27=>"mapSpT0.png",
        7 when r is <=2 or 4=>"mapShT3.png",7 when r is >=5 and <=8=>"mapShT2.png",7 when r is 9 or 16 or 22=>"mapShT1.png",7 when r is 3 or >=10 and <=15 or >=17 and <=21 or >=23 and <=26=>"mapShT0.png",
        8 when r is <=6=>"mapBotW2.png",8 when r is 7 or 8=>"mapBotW1.png",8 when r==9=>"mapBotW0.png",9=>"mapIC0.png",10=>"mapGAC0.png",11=>"mapGTG.png",
        // M9 final: the packaged GoldenReference Market-family minimaps existed but the migrated scene selector omitted them.
        // Standard OoT scene ids 27-34 are Market Entrance / Back Alley / Market; 35-37 are the Temple-of-Time exterior.
        27 or 28 or 29=>"mapMKE.png",30 or 31=>"mapMKBA.png",32 or 33=>"mapMK.png",34=>"mapMK2.png",35 or 36 or 37=>"mapOToT.png",
        67=>"mapToT.png",81=>"mapHF.png",82=>"mapKV.png",83=>"mapGY.png",84=>"mapZR.png",85=>"mapKF.png",86=>"mapSFM.png",87=>"mapLH.png",88=>"mapZD.png",89=>"mapZF.png",90=>"mapGV.png",91=>"mapLW.png",92=>"mapDC.png",93 or 12=>"mapGF.png",94=>"mapHW2.png",95=>"mapHC.png",96=>"mapDMT.png",97=>"mapDMC.png",98=>"mapGC.png",99=>"mapLLR.png",100=>"mapOGC.png",_=>null
    };
}
