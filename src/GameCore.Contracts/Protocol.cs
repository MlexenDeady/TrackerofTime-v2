using System.Text.Json;
namespace TrackerOfTime.V2.GameCore.Contracts;
public static class Protocol {
 public const int Version=1, PayloadVersion=1, MaxFrameBytes=1024*1024, MaxRdramRead=65536, RdramBytes=8*1024*1024;
 public const string DefaultPipe="TrackerOfTime.V2.GameCore.v1";
}
public sealed record Request(int ProtocolVersion,string RequestId,string MessageType,int PayloadVersion,JsonElement Payload);
public sealed record Response(int ProtocolVersion,string RequestId,string MessageType,int PayloadVersion,bool Success,object? Payload,string? Error);
public sealed record HostStatus(string State,bool RomLoaded,bool Executing,string Renderer,bool RdramAvailable,int RdramBytes);
public sealed record Capabilities(string Core,string CoreVersion,string FrontendApi,string ConfigApi,string DebugApi,string VidExtApi,string Renderer,string[] Operations);
