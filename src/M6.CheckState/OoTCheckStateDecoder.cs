using TrackerOfTime.V2.M6.Memory;

namespace TrackerOfTime.V2.M6.CheckState;

public enum CheckKnowledge : byte { Unknown=0, Unchecked=1, Checked=2 }
public sealed record CheckState(string LocationId, CheckKnowledge State, bool Forced=false);
public sealed record CheckScannerSettings(int? SkulltulaMode, byte GoldSkulltulas, int? GoldSkulltulaLocationMode, bool? ScrubShuffle);
public sealed record CheckInventoryMirror(IReadOnlyList<bool> Equipment,IReadOnlyList<bool> Upgrades,IReadOnlyList<bool> QuestItems,bool KnifeCheck);
public sealed record CheckStateSnapshot(IReadOnlyDictionary<string,CheckState> Checks,IReadOnlyList<long> RawGroups,CheckInventoryMirror InventoryMirror,string Source);

/// <summary>M6.4 N64-only reconstruction of V1/M10 readChestData/parseChestData/checkLoc + scanSingleChecks memory-backed check state.</summary>
public sealed class OoTCheckStateDecoder(GoldenRdramReader reader)
{
    private readonly Dictionary<string,bool> checkedState=new(StringComparer.Ordinal);
    private readonly HashSet<string> knownState=new(StringComparer.Ordinal);
    private readonly long[] rawGroups=new long[126];
    private readonly bool[] equipment=new bool[32],upgrades=new bool[32],questItems=new bool[32];
    private bool knifeCheck;

    public async Task<CheckStateSnapshot> CaptureAsync(short locationCode,CheckScannerSettings settings,CancellationToken ct=default)
    {
        for(int i=0;i<118;i++)
        {
            bool checkAgain=true;
            if(i<=59 || i>=100)
            {
                long doMath=locationCode*28L+0x11A6A4; int dynamicAddress=0x1CA1D8;
                if(i<=2 || i is >=103 and <=113 || i is >=115 and <=117){doMath+=4;dynamicAddress-=16;}
                else if(i is >=3 and <=30 || i is >=100 and <=102 || i==114){doMath+=12;dynamicAddress+=12;}
                if(doMath==GoldenCheckData.N64[i]){checkAgain=false;ApplyParse(i,await ReadRawAsync(dynamicAddress,GoldenCheckData.ArrHigh[i],ct));}
            }
            else if(i is >=78 and <=83)
                checkAgain=settings.SkulltulaMode switch {0=>false,1=>true,>1=>settings.GoldSkulltulas<50,_=>false};
            else if(i is >=84 and <=98)
                checkAgain=settings.ScrubShuffle==true;
            if(checkAgain) ApplyParse(i,await ReadRawAsync(GoldenCheckData.N64[i],GoldenCheckData.ArrHigh[i],ct));
        }
        await ScanSingleChecksAsync(settings,ct);
        return Snapshot();
    }

    private async Task ScanSingleChecksAsync(CheckScannerSettings s,CancellationToken ct)
    {
        if(s.SkulltulaMode>0 && s.GoldSkulltulaLocationMode>=1) SetLoc("B0",GoldenCheckBit(await reader.ReadInt32Async(0x11B09C,ct),22));
        SetLoc("B1",GoldenCheckBit(await reader.ReadInt32Async(0x11B0B8,ct),24));
        SetLoc("B2",GoldenCheckBit(await reader.ReadInt32Async(0x11B128,ct),6));
        SetLoc("B3",GoldenCheckBit(await reader.ReadInt32Async(0x11B144,ct),3));
        SetLoc("B4",GoldenCheckBit(await reader.ReadInt32Async(0x11AFBC,ct),3));
        SetLoc("C00",GoldenCheckBit(await reader.ReadInt32Async(0x11B0D4,ct),3));
        if(s.ScrubShuffle==true) SetLoc("C01",GoldenCheckBit(await reader.ReadInt32Async(0x11B150,ct),6));
        int c2c4=await reader.ReadInt32Async(0x11B4B0,ct); SetLoc("C02",GoldenCheckBit(c2c4,23)); SetLoc("C04",GoldenCheckBit(c2c4,25));
        SetLoc("C05",GoldenCheckBit(await reader.ReadInt32Async(0x11B4D4,ct),6));
    }

    private async Task<long> ReadRawAsync(int address,int high,CancellationToken ct)
        => high<=7 ? await reader.ReadByteAsync(address,ct) : high<=15 ? await reader.ReadInt16Async(address,ct) : await reader.ReadInt32Async(address,ct);

    private void ApplyParse(int loc,long raw)
    {
        rawGroups[loc]=raw; int high=GoldenCheckData.ArrHigh[loc]; int start=high<=7?7:high<=15?15:31; ulong compare=start==7?0x80UL:start==15?0x8000UL:0x80000000UL;
        ulong value=raw<0 ? unchecked((ulong)(raw+(long)(compare*2))) : (ulong)raw;
        for(int bit=start;bit>=0;bit--){bool hit=value>=compare && value>0;if(hit)value-=compare;string id=loc.ToString()+bit.ToString("00");if(GoldenCheckData.Known.Contains(id))SetLoc(id,hit);compare/=2;}
    }

    private void SetLoc(string id,bool value)
    {
        if(!GoldenCheckData.Known.Contains(id))return; checkedState[id]=value;knownState.Add(id);
        if(id is "6500" or "6501" or "6502" or "6503"){bool all=Check("6500")&&Check("6501")&&Check("6502")&&Check("6503");if(GoldenCheckData.Known.Contains("CARD")){checkedState["CARD"]=all;knownState.Add("CARD");}}
        if(GoldenCheckData.Inventory.Contains(id))MirrorInventory(id,value);
    }
    private void MirrorInventory(string id,bool value){if(id=="7508"){knifeCheck=value;return;}if(id.Length<4||!int.TryParse(id[2..],out int bit)||bit<0||bit>31)return;switch(id[..2]){case "74":equipment[bit]=value;break;case "76":upgrades[bit]=value;break;case "77":questItems[bit]=value;break;}}
    private bool Check(string id)=>knownState.Contains(id)&&checkedState.TryGetValue(id,out bool v)&&v;
    public CheckStateSnapshot Snapshot()
    {
        var d=new Dictionary<string,CheckState>(StringComparer.Ordinal);foreach(string id in GoldenCheckData.Known){var st=!knownState.Contains(id)?CheckKnowledge.Unknown:Check(id)?CheckKnowledge.Checked:CheckKnowledge.Unchecked;bool forced=id=="6512"&&Check("6512")&&!Check("7316");d[id]=new(id,st,forced);}
        return new(d,Array.AsReadOnly((long[])rawGroups.Clone()),new(Array.AsReadOnly((bool[])equipment.Clone()),Array.AsReadOnly((bool[])upgrades.Clone()),Array.AsReadOnly((bool[])questItems.Clone()),knifeCheck),"V1/M10 readChestData/parseChestData/checkLoc + N64 scanSingleChecks");
    }
    public static bool GoldenCheckBit(long word,byte bit){uint v=unchecked((uint)word);int nib=(int)((v>>(4*(bit/4)))&0xF);return (bit%4) switch{0=>nib is 1 or 3 or 5 or 7 or 9 or 11 or 13,1=>nib is 2 or 3 or 6 or 7 or 10 or 11 or 14 or 15,2=>nib is >=4 and <=7 or >=12 and <=15,3=>nib is >=8 and <=15,_=>false};}
}

internal static class GoldenCheckData
{
    internal static readonly int[] N64={
        0x11AD1C,0x11AF84,0x11B02C,0x11A720,0x11A73C,0x11A774,0x11A790,0x11A7AC,
        0x11A7E4,0x11A81C,0x11A88C,0x11A8A8,0x11A8C4,0x11A8E0,0x11A8FC,0x11A918,
        0x11A934,0x11A950,0x11ACB4,0x11AD78,0x11AE90,0x11AF00,0x11AFC4,0x11AFE0,
        0x11B034,0x11B06C,0x11B088,0x11B0C0,0x11B130,0x11B14C,0x11B168,0x11A6A4,
        0x11A6C0,0x11A6DC,0x11A6F8,0x11A714,0x11A730,0x11A74C,0x11A768,0x11A784,
        0x11A7A0,0x11A7BC,0x11A7D8,0x11A810,0x11A89C,0x11AB04,0x11AD6C,0x11AD88,
        0x11ADA4,0x11ADC0,0x11AE84,0x11AFF0,0x11B028,0x11B044,0x11B07C,0x11B0B4,
        0x11B0D0,0x11B0EC,0x11B124,0x11B15C,0x11A640,0x11B490,0x11B4A4,0x11B4A8,
        0x11B4AC,0x11B4B4,0x11B4B8,0x11B4BC,0x11B4C0,0x11B4C4,0x11B4E8,0x11B4EC,
        0x11B4F8,0x11B894,0x11A66C,0x11A60C,0x11A670,0x11A674,0x11B46C,0x11B470,
        0x11B474,0x11B478,0x11B47C,0x11B480,0x11A6B4,0x11A6D0,0x11A6EC,0x11A820,
        0x11A874,0x11A900,0x11A954,0x11A970,0x11A98C,0x11AA18,0x11AA88,0x11AAC0,
        0x11AADC,0x11AAF8,0x11B0A8,0x11AB84,0x11AC60,0x11AC98,0x11A6E8,0x11A6A8,
        0x11A6C4,0x11A6E0,0x11A6FC,0x11A718,0x11A734,0x11A750,0x11A76C,0x11A7A4,
        0x11A7DC,0x11A814,0x11B0F8,0x11B160,0x11AFD8,0x11A788,0x11B178,0x11B00C,
        0x11ADDC,0x11AD50,0x11AD18,0x11ADF8,0x11AF80,0x11AB9C};
    internal static readonly byte[] ArrHigh={
        24,31,31,28,1,6,2,1,1,1,31,31,31,31,31,31,
        31,31,24,25,7,25,8,11,30,20,24,13,30,8,31,6,
        10,10,15,13,10,31,22,20,2,11,20,20,0,3,26,0,
        0,0,0,0,0,0,0,11,0,0,1,2,8,11,26,8,
        29,12,29,20,31,28,25,9,3,16,30,8,15,22,28,28,
        27,31,27,11,5,8,1,9,3,9,9,6,9,11,6,6,
        6,9,10,0,24,25,24,16,26,29,31,31,21,30,29,0,
        29,30,1,28,9,29,0,0,0,0,0,0,0,0};
    internal static readonly HashSet<string> Known=new(StringComparer.Ordinal){
        "008","016","024","10024","10124","10125","10224","103","1031","10316",
        "10410","10426","10529","10600","10601","10602","10603","10604","10606","10620",
        "10628","10629","10630","10631","10710","10718","10720","10723","10724","10725",
        "10726","10727","10729","10730","10731","10801","10802","10805","10806","10809",
        "10820","10821","10901","10903","10904","10909","10911","10912","10913","10918",
        "10920","10921","10923","10927","10928","10930","11016","11020","11021","11022",
        "11023","11024","11025","11027","11029","11201","11203","11204","11205","11206",
        "11207","11209","11210","11220","11223","11229","11304","1131","11330","11401",
        "11501","11508","11511","11512","11527","11528","11602","11608","11609","11720",
        "11721","11727","11728","11729","122","123","1231","124","125","126",
        "127","128","129","130","131","1331","1431","1531","1631","1731",
        "1801","1824","1901","1924","1925","2001","2007","202","2101","211",
        "2124","2125","2204","2208","2301","2304","231","2311","2430","2501",
        "2520","2601","2602","2624","2713","2830","2902","2908","3001","3031",
        "3100","3101","3102","3103","3104","3105","3106","3200","3201","3202",
        "3203","3204","3205","3206","3208","3210","328","3300","3301","3302",
        "3303","3304","3305","3306","3307","3308","3309","3310","3400","3401",
        "3402","3403","3404","3405","3406","3407","3409","3411","3412","3413",
        "3414","3415","3500","3501","3502","3503","3504","3505","3506","3507",
        "3508","3509","3510","3511","3512","3513","3600","3601","3602","3603",
        "3605","3606","3607","3608","3609","3610","3700","3701","3702","3703",
        "3704","3705","3706","3707","3708","3710","3712","3713","3714","3715",
        "3718","3720","3721","3724","3725","3726","3727","3728","3729","3730",
        "3731","3801","3802","3803","3804","3805","3806","3807","3808","3809",
        "3810","3811","3812","3813","3814","3815","3816","3820","3821","3822",
        "3901","3902","3903","3904","3905","3907","3908","3909","3910","3912",
        "3914","3916","3920","4000","4001","4002","401","4111","4200","4201",
        "4202","4203","4204","4205","4206","4207","4208","4209","4210","4211",
        "4212","4213","4214","4215","4216","4217","4218","4219","4220","4300",
        "4301","4302","4303","4304","4305","4306","4307","4308","4309","4310",
        "4311","4312","4313","4314","4315","4316","4317","4318","4320","4400",
        "4500","4501","4502","4503","4600","4602","4603","4608","4609","4610",
        "4612","4617","4620","4623","4626","4700","4800","4900","5000","501",
        "506","5100","5200","5300","5400","5509","5511","5600","5700","5801",
        "5900","5901","5902","6008","601","602","6110","6111","6202","6204",
        "6208","6214","6220","6223","6226","6303","6306","6308","6400","6401",
        "6402","6404","6405","6407","6408","6409","6410","6411","6416","6427",
        "6429","6500","6501","6502","6503","6512","6611","6612","6613","6614",
        "6615","6625","6628","6629","6700","6701","6702","6703","6704","6705",
        "6706","6710","6711","6712","6713","6714","6717","6719","6720","6800",
        "6801","6802","6805","6806","6807","6808","6809","6810","6811","6813",
        "6814","6815","6818","6827","6828","6829","6830","6831","6908","6909",
        "6910","6911","6928","701","7014","7025","7109","7200","7201","7202",
        "7203","7316","7416","7417","7418","7419","7420","7421","7422","7424",
        "7425","7426","7428","7429","7430","7508","7600","7601","7603","7604",
        "7606","7607","7609","7610","7612","7613","7614","7615","7700","7701",
        "7702","7703","7704","7705","7706","7707","7708","7709","7710","7711",
        "7712","7713","7714","7715","7716","7717","7718","7719","7720","7721",
        "7722","7800","7801","7802","7803","7808","7809","7810","7811","7812",
        "7816","7817","7818","7819","7824","7825","7826","7827","7828","7900",
        "7901","7902","7903","7904","7908","7909","7910","7911","7912","7916",
        "7917","7918","7919","7920","7924","7925","7926","7927","7928","8000",
        "8001","8002","8008","8009","801","8010","8016","8017","8024","8025",
        "8026","8027","8100","8101","8102","8108","8109","8110","8111","8116",
        "8117","8118","8119","8124","8125","8126","8127","8128","8129","8130",
        "8131","8200","8201","8202","8203","8204","8205","8206","8207","8208",
        "8209","8210","8211","8212","8213","8214","8215","8216","8217","8218",
        "8219","8220","8224","8225","8226","8227","8300","8301","8308","8309",
        "8310","8311","8405","8501","8502","8504","8505","8508","8601","8701",
        "8704","8706","8708","8709","8803","8908","8909","9008","9009","901",
        "9101","9104","9106","9208","9209","9304","9311","9401","9404","9406",
        "9501","9504","9506","9601","9604","9606","9708","9709","9801","9802",
        "9810","B0","B1","B2","B3","B4","C00","C01","C02","C04",
        "C05","CARD"};
    internal static readonly HashSet<string> Inventory=new(StringComparer.Ordinal){
        "7416","7417","7418","7419","7420","7421","7422","7424","7425","7426",
        "7428","7429","7430","7508","7600","7601","7603","7604","7606","7607",
        "7609","7610","7612","7613","7614","7615","7700","7701","7702","7703",
        "7704","7705","7706","7707","7708","7709","7710","7711","7712","7713",
        "7714","7715","7716","7717","7718","7719","7720","7721","7722"};
}
