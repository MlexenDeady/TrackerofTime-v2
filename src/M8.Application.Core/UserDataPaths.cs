namespace TrackerOfTime.V2.M8.Application.Core;

public sealed record M8UserDataPaths(string Root, string Logs, string Settings, string Outputs, string Temporary)
{
    public static M8UserDataPaths CreateDefault()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("LocalApplicationData is unavailable.");
        var root = Path.Combine(local, "TrackerOfTime", "V2");
        return new(root, Path.Combine(root, "Logs"), Path.Combine(root, "Settings"), Path.Combine(root, "Outputs"), Path.Combine(root, "Temp"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root); Directory.CreateDirectory(Logs); Directory.CreateDirectory(Settings);
        Directory.CreateDirectory(Outputs); Directory.CreateDirectory(Temporary);
    }
}
