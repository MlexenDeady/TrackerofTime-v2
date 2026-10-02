using System.IO;
using System.Windows;
namespace TrackerOfTime.V2.M8_5.Desktop;
public partial class App : Application
{
 protected override void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  try
  {
   var root=ResolveApplicationRoot(AppContext.BaseDirectory);
   MainWindow=new MainWindow(root);
   MainWindow.Show();
   if(e.Args.Any(a=>a.Equals("--smoke",StringComparison.OrdinalIgnoreCase))) _=Dispatcher.BeginInvoke(new Action(()=>MainWindow.Close()));
  }
  catch(Exception ex)
  {
   MessageBox.Show(ex.ToString(),"Tracker of Time V2 - Startfehler",MessageBoxButton.OK,MessageBoxImage.Error);
   Shutdown(1);
  }
 }

 static string ResolveApplicationRoot(string start)
 {
  // Portable publish: runtime assets are emitted next to the application.
  if(IsApplicationRoot(start)) return Path.GetFullPath(start);

  // Development: walk upwards from bin/... until the repository root is found.
  var d=new DirectoryInfo(start);
  while(d is not null)
  {
   if(File.Exists(Path.Combine(d.FullName,"TrackerOfTime.V2.sln")) && IsApplicationRoot(d.FullName)) return d.FullName;
   d=d.Parent;
  }
  throw new DirectoryNotFoundException("Tracker of Time V2 runtime files were not found next to the application.");
 }

 static bool IsApplicationRoot(string path) =>
  Directory.Exists(Path.Combine(path,"third_party","OoTR")) &&
  Directory.Exists(Path.Combine(path,"src","OoTR.Host"));
}
