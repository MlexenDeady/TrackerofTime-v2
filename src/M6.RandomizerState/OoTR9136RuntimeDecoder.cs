using System.Text;
using TrackerOfTime.V2.M6.Memory;
namespace TrackerOfTime.V2.M6.RandomizerState;
public enum OoTRLayoutStatus:byte { NotDetected=0, OoTR9136FLum=1 }
public enum BridgeCondition:int { Open=0,Medallions=1,Dungeons=2,Stones=3,Vanilla=4,Tokens=5,Hearts=6 }
public enum LacsCondition:int { Vanilla=0,Medallions=1,Dungeons=2,Stones=3,Tokens=4,Hearts=5 }
public sealed record OoTR9136RuntimeSnapshot(OoTRLayoutStatus Layout,uint LayoutPointer,IReadOnlyList<bool> DungeonIsMq,IReadOnlyList<sbyte> DungeonRewards,BridgeCondition? Bridge,ushort BridgeCountRaw,LacsCondition? Lacs,ushort LacsCountRaw,bool CowShuffle,IReadOnlyList<string> DungeonInfoEntranceNames,string Source);
public sealed class OoTR9136RuntimeDecoder(GoldenRdramReader reader)
{
 public const int LayoutPointerOffset=0x400008; public const uint ExpectedExternContext=0x8042C13C;
 public async Task<OoTR9136RuntimeSnapshot> CaptureAsync(CancellationToken ct=default){uint p=await reader.ReadUInt32Async(LayoutPointerOffset,ct);if(p!=ExpectedExternContext)return Empty(p);byte[] mq=await reader.ReadBytesAsync(0x401E56,14,ct);byte[] rw=await reader.ReadBytesAsync(0x401E48,14,ct);int b=await reader.ReadInt32Async(0x401E64,ct);int l=await reader.ReadInt32Async(0x401E68,ct);ushort bc=await reader.ReadUInt16Async(0x401E6C,ct),lc=await reader.ReadUInt16Async(0x401E6E,ct);byte cow=await reader.ReadByteAsync(0x401DDC,ct);byte[] en=await reader.ReadBytesAsync(0x401F5B,12*9,ct);return new(OoTRLayoutStatus.OoTR9136FLum,p,Array.AsReadOnly(mq.Select(x=>x!=0).ToArray()),Array.AsReadOnly(rw.Select(x=>unchecked((sbyte)x)).ToArray()),Enum.IsDefined(typeof(BridgeCondition),b)?(BridgeCondition)b:null,bc,Enum.IsDefined(typeof(LacsCondition),l)?(LacsCondition)l:null,lc,cow!=0,Array.AsReadOnly(DecodeNames(en)),"OoTR 9.1.36 f.LUM source-fixed runtime layout");}
 static string[] DecodeNames(byte[] x){var a=new string[12];for(int i=0;i<12;i++){var s=x.AsSpan(i*9,9);int n=s.IndexOf((byte)0);if(n<0)n=9;a[i]=Encoding.ASCII.GetString(s[..n]);}return a;}
 static OoTR9136RuntimeSnapshot Empty(uint p)=>new(OoTRLayoutStatus.NotDetected,p,Array.AsReadOnly(new bool[14]),Array.AsReadOnly(Enumerable.Repeat((sbyte)-1,14).ToArray()),null,0,null,0,false,Array.AsReadOnly(new string[12]),"OoTR 9.1.36 f.LUM layout not detected; no version-specific fields dereferenced");
}
