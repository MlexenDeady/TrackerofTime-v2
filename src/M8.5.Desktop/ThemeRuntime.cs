using System.Windows;
using System.Windows.Media;
namespace TrackerOfTime.V2.M8_5.Desktop;

internal static class ThemeRuntime
{
 internal static readonly string[] Names={"Hyrule","Zora","Gerudo","Ocarina of Time","Nightly","Midnight","Simple Dark","Light Parchment"};
 sealed record Palette(string Window,string Accent,string Foreground,string Panel,string Panel2,string HeaderTop,string HeaderBottom,string InsetTop,string InsetBottom,string ActiveTop,string ActiveBottom,string Frame,string FrameFill);
 static readonly Dictionary<string,Palette> Palettes=new(StringComparer.Ordinal)
 {
  ["Hyrule"]=new("#020503","#D5A93A","#F4E7C1","#07100D","#0D1D17","#19291F","#020705","#020605","#08110D","#396C2D","#0D2112","#D7AE42","#C9020705"),
  ["Zora"]=new("#031117","#62C9E8","#DDF8FF","#082630","#0D3440","#164B5C","#041217","#020C10","#0A2631","#176B82","#0A3542","#77DDF2","#D0041820"),
  ["Gerudo"]=new("#160A06","#E59B43","#FFE2B1","#32170C","#44210F","#5B2A13","#180A05","#120704","#32170C","#A14E20","#4C1D0B","#E7A64E","#D0140804"),
  ["Ocarina of Time"]=new("#06110C","#D6B33E","#F4E7BE","#10231A","#163126","#254A34","#07120D","#04100A","#10231A","#35743D","#15351D","#D8BC55","#D005100A"),
  ["Nightly"]=new("#080A17","#7C86E8","#E5E8FF","#151A35","#1D2346","#252C58","#090B18","#070914","#151A35","#4A519B","#242957","#929AF5","#D0060815"),
  ["Midnight"]=new("#010309","#4775B8","#D7E6FF","#07101E","#0A1729","#10294A","#02040A","#01040A","#08101E","#214A79","#0B223D","#4F80C9","#E0010309"),
  ["Simple Dark"]=new("#101010","#8C8C8C","#F0F0F0","#1B1B1B","#242424","#292929","#111111","#0D0D0D","#1B1B1B","#3B3B3B","#202020","#555555","#F0101010"),
  ["Light Parchment"]=new("#CDBB91","#765321","#2B2114","#E7D8B3","#DCC99C","#F1E5C7","#C8B382","#BDAA7E","#EADCB9","#B88B45","#8E652E","#765321","#E8CDBB91")
 };
 internal static string Current {get;private set;}="Hyrule";
 internal static void Apply(Application app,string name)
 {
  if(!Palettes.TryGetValue(name,out var p)) p=Palettes["Hyrule"]; Current=name;
  SetSolid(app,"WindowBackground",p.Window); SetSolid(app,"ThemeAccent",p.Accent); SetSolid(app,"ThemeForeground",p.Foreground); SetSolid(app,"ThemePanel",p.Panel); SetSolid(app,"ThemeFrame",p.Frame); SetSolid(app,"ThemeFrameFill",p.FrameFill);
  SetSolid(app,"Gold",p.Accent); SetSolid(app,"GoldBright",p.Accent); SetSolid(app,"Text",p.Foreground); SetSolid(app,"Panel",p.Panel); SetSolid(app,"Panel2",p.Panel2);
  SetGradient(app,"HeaderMaterial",p.HeaderTop,p.HeaderBottom); SetGradient(app,"PanelMaterial",p.Panel2,p.Panel); SetGradient(app,"InsetMaterial",p.InsetTop,p.InsetBottom); SetGradient(app,"ActiveNavMaterial",p.ActiveTop,p.ActiveBottom);
 }
 static void SetSolid(Application app,string key,string hex)
 {
  var c=(Color)ColorConverter.ConvertFromString(hex);
  if(app.Resources[key] is SolidColorBrush b){if(b.IsFrozen){app.Resources[key]=new SolidColorBrush(c);}else b.Color=c;} else app.Resources[key]=new SolidColorBrush(c);
 }
 static void SetGradient(Application app,string key,string top,string bottom)
 {
  var a=(Color)ColorConverter.ConvertFromString(top); var z=(Color)ColorConverter.ConvertFromString(bottom);
  if(app.Resources[key] is LinearGradientBrush g && !g.IsFrozen && g.GradientStops.Count>=2){g.GradientStops[0].Color=a;g.GradientStops[^1].Color=z;if(g.GradientStops.Count>2)g.GradientStops[1].Color=Color.FromArgb(255,(byte)((a.R+z.R)/2),(byte)((a.G+z.G)/2),(byte)((a.B+z.B)/2));}
  else app.Resources[key]=new LinearGradientBrush(a,z,90);
 }
}
