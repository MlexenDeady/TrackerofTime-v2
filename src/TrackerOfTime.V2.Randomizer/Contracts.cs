namespace TrackerOfTime.V2.Randomizer;
public static class RandomizerProtocol { public const int Major=1; public const int Minor=0; }
public sealed record HostRequest(string RequestId,string MessageType,object? Payload=null,int ProtocolMajor=1,int ProtocolMinor=0);
public sealed record HostError(string Code,string Message,string? Detail=null);
public sealed record HostResponse(string RequestId,bool Success,string MessageType,object? Payload,HostError? Error,int ProtocolMajor,int ProtocolMinor);
public sealed record ValidateRomRequest(string RomPath);
public sealed record ConvertSettingsRequest(string? SettingsString,Dictionary<string,object?>? Settings);
public sealed record GenerateSeedRequest(string RomPath,string? Seed,string? SettingsString,Dictionary<string,object?>? Settings,string OutputDirectory,int WorldCount=1,bool CreatePatchFile=true,bool CreateCompressedRom=false,bool CreateUncompressedRom=false,bool CreateSpoiler=true,string? DistributionFile=null);

public sealed record CancelRequest(string TargetRequestId);
