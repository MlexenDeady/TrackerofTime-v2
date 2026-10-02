using System.Buffers.Binary;
using TrackerOfTime.V2.M6.GameState;
using TrackerOfTime.V2.M6.SnapshotAdapter;
namespace TrackerOfTime.V2.M8_5.Desktop;

// It keeps only V1's session-local visited/exit-map state; frozen tracker semantics stay untouched.
internal sealed class V1EntranceRuntime
{
 readonly bool[] visited=Enumerable.Repeat(true,38).ToArray();
 readonly byte[][] exitMap=CreateDefaultExitMap();
 byte oldMode;
 bool? detectedOverworld,detectedDungeon;
 public async Task<EntranceRandomizerState> CaptureAsync(GameSnapshotAdapterResult snapshot,Func<int,int,Task<byte[]>> read)
 {
  bool ow,dun;
  var raw=snapshot.EntranceRandomizer.Value;
  if(raw.OverworldMemoryFlag.HasValue || raw.DungeonMemoryFlag.HasValue)
  {
   ow=raw.OverworldMemoryFlag is >0; dun=raw.DungeonMemoryFlag is >0;
  }
  else
  {
   if(!detectedOverworld.HasValue || !detectedDungeon.HasValue)
   {
    var detected=await DetectGoldenErRuntimeAsync(snapshot,read);
    detectedOverworld=detected.Overworld; detectedDungeon=detected.Dungeon;
   }
   ow=detectedOverworld==true; dun=detectedDungeon==true;
  }
  byte mode=(byte)((ow?1:0)+(dun?2:0));
  if(mode!=oldMode){if((mode&1)!=0)for(int i=0;i<=25;i++){visited[i]=false;Array.Fill(exitMap[i],(byte)255);}if(mode>1)for(int i=26;i<=37;i++){visited[i]=false;Array.Fill(exitMap[i],(byte)255);}visited[37]=true;oldMode=mode;}
  var scene=snapshot.Map.Value.LocationCode;var mq=snapshot.MasterQuest.Value.Flags;var age=snapshot.Game.Value.Age;
  if(mode==0)return new(0,false,false,null,Array.Empty<GoldenEntranceRead>(),exitMap.Select(x=>x.ToArray()).ToArray(),Array.AsReadOnly(visited),SnapshotEvidence.Proven,"V1 ER disabled");
  var (area,reads)=V1EntranceRandomizerAdapter.ResolveReads(scene,mq,ow,dun);var exits=new List<GoldenEntranceRead>();
  if(area.HasValue){visited[area.Value]=true;foreach(var (slot,address) in reads){var b=await read(address,2);int code=BinaryPrimitives.ReadUInt16LittleEndian(b)&0xFFF;var d=V1EntranceRandomizerAdapter.Decode(code,age);exits.Add(new(slot,address,d));if(d.ReachArea!=255)exitMap[area.Value][slot]=d.ReachArea;}}
  return new(mode,ow,dun,area,exits.AsReadOnly(),exitMap.Select(x=>x.ToArray()).ToArray(),Array.AsReadOnly(visited),area.HasValue?SnapshotEvidence.Proven:SnapshotEvidence.Partial,"V1 minimap ER session adapter");
 }
 async Task<(bool Overworld,bool Dungeon)> DetectGoldenErRuntimeAsync(GameSnapshotAdapterResult snapshot,Func<int,int,Task<byte[]>> read)
 {
  // Exact V1 fallback from TrackerScanService.DetectGoldenErRuntime(): when the
  // revision-specific ER flags are unavailable, compare the live routing tables
  // used by scanER() against V1's vanilla exit map.  This is seed-state evidence,
  // not a guess based on the currently visible scene.
  var vanilla=CreateDefaultExitMap(); var mq=snapshot.MasterQuest.Value.Flags; var age=snapshot.Game.Value.Age;
  async Task<bool> PoolChanged(bool overworld,bool dungeon,int[] scenes)
  {
   foreach(int scene in scenes)
   {
    var (area,reads)=V1EntranceRandomizerAdapter.ResolveReads(scene,mq,overworld,dungeon);
    if(!area.HasValue)continue;
    foreach(var (slot,address) in reads)
    {
     var bytes=await read(address,2); int code=BinaryPrimitives.ReadUInt16LittleEndian(bytes)&0xFFF;
     var dest=V1EntranceRandomizerAdapter.Decode(code,age);
     if(dest.ReachArea==255)continue;
     if(area.Value<vanilla.Length && slot<vanilla[area.Value].Length && vanilla[area.Value][slot]!=dest.ReachArea)return true;
    }
   }
   return false;
  }
  int[] overworldScenes=[27,28,29,30,31,32,33,34,35,36,37,67,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100];
  int[] dungeonScenes=[0,1,2,3,4,5,6,7,8,9,10,11,13,82,83,85,86,87,89,92,93,12,96,97,100];
  return(await PoolChanged(true,false,overworldScenes),await PoolChanged(false,true,dungeonScenes));
 }
 public void Reset(){Array.Fill(visited,true);visited[37]=true;var d=CreateDefaultExitMap();for(int i=0;i<38;i++)Array.Copy(d[i],exitMap[i],7);oldMode=0;detectedOverworld=null;detectedDungeon=null;}
 public static byte[][] CreateDefaultExitMap(){var m=Enumerable.Range(0,38).Select(_=>Enumerable.Repeat((byte)255,7).ToArray()).ToArray();void S(int a,int e,int v)=>m[a][e]=(byte)v;S(0,0,7);S(0,1,10);S(1,0,10);S(1,1,10);S(2,0,13);S(2,1,197);S(2,2,11);S(2,3,199);S(2,4,198);S(3,0,51);S(3,1,197);S(3,2,11);S(4,0,200);S(4,1,10);S(5,0,11);S(6,0,15);S(6,1,4);S(6,2,34);S(6,3,44);S(6,4,197);S(6,5,9);S(6,6,42);S(7,1,20);S(7,2,7);S(7,3,18);S(8,1,15);S(9,0,7);S(9,1,37);S(9,2,2);S(10,1,8);S(10,2,2);S(11,1,3);S(12,1,7);S(12,2,37);S(13,0,40);S(13,1,36);S(13,2,42);S(14,2,38);S(15,0,42);S(15,1,7);S(15,2,46);S(16,0,5);S(16,1,0);S(16,2,35);S(16,3,30);S(16,4,0);S(16,5,7);S(17,1,49);S(18,1,45);S(18,2,48);S(19,0,50);S(19,1,47);S(20,0,10);S(21,1,29);S(21,2,17);S(21,3,22);S(22,1,31);S(22,2,21);S(23,0,25);S(23,1,20);S(23,2,2);S(24,0,7);S(25,0,191);S(25,1,10);S(10,0,60);S(26,0,1);S(21,0,69);S(27,0,20);S(14,0,79);S(28,0,40);S(11,0,88);S(29,0,6);S(22,0,108);S(30,0,28);S(12,0,119);S(31,0,42);S(17,0,132);S(32,0,50);S(8,0,150);S(33,0,19);S(7,0,174);S(34,0,15);S(14,1,178);S(35,0,41);S(18,0,179);S(36,0,46);S(25,0,193);S(37,0,51);S(37,1,196);return m;}
}
