using TrackerOfTime.V2.M6.GameState;
using TrackerOfTime.V2.M6.SnapshotAdapter;
namespace TrackerOfTime.V2.M8_5.Desktop;

public enum GoldenExitTextAlignment : byte { Left=0, Centre=1, Right=2 }
public sealed record GoldenEntranceLabel(int Slot,int Address,int ExitCode,string Text,int X,int Y,GoldenExitTextAlignment Alignment,byte ReachArea,byte VisitedMap,bool DestinationVisited);

/// <summary>Direct visual port of minimap.vb scanER(): lPoints/aAlign + visited masking + DrawString placement.</summary>
public static class V1EntranceDisplayAdapter
{
    private sealed record P(int X,int Y,GoldenExitTextAlignment A=GoldenExitTextAlignment.Left);

    public static IReadOnlyList<GoldenEntranceLabel> Resolve(int location, EntranceRandomizerState state, bool setScrub=false)
    {
        if(state.Mode==0 || state.LocationArray is null) return Array.Empty<GoldenEntranceLabel>(); // Golden: If iOldER = 0 Then Exit Sub
        var points=ResolvePoints(location,state.OverworldEnabled,state.DungeonEnabled,setScrub);
        var result=new List<GoldenEntranceLabel>();
        foreach(var e in state.Exits)
        {
            if(e.Destination is null || !points.TryGetValue(e.Slot,out var p)) continue;
            var d=e.Destination; bool visited=d.VisitedMap==255 || (d.VisitedMap<state.Visited.Count && state.Visited[d.VisitedMap]);
            string text=visited?d.Label:"?";
            var (x,a)=GoldenLeft(p.X,p.A,text.Length);
            result.Add(new(e.Slot,e.Address,d.ExitCode,text,x,p.Y,a,d.ReachArea,d.VisitedMap,visited));
        }
        return result.AsReadOnly();
    }

    // Golden uses a fixed 15 px/character estimate and clamps against x=548 before DrawString.
    private static (int X,GoldenExitTextAlignment A) GoldenLeft(int x,GoldenExitTextAlignment a,int len)
    {
        int width=len*15;
        if(a==GoldenExitTextAlignment.Left && x+width+4>548){x=548;a=GoldenExitTextAlignment.Right;}
        else if(a==GoldenExitTextAlignment.Centre)
        {
            double limit=x>278 ? x+width/2d+4 : x-width/2d+4;
            if(x>278 && limit>548){x=548;a=GoldenExitTextAlignment.Right;}
            else if(x<=278 && limit<0){x=0;a=GoldenExitTextAlignment.Left;}
        }
        else if(a==GoldenExitTextAlignment.Right && x-width-4<0){x=0;a=GoldenExitTextAlignment.Left;}
        if(a==GoldenExitTextAlignment.Centre)x=(int)(x-width/2d-4);
        else if(a==GoldenExitTextAlignment.Right)x=x-width-4;
        return(x,a);
    }

    public static IReadOnlyDictionary<int,(int X,int Y,GoldenExitTextAlignment Alignment)> GoldenPoints(int location,bool overworld,bool dungeon,bool setScrub=false)
        => ResolvePoints(location,overworld,dungeon,setScrub).ToDictionary(k=>k.Key,v=>(v.Value.X,v.Value.Y,v.Value.A));

    private static Dictionary<int,P> ResolvePoints(int location,bool overworld,bool dungeon,bool setScrub)
    {
        var p=new Dictionary<int,P>(); int ent=0; void S(int slot,int x,int y,GoldenExitTextAlignment a=GoldenExitTextAlignment.Left)=>p[slot]=new(x,y,a);
        if(dungeon) switch(location)
        {
            case 0:case 1:case 2:case 3:case 4:case 5:case 6:case 7:S(0,278,390,GoldenExitTextAlignment.Centre);S(1,278,3,GoldenExitTextAlignment.Centre);break;
            case 8:case 9:case 11:case 13:S(0,278,390,GoldenExitTextAlignment.Centre);break;
            case 10:S(1,278,390,GoldenExitTextAlignment.Centre);break;
        }
        // Golden always reserves the dungeon-entrance point/slot on these overworld scenes; read occurs only when dungeon ER is enabled.
        switch(location)
        {
            case 82:S(0,369,223,GoldenExitTextAlignment.Centre);ent=1;break;
            case 83:S(0,492,202);ent=1;break;
            case 85:S(0,440,234,GoldenExitTextAlignment.Centre);ent=1;break;
            case 86:S(0,278,3,GoldenExitTextAlignment.Centre);ent=1;break;
            case 87:S(0,311,278,GoldenExitTextAlignment.Centre);ent=1;break;
            case 89:S(0,262,152);S(1,346,8,GoldenExitTextAlignment.Centre);ent=2;break;
            case 92:S(0,85,163,GoldenExitTextAlignment.Centre);ent=1;break;
            case 93:case 12:S(0,275,242,GoldenExitTextAlignment.Centre);ent=1;break;
            case 96:S(0,260,174,GoldenExitTextAlignment.Right);ent=1;break;
            case 97:S(0,308,3,GoldenExitTextAlignment.Centre);ent=1;break;
            case 100:S(0,278,3); /* Golden sets aAlign(1), not (0). Preserve effective slot-0 Left. */ ent=1;break;
        }
        if(!overworld)return p;
        switch(location)
        {
            case >=27 and <=29:S(0,482,73,GoldenExitTextAlignment.Centre);S(1,67,346,GoldenExitTextAlignment.Centre);break;
            case 30:case 31:S(0,266,307);S(1,266,70);break;
            case 32:case 33:S(0,278,3,GoldenExitTextAlignment.Centre);S(1,278,390,GoldenExitTextAlignment.Centre);S(2,456,81);S(3,98,56,GoldenExitTextAlignment.Right);S(4,98,330,GoldenExitTextAlignment.Right);break;
            case 34:S(0,278,3,GoldenExitTextAlignment.Centre);S(1,278,390,GoldenExitTextAlignment.Centre);S(2,456,70);break;
            case 35:case 36:case 37:S(0,114,18,GoldenExitTextAlignment.Centre);S(1,442,377,GoldenExitTextAlignment.Centre);break;
            case 67:S(0,278,385,GoldenExitTextAlignment.Centre);break;
            case 81:S(0,418,64);S(1,437,215);S(2,447,121);S(3,104,183,GoldenExitTextAlignment.Right);S(4,313,14,GoldenExitTextAlignment.Centre);S(5,280,151,GoldenExitTextAlignment.Centre);S(6,195,380,GoldenExitTextAlignment.Centre);break;
            case 82:S(ent++,298,11,GoldenExitTextAlignment.Centre);S(ent++,57,294,GoldenExitTextAlignment.Right);S(ent,493,312);break;
            case 83:S(ent,57,225,GoldenExitTextAlignment.Right);break;
            case 84:S(0,46,319,GoldenExitTextAlignment.Right);S(1,509,92);S(2,483,157,GoldenExitTextAlignment.Centre);break;
            case 85:S(ent++,79,187,GoldenExitTextAlignment.Right);S(ent,172,84,GoldenExitTextAlignment.Centre);break;
            case 86:S(ent,278,389,GoldenExitTextAlignment.Centre);break;
            case 87:S(ent++,276,5,GoldenExitTextAlignment.Centre);S(ent,313,126,GoldenExitTextAlignment.Centre);break;
            case 88:S(0,291,24,GoldenExitTextAlignment.Centre);S(1,182,291,GoldenExitTextAlignment.Centre);S(2,352,387,GoldenExitTextAlignment.Right);break;
            case 89:S(ent,189,268,GoldenExitTextAlignment.Right);break;
            case 90:S(0,328,387,GoldenExitTextAlignment.Centre);S(1,464,218);S(2,84,117,GoldenExitTextAlignment.Right);break;
            case 91:S(0,299,14,GoldenExitTextAlignment.Centre);S(1,248,251,GoldenExitTextAlignment.Centre);S(2,423,158);S(3,299,setScrub?128:117,GoldenExitTextAlignment.Centre);S(4,192,341);S(5,127,321,GoldenExitTextAlignment.Right);break;
            case 92:S(ent,476,179);break;
            case 93:S(ent++,277,350);S(ent,145,133,GoldenExitTextAlignment.Right);break;
            case 94:S(0,10,99,GoldenExitTextAlignment.Right);S(1,545,336);break;
            case 95:S(0,162,365,GoldenExitTextAlignment.Centre);break;
            case 96:S(ent++,320,149);S(ent++,259,384,GoldenExitTextAlignment.Centre);S(ent,315,3,GoldenExitTextAlignment.Centre);break;
            case 97:S(ent++,129,192,GoldenExitTextAlignment.Right);S(ent,199,387,GoldenExitTextAlignment.Centre);break;
            case 98:S(0,281,3,GoldenExitTextAlignment.Centre);S(1,278,390,GoldenExitTextAlignment.Centre);S(2,351,361);break;
            case 99:S(0,348,14,GoldenExitTextAlignment.Centre);break;
            case 100:S(ent,278,390,GoldenExitTextAlignment.Centre);break;
        }
        return p;
    }
}
