using TrackerOfTime.V2.M6.GameState;
using TrackerOfTime.V2.M6.SnapshotAdapter;
namespace TrackerOfTime.V2.M8_5.Desktop;

public sealed record GoldenEntranceDestination(int ExitCode, string Label, byte ReachArea, byte VisitedMap);
public sealed record GoldenEntranceRead(int Slot, int Address, GoldenEntranceDestination? Destination);
public sealed record EntranceRandomizerState(byte Mode, bool OverworldEnabled, bool DungeonEnabled, byte? LocationArray, IReadOnlyList<GoldenEntranceRead> Exits, byte[][] ExitMap, IReadOnlyList<bool> Visited, SnapshotEvidence Evidence, string Source);

/// <summary>Direct data/behavior port of Golden minimap.vb: erExitArray/getER/scanER/exit2label/zone2map. No UI policy.</summary>
public static class V1EntranceRandomizerAdapter
{
    private sealed record D(string Label, byte Reach);
    private static readonly IReadOnlyDictionary<int,D> Destinations = new Dictionary<int,D>
    {
        [0x09C] = new("KF", 0), 
        [0x0BB] = new("KF", 0), 
        [0x0C1] = new("KF", 0), 
        [0x0C9] = new("KF", 0), 
        [0x209] = new("KF", 0), 
        [0x211] = new("KF", 0), 
        [0x266] = new("KF", 0), 
        [0x26A] = new("KF", 0), 
        [0x272] = new("KF", 0), 
        [0x286] = new("KF", 0), 
        [0x33C] = new("KF", 0), 
        [0x433] = new("KF", 0), 
        [0x437] = new("KF", 0), 
        [0x443] = new("KF", 0), 
        [0x447] = new("KF", 0), 
        [0x457] = new("KF", 0), 
        [0x20D] = new("KF", 0), 
        [0x11E] = new("LW Front", 2), 
        [0x4D6] = new("LW Front", 2), 
        [0x4DA] = new("LW Front", 2), 
        [0x1A9] = new("LW Back", 3), 
        [0x4DE] = new("LW Bridge", 4), 
        [0x5E0] = new("LW Bridge", 8), 
        [0x0FC] = new("SFM", 5), 
        [0x215] = new("SFM", 5), 
        [0x600] = new("SFM", 5), 
        [0x608] = new("SFM", 5), 
        [0x17D] = new("HF", 7), 
        [0x181] = new("HF", 7), 
        [0x185] = new("HF", 7), 
        [0x189] = new("HF", 7), 
        [0x18D] = new("HF", 7), 
        [0x1F9] = new("HF", 7), 
        [0x1FD] = new("HF", 7), 
        [0x27E] = new("HF", 7), 
        [0x311] = new("HF", 7), 
        [0x04F] = new("LLR", 9), 
        [0x157] = new("LLR", 9), 
        [0x2F9] = new("LLR", 9), 
        [0x378] = new("LLR", 9), 
        [0x42F] = new("LLR", 9), 
        [0x5D0] = new("LLR", 9), 
        [0x5D4] = new("LLR", 9), 
        [0x033] = new("MK Entrance", 197), 
        [0x034] = new("MK Entrance", 197), 
        [0x035] = new("MK Entrance", 197), 
        [0x036] = new("MK Entrance", 197), 
        [0x26E] = new("MK Entrance", 197), 
        [0x26F] = new("MK Entrance", 197), 
        [0x270] = new("MK Entrance", 197), 
        [0x271] = new("MK Entrance", 197), 
        [0x276] = new("MK Entrance", 197), 
        [0x277] = new("MK Entrance", 197), 
        [0x278] = new("MK Entrance", 197), 
        [0x279] = new("MK Entrance", 197), 
        [0x063] = new("MK", 10), 
        [0x067] = new("MK", 10), 
        [0x07E] = new("MK", 10), 
        [0x0B1] = new("MK", 10), 
        [0x16D] = new("MK", 10), 
        [0x1CD] = new("MK", 10), 
        [0x1D1] = new("MK", 10), 
        [0x1D5] = new("MK", 10), 
        [0x25A] = new("MK", 10), 
        [0x25E] = new("MK", 10), 
        [0x262] = new("MK", 10), 
        [0x263] = new("MK", 10), 
        [0x29E] = new("MK", 10), 
        [0x29F] = new("MK", 10), 
        [0x2A2] = new("MK", 10), 
        [0x388] = new("MK", 10), 
        [0x29A] = new("MK Back Alley", 198), 
        [0x29B] = new("MK Back Alley", 198), 
        [0x29C] = new("MK Back Alley", 198), 
        [0x29D] = new("MK Back Alley", 198), 
        [0x0AD] = new("MK Back Alley", 199), 
        [0x0AE] = new("MK Back Alley", 199), 
        [0x0AF] = new("MK Back Alley", 199), 
        [0x0B0] = new("MK Back Alley", 199), 
        [0x171] = new("Outside ToT", 1), 
        [0x172] = new("Outside ToT", 1), 
        [0x472] = new("Outside ToT", 1), 
        [0x053] = new("ToT", 200), 
        [0x054] = new("ToT", 200), 
        [0x5F4] = new("ToT", 200), 
        [0x138] = new("OGC", 51), 
        [0x23D] = new("OGC", 51), 
        [0x340] = new("OGC", 51), 
        [0x07A] = new("Castle Courtyard", 13), 
        [0x296] = new("Castle Courtyard", 13), 
        [0x400] = new("Zelda's Courtyard", 13), 
        [0x5F0] = new("Zelda's Courtyard", 13), 
        [0x03B] = new("KV", 15), 
        [0x072] = new("KV", 15), 
        [0x0B7] = new("KV", 15), 
        [0x0DB] = new("KV", 15), 
        [0x195] = new("KV", 15), 
        [0x201] = new("KV", 15), 
        [0x2FD] = new("KV", 15), 
        [0x2A6] = new("KV", 15), 
        [0x345] = new("KV", 15), 
        [0x349] = new("KV", 15), 
        [0x34D] = new("KV", 15), 
        [0x351] = new("KV", 15), 
        [0x384] = new("KV", 15), 
        [0x39C] = new("KV", 15), 
        [0x3EC] = new("KV", 15), 
        [0x44B] = new("KV", 15), 
        [0x554] = new("KV Roofs", 16), 
        [0x191] = new("KV", 17), 
        [0x0E4] = new("GY", 18), 
        [0x30D] = new("GY", 18), 
        [0x355] = new("GY", 18), 
        [0x205] = new("GY near Temple", 19), 
        [0x568] = new("GY near Temple", 19), 
        [0x580] = new("GY near Temple", 19), 
        [0x13D] = new("DMT Lower", 20), 
        [0x1B9] = new("DMT Lower", 20), 
        [0x242] = new("DMT Lower", 20), 
        [0x47A] = new("DMT Lower", 20), 
        [0x1BD] = new("DMT Upper", 21), 
        [0x315] = new("DMT Upper", 21), 
        [0x45B] = new("DMT Upper", 21), 
        [0x147] = new("DMC Upper", 22), 
        [0x246] = new("DMC near GC", 24), 
        [0x482] = new("DMC near GC", 24), 
        [0x4BE] = new("DMC near GC", 24), 
        [0x4F6] = new("DMC Warp", 27), 
        [0x564] = new("DMC Warp", 27), 
        [0x24A] = new("DMC near Temple", 28), 
        [0x14D] = new("GC", 29), 
        [0x3FC] = new("GC", 29), 
        [0x4E2] = new("GC", 30), 
        [0x1C1] = new("GC Darunia", 31), 
        [0x37C] = new("GC Shop", 32), 
        [0x0EA] = new("ZR Front", 34), 
        [0x199] = new("ZR", 35), 
        [0x1DD] = new("ZR", 35), 
        [0x19D] = new("ZR", 36), 
        [0x108] = new("ZD", 37), 
        [0x153] = new("ZD", 37), 
        [0x328] = new("ZD", 37), 
        [0x3C4] = new("ZD", 37), 
        [0x1A1] = new("ZD Behind King", 38), 
        [0x10E] = new("ZF", 40), 
        [0x221] = new("ZF", 40), 
        [0x225] = new("ZF", 40), 
        [0x371] = new("ZF", 40), 
        [0x394] = new("ZF", 40), 
        [0x3D4] = new("ZF Ledge", 41), 
        [0x043] = new("LH", 42), 
        [0x102] = new("LH", 42), 
        [0x219] = new("LH", 42), 
        [0x21D] = new("LH", 42), 
        [0x3CC] = new("LH", 42), 
        [0x560] = new("LH", 42), 
        [0x604] = new("LH", 42), 
        [0x60C] = new("LH", 42), 
        [0x309] = new("LH", 43), 
        [0x45F] = new("LH", 43), 
        [0x117] = new("GV Hyrule Side", 44), 
        [0x22D] = new("GV Gerudo Side", 45), 
        [0x3A0] = new("GV Gerudo Side", 45), 
        [0x3D0] = new("GV Gerudo Side", 45), 
        [0x129] = new("GF", 46), 
        [0x3A8] = new("GF", 46), 
        [0x3AC] = new("GF Behind Gate", 47), 
        [0x130] = new("HW Gerudo Side", 48), 
        [0x365] = new("HW Colossus Side", 49), 
        [0x123] = new("DC", 50), 
        [0x1F1] = new("DC", 50), 
        [0x1E1] = new("DC", 50), 
        [0x57C] = new("DC", 50), 
        [0x588] = new("DC", 50), 
        [0x610] = new("DC", 50), 
        [0x1E5] = new("DC Statue Hand Right", 50), 
        [0x1E9] = new("DC Statue Hand left", 50), 
        [0x4C2] = new("OGC Fairy", 51), 
        [0x578] = new("HC Fairy", 13), 
        [0x000] = new("Deku Tree", 60), 
        [0x40F] = new("Queen Gohma", 255), 
        [0x004] = new("Dodongo's Cavern", 69), 
        [0x40B] = new("King Dodongo", 255), 
        [0x028] = new("Jabu-Jabu's Belly", 79), 
        [0x301] = new("Barinade", 255), 
        [0x169] = new("Forest Temple", 88), 
        [0x00C] = new("Phantom Ganon", 255), 
        [0x165] = new("Fire Temple", 108), 
        [0x305] = new("Volvagia", 255), 
        [0x010] = new("Water Temple", 119), 
        [0x417] = new("Morpha", 255), 
        [0x082] = new("Spirit Temple", 132), 
        [0x3F0] = new("Spirit Temple", 132), 
        [0x3F4] = new("Spirit Temple", 132), 
        [0x08D] = new("Twinrova", 255), 
        [0x037] = new("Shadow Temple", 150), 
        [0x413] = new("Bongo Bongo", 255), 
        [0x098] = new("BotW", 174), 
        [0x088] = new("Ice Cavern", 178), 
        [0x008] = new("GTG", 179), 
        [0x467] = new("Ganon's Castle", 255), 
        [0x534] = new("Ganon's Castle", 255), 
        [0x41B] = new("Ganon's Tower", 255), 
        [0x41F] = new("Ganondorf", 255),     };

    public static GoldenEntranceDestination Decode(int exitCode, OoTAge age)
    {
        exitCode &= 0xFFF;
        if (exitCode is 0x138 or 0x23D or 0x340)
            return age == OoTAge.Adult ? new(exitCode,"OGC",51,ZoneToMap(51,age)) : new(exitCode,"HC",13,ZoneToMap(13,age));
        if (!Destinations.TryGetValue(exitCode,out var d)) return new(exitCode,$"{exitCode:X3}?",255,255);
        return new(exitCode,d.Label,d.Reach,ZoneToMap(d.Reach,age));
    }

    public static byte ZoneToMap(byte z, OoTAge age) => z switch
    {
        0 or 1=>10, >=2 and <=4 or 8=>16, 5 or 6=>11, 7=>6, 9=>24, 197=>0,
        10=>age==OoTAge.Adult?(byte)3:(byte)2, 198 or 199=>1, 11=>4, 12 or 200=>5, 13=>20,
        >=15 and <=17=>7, 18 or 19=>8, 20 or 21=>21, >=22 and <=28=>22, >=29 and <=33=>23,
        >=34 and <=36=>9, >=37 and <=39=>13, 40 or 41=>14, 42 or 43=>12, 44 or 45 or 56 or 57=>15,
        46 or 47=>18, 48 or 49 or 55=>19, 50 or 54=>17, 51 or 53=>25,
        >=60 and <=68=>26, >=69 and <=78=>27, >=79 and <=87=>28, >=88 and <=107=>29,
        >=108 and <=118=>30, >=119 and <=131=>31, >=132 and <=149=>32, >=150 and <=173=>33,
        >=174 and <=177=>34, 178 or >=201 and <=204=>35, >=179 and <=192=>36, >=193 and <=196=>37, _=>255
    };

    public static (byte? LocationArray, IReadOnlyList<(int Slot,int Address)> Reads) ResolveReads(int location, IReadOnlyList<bool> mq, bool overworld, bool dungeon)
    {
        var x=new Dictionary<int,int>(); byte? area=null; int ent=0;
        bool MQ(int i)=>i<mq.Count&&mq[i]; void S(int slot,int addr){x[slot]=addr;}
        if(dungeon)
        {
            switch(location)
            {
                case 0:S(0,MQ(0)?0x3770B6:0x377116);S(1,0x377114);area=26;break;
                case 1:S(0,MQ(1)?0x36FA8E:0x36FABE);S(1,0x36FABC);area=27;break;
                case 2:S(0,MQ(2)?0x36F40E:0x36F43E);S(1,0x36F43C);area=28;break;
                case 3:S(0,0x36ED12);S(1,0x36ED10);area=29;break;
                case 4:S(0,MQ(4)?0x36A376:0x36A3C6);S(1,0x36A3C4);area=30;break;
                case 5:S(0,MQ(5)?0x36EF36:0x36EF76);S(1,0x36EF74);area=31;break;
                case 6:S(0,MQ(6)?0x36B12A:0x36B1EA);S(1,0x36B1E8);area=32;break;
                case 7:S(0,0x36C86E);S(1,0x36C86C);area=33;break;
                case 8:S(0,MQ(8)?0x378566:0x3785C6);area=34;break;
                case 9:S(0,MQ(9)?0x37344E:0x37352E);area=35;break;
                case 10:S(1,0x37434A);break;
                case 11:S(0,0x373686);area=36;break;
                case 13:S(0,0x3634BE);area=37;break;
            }
            switch(location)
            {
                case 82:S(0,0x368A82);ent=1;area=7;break; case 83:S(0,0x378E44);ent=1;area=8;break;
                case 85:S(0,0x3738F4);ent=1;area=10;break; case 86:S(0,0x36FCD4);ent=1;area=11;break;
                case 87:S(0,0x3696A6);ent=1;area=12;break; case 89:S(0,0x3733CC);S(1,0x3733D0);ent=2;area=14;break;
                case 92:S(0,0x36B5AC);ent=1;area=17;break; case 93: case 12:S(0,0x374D02);ent=1;area=18;break;
                case 96:S(0,0x365FF0);ent=1;area=21;break; case 97:S(0,0x374B9E);ent=1;area=22;break;
                case 100:S(0,0x37FEC0);ent=1;area=25;break;
            }
        }
        // Golden scanER() reserves lPoints/readExits slots for an overworld scene's
        // dungeon entrance even when Dungeon ER itself is disabled.  The read address is
        // only populated when isDungeon=True, but `ent` is still advanced before the
        // Overworld ER exits are appended.  Preserve that slot numbering here.
        if(!dungeon)
        {
            ent = location switch
            {
                89 => 2,
                82 or 83 or 85 or 86 or 87 or 92 or 93 or 12 or 96 or 97 or 100 => 1,
                _ => 0
            };
        }
        if(overworld)
        {
            switch(location)
            {
                case >=27 and <=29:S(0,0x384650-(location==28?0x48:0));S(1,0x384652-(location==28?0x48:0));area=0;break;
                case 30: case 31:S(0,0x383824-(location==31?0x98:0));S(1,0x383826-(location==31?0x98:0));area=1;break;
                case 32: case 33:{int d=location==33?0x40:0;S(0,0x3824A8+d);S(1,0x3824AA+d);S(2,0x3824AE+d);S(3,0x3824AC+d);S(4,0x3824B2+d);area=2;break;}
                case 34:S(0,0x383478);S(1,0x38347A);S(2,0x38347E);area=3;break;
                case 35: case 36:{int d=location==36?0x18:0;S(0,0x383524-d);S(1,0x383526-d);area=4;break;}
                case 37:S(0,0x383574);S(1,0x383576);area=4;break; case 67:S(0,0x372332);area=5;break;
                case 81:S(0,0x36BF9C);S(1,0x36BFA0);S(2,0x36BFA2);S(3,0x36BFA4);S(4,0x36BFA8);S(5,0x36BFAA);S(6,0x36BFAE);area=6;break;
                case 82:S(ent++,0x368A78);S(ent++,0x368A7A);S(ent,0x368A7E);area=7;break;
                case 83:S(ent,0x378E46);area=8;break; case 84:S(0,0x37951C);S(1,0x37951E);S(2,0x379522);area=9;break;
                case 85:S(ent++,0x3738FA);S(ent,0x373902);area=10;break; case 86:S(ent,0x36FCD6);area=11;break;
                case 87:S(ent++,0x3696A2);S(ent,0x3696AE);area=12;break; case 88:S(0,0x37B26C);S(1,0x37B26E);S(2,0x37B270);area=13;break;
                case 89:S(ent,0x3733D2);area=14;break; case 90:S(0,0x373904);S(1,0x373906);S(2,0x373908);area=15;break;
                case 91:S(0,0x37475C);S(1,0x37475E);S(2,0x374768);S(3,0x37476A);S(4,0x37476C);S(5,0x37476E);area=16;break;
                case 92:S(ent,0x36B5AE);area=17;break; case 93:S(ent++,0x374CE6);S(ent,0x374D00);area=18;break;
                case 94:S(0,0x37EBFC);S(1,0x37EBFE);area=19;break; case 95:S(0,0x36C55E);area=20;break;
                case 96:S(ent++,0x365FEC);S(ent++,0x365FEE);S(ent,0x365FF2);area=21;break;
                case 97:S(ent++,0x374B98);S(ent,0x374B9A);area=22;break; case 98:S(0,0x37A64C);S(1,0x37A64E);S(2,0x37A650);area=23;break;
                case 99:S(0,0x377C12);area=24;break; case 100:S(ent,0x37FEC2);area=25;break;
            }
        }
        return (area,x.OrderBy(k=>k.Key).Select(k=>(k.Key,k.Value)).ToArray());
    }

    public static EntranceRandomizerState Capture(int location, OoTAge age, IReadOnlyList<bool> mq, bool overworld, bool dungeon, Func<int,int> readExit, byte[][]? previousExitMap=null, IReadOnlyList<bool>? previousVisited=null)
    {
        byte mode=(byte)((overworld?1:0)+(dungeon?2:0));
        var exitMap=previousExitMap is null?V1EntranceRuntime.CreateDefaultExitMap():previousExitMap.Select(a=>a.ToArray()).ToArray();
        var visited=previousVisited?.ToArray()??Enumerable.Repeat(true,38).ToArray();
        if(mode==0) return new(mode,false,false,null,Array.Empty<GoldenEntranceRead>(),exitMap,visited,SnapshotEvidence.Proven,"ER disabled");
        var (area,reads)=ResolveReads(location,mq,overworld,dungeon); var result=new List<GoldenEntranceRead>();
        if(area.HasValue){visited[area.Value]=true; foreach(var (slot,address) in reads){int code=readExit(address)&0xFFF;var d=Decode(code,age);result.Add(new(slot,address,d));if(d.ReachArea!=255)exitMap[area.Value][slot]=d.ReachArea;}}
        return new(mode,overworld,dungeon,area,result.AsReadOnly(),exitMap,Array.AsReadOnly(visited),area.HasValue?SnapshotEvidence.Proven:SnapshotEvidence.Partial,"V1 minimap ER");
    }
}
