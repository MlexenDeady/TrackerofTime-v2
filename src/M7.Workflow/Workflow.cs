using TrackerOfTime.V2.Randomizer;
namespace TrackerOfTime.V2.M7.Workflow;

public enum M7WorkflowState { Idle, RomSelected, ValidatingRom, RomValid, EditingSettings, Generating, Cancelling, Cancelled, Generated, OutputReady, GameStartRequested, TrackerStartRequested, Error }
public sealed record M7OutputOptions(string OutputDirectory,bool CreatePatchFile=true,bool CreateCompressedRom=false,bool CreateUncompressedRom=false,bool CreateSpoiler=true,string? DistributionFile=null);
public sealed record M7GenerationInput(string RomPath,string? Seed,string? SettingsString,IReadOnlyDictionary<string,object?> Settings,int WorldCount,M7OutputOptions Output);
public sealed record M7WorkflowSnapshot(M7WorkflowState State,string? RomPath,bool RomValidated,string Status,string? Error,bool OutputStale,IReadOnlyList<string> OutputFiles,string? SettingsString);

public static class M7WorkflowRules {
 public static bool CanValidate(M7WorkflowSnapshot s)=>!string.IsNullOrWhiteSpace(s.RomPath)&&s.State is not M7WorkflowState.ValidatingRom and not M7WorkflowState.Generating and not M7WorkflowState.Cancelling;
 public static bool CanGenerate(M7WorkflowSnapshot s,M7OutputOptions o)=>s.RomValidated&&!string.IsNullOrWhiteSpace(s.RomPath)&&!string.IsNullOrWhiteSpace(o.OutputDirectory)&&s.State is not M7WorkflowState.ValidatingRom and not M7WorkflowState.Generating and not M7WorkflowState.Cancelling;
 public static M7WorkflowSnapshot SelectRom(M7WorkflowSnapshot s,string? path)=>s with{State=string.IsNullOrWhiteSpace(path)?M7WorkflowState.Idle:M7WorkflowState.RomSelected,RomPath=path,RomValidated=false,Status=string.IsNullOrWhiteSpace(path)?"Select a ROM.":"ROM selected; validation required.",Error=null,OutputStale=s.OutputFiles.Count>0};
 public static M7WorkflowSnapshot SettingsChanged(M7WorkflowSnapshot s,string? settingsString=null)=>s with{State=s.RomValidated?M7WorkflowState.EditingSettings:s.State,SettingsString=settingsString??s.SettingsString,OutputStale=s.OutputFiles.Count>0||s.State is M7WorkflowState.Generated or M7WorkflowState.OutputReady,Status="Settings changed."};
 public static M7WorkflowSnapshot RequestGameStart(M7WorkflowSnapshot s)=>s.State is M7WorkflowState.OutputReady or M7WorkflowState.Generated ? s with{State=M7WorkflowState.GameStartRequested,Status="Game start requested."}:throw new InvalidOperationException("Generated output required before game start.");
 public static M7WorkflowSnapshot RequestTrackerStart(M7WorkflowSnapshot s)=>s.State is M7WorkflowState.GameStartRequested or M7WorkflowState.OutputReady or M7WorkflowState.Generated ? s with{State=M7WorkflowState.TrackerStartRequested,Status="Tracker start requested."}:throw new InvalidOperationException("Generated output required before tracker start.");
 public static GenerateSeedRequest ToFrozenM3Request(M7GenerationInput x)=>new(x.RomPath,x.Seed,x.SettingsString,new Dictionary<string,object?>(x.Settings),x.Output.OutputDirectory,x.WorldCount,x.Output.CreatePatchFile,x.Output.CreateCompressedRom,x.Output.CreateUncompressedRom,x.Output.CreateSpoiler,x.Output.DistributionFile);
}

public static class M7OoTRSettingCatalog {
 public static readonly IReadOnlyDictionary<string,string> ProvenUiTypes=new Dictionary<string,string>(StringComparer.Ordinal){
  ["rom"]="FileInput(.z64/.n64)",["seed"]="Text",["output_dir"]="DirectoryInput",["world_count"]="Number",
  ["logic_rules"]="Choice(glitchless|advanced|none)",["shuffle_dungeon_entrances"]="Choice(off|simple|all)",["shuffle_overworld_entrances"]="Boolean",
  ["mq_dungeons_mode"]="Choice(vanilla|mq|specific|count|random)",["mq_dungeons_count"]="Integer(0..12)",["allowed_tricks"]="MultiSelect/Search",
  ["starting_items"]="Dictionary",["create_patch_file"]="Boolean",["create_compressed_rom"]="Boolean",["create_uncompressed_rom"]="Boolean",["create_spoiler"]="Boolean"
 };
}
