using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using TrackerOfTime.V2.M8.Application.Core;
using TrackerOfTime.V2.M8_2.Integration;
using TrackerOfTime.V2.M8_4.Integration;
using TrackerOfTime.V2.M6.SnapshotAdapter;
namespace TrackerOfTime.V2.M8_5.Desktop;
public partial class MainWindow:Window
{
 readonly System.Windows.Threading.DispatcherTimer _inputWatch=new(){Interval=TimeSpan.FromSeconds(1)}; readonly System.Windows.Threading.DispatcherTimer _trackerWatch=new(){Interval=TimeSpan.FromMilliseconds(750)}; readonly string _root; readonly M8ApplicationStateMachine _state=new(); readonly M8UserDataPaths _paths=M8UserDataPaths.CreateDefault(); readonly InMemoryM8Diagnostics _memoryDiagnostics=new(); M84ProductiveSession? _session; M84SessionPlan? _plan; RandomizerWorkspace? _randomizerWorkspace; GameSnapshotAdapterResult? _lastTrackerSnapshot; string _lastTrackerReadiness="Live"; bool _trackerAuto,_trackerCaptureBusy; bool _busy,_closing; DateTimeOffset? _sessionStartedAt; HashSet<string>? _previousOwnedState; readonly V1EntranceRuntime _v1Entrance=new(); EntranceRandomizerState? _v1EntranceState; bool _minimapPanning; Point _minimapPanStart; double _minimapPanX,_minimapPanY; string? _minimapZoomKey; string? _selectedMapCheck;
 public MainWindow(string root){_root=root;InitializeComponent();_paths.EnsureCreated();InitializeInputBindings();_randomizerWorkspace=new RandomizerWorkspace(root,_paths,_memoryDiagnostics);RandomizerWorkspaceHost.Content=_randomizerWorkspace;_state.MarkReady();PreviewMouseUp+=Window_PreviewMouseUp;_inputWatch.Tick+=(_,_)=>RefreshLiveInputStatus();_inputWatch.Start();_trackerWatch.Tick+=async (_,_)=>await AutoCaptureTrackerAsync();_trackerWatch.Start();ThemeRuntime.Apply(Application.Current,"Hyrule");ApplyThemePresentation("Hyrule");UiLocalization.SetGerman(true);UiLocalization.Apply(this);_randomizerWorkspace.ApplyLanguage(true);Refresh();}
 async void ChooseRom_Click(object s,RoutedEventArgs e){var d=new OpenFileDialog{Title="Nintendo 64 ROM auswählen",Filter="Nintendo 64 ROM (*.z64;*.n64;*.v64)|*.z64;*.n64;*.v64|Alle Dateien (*.*)|*.*"};if(d.ShowDialog(this)!=true)return;try{var diag=new M82SessionDiagnostics(_memoryDiagnostics,_paths.Logs,Renderer());await using var g=new TrackerOfTime.V2.M8_1.Integration.GameRuntimeService(diag);var c=new M84UnifiedApplicationCoordinator(_state,g,diag);_plan=c.PlanUserRom(d.FileName);RomPathText.Text=d.FileName;GameTitleText.Text=string.IsNullOrWhiteSpace(_plan.Inspection.InternalName)?"Unbenannte N64 ROM":_plan.Inspection.InternalName.Trim();GameProfileText.Text=$"{_plan.Inspection.GameProfile}";ProfileText.Text=$"{_plan.Inspection.GameProfile}";RouteStatus.Text=$"ROM: {_plan.Inspection.ByteOrder} • {_plan.Inspection.TrackerCapability}";TrackerText.Text=_plan.StartTrackerWhenLive?"Tracker-Kandidat erkannt; wartet auf Live-Daten.":"Emulator-only Profil.";Log($"ROM erkannt: {_plan.Inspection.InternalName} / {_plan.Inspection.ByteOrder}");}catch(Exception ex){MessageBox.Show(ex.Message,"ROM-Prüfung",MessageBoxButton.OK,MessageBoxImage.Error);}Refresh();}
 async void StartGame_Click(object s,RoutedEventArgs e){if(_plan is null||_busy)return;SetBusy(true);ApplyInputProfileEnvironment();var renderer=Renderer();GameHost.Renderer=renderer;var diag=new M82SessionDiagnostics(_memoryDiagnostics,_paths.Logs,renderer);try{_session=new M84ProductiveSession(_root,_paths,_state,diag);Log($"GameCore startet mit {renderer} …");
        // Start native-window capture in parallel with GameCore startup. This catches the renderer as soon as
        // the host PID exists, before Rice/Glide can remain visible as a free desktop window.
        var captureTask=CaptureRendererDuringStartupAsync(_session);
        var requestController=ControllerBox.IsChecked==true;
        if(requestController)
        {
         // USB/wireless XInput devices can need a short settle interval after activation.
         // Retry only before provider selection; if still absent M8.4 deliberately falls back to Input-SDL keyboard.
         for(var attempt=0;attempt<8;attempt++)
         {
          var probe=M84XInputAvailability.Probe();
          if(probe.XInputAvailable&&probe.ControllerConnected)break;
          if(attempt==7)Log("Kein Live-XInput erkannt; Tastatur-Fallback wird verwendet.");
          else await Task.Delay(125);
         }
        }
        await _session.StartAsync(_plan,renderer,requestController);
        var attached=await captureTask;
        HostText.Text=_session.HostProcessId?.ToString()??"unbekannt";InputStatus.Text=$"🎮 {_session.InputStatus}";GameCoreStatus.Text="● GameCore: läuft";SideGameCoreStatus.Text="● Mupen64Plus: läuft";SideInputStatus.Text=InputStatus.Text;SessionStatusText.Text="● Läuft";_sessionStartedAt=DateTimeOffset.Now;SessionTimeText.Text="00:00:00";
        EmulatorPlaceholder.Visibility=attached?Visibility.Collapsed:Visibility.Visible;
        if(_session.HostProcessId is int pid) Log(attached?$"Native Emulatorfenster eingebettet (PID {pid}).":$"GameCore läuft (PID {pid}), aber kein natives Renderfenster zum Einbetten gefunden.");TrackerText.Text=_plan.StartTrackerWhenLive?"Tracker-Kandidat; Live-Snapshot kann jetzt geprüft werden.":"Für diese ROM kein Tracker-Profil.";}catch(Exception ex){_state.Fail(ex.Message);await DisposeSession();MessageBox.Show(ex+$"\n\nSession log: {diag.LogFile}","GameCore Start",MessageBoxButton.OK,MessageBoxImage.Error);}finally{SetBusy(false);Refresh();}}
 async void ProbeTracker_Click(object s,RoutedEventArgs e)
 {
  if(_session is null||_plan is null||_busy)return;
  await CaptureTrackerAndRenderAsync(showErrors:true);
  if(_lastTrackerSnapshot is not null){_trackerAuto=true;TrackerButton.Content="◉  Tracker läuft automatisch";Log("Tracker Auto-Scan aktiviert (750 ms).");}
 }
 async Task AutoCaptureTrackerAsync()
 {
  if(!_trackerAuto||_session is null||_plan is null||_busy||_trackerCaptureBusy||!_plan.StartTrackerWhenLive)return;
  await CaptureTrackerAndRenderAsync(showErrors:false);
 }
 async Task CaptureTrackerAndRenderAsync(bool showErrors)
 {
  if(_session is null||_plan is null||_trackerCaptureBusy)return;
  _trackerCaptureBusy=true;
  try
  {
   var f=await _session.CaptureTrackerAsync(_plan);
   _lastTrackerSnapshot=f.FrozenSnapshot;_lastTrackerReadiness=f.Readiness.ToString();
   _v1EntranceState=await _v1Entrance.CaptureAsync(f.FrozenSnapshot,(o,c)=>_session.ReadRdramAsync(o,c));
   RenderTrackerFrame(f.FrozenSnapshot,_lastTrackerReadiness);
   RenderTrackerWorkspace(f.FrozenSnapshot,_lastTrackerReadiness);
   RenderDashboardWorkspaces(f.FrozenSnapshot,_lastTrackerReadiness);
   
   TrackerStatus.Text=$"◉ Tracker: {f.Readiness} / {f.FrozenSnapshot.Game.Evidence}";
   SideTrackerStatus.Text=$"◉ Tracker: {f.Readiness} / {f.FrozenSnapshot.Game.Evidence}";
   if(showErrors)Log($"Tracker-Daten: {f.Readiness}, evidence={f.FrozenSnapshot.Game.Evidence}, loaded={f.FrozenSnapshot.Game.Value.IsLoaded}");
  }
  catch(Exception ex){if(showErrors)MessageBox.Show(ex.Message,"Tracker",MessageBoxButton.OK,MessageBoxImage.Warning);else{_trackerAuto=false;TrackerButton.Content="◉  Tracker starten";Log("Tracker Auto-Scan pausiert: "+ex.Message);}}
  finally{_trackerCaptureBusy=false;Refresh();}
 }
 async void StopGame_Click(object s,RoutedEventArgs e){if(_busy)return;SetBusy(true);try{GameHost.Detach();EmulatorPlaceholder.Visibility=Visibility.Visible;if(_session is not null)await _session.StopAsync();Log("Session sauber gestoppt; Emulatorfenster detached.");TrackerText.Text="Tracker: gestoppt";TrackerStatus.Text="◉ Tracker: offline";SideTrackerStatus.Text="◉ Tracker: offline";SideSnapshotStatus.Text="Datenstand: —";GameCoreStatus.Text="● GameCore: bereit";SideGameCoreStatus.Text="● Mupen64Plus: bereit";InputStatus.Text="🎮 Input: nicht gestartet";SideInputStatus.Text="🎮 Input: nicht gestartet";_trackerAuto=false;_lastTrackerSnapshot=null;TrackerButton.Content="◉  Tracker starten";TrackerLiveGrid.Children.Clear();TrackerWorkspaceLiveGrid.Children.Clear();TrackerWorkspaceDungeonPanel.Children.Clear();TrackerWorkspaceStateText.Text="Wartet auf Live-Daten.";TrackerWorkspaceEvidenceText.Text="Status: —";TrackerWorkspaceCapturedText.Text="Datenstand: —";TrackerWorkspaceRegionText.Text="Region: —";RegionRowsPanel.Children.Clear();MinimapOverlay.Children.Clear();MinimapOverlayViewbox.Visibility=Visibility.Collapsed;EntranceHintText.Text="";RegionText.Text="Noch keine Live-Region";PositionText.Text="Position: —";MapAreaCodeText.Text="KARTE\nwartet auf Live-Daten";HostText.Text="nicht gestartet";SessionStatusText.Text="● Bereit";_sessionStartedAt=null;SessionTimeText.Text="00:00:00";_previousOwnedState=null;_v1Entrance.Reset();_v1EntranceState=null;}catch(Exception ex){MessageBox.Show(ex.Message,"Stop",MessageBoxButton.OK,MessageBoxImage.Error);}finally{await DisposeSession();SetBusy(false);Refresh();}}
 async void Reattach_Click(object s,RoutedEventArgs e){if(_session?.HostProcessId is not int pid){Log("Kein laufender GameCore-Host zum Einbetten.");return;}var ok=await GameHost.AttachProcessAsync(pid,TimeSpan.FromSeconds(8));EmulatorPlaceholder.Visibility=ok?Visibility.Collapsed:Visibility.Visible;Log(ok?"Emulatorfenster erneut eingebettet.":"Kein geeignetes Rendererfenster zum Einbetten gefunden.");}
 async Task<bool> CaptureRendererDuringStartupAsync(M84ProductiveSession session)
 {
  var until=DateTime.UtcNow+TimeSpan.FromSeconds(20);
  while(DateTime.UtcNow<until)
  {
   if(session.HostProcessId is int pid) return await GameHost.AttachProcessAsync(pid,TimeSpan.FromSeconds(18));
   await Task.Delay(15);
  }
  return false;
 }
 void Window_PreviewMouseUp(object sender,MouseButtonEventArgs e)
 {
  if(_session is null||!_session.UsesKeyboardFallback)return;
  Dispatcher.BeginInvoke(new Action(()=>GameHost.FocusGame()),System.Windows.Threading.DispatcherPriority.ContextIdle);
 }

 void RefreshLiveInputStatus()
 {
  if(_session is null||ControllerBox.IsChecked!=true)return;
  var p=M84XInputAvailability.Probe();
  if(p.XInputAvailable&&p.ControllerConnected) InputStatus.Text=$"🎮 Direct-XInput controller {p.ControllerIndex} + keyboard active";
  else if(_session.UsesKeyboardFallback) InputStatus.Text="⌨ Keyboard active • XInput hot-plug waiting";
  SideInputStatus.Text=InputStatus.Text;
  if(_sessionStartedAt is DateTimeOffset started){var elapsed=DateTimeOffset.Now-started;SessionTimeText.Text=$"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";}
 }

 void RenderTrackerFrame(GameSnapshotAdapterResult snapshot,string readiness)
 {
  TrackerLiveGrid.Children.Clear();
  var inventory=snapshot.Inventory.Value.Slots.Where(x=>x.ItemId<=55&&!string.Equals(x.ItemName,"Empty Slot",StringComparison.OrdinalIgnoreCase)).GroupBy(x=>x.ItemId).ToDictionary(g=>g.Key,g=>g.First());
  var equipment=snapshot.Equipment.Value.Entries.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
  var upgrades=snapshot.Upgrades.Value.Entries.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
  var quests=snapshot.QuestItems.Value.Entries.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
  var songs=snapshot.Songs.Value.Entries.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);

  // Master-sketch compact tracker: one persistent surface, no category pages.
  // V1 semantics stay intact; the seven-column V2 grid only composes them compactly.
  RenderCompactItems(inventory,equipment,upgrades,snapshot);
  RenderEquipment(equipment,upgrades,snapshot);
  RenderQuest(quests,snapshot);
  RenderSongs(songs,snapshot);
  RenderDungeonCompact(snapshot);

  var ownedInventory=snapshot.Inventory.Value.Slots.Count(x=>x.Owned&&!string.Equals(x.ItemName,"Empty Slot",StringComparison.OrdinalIgnoreCase));
  var ownedEquipment=snapshot.Equipment.Value.Entries.Count(x=>x.Owned);var up=snapshot.Upgrades.Value.Entries.Count(x=>x.Level>0);var q=snapshot.QuestItems.Value.Entries.Count(x=>x.Owned);var so=snapshot.Songs.Value.Entries.Count(x=>x.Owned);
  var checkedCount=snapshot.Checks.Value.Values.Count(x=>x.State.ToString()=="Checked");var knownChecks=snapshot.Checks.Value.Values.Count(x=>x.State.ToString()!="Unknown");
  var dungeonDone=snapshot.DungeonItems.Value.Entries.Count(x=>x.Map==true&&x.Compass==true&&(x.BossKey==true||x.BossKey is null));
  ProgressItemsText.Text=ownedInventory.ToString();ProgressEquipmentText.Text=$"{ownedEquipment}+{up}";ProgressSongsText.Text=$"{so} / 12";ProgressQuestText.Text=q.ToString();ProgressDungeonsText.Text=dungeonDone.ToString();ProgressChecksText.Text=$"{checkedCount} / {knownChecks}";
  TrackerText.Text=$"{readiness} • {snapshot.Game.Evidence} • {snapshot.CapturedAt:HH:mm:ss}";
  SideSnapshotStatus.Text=$"Datenstand: {UiLocalization.T(snapshot.Game.Evidence.ToString())} • {snapshot.CapturedAt:HH:mm:ss}";
  var map=snapshot.Map.Value;MapAreaCodeText.Text=string.IsNullOrWhiteSpace(map.MinimapAsset)?$"{map.AreaCode}\n{map.RegionKind}":$"{map.AreaCode}\n{map.MinimapAsset}";RegionText.Text=$"{map.AreaCode} • {map.RegionKind} • Scene {map.LocationCode}";SetLiveMinimap(snapshot);
  var currentPoints=V1MinimapPresentationAdapter.For(map.LastMinimap,map.Room,map.LastMinimap is >=0 and <=11&&snapshot.MasterQuest.Value.Flags.Count>map.LastMinimap?snapshot.MasterQuest.Value.Flags[map.LastMinimap]:null);var currentStates=currentPoints.Select(p=>snapshot.Checks.Value.TryGetValue(p.LocationId,out var st)?st:null).Where(x=>x is not null&&x.State.ToString()!="Unknown").ToArray();var currentDone=currentStates.Count(x=>x!.State.ToString()=="Checked");var currentTotal=currentStates.Length;
  if(currentTotal==0){RegionProgressText.Text="● keine belastbaren Check-Daten für diese Minimap";RegionProgressText.Foreground=Brush("#8A3E3E");}
  else{var ratio=(double)currentDone/currentTotal;RegionProgressText.Text=$"● {map.AreaCode}: {currentDone} / {currentTotal}";RegionProgressText.Foreground=Brush(ratio>=0.999?"#55C96B":ratio>0?"#E0A43A":"#B84C4C");}
  var pos=snapshot.PlayerPosition.Value;PositionText.Text=pos is null?"Position: nicht verfügbar":$"Position: X {pos.X:0.##}  Y {pos.Y:0.##}  Z {pos.Z:0.##}";
  EmitTrackerEvents(snapshot);
  UpdateTrackerTabVisuals();
 }
 void RenderCompactItems(IReadOnlyDictionary<byte,InventorySlotSnapshot> inventory,IReadOnlyDictionary<string,EquipmentEntrySnapshot> equipment,IReadOnlyDictionary<string,UpgradeStateSnapshot> upgrades,GameSnapshotAdapterResult snapshot)
 {
  // V1 semantics in the compact V2 composition: definitions are fixed slots; memory only changes state/stage.
  foreach(byte id in new byte[]{0,1,2,3,4,5,6,9,12,13,14,15,16,17,18,19}){inventory.TryGetValue(id,out var v);var name=ItemName(id);AddTrackerTile(name,v?.Owned==true,v is not null&&v.Quantity>1?$"×{v.Quantity}":"",TrackerAsset(name),snapshot.Inventory.Evidence);}
  InventorySlotSnapshot? oot = null; InventorySlotSnapshot? fairy = null; inventory.TryGetValue(8, out oot); inventory.TryGetValue(7, out fairy); var ocarina=oot?.Owned==true?8:7; AddTrackerTile(ocarina==8?"Ocarina of Time":"Fairy Ocarina",oot?.Owned==true||fairy?.Owned==true,oot?.Owned==true?"2/2":fairy?.Owned==true?"1/2":"",TrackerAsset(ocarina==8?"Ocarina of Time":"Fairy Ocarina"),snapshot.Inventory.Evidence);
  var hook=inventory.TryGetValue(11,out var longshot)&&longshot.Owned?11:10;inventory.TryGetValue(10,out var hookshot);AddTrackerTile(hook==11?"Longshot":"Hookshot",hook==11||(hookshot?.Owned==true),hook==11?"2/2":hookshot?.Owned==true?"1/2":"",TrackerAsset(hook==11?"Longshot":"Hookshot"),snapshot.Inventory.Evidence);
  var bottles=inventory.Values.Where(x=>x.ItemId is >=20 and <=32&&x.Owned).ToArray();var bottleCount=Math.Min(4,bottles.Length);AddTrackerTile("Bottle",bottleCount>0,bottleCount>0?$"×{bottleCount}":"",TrackerAsset("Empty Bottle"),snapshot.Inventory.Evidence);
  var content=bottles.FirstOrDefault(x=>x.ItemId>20);var contentName=content?.ItemName??"Empty Bottle";AddTrackerTile(contentName,content is not null,content is null?"":"IN",TrackerAsset(contentName),snapshot.Inventory.Evidence);
  AddTradeTile("Child Trade",33,44,inventory,snapshot.Inventory.Evidence);AddTradeTile("Adult Trade",45,55,inventory,snapshot.Inventory.Evidence);
 }
 void AddTradeTile(string label,byte first,byte last,IReadOnlyDictionary<byte,InventorySlotSnapshot> inventory,SnapshotEvidence evidence){var v=inventory.Values.FirstOrDefault(x=>x.ItemId>=first&&x.ItemId<=last&&x.Owned);var name=v?.ItemName??ItemName(first);AddTrackerTile(label,v is not null,v is null?"":name,TrackerAsset(name),evidence);}
 void RenderEquipment(IReadOnlyDictionary<string,EquipmentEntrySnapshot> equipment,IReadOnlyDictionary<string,UpgradeStateSnapshot> upgrades,GameSnapshotAdapterResult snapshot){foreach(var id in new[]{"Kokiri Sword","Master Sword","Biggoron's Sword","Deku Shield","Hylian Shield","Mirror Shield","Kokiri Tunic","Goron Tunic","Zora Tunic","Kokiri Boots","Iron Boots","Hover Boots"}){equipment.TryGetValue(id,out var v);AddTrackerTile(id,v?.Owned==true,"",TrackerAsset(id),snapshot.Equipment.Evidence);}foreach(var id in new[]{"Quiver","Bomb Bag","Gauntlet","Scale","Wallet","Bullet Bag"}){upgrades.TryGetValue(id,out var v);AddTrackerTile(id,v is not null&&v.Level>0,v is not null&&v.Level>0?$"Lv{v.Level}":"",TrackerAsset(id,v?.Level??0),snapshot.Upgrades.Evidence);}}
 void RenderQuest(IReadOnlyDictionary<string,QuestItemSnapshot> quests,GameSnapshotAdapterResult snapshot){foreach(var id in new[]{"Kokiri Emerald","Goron Ruby","Zora Sapphire","Forest Medallion","Fire Medallion","Water Medallion","Spirit Medallion","Shadow Medallion","Light Medallion","Stone of Agony","Gerudo's Membership Card"}){quests.TryGetValue(id,out var v);AddTrackerTile(id,v?.Owned==true,"",TrackerAsset(id),snapshot.QuestItems.Evidence);}}
 void RenderSongs(IReadOnlyDictionary<string,SongSnapshot> songs,GameSnapshotAdapterResult snapshot){foreach(var id in new[]{"Minuet of Forest","Bolero of Fire","Serenade of Water","Requiem of Spirit","Nocturne of Shadow","Prelude of Light","Zelda's Lullaby","Epona's Song","Saria's Song","Sun's Song","Song of Time","Song of Storms"}){songs.TryGetValue(id,out var v);var tile=AddTrackerTile(id,v?.Owned==true,"♪",TrackerAsset(id),snapshot.Songs.Evidence);tile.Tag=id;tile.Cursor=Cursors.Hand;tile.MouseLeftButtonUp+=SongTile_Click;}}
 void SongTile_Click(object sender,MouseButtonEventArgs e){if(sender is Border b&&b.Tag is string song)Log($"{song}: {SongNotes(song)}");}
 static string SongNotes(string id)=>id switch{"Zelda's Lullaby"=>"◀ ▲ ▶ ◀ ▲ ▶","Epona's Song"=>"▲ ◀ ▶ ▲ ◀ ▶","Saria's Song"=>"▼ ▶ ◀ ▼ ▶ ◀","Sun's Song"=>"▶ ▼ ▲ ▶ ▼ ▲","Song of Time"=>"▶ A ▼ ▶ A ▼","Song of Storms"=>"A ▼ ▲ A ▼ ▲","Minuet of Forest"=>"A ▲ ◀ ▶ ◀ ▶","Bolero of Fire"=>"▼ A ▼ A ▶ ▼ ▶ ▼","Serenade of Water"=>"A ▼ ▶ ▶ ◀","Requiem of Spirit"=>"A ▼ A ▶ ▼ A","Nocturne of Shadow"=>"◀ ▶ ▶ A ◀ ▶ ▼","Prelude of Light"=>"▲ ▶ ▲ ▶ ◀ ▲",_=>""};
 static string ItemName(byte id)=>new[]{"Deku Stick","Deku Nut","Bombs","Bow","Fire Arrow","Din's Fire","Fairy Slingshot","Fairy Ocarina","Ocarina of Time","Bombchus","Hookshot","Longshot","Ice Arrows","Farore's Wind","Boomerang","Lens of Truth","Magic Beans","Megaton Hammer","Light Arrows","Nayru's Love","Empty Bottle","Red Potion","Green Potion","Blue Potion","Bottled Fairy","Fish","Lon Lon Milk","Ruto's Letter","Blue Fire","Bug","Big Poe","Half Milk","Poe","Weird Egg","Chicken","Zelda's Letter","Keaton Mask","Skull Mask","Spooky Mask","Bunny Hood","Goron Mask","Zora Mask","Gerudo Mask","Mask of Truth","Sold Out","Pocket Egg","Pocket Cucco","Cojiro","Odd Mushroom","Odd Potion","Poacher's Saw","Broken Goron's Sword","Prescription","Eyeball Frog","Eye Drops","Claim Check"}[id];
 void EmitTrackerEvents(GameSnapshotAdapterResult snapshot)
 {
  var now=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var x in snapshot.Inventory.Value.Slots.Where(x=>x.Owned&&!string.Equals(x.ItemName,"Empty Slot",StringComparison.OrdinalIgnoreCase)))now.Add("I:"+x.ItemName);
  foreach(var x in snapshot.Equipment.Value.Entries.Where(x=>x.Owned))now.Add("E:"+x.Id);
  foreach(var x in snapshot.QuestItems.Value.Entries.Where(x=>x.Owned))now.Add("Q:"+x.Id);
  foreach(var x in snapshot.Songs.Value.Entries.Where(x=>x.Owned))now.Add("S:"+x.Id);
  if(_previousOwnedState is not null)foreach(var key in now.Except(_previousOwnedState).Take(8)){var name=key[2..];Log(key.StartsWith("S:")?$"{name} gelernt":$"{name} erhalten");}
  _previousOwnedState=now;
 }
 void RenderDungeonCompact(GameSnapshotAdapterResult snapshot)
 {
  var names=new[]{"Deku Tree","Dodongo's Cavern","Jabu-Jabu","Forest Temple","Fire Temple","Water Temple","Spirit Temple","Shadow Temple","Bottom of the Well","Ice Cavern","Ganon's Castle"};
  var entries=snapshot.DungeonItems.Value.Entries; var keys=snapshot.DungeonKeys.Value.Keys;
  for(int i=0;i<entries.Count&&i<names.Length;i++)
  {
   var e=entries[i]; var parts=new List<string>();
   if(e.Map==true)parts.Add("M"); if(e.Compass==true)parts.Add("C"); if(e.BossKey==true)parts.Add("B");
   if(i<keys.Count&&keys[i]>0)parts.Add($"K{keys[i]}");
   var owned=parts.Count>0; var badge=owned?string.Join("·",parts):"";
   AddTrackerTile(names[i],owned,badge,"Assets/Tracker/Dungeons/dungeonMap.bmp",snapshot.DungeonItems.Evidence);
  }
 }
 void RenderDungeonTracker(GameSnapshotAdapterResult snapshot)
 {
  string[] names={"Deku Tree","Dodongo's Cavern","Jabu-Jabu","Forest Temple","Fire Temple","Water Temple","Spirit Temple","Shadow Temple","Bottom of the Well","Ice Cavern","Ganon's Castle"};
  var entries=snapshot.DungeonItems.Value.Entries;var keys=snapshot.DungeonKeys.Value.Keys;
  for(int i=0;i<entries.Count&&i<names.Length;i++){var e=entries[i];var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(2,2,2,3),ToolTip=names[i]};row.Children.Add(new TextBlock{Text=names[i],Width=112,FontSize=10,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});void I(string label,bool? owned,string asset){if(owned is null)return;row.Children.Add(CreateTrackerTile(label,owned==true,"",asset,snapshot.DungeonItems.Evidence,32));}I("Map",e.Map,"Assets/Tracker/Dungeons/dungeonMap.bmp");I("Compass",e.Compass,"Assets/Tracker/Dungeons/dungeonCompass.bmp");I("Boss Key",e.BossKey,"Assets/Tracker/Dungeons/dungeonBossKey.bmp");if(i<keys.Count)row.Children.Add(new TextBlock{Text=$"🔑 {keys[i]}",Width=42,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,FontSize=10,Foreground=System.Windows.Media.Brushes.Wheat});TrackerLiveGrid.Children.Add(row);}
 }
 void RenderTrackerWorkspace(GameSnapshotAdapterResult snapshot,string readiness)
 {
  TrackerWorkspaceLiveGrid.Children.Clear();TrackerWorkspaceDungeonPanel.Children.Clear();
  var inventory=snapshot.Inventory.Value.Slots.Where(x=>x.ItemId<=55&&!string.Equals(x.ItemName,"Empty Slot",StringComparison.OrdinalIgnoreCase)).GroupBy(x=>x.ItemId).ToDictionary(g=>g.Key,g=>g.First());
  var equipment=snapshot.Equipment.Value.Entries.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
  var upgrades=snapshot.Upgrades.Value.Entries.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
  var quests=snapshot.QuestItems.Value.Entries.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
  var songs=snapshot.Songs.Value.Entries.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
  void Tile(string label,bool owned,string badge,string? asset,SnapshotEvidence evidence){TrackerWorkspaceLiveGrid.Children.Add(CreateTrackerTile(label,owned,badge,asset,evidence,54));}
  foreach(byte id in new byte[]{0,1,2,3,4,5,6,9,12,13,14,15,16,17,18,19}){inventory.TryGetValue(id,out var v);var name=ItemName(id);Tile(name,v?.Owned==true,v is not null&&v.Quantity>1?$"×{v.Quantity}":"",TrackerAsset(name),snapshot.Inventory.Evidence);}
  InventorySlotSnapshot? oot=null;InventorySlotSnapshot? fairy=null;inventory.TryGetValue(8,out oot);inventory.TryGetValue(7,out fairy);var ocarina=oot?.Owned==true?8:7;Tile(ocarina==8?"Ocarina of Time":"Fairy Ocarina",oot?.Owned==true||fairy?.Owned==true,oot?.Owned==true?"2/2":fairy?.Owned==true?"1/2":"",TrackerAsset(ocarina==8?"Ocarina of Time":"Fairy Ocarina"),snapshot.Inventory.Evidence);
  inventory.TryGetValue(11,out var longshot);inventory.TryGetValue(10,out var hookshot);var longOwned=longshot?.Owned==true;Tile(longOwned?"Longshot":"Hookshot",longOwned||hookshot?.Owned==true,longOwned?"2/2":hookshot?.Owned==true?"1/2":"",TrackerAsset(longOwned?"Longshot":"Hookshot"),snapshot.Inventory.Evidence);
  var bottles=inventory.Values.Where(x=>x.ItemId is >=20 and <=32&&x.Owned).ToArray();var bottleCount=Math.Min(4,bottles.Length);Tile("Bottle",bottleCount>0,bottleCount>0?$"×{bottleCount}":"",TrackerAsset("Empty Bottle"),snapshot.Inventory.Evidence);var content=bottles.FirstOrDefault(x=>x.ItemId>20);var contentName=content?.ItemName??"Empty Bottle";Tile(contentName,content is not null,content is null?"":"IN",TrackerAsset(contentName),snapshot.Inventory.Evidence);
  void Trade(string label,byte first,byte last){var v=inventory.Values.FirstOrDefault(x=>x.ItemId>=first&&x.ItemId<=last&&x.Owned);var name=v?.ItemName??ItemName(first);Tile(label,v is not null,v?.ItemName??"",TrackerAsset(name),snapshot.Inventory.Evidence);}Trade("Child Trade",33,44);Trade("Adult Trade",45,55);
  foreach(var id in new[]{"Kokiri Sword","Master Sword","Biggoron's Sword","Deku Shield","Hylian Shield","Mirror Shield","Kokiri Tunic","Goron Tunic","Zora Tunic","Kokiri Boots","Iron Boots","Hover Boots"}){equipment.TryGetValue(id,out var v);Tile(id,v?.Owned==true,"",TrackerAsset(id),snapshot.Equipment.Evidence);}foreach(var id in new[]{"Quiver","Bomb Bag","Gauntlet","Scale","Wallet","Bullet Bag"}){upgrades.TryGetValue(id,out var v);Tile(id,v is not null&&v.Level>0,v is not null&&v.Level>0?$"Lv{v.Level}":"",TrackerAsset(id,v?.Level??0),snapshot.Upgrades.Evidence);}
  foreach(var id in new[]{"Kokiri Emerald","Goron Ruby","Zora Sapphire","Forest Medallion","Fire Medallion","Water Medallion","Spirit Medallion","Shadow Medallion","Light Medallion","Stone of Agony","Gerudo's Membership Card"}){quests.TryGetValue(id,out var v);Tile(id,v?.Owned==true,"",TrackerAsset(id),snapshot.QuestItems.Evidence);}
  foreach(var id in new[]{"Minuet of Forest","Bolero of Fire","Serenade of Water","Requiem of Spirit","Nocturne of Shadow","Prelude of Light","Zelda's Lullaby","Epona's Song","Saria's Song","Sun's Song","Song of Time","Song of Storms"}){songs.TryGetValue(id,out var v);var tile=CreateTrackerTile(id,v?.Owned==true,"♪",TrackerAsset(id),snapshot.Songs.Evidence,54);tile.Tag=id;tile.Cursor=Cursors.Hand;tile.MouseLeftButtonUp+=SongTile_Click;TrackerWorkspaceLiveGrid.Children.Add(tile);}
  string[] names={"Deku Tree","Dodongo's Cavern","Jabu-Jabu","Forest Temple","Fire Temple","Water Temple","Spirit Temple","Shadow Temple","Bottom of the Well","Ice Cavern","Ganon's Castle"};var entries=snapshot.DungeonItems.Value.Entries;var keys=snapshot.DungeonKeys.Value.Keys;for(int i=0;i<entries.Count&&i<names.Length;i++){var e=entries[i];var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(2,3,2,4),ToolTip=names[i]};row.Children.Add(new TextBlock{Text=names[i],Width=130,FontSize=10,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});void D(string label,bool? owned,string asset){if(owned is not null)row.Children.Add(CreateTrackerTile(label,owned==true,"",asset,snapshot.DungeonItems.Evidence,34));}D("Map",e.Map,"Assets/Tracker/Dungeons/dungeonMap.bmp");D("Compass",e.Compass,"Assets/Tracker/Dungeons/dungeonCompass.bmp");D("Boss Key",e.BossKey,"Assets/Tracker/Dungeons/dungeonBossKey.bmp");if(i<keys.Count)row.Children.Add(new TextBlock{Text=$"🔑 {keys[i]}",Width=46,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,FontSize=10,Foreground=System.Windows.Media.Brushes.Wheat});TrackerWorkspaceDungeonPanel.Children.Add(row);}
  var ownedInventory=snapshot.Inventory.Value.Slots.Count(x=>x.Owned&&!string.Equals(x.ItemName,"Empty Slot",StringComparison.OrdinalIgnoreCase));var ownedEquipment=snapshot.Equipment.Value.Entries.Count(x=>x.Owned);var up=snapshot.Upgrades.Value.Entries.Count(x=>x.Level>0);var q=snapshot.QuestItems.Value.Entries.Count(x=>x.Owned);var so=snapshot.Songs.Value.Entries.Count(x=>x.Owned);var checkedCount=snapshot.Checks.Value.Values.Count(x=>x.State.ToString()=="Checked");var knownChecks=snapshot.Checks.Value.Values.Count(x=>x.State.ToString()!="Unknown");var dungeonDone=snapshot.DungeonItems.Value.Entries.Count(x=>x.Map==true&&x.Compass==true&&(x.BossKey==true||x.BossKey is null));
  TrackerWorkspaceItemsText.Text=$"Items {ownedInventory}";TrackerWorkspaceEquipmentText.Text=$"Ausrüstung {ownedEquipment}+{up}";TrackerWorkspaceSongsText.Text=$"Songs {so} / 12";TrackerWorkspaceQuestText.Text=$"Quest {q}";TrackerWorkspaceDungeonsText.Text=$"Dungeons {dungeonDone}";TrackerWorkspaceChecksText.Text=$"Checks {checkedCount} / {knownChecks}";TrackerWorkspaceStateText.Text=$"{readiness} • {UiLocalization.T(snapshot.Game.Evidence.ToString())} • Live-Daten aus derselben Spielsitzung wie Play";TrackerWorkspaceEvidenceText.Text=$"Status: {UiLocalization.T(snapshot.Game.Evidence.ToString())}";TrackerWorkspaceCapturedText.Text=$"Datenstand: {snapshot.CapturedAt:HH:mm:ss}";TrackerWorkspaceRegionText.Text=$"Region: {snapshot.Map.Value.AreaCode} • {snapshot.Map.Value.RegionKind}";
 }
 void SetTrackerTab(string tab){if(_lastTrackerSnapshot is not null)RenderTrackerFrame(_lastTrackerSnapshot,_lastTrackerReadiness);}
 void UpdateTrackerTabVisuals() { }
 static System.Windows.Media.Brush Brush(string hex)=>(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
 void SetLiveMinimap(GameSnapshotAdapterResult snapshot)
 {
  var map=snapshot.Map.Value;var asset=map.MinimapAsset;
  var zoomKey=$"{map.LastMinimap}:{map.Room?.ToString()??"-"}";if(_minimapZoomKey!=zoomKey){_minimapZoomKey=zoomKey;ResetMinimapView();_selectedMapCheck=null;}
  MinimapOverlay.Children.Clear();MinimapOverlayViewbox.Visibility=Visibility.Collapsed;
  if(string.IsNullOrWhiteSpace(asset)){LiveMinimapImage.Source=null;LiveMinimapImage.Visibility=Visibility.Collapsed;WorldMapImage.Visibility=Visibility.Visible;WorldMapShade.Visibility=Visibility.Visible;MapAreaCodeText.Visibility=Visibility.Visible;RenderRegionRows(snapshot);RenderEntranceHint(snapshot);return;}
  try
  {
   var file=System.IO.Path.GetFileName(asset.Replace('\\','/'));
   LiveMinimapImage.Source=new System.Windows.Media.Imaging.BitmapImage(new Uri($"pack://application:,,,/TrackerOfTime.V2.M8.5.Desktop;component/Assets/Tracker/Maps/{file}",UriKind.Absolute));
   LiveMinimapImage.Visibility=Visibility.Visible;WorldMapImage.Visibility=Visibility.Collapsed;WorldMapShade.Visibility=Visibility.Collapsed;MapAreaCodeText.Visibility=Visibility.Collapsed;
   RenderV1MinimapMarkers(snapshot);MinimapOverlayViewbox.Visibility=Visibility.Visible;
  }
  catch{LiveMinimapImage.Source=null;LiveMinimapImage.Visibility=Visibility.Collapsed;WorldMapImage.Visibility=Visibility.Visible;WorldMapShade.Visibility=Visibility.Visible;MapAreaCodeText.Visibility=Visibility.Visible;}
  RenderRegionRows(snapshot);RenderEntranceHint(snapshot);
 }
 void RenderV1MinimapMarkers(GameSnapshotAdapterResult snapshot)
 {
  var map=snapshot.Map.Value;bool? mq=map.LastMinimap is >=0 and <=11 && snapshot.MasterQuest.Value.Flags.Count>map.LastMinimap?snapshot.MasterQuest.Value.Flags[map.LastMinimap]:null;
  foreach(var point in V1MinimapPresentationAdapter.For(map.LastMinimap,map.Room,mq))
  {
   snapshot.Checks.Value.TryGetValue(point.LocationId,out var state);var knowledge=state?.State.ToString()??"Unknown";var isChecked=knowledge=="Checked";var uncheckedKnown=knowledge=="Unchecked";
   var marker=new Border{Width=14,Height=14,CornerRadius=new CornerRadius(7),BorderThickness=new Thickness(_selectedMapCheck==point.LocationId?4:2.5),ToolTip=$"{UiLocalization.T(V1MinimapPresentationAdapter.Name(point.LocationId))} • {UiLocalization.T(knowledge)}",Tag=point.LocationId,Cursor=Cursors.Hand};marker.MouseLeftButtonUp+=MinimapCheck_Click;
   if(isChecked){marker.Background=Brush("#3B62E879");marker.BorderBrush=Brush("#62E879");}
   else if(uncheckedKnown){marker.Background=Brush("#D9B94848");marker.BorderBrush=Brush("#FFE8C8");}
   else{marker.Background=Brushes.Transparent;marker.BorderBrush=Brush("#8A918D");marker.Opacity=.7;}
   Canvas.SetLeft(marker,point.X-7);Canvas.SetTop(marker,point.Y-7);MinimapOverlay.Children.Add(marker);
  }
  // V1 contains a verified world->minimap calibration for Haunted Wasteland (map 94).
  // It is intentionally retained as the only verified player-position projection in the current V2 scope.
  // General map calibration/player tracking is postponed to a later update rather than approximated here.
  if(map.LastMinimap==94 && snapshot.PlayerPosition.Value is { Rotation:int rotation } pos)
  {
   double cx=((pos.X+4550)/8200)*400+53,cy=((pos.Z+3750)/8200)*400+17;double ha=(((rotation/65535d*360)-90)*-1)*Math.PI/180,ta=(((rotation/65535d*360)+90)*-1)*Math.PI/180;
   MinimapOverlay.Children.Add(new Line{X1=8*Math.Cos(ta)+cx,Y1=8*Math.Sin(ta)+cy,X2=8*Math.Cos(ha)+cx,Y2=8*Math.Sin(ha)+cy,Stroke=Brush("#F4D24E"),StrokeThickness=4,ToolTip="Spielerposition • Haunted Wasteland"});
  }
 }

 void MinimapCheck_Click(object sender,MouseButtonEventArgs e)
 {
  if(sender is not Border { Tag:string id } selected)return;_selectedMapCheck=id;foreach(var child in MinimapOverlay.Children.OfType<Border>())if(child.Tag is string)child.BorderThickness=new Thickness(2.5);selected.BorderThickness=new Thickness(4);var name=UiLocalization.T(V1MinimapPresentationAdapter.Name(id));var state="Unknown";if(_lastTrackerSnapshot?.Checks.Value.TryGetValue(id,out var st)==true)state=st.State.ToString();Log($"{(UiLocalization.German?"Karten-Check":"Map check")}: {name} • {UiLocalization.T(state)}");e.Handled=true;
 }
 void Minimap_MouseWheel(object sender,MouseWheelEventArgs e)
 {
  if(LiveMinimapImage.Visibility!=Visibility.Visible)return;double old=MinimapScale.ScaleX,next=Math.Clamp(old+(e.Delta>0?.25:-.25),1,5);if(Math.Abs(next-old)<.001)return;var p=e.GetPosition(MinimapZoomSurface);double factor=next/old;MinimapTranslate.X=p.X-(p.X-MinimapTranslate.X)*factor;MinimapTranslate.Y=p.Y-(p.Y-MinimapTranslate.Y)*factor;MinimapScale.ScaleX=MinimapScale.ScaleY=next;if(next<=1.001){MinimapTranslate.X=MinimapTranslate.Y=0;}e.Handled=true;
 }
 void Minimap_RightButtonDown(object sender,MouseButtonEventArgs e){if(MinimapScale.ScaleX<=1.001)return;_minimapPanning=true;_minimapPanStart=e.GetPosition(this);_minimapPanX=MinimapTranslate.X;_minimapPanY=MinimapTranslate.Y;MinimapZoomSurface.CaptureMouse();e.Handled=true;}
 void Minimap_MouseMove(object sender,MouseEventArgs e){if(!_minimapPanning)return;var p=e.GetPosition(this);MinimapTranslate.X=_minimapPanX+(p.X-_minimapPanStart.X);MinimapTranslate.Y=_minimapPanY+(p.Y-_minimapPanStart.Y);}
 void Minimap_RightButtonUp(object sender,MouseButtonEventArgs e){if(!_minimapPanning)return;_minimapPanning=false;MinimapZoomSurface.ReleaseMouseCapture();e.Handled=true;}
 void ResetMinimapView(){MinimapScale.ScaleX=MinimapScale.ScaleY=1;MinimapTranslate.X=MinimapTranslate.Y=0;_minimapPanning=false;}
  static readonly string[] V1RegionCodes={"KF","LW","SFM","HF","LLR","MK","TT","HC","KV","GY","DMT","DMC","GC","ZR","ZD","ZF","LH","GV","GF","HW","DC","OGC"};
 void RenderRegionRows(GameSnapshotAdapterResult snapshot)
 {
  RegionRowsPanel.Children.Clear();var checks=snapshot.Checks.Value;var rows=V1RegionCodes.Select(code=>{var ids=V1MinimapPresentationAdapter.Metadata.Values.Where(m=>m.Area==code).Select(m=>m.Id).Distinct().ToArray();var states=ids.Select(id=>checks.TryGetValue(id,out var st)?st:null).Where(x=>x is not null).ToArray();int total=states.Count(x=>x!.State.ToString()!="Unknown"),done=states.Count(x=>x!.State.ToString()=="Checked");return(code,total,done);}).Where(x=>x.total>0||x.code==snapshot.Map.Value.AreaCode).ToList();
  foreach(var r in rows.Take(8)){var fg=r.total>0&&r.done==r.total?"#55C96B":r.done>0?"#E0A43A":"#B84C4C";RegionRowsPanel.Children.Add(new TextBlock{Text=$"● {r.code}  {r.done}/{r.total}",Foreground=Brush(fg),FontSize=9,Margin=new Thickness(3,1,3,1),ToolTip=RegionName(r.code)});}
 }
 void RenderEntranceHint(GameSnapshotAdapterResult snapshot)
 {
  var er=snapshot.EntranceRandomizer.Value;
  if(_v1EntranceState is not null && _v1EntranceState.Mode!=0)
  {
   var labels=V1EntranceDisplayAdapter.Resolve(snapshot.Map.Value.LocationCode,_v1EntranceState);
   foreach(var l in labels)
   {
    var tb=new TextBlock{Text=l.Text,Foreground=l.DestinationVisited?Brush("#F4D24E"):Brush("#F0E6C2"),Background=Brush("#B0080D0A"),FontWeight=FontWeights.Bold,FontSize=13,Padding=new Thickness(2,0,2,0),ToolTip=l.DestinationVisited?$"Ausgang gelernt: {l.Text}":"Unbekannter Ausgang • erst durchqueren"};
    Canvas.SetLeft(tb,l.X);Canvas.SetTop(tb,l.Y);MinimapOverlay.Children.Add(tb);
   }
   EntranceHintText.Text=labels.Count==0?(er.OverworldMemoryFlag is >0?"ER Oberwelt aktiv":"ER Dungeons aktiv"):string.Join("  •  ",labels.Select(x=>$"Ausgang {x.Slot+1}: {x.Text}"));
  }
  else EntranceHintText.Text="";
 }
 static string RegionName(string c)=>c switch{"KF"=>"Kokiri Forest","LW"=>"Lost Woods","SFM"=>"Sacred Forest Meadow","HF"=>"Hyrule Field","LLR"=>"Lon Lon Ranch","MK"=>"Market","TT"=>"Temple of Time","HC"=>"Hyrule Castle","KV"=>"Kakariko Village","GY"=>"Graveyard","DMT"=>"Death Mountain Trail","DMC"=>"Death Mountain Crater","GC"=>"Goron City","ZR"=>"Zora's River","ZD"=>"Zora's Domain","ZF"=>"Zora's Fountain","LH"=>"Lake Hylia","GV"=>"Gerudo Valley","GF"=>"Gerudo Fortress","HW"=>"Haunted Wasteland","DC"=>"Desert Colossus","OGC"=>"Outside Ganon's Castle",_=>c};

 Border AddTrackerTile(string label,bool owned,string badge,string? asset,SnapshotEvidence evidence){var tile=CreateTrackerTile(label,owned,badge,asset,evidence,38);TrackerLiveGrid.Children.Add(tile);return tile;}
 Border CreateTrackerTile(string label,bool owned,string badge,string? asset,SnapshotEvidence evidence,double size)
 {
  var panel=new Grid();
  if(asset is not null){try{var img=new System.Windows.Controls.Image{Source=new System.Windows.Media.Imaging.BitmapImage(new Uri($"pack://application:,,,/TrackerOfTime.V2.M8.5.Desktop;component/{asset}")),Stretch=System.Windows.Media.Stretch.Uniform,Margin=new Thickness(3),Opacity=owned?1.0:0.18};panel.Children.Add(img);}catch{}}
  if(!string.IsNullOrWhiteSpace(badge))panel.Children.Add(new TextBlock{Text=badge,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(1),FontSize=8,FontWeight=FontWeights.Bold,Foreground=owned?System.Windows.Media.Brushes.White:System.Windows.Media.Brushes.Gray});
  return new Border{Width=size,Height=size,Margin=new Thickness(2),Padding=new Thickness(1),CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(1),BorderBrush=(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(owned?"#C99C31":"#3D351F")!,Background=(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(owned?"#101A13":"#050A08")!,Child=panel,ToolTip=$"{label} • {(owned?"vorhanden":"nicht vorhanden")} • {evidence}"};
 }
 string? TrackerAsset(string id,int level=0)=>id switch
 {
  "Deku Stick"=>"Assets/Tracker/Items/dekuStick.bmp","Deku Nut"=>"Assets/Tracker/Items/dekuNut.bmp","Bombs"=>"Assets/Tracker/Items/bombs.bmp","Bow"=>"Assets/Tracker/Items/bow.bmp","Fire Arrow"=>"Assets/Tracker/Items/fireArrow.bmp","Din's Fire"=>"Assets/Tracker/Items/dinsFire.bmp","Fairy Slingshot"=>"Assets/Tracker/Items/fairySlingshot.bmp","Ocarina of Time"=>"Assets/Tracker/Items/ocarinaOfTime.bmp","Bombchus"=>"Assets/Tracker/Items/bombchus.bmp","Hookshot"=>"Assets/Tracker/Items/hookshot.bmp","Ice Arrows"=>"Assets/Tracker/Items/iceArrows.bmp","Farore's Wind"=>"Assets/Tracker/Items/faroresWind.bmp","Boomerang"=>"Assets/Tracker/Items/boomerang.bmp","Lens of Truth"=>"Assets/Tracker/Items/lensOfTruth.bmp","Magic Beans"=>"Assets/Tracker/Items/magicBeans.bmp","Megaton Hammer"=>"Assets/Tracker/Items/megatonHammer.bmp","Light Arrows"=>"Assets/Tracker/Items/lightArrows.bmp","Empty Bottle"=>"Assets/Tracker/Items/bottleEmpty.bmp","Fairy Ocarina"=>"Assets/Tracker/Items/fairyOcarina.bmp","Longshot"=>"Assets/Tracker/Items/longshot.bmp","Red Potion"=>"Assets/Tracker/Items/bottleRedPotion.bmp","Green Potion"=>"Assets/Tracker/Items/bottleGreenPotion.bmp","Blue Potion"=>"Assets/Tracker/Items/bottleBluePotion.bmp","Bottled Fairy"=>"Assets/Tracker/Items/bottleBottledFairy.bmp","Fish"=>"Assets/Tracker/Items/bottleFish.bmp","Lon Lon Milk"=>"Assets/Tracker/Items/bottleLonLonMilk.bmp","Ruto's Letter"=>"Assets/Tracker/Items/bottleLetter.bmp","Blue Fire"=>"Assets/Tracker/Items/bottleBlueFire.bmp","Bug"=>"Assets/Tracker/Items/bottleBug.bmp","Big Poe"=>"Assets/Tracker/Items/bottleBigPoe.bmp","Half Milk"=>"Assets/Tracker/Items/bottleLonLonMilkHalf.bmp","Poe"=>"Assets/Tracker/Items/bottlePoe.bmp","Weird Egg"=>"Assets/Tracker/Items/youngWeirdEgg.bmp","Chicken"=>"Assets/Tracker/Items/youngChicken.bmp","Zelda's Letter"=>"Assets/Tracker/Items/youngZeldasLetter.bmp","Keaton Mask"=>"Assets/Tracker/Items/youngKeatonMask.bmp","Skull Mask"=>"Assets/Tracker/Items/youngSkullMask.bmp","Spooky Mask"=>"Assets/Tracker/Items/youngSpookyMask.bmp","Bunny Hood"=>"Assets/Tracker/Items/youngBunnyHood.bmp","Goron Mask"=>"Assets/Tracker/Items/youngGoronMask.bmp","Zora Mask"=>"Assets/Tracker/Items/youngZoraMask.bmp","Gerudo Mask"=>"Assets/Tracker/Items/youngGerudoMask.bmp","Mask of Truth"=>"Assets/Tracker/Items/youngMaskOfTruth.bmp","Sold Out"=>"Assets/Tracker/Items/youngSoldOut.bmp","Pocket Egg"=>"Assets/Tracker/Items/adultPocketEgg.bmp","Pocket Cucco"=>"Assets/Tracker/Items/adultPocketCucco.bmp","Cojiro"=>"Assets/Tracker/Items/adultCojiro.bmp","Odd Mushroom"=>"Assets/Tracker/Items/adultOddMushroom.bmp","Odd Potion"=>"Assets/Tracker/Items/adultOddPotion.bmp","Poacher's Saw"=>"Assets/Tracker/Items/adultPoachersSaw.bmp","Broken Goron's Sword"=>"Assets/Tracker/Items/adultGoronsSwordBroken.bmp","Prescription"=>"Assets/Tracker/Items/adultPrescription.bmp","Eyeball Frog"=>"Assets/Tracker/Items/adultEyeballFrog.bmp","Eye Drops"=>"Assets/Tracker/Items/adultEyeDrops.bmp","Claim Check"=>"Assets/Tracker/Items/adultClaimCheck.bmp",
  "Kokiri Sword"=>"Assets/Tracker/Equipment/equipKokiriSword.bmp","Master Sword"=>"Assets/Tracker/Equipment/equipMasterSword.bmp","Biggoron's Sword"=>"Assets/Tracker/Equipment/equipBiggoronsSword.bmp","Deku Shield"=>"Assets/Tracker/Equipment/equipDekuShield.bmp","Hylian Shield"=>"Assets/Tracker/Equipment/equipHylianShield.bmp","Mirror Shield"=>"Assets/Tracker/Equipment/equipMirrorShield.bmp","Kokiri Tunic"=>"Assets/Tracker/Equipment/equipKokiriTunic.bmp","Goron Tunic"=>"Assets/Tracker/Equipment/equipGoronTunic.bmp","Zora Tunic"=>"Assets/Tracker/Equipment/equipZoraTunic.bmp","Kokiri Boots"=>"Assets/Tracker/Equipment/equipKokiriBoots.bmp","Iron Boots"=>"Assets/Tracker/Equipment/equipIronBoots.bmp","Hover Boots"=>"Assets/Tracker/Equipment/equipHoverBoots.bmp",
  "Kokiri Emerald"=>"Assets/Tracker/Quest/stoneKokiri.bmp","Goron Ruby"=>"Assets/Tracker/Quest/stoneGoron.bmp","Zora Sapphire"=>"Assets/Tracker/Quest/stoneZora.bmp","Forest Medallion"=>"Assets/Tracker/Quest/medalForest.bmp","Fire Medallion"=>"Assets/Tracker/Quest/medalFire.bmp","Water Medallion"=>"Assets/Tracker/Quest/medalWater.bmp","Spirit Medallion"=>"Assets/Tracker/Quest/medalSpirit.bmp","Shadow Medallion"=>"Assets/Tracker/Quest/medalShadow.bmp","Light Medallion"=>"Assets/Tracker/Quest/medalLight.bmp","Stone of Agony"=>"Assets/Tracker/Quest/stoneOfAgony.bmp","Gerudo's Membership Card"=>"Assets/Tracker/Quest/gerudosCard.bmp",
  "Minuet of Forest"=>"Assets/Tracker/Songs/songForest.bmp","Bolero of Fire"=>"Assets/Tracker/Songs/songFire.bmp","Serenade of Water"=>"Assets/Tracker/Songs/songWater.bmp","Requiem of Spirit"=>"Assets/Tracker/Songs/songSpirit.bmp","Nocturne of Shadow"=>"Assets/Tracker/Songs/songShadow.bmp","Prelude of Light"=>"Assets/Tracker/Songs/songLight.bmp","Zelda's Lullaby"=>"Assets/Tracker/Songs/songNormal.bmp","Epona's Song"=>"Assets/Tracker/Songs/songNormal.bmp","Saria's Song"=>"Assets/Tracker/Songs/songNormal.bmp","Sun's Song"=>"Assets/Tracker/Songs/songNormal.bmp","Song of Time"=>"Assets/Tracker/Songs/songNormal.bmp","Song of Storms"=>"Assets/Tracker/Songs/songNormal.bmp",
  "Quiver"=>$"Assets/Tracker/Equipment/upgradeQuiver{Math.Clamp(level,1,3)}.bmp","Bomb Bag"=>$"Assets/Tracker/Equipment/upgradeBombBag{Math.Clamp(level,1,3)}.bmp","Wallet"=>$"Assets/Tracker/Equipment/upgradeWallet{Math.Clamp(level,1,3)}.bmp","Bullet Bag"=>$"Assets/Tracker/Equipment/upgradeBulletBag{Math.Clamp(level,1,3)}.bmp","Gauntlet"=>level>=3?"Assets/Tracker/Equipment/upgradeGoldenGauntlets.bmp":level>=2?"Assets/Tracker/Equipment/upgradeSilverGauntlets.bmp":level>=1?"Assets/Tracker/Equipment/upgradeGoronsBracelet.bmp":null,"Scale"=>level>=2?"Assets/Tracker/Equipment/upgradeGoldenScale.bmp":level>=1?"Assets/Tracker/Equipment/upgradeSilverScale.bmp":null,
  _=>null
 };

 string Renderer()=>((ComboBoxItem)RendererBox.SelectedItem).Content.ToString()!; void Log(string t){EventLog.Items.Insert(0,new TextBlock{Text=$"[{DateTime.Now:HH:mm:ss}]  {UiLocalization.T(t)}",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#CFD8D1"),Margin=new Thickness(0,1,0,1)});while(EventLog.Items.Count>30)EventLog.Items.RemoveAt(EventLog.Items.Count-1);} void Refresh(){StartButton.IsEnabled=!_busy&&_session is null&&_plan is not null;ChooseRomButton.IsEnabled=!_busy&&_session is null;var canConfigure=!_busy&&_session is null;RendererBox.IsHitTestVisible=canConfigure;RendererBox.Focusable=canConfigure;ControllerBox.IsEnabled=canConfigure;StopButton.IsEnabled=!_busy&&_session is not null;TrackerButton.IsEnabled=!_busy&&_session is not null&&_plan?.StartTrackerWhenLive==true;UiLocalization.Apply(this);} void SetBusy(bool b){_busy=b;Refresh();}
 async Task DisposeSession(){if(_session is null)return;await _session.DisposeAsync();_session=null;} async void Window_Closing(object? s,CancelEventArgs e){if(_closing)return;_trackerWatch.Stop();_inputWatch.Stop();e.Cancel=true;_closing=true;SetBusy(true);try{GameHost.Detach();await DisposeSession();if(_randomizerWorkspace is not null)await _randomizerWorkspace.ShutdownAsync();}finally{Close();}}
 void TitleBar_MouseLeftButtonDown(object s,MouseButtonEventArgs e){if(e.ButtonState==MouseButtonState.Pressed){if(e.ClickCount==2)ToggleMax();else DragMove();}} void Minimize_Click(object s,RoutedEventArgs e)=>WindowState=WindowState.Minimized;void Maximize_Click(object s,RoutedEventArgs e){ToggleMax();Keyboard.ClearFocus();}void ToggleMax(){WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;Dispatcher.BeginInvoke(new Action(()=>{Keyboard.ClearFocus();GameHost.SyncBounds();}),System.Windows.Threading.DispatcherPriority.Loaded);} void Close_Click(object s,RoutedEventArgs e)=>Close();

 void Screenshot_Click(object s,RoutedEventArgs e)
 {
  try
  {
   var dpi=VisualTreeHelper.GetDpi(this);var width=Math.Max(1,(int)(ActualWidth*dpi.DpiScaleX));var height=Math.Max(1,(int)(ActualHeight*dpi.DpiScaleY));
   var bmp=new RenderTargetBitmap(width,height,96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bmp.Render(this);
   var dir=System.IO.Path.Combine(_paths.Root,"Screenshots");Directory.CreateDirectory(dir);var file=System.IO.Path.Combine(dir,$"TrackerOfTime-{DateTime.Now:yyyyMMdd-HHmmss}.png");
   var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using(var fs=File.Create(file))enc.Save(fs);Log($"Screenshot gespeichert: {file}");
  }catch(Exception ex){MessageBox.Show(ex.Message,"Screenshot",MessageBoxButton.OK,MessageBoxImage.Warning);}
 }
 void RenderDashboardWorkspaces(GameSnapshotAdapterResult snapshot,string readiness)
 {
  var checkedCount=snapshot.Checks.Value.Values.Count(x=>x.State.ToString()=="Checked");var knownChecks=snapshot.Checks.Value.Values.Count(x=>x.State.ToString()!="Unknown");var map=snapshot.Map.Value;
  HomeTrackerText.Text=$"{readiness} • {UiLocalization.T(snapshot.Game.Evidence.ToString())}";HomeRegionText.Text=$"Region: {map.AreaCode} • {map.RegionKind}";HomeChecksText.Text=$"Checks: {checkedCount} / {knownChecks}";HomeSnapshotText.Text=$"Datenstand: {snapshot.CapturedAt:HH:mm:ss}";
  WorldSnapshotText.Text=$"Datenstand: {snapshot.CapturedAt:HH:mm:ss}";WorldCurrentRegionText.Text=map.AreaCode;WorldAreaText.Text=$"{map.RegionKind} • Scene {map.LocationCode}";WorldChecksText.Text=$"Checks: {checkedCount} / {knownChecks}";WorldEvidenceText.Text=$"Status: {UiLocalization.T(snapshot.Checks.Evidence.ToString())}";
  RenderWorldChecks(snapshot);WorldRegionRowsPanel.Children.Clear();var points=V1MinimapPresentationAdapter.For(map.LastMinimap,map.Room,map.LastMinimap is >=0 and <=11&&snapshot.MasterQuest.Value.Flags.Count>map.LastMinimap?snapshot.MasterQuest.Value.Flags[map.LastMinimap]:null);
  foreach(var point in points){if(!snapshot.Checks.Value.TryGetValue(point.LocationId,out var state))continue;var st=state.State.ToString();if(st=="Unknown")continue;var label=st=="Checked"?"Checked":"Unchecked";var row=new TextBlock{Text=$"{UiLocalization.T(V1MinimapPresentationAdapter.Name(point.LocationId))}  •  {UiLocalization.T(label)}",Margin=new Thickness(2,3,2,3),TextWrapping=TextWrapping.Wrap,Foreground=Brush(st=="Checked"?"#55C96B":"#E0A43A")};WorldRegionRowsPanel.Children.Add(row);}if(WorldRegionRowsPanel.Children.Count==0)WorldRegionRowsPanel.Children.Add(new TextBlock{Text="Für die aktuelle Minimap liegen keine belastbaren Checkpunkte vor.",Foreground=Brush("#829087"),TextWrapping=TextWrapping.Wrap});
 }
 void RenderWorldChecks(GameSnapshotAdapterResult snapshot)
 {
  WorldChecksOverlay.Children.Clear();var anchors=new Dictionary<int,Point>{{81,new(282,159)},{82,new(335,113)},{83,new(358,102)},{84,new(429,134)},{85,new(448,254)},{86,new(416,213)},{87,new(234,278)},{88,new(458,169)},{89,new(493,128)},{90,new(116,180)},{91,new(399,183)},{92,new(326,78)},{93,new(70,121)},{94,new(35,94)},{95,new(281,88)},{96,new(358,65)},{97,new(382,57)},{98,new(410,80)},{99,new(264,207)},{100,new(290,82)}};
  foreach(var p in V1MinimapPresentationAdapter.OverworldPoints){if(!anchors.TryGetValue(p.Map,out var a)||!snapshot.Checks.Value.TryGetValue(p.LocationId,out var state))continue;var st=state.State.ToString();if(st=="Unknown")continue;var dx=(p.X-278)/278.0*24;var dy=(p.Y-208)/208.0*18;var box=new Border{Width=7,Height=7,Background=Brush(st=="Checked"?"#42D25A":"#D7473F"),BorderBrush=Brush("#F6E6A5"),BorderThickness=new Thickness(.7),ToolTip=$"{UiLocalization.T(V1MinimapPresentationAdapter.Name(p.LocationId))} • {UiLocalization.T(st)}",Tag=p.LocationId,Cursor=Cursors.Hand};box.MouseLeftButtonUp+=(_,_)=>{_selectedMapCheck=p.LocationId;Log($"{(UiLocalization.German?"Karten-Check":"World Check")}: {UiLocalization.T(V1MinimapPresentationAdapter.Name(p.LocationId))} • {UiLocalization.T(st)}");};Canvas.SetLeft(box,a.X+dx-3.5);Canvas.SetTop(box,a.Y+dy-3.5);WorldChecksOverlay.Children.Add(box);}
 }
 void RefreshHomeWorkspace(){HomeGameText.Text=_plan is null?"Keine ROM ausgewählt":(string.IsNullOrWhiteSpace(_plan.Inspection.InternalName)?"Unbenannte N64 ROM":_plan.Inspection.InternalName.Trim());HomeProfileText.Text=$"Profil: {(_plan is null?"—":_plan.Inspection.GameProfile)}";HomeSessionText.Text=_session is null?"Session: bereit":"Session: läuft";HomeRendererText.Text=$"Renderer: {Renderer()}";HomeInputText.Text=$"Input: {(ControllerBox.IsChecked==true?"Direct-XInput":"Input-SDL / Keyboard")}";if(_lastTrackerSnapshot is not null)RenderDashboardWorkspaces(_lastTrackerSnapshot,_lastTrackerReadiness);}
 void SyncSettingsFromPlay(){SettingsRendererBox.SelectedIndex=RendererBox.SelectedIndex;SettingsControllerBox.IsChecked=ControllerBox.IsChecked;var canChangeRenderer=!_busy&&_session is null;var canConfigureInput=!_busy;SettingsRendererBox.IsEnabled=canChangeRenderer;SettingsControllerBox.IsEnabled=canChangeRenderer;InputBindingsPanel.IsEnabled=canConfigureInput;SaveInputBindingsButton.IsEnabled=canConfigureInput;ResetInputBindingsButton.IsEnabled=canConfigureInput;ConfigureControllerButton.IsEnabled=canConfigureInput;ConfigureKeyboardButton.IsEnabled=canConfigureInput;SettingsLockText.Text=_session is null?"Renderer und Eingabebelegung können geändert werden.":"Session läuft • Eingabebelegung kann live geändert werden; Renderer bleibt bis zum Stop gesperrt.";}
 readonly string[] _inputActions={"STICK_UP","STICK_DOWN","STICK_LEFT","STICK_RIGHT","DPAD_UP","DPAD_DOWN","DPAD_LEFT","DPAD_RIGHT","START","Z","B","A","C_UP","C_DOWN","C_LEFT","C_RIGHT","R","L"};
 readonly Dictionary<string,string> _inputLabels=new(){["STICK_UP"]="Analog ↑",["STICK_DOWN"]="Analog ↓",["STICK_LEFT"]="Analog ←",["STICK_RIGHT"]="Analog →",["DPAD_UP"]="D-Pad ↑",["DPAD_DOWN"]="D-Pad ↓",["DPAD_LEFT"]="D-Pad ←",["DPAD_RIGHT"]="D-Pad →",["START"]="Start",["Z"]="Z",["B"]="B",["A"]="A",["C_UP"]="C ↑",["C_DOWN"]="C ↓",["C_LEFT"]="C ←",["C_RIGHT"]="C →",["R"]="R",["L"]="L"};
 readonly Dictionary<string,TextBlock> _keyboardBindingValues=new(),_controllerBindingValues=new();
 string InputProfilePath=>System.IO.Path.Combine(_paths.Settings,"emulator-input.ini");
 static readonly string[] KeyboardChoices={"Up","Down","Left","Right","Enter","Space","A","B","C","D","E","F","G","H","I","J","K","L","Q","R","S","T","U","V","W","X","Y","Z","LeftShift","RightShift","LeftCtrl","RightCtrl","NumPad0","NumPad1","NumPad2","NumPad3","NumPad4","NumPad5","NumPad6","NumPad7","NumPad8","NumPad9","None"};
 static readonly string[] ControllerChoices={"DPadUp","DPadDown","DPadLeft","DPadRight","A","B","X","Y","LB","RB","Back","Start","LThumb","RThumb","LT","RT","LeftStickUp","LeftStickDown","LeftStickLeft","LeftStickRight","RightStickUp","RightStickDown","RightStickLeft","RightStickRight","None"};
 static readonly Dictionary<string,(string key,string pad)> DefaultBindings=new(){["STICK_UP"]=("Up","LeftStickUp"),["STICK_DOWN"]=("Down","LeftStickDown"),["STICK_LEFT"]=("Left","LeftStickLeft"),["STICK_RIGHT"]=("Right","LeftStickRight"),["DPAD_UP"]=("T","DPadUp"),["DPAD_DOWN"]=("G","DPadDown"),["DPAD_LEFT"]=("F","DPadLeft"),["DPAD_RIGHT"]=("H","DPadRight"),["START"]=("Enter","Start"),["Z"]=("S","LT"),["B"]=("X","X"),["A"]=("C","A"),["C_UP"]=("I","RightStickUp"),["C_DOWN"]=("K","RightStickDown"),["C_LEFT"]=("J","RightStickLeft"),["C_RIGHT"]=("L","RightStickRight"),["R"]=("D","RB"),["L"]=("A","LB")};
 void InitializeInputBindings()
 {
  InputProfilePathText.Text=InputProfilePath;InputBindingsPanel.Children.Clear();_keyboardBindingValues.Clear();_controllerBindingValues.Clear();var saved=LoadInputProfile();
  var head=new Grid{Margin=new Thickness(0,0,0,4)};head.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(105)});head.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});head.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
  var h1=new TextBlock{Text="N64",FontWeight=FontWeights.Bold,Foreground=Brush("#9DAA9F")};var h2=new TextBlock{Text="Tastatur",FontWeight=FontWeights.Bold,Foreground=Brush("#9DAA9F")};var h3=new TextBlock{Text="Controller",FontWeight=FontWeights.Bold,Foreground=Brush("#9DAA9F")};Grid.SetColumn(h2,1);Grid.SetColumn(h3,2);head.Children.Add(h1);head.Children.Add(h2);head.Children.Add(h3);InputBindingsPanel.Children.Add(head);
  foreach(var action in _inputActions){var row=new Grid{Margin=new Thickness(0,2,0,2)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(105)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});var label=new TextBlock{Text=_inputLabels[action],VerticalAlignment=VerticalAlignment.Center,Foreground=Brush("#E8D69E"),Margin=new Thickness(3,0,8,0)};row.Children.Add(label);var d=DefaultBindings[action];var kb=BindingValue(saved.TryGetValue("K_"+action,out var k)?k:d.key);Grid.SetColumn(kb,1);row.Children.Add(kb);var pad=BindingValue(saved.TryGetValue("P_"+action,out var pv)?pv:d.pad);Grid.SetColumn(pad,2);row.Children.Add(pad);_keyboardBindingValues[action]=kb;_controllerBindingValues[action]=pad;InputBindingsPanel.Children.Add(row);}  RefreshControllerVisual();
 }
 TextBlock BindingValue(string value)=>new(){Text=value,Margin=new Thickness(2),Padding=new Thickness(7,4,7,4),Background=Brush("#101815"),Foreground=Brush("#F4E7BE"),VerticalAlignment=VerticalAlignment.Center};
 void RefreshControllerVisual(string? activeAction=null)
 {
  if(ControllerVisualPanel is null)return;ControllerVisualPanel.Children.Clear();
  var actions=new[]{"STICK_UP","DPAD_UP","DPAD_DOWN","DPAD_LEFT","DPAD_RIGHT","START","Z","B","A","C_UP","C_DOWN","C_LEFT","C_RIGHT","R","L"};
  foreach(var a in actions){if(!_controllerBindingValues.TryGetValue(a,out var value))continue;var title=a=="STICK_UP"?"Analog":_inputLabels[a];var shown=a=="STICK_UP"?(value.Text.StartsWith("RightStick",StringComparison.OrdinalIgnoreCase)?"Right Stick X/Y":"Left Stick X/Y"):FriendlyPad(value.Text);var box=new Border{Background=Brush(a==activeAction?"#244C35":"#101815"),BorderBrush=Brush(a==activeAction?"#F1D477":"#725817"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Padding=new Thickness(6,4,6,4),Margin=new Thickness(2)};box.Child=new TextBlock{Text=$"{title}  →  {shown}",Foreground=Brush(a==activeAction?"#FFF0A8":"#E8D69E"),FontWeight=a==activeAction?FontWeights.Bold:FontWeights.Normal};ControllerVisualPanel.Children.Add(box);}
 }
 static string FriendlyPad(string value)=>value switch{"DPadUp"=>"D-Pad ↑","DPadDown"=>"D-Pad ↓","DPadLeft"=>"D-Pad ←","DPadRight"=>"D-Pad →","LeftStickUp"=>"Left Stick ↑","LeftStickDown"=>"Left Stick ↓","LeftStickLeft"=>"Left Stick ←","LeftStickRight"=>"Left Stick →","RightStickUp"=>"Right Stick ↑","RightStickDown"=>"Right Stick ↓","RightStickLeft"=>"Right Stick ←","RightStickRight"=>"Right Stick →","LT"=>"Left Trigger","RT"=>"Right Trigger","LB"=>"Left Shoulder","RB"=>"Right Shoulder",_=>value};

 async void ConfigureController_Click(object s,RoutedEventArgs e){ConfigureControllerButton.IsEnabled=false;ConfigureKeyboardButton.IsEnabled=false;try{await ShowN64ControllerConfiguratorAsync();}finally{ConfigureControllerButton.IsEnabled=true;ConfigureKeyboardButton.IsEnabled=true;}}
 async Task ShowN64ControllerConfiguratorAsync()
 {
  var w=new Window{Title="N64 Controller Configuration",Owner=this,Width=920,Height=650,MinWidth=820,MinHeight=590,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#07100D"),Foreground=Brush("#F4E7BE")};
  var root=new Grid{Margin=new Thickness(16)};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  var top=new Grid{Margin=new Thickness(0,0,0,12)};top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});top.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  var device=new StackPanel{Orientation=Orientation.Horizontal};device.Children.Add(new TextBlock{Text="INPUT DEVICE",Foreground=Brush("#E3BB4C"),FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)});var dev=new ComboBox{Width=270,SelectedIndex=0};dev.Items.Add("XInput Controller (Auto Detect)");device.Children.Add(dev);var refresh=new Button{Content="↻  Refresh",Margin=new Thickness(8,0,0,0),Padding=new Thickness(12,5,12,5)};device.Children.Add(refresh);top.Children.Add(device);var status=new TextBlock{Text="● Controller suchen …",Foreground=Brush("#9DAA9F"),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(status,1);top.Children.Add(status);root.Children.Add(top);
  var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(260)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(260)});Grid.SetRow(body,1);root.Children.Add(body);
  var left=new StackPanel{Margin=new Thickness(0,0,12,0)};var center=new Grid{Margin=new Thickness(8)};var right=new StackPanel{Margin=new Thickness(12,0,0,0)};Grid.SetColumn(center,1);Grid.SetColumn(right,2);body.Children.Add(left);body.Children.Add(center);body.Children.Add(right);
  var buttons=new Dictionary<string,Button>();
  Border Group(string title,params string[] actions){var sp=new StackPanel();sp.Children.Add(new TextBlock{Text=title,Foreground=Brush("#E3BB4C"),FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,6)});foreach(var a in actions){var row=new Grid{Margin=new Thickness(0,2,0,2)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(92)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});var lab=new TextBlock{Text=_inputLabels[a],VerticalAlignment=VerticalAlignment.Center,Foreground=Brush("#D8CDA9")};var b=new Button{Content=FriendlyPad(_controllerBindingValues[a].Text),Tag=a,Padding=new Thickness(7,4,7,4),HorizontalContentAlignment=HorizontalAlignment.Left,ToolTip="Klicken und anschließend die gewünschte Controller-Eingabe betätigen"};Grid.SetColumn(b,1);row.Children.Add(lab);row.Children.Add(b);sp.Children.Add(row);buttons[a]=b;}return new Border{Background=Brush("#0B1511"),BorderBrush=Brush("#725817"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Padding=new Thickness(10),Margin=new Thickness(0,0,0,10),Child=sp};}
  left.Children.Add(Group("DIGITAL PAD","DPAD_UP","DPAD_DOWN","DPAD_LEFT","DPAD_RIGHT"));left.Children.Add(Group("ANALOG STICK","STICK_UP","STICK_DOWN","STICK_LEFT","STICK_RIGHT"));left.Children.Add(Group("LEFT / Z","L","Z"));
  right.Children.Add(Group("C-BUTTONS","C_UP","C_DOWN","C_LEFT","C_RIGHT"));right.Children.Add(Group("FACE BUTTONS","B","A"));right.Children.Add(Group("RIGHT / START","R","START"));
  center.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});center.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});center.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  var title=new TextBlock{Text="NINTENDO 64",HorizontalAlignment=HorizontalAlignment.Center,Foreground=Brush("#F1D477"),FontWeight=FontWeights.Bold,FontSize=20,Margin=new Thickness(0,10,0,4)};center.Children.Add(title);
  var pad=new Border{Background=Brush("#171E1B"),BorderBrush=Brush("#9A7A25"),BorderThickness=new Thickness(2),CornerRadius=new CornerRadius(70,70,105,105),Margin=new Thickness(12),Padding=new Thickness(20)};Grid.SetRow(pad,1);var pg=new Grid();pg.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});pg.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});pg.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});pg.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});var dpad=new TextBlock{Text="✚",FontSize=70,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=Brush("#9DAA9F")};var face=new TextBlock{Text="C  ●\n   B  A",FontSize=26,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=Brush("#E3BB4C")};Grid.SetColumn(face,1);var stick=new TextBlock{Text="◎\nANALOG",FontSize=22,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=Brush("#B7BDB8")};Grid.SetRow(stick,1);Grid.SetColumnSpan(stick,2);pg.Children.Add(dpad);pg.Children.Add(face);pg.Children.Add(stick);pad.Child=pg;center.Children.Add(pad);
  var hint=new TextBlock{Text="Klicke eine N64-Funktion und betätige danach die gewünschte Taste / Richtung am Controller.",TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Foreground=Brush("#9DAA9F"),Margin=new Thickness(8)};Grid.SetRow(hint,2);center.Children.Add(hint);
  var bottom=new Grid{Margin=new Thickness(0,12,0,0)};bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var auto=new Button{Content="⚙  Auto-Configure",Padding=new Thickness(16,7,16,7),Margin=new Thickness(0,0,8,0)};var reset=new Button{Content="↶  Reset",Padding=new Thickness(16,7,16,7)};var leftBtns=new StackPanel{Orientation=Orientation.Horizontal};leftBtns.Children.Add(auto);leftBtns.Children.Add(reset);bottom.Children.Add(leftBtns);var ok=new Button{Content="✓  Speichern & Anwenden",Padding=new Thickness(16,7,16,7),Margin=new Thickness(4)};var cancel=new Button{Content="Abbrechen",Padding=new Thickness(16,7,16,7),Margin=new Thickness(4)};var rightBtns=new StackPanel{Orientation=Orientation.Horizontal};rightBtns.Children.Add(ok);rightBtns.Children.Add(cancel);Grid.SetColumn(rightBtns,1);bottom.Children.Add(rightBtns);Grid.SetRow(bottom,2);root.Children.Add(bottom);w.Content=root;
  void UpdateStatus(){var p=ReadPad();status.Text=p is null?"● Kein XInput-Controller erkannt":"● Controller verbunden";status.Foreground=Brush(p is null?"#D49B45":"#55C96B");}refresh.Click+=(_,_)=>UpdateStatus();UpdateStatus();
  async Task CaptureOne(string action){var b=buttons[action];var old=b.Content;b.Content="Eingabe drücken …";b.BorderBrush=Brush("#F1D477");await Task.Delay(220);var previous=ReadPad();string? found=null;var until=DateTime.UtcNow.AddSeconds(8);while(DateTime.UtcNow<until&&found is null){await Task.Delay(30);var now=ReadPad();found=DetectPadInput(previous,now);previous=now;}if(found is not null){_controllerBindingValues[action].Text=found;b.Content=FriendlyPad(found);}else b.Content=old;b.ClearValue(Button.BorderBrushProperty);}
  foreach(var kv in buttons)kv.Value.Click+=async (_,_)=>await CaptureOne((string)kv.Value.Tag);
  auto.Click+=async (_,_)=>{auto.IsEnabled=false;try{var analog=ReadPad();hint.Text="Analog Stick → gewünschten Stick deutlich bewegen …";string? which=null;var until=DateTime.UtcNow.AddSeconds(8);while(DateTime.UtcNow<until&&which is null){await Task.Delay(30);which=DetectAnalogStick(ReadPad());}if(which is not null){var pre=which=="RightStick"?"RightStick":"LeftStick";foreach(var q in new[]{("STICK_UP","Up"),("STICK_DOWN","Down"),("STICK_LEFT","Left"),("STICK_RIGHT","Right")}){_controllerBindingValues[q.Item1].Text=pre+q.Item2;buttons[q.Item1].Content=FriendlyPad(pre+q.Item2);}}foreach(var a in new[]{"DPAD_UP","DPAD_DOWN","DPAD_LEFT","DPAD_RIGHT","START","Z","B","A","C_UP","C_DOWN","C_LEFT","C_RIGHT","R","L"}){hint.Text=$"{_inputLabels[a]} → Eingabe drücken …";await CaptureOne(a);}hint.Text="Auto-Configure abgeschlossen. Speichern & Anwenden übernimmt die Belegung live.";}finally{auto.IsEnabled=true;}};
  reset.Click+=(_,_)=>{foreach(var a in _inputActions){_controllerBindingValues[a].Text=DefaultBindings[a].pad;if(buttons.TryGetValue(a,out var b))b.Content=FriendlyPad(DefaultBindings[a].pad);}hint.Text="Standardbelegung wiederhergestellt.";};
  ok.Click+=(_,_)=>{SaveInputProfile();RefreshControllerVisual();w.DialogResult=true;w.Close();Log(_session is null?"N64-Controllerprofil gespeichert.":"N64-Controllerprofil gespeichert • laufendes Input-Plugin übernimmt die Belegung live.");};cancel.Click+=(_,_)=>w.Close();w.ShowDialog();
 }
 void ConfigureKeyboard_Click(object s,RoutedEventArgs e){var dlg=CaptureDialog("Tastatur konfigurieren","Für jede N64-Funktion die gewünschte Taste drücken.",out var prompt,out var skip,out var cancel);var index=0;void Next(){if(index>=_inputActions.Length){SaveInputProfile();dlg.Close();Log("Tastaturbelegung vollständig erfasst und gespeichert.");return;}prompt.Text=$"{_inputLabels[_inputActions[index]]}  →  Taste drücken …";}dlg.PreviewKeyDown+=(_,ev)=>{if(ev.Key==Key.Escape)return;var name=KeyName(ev.Key);if(name is null)return;_keyboardBindingValues[_inputActions[index]].Text=name;index++;ev.Handled=true;Next();};skip.Click+=(_,_)=>{index++;Next();};cancel.Click+=(_,_)=>dlg.Close();Next();dlg.ShowDialog();}
 Window CaptureDialog(string title,string intro,out TextBlock prompt,out Button skip,out Button cancel){var w=new Window{Title=title,Owner=this,Width=520,Height=220,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Background=Brush("#07100D"),Foreground=Brush("#F4E7BE")};var panel=new StackPanel{Margin=new Thickness(18)};panel.Children.Add(new TextBlock{Text=intro,TextWrapping=TextWrapping.Wrap,Foreground=Brush("#9DAA9F"),Margin=new Thickness(0,0,0,18)});prompt=new TextBlock{FontSize=18,FontWeight=FontWeights.Bold,Foreground=Brush("#F1D477"),Margin=new Thickness(0,0,0,20)};panel.Children.Add(prompt);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};skip=new Button{Content="Überspringen",MinWidth=110,Margin=new Thickness(4)};cancel=new Button{Content="Abbrechen",MinWidth=100,Margin=new Thickness(4)};buttons.Children.Add(skip);buttons.Children.Add(cancel);panel.Children.Add(buttons);w.Content=panel;return w;}
 static string? KeyName(Key key)=>key switch{Key.Up=>"Up",Key.Down=>"Down",Key.Left=>"Left",Key.Right=>"Right",Key.Enter=>"Enter",Key.Space=>"Space",Key.LeftShift=>"LeftShift",Key.RightShift=>"RightShift",Key.LeftCtrl=>"LeftCtrl",Key.RightCtrl=>"RightCtrl",>=Key.A and <=Key.Z=>key.ToString(),>=Key.NumPad0 and <=Key.NumPad9=>key.ToString(),_=>null};
 [StructLayout(LayoutKind.Sequential)] struct XInputGamepad{public ushort Buttons;public byte LeftTrigger,RightTrigger;public short ThumbLX,ThumbLY,ThumbRX,ThumbRY;} [StructLayout(LayoutKind.Sequential)] struct XInputState{public uint Packet;public XInputGamepad Gamepad;}
 [DllImport("xinput1_4.dll",EntryPoint="XInputGetState")] static extern uint XInputGetState14(uint index,out XInputState state);[DllImport("xinput1_3.dll",EntryPoint="XInputGetState")] static extern uint XInputGetState13(uint index,out XInputState state);
 static XInputState? ReadPad(){for(uint i=0;i<4;i++){try{if(XInputGetState14(i,out var s)==0)return s;}catch(DllNotFoundException){try{if(XInputGetState13(i,out var s)==0)return s;}catch{}}catch{}}return null;}
 static string? DetectAnalogStick(XInputState? state){if(state is null)return null;var g=state.Value.Gamepad;const int d=16000;var left=Math.Max(Math.Abs((int)g.ThumbLX),Math.Abs((int)g.ThumbLY));var right=Math.Max(Math.Abs((int)g.ThumbRX),Math.Abs((int)g.ThumbRY));if(left<d&&right<d)return null;return right>left?"RightStick":"LeftStick";}
 static string? DetectPadInput(XInputState? oldState,XInputState? state){if(state is null)return null;var g=state.Value.Gamepad;var old=oldState?.Gamepad??default;var newly=(ushort)(g.Buttons&~old.Buttons);var btns=new (ushort,string)[]{(0x0001,"DPadUp"),(0x0002,"DPadDown"),(0x0004,"DPadLeft"),(0x0008,"DPadRight"),(0x0010,"Start"),(0x0020,"Back"),(0x0040,"LThumb"),(0x0080,"RThumb"),(0x0100,"LB"),(0x0200,"RB"),(0x1000,"A"),(0x2000,"B"),(0x4000,"X"),(0x8000,"Y")};foreach(var b in btns)if((newly&b.Item1)!=0)return b.Item2;if(g.LeftTrigger>80&&old.LeftTrigger<=80)return "LT";if(g.RightTrigger>80&&old.RightTrigger<=80)return "RT";const int d=16000;if(g.ThumbLY>d&&old.ThumbLY<=d)return "LeftStickUp";if(g.ThumbLY<-d&&old.ThumbLY>=-d)return "LeftStickDown";if(g.ThumbLX<-d&&old.ThumbLX>=-d)return "LeftStickLeft";if(g.ThumbLX>d&&old.ThumbLX<=d)return "LeftStickRight";if(g.ThumbRY>d&&old.ThumbRY<=d)return "RightStickUp";if(g.ThumbRY<-d&&old.ThumbRY>=-d)return "RightStickDown";if(g.ThumbRX<-d&&old.ThumbRX>=-d)return "RightStickLeft";if(g.ThumbRX>d&&old.ThumbRX<=d)return "RightStickRight";return null;}
 Dictionary<string,string> LoadInputProfile(){var r=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);try{if(File.Exists(InputProfilePath))foreach(var line in File.ReadAllLines(InputProfilePath)){var i=line.IndexOf('=');if(i>0)r[line[..i].Trim()]=line[(i+1)..].Trim();}}catch{}return r;}
 void SaveInputProfile(){Directory.CreateDirectory(_paths.Settings);using(var w=new StreamWriter(InputProfilePath,false)){w.WriteLine("# Tracker of Time V2 emulator input profile");foreach(var a in _inputActions){w.WriteLine($"K_{a}={_keyboardBindingValues[a].Text}");w.WriteLine($"P_{a}={_controllerBindingValues[a].Text}");}}ApplyInputProfileEnvironment();}
 void SaveInputBindings_Click(object s,RoutedEventArgs e){SaveInputProfile();Log(_session is null?"Emulator-Eingabebelegung gespeichert.":"Emulator-Eingabebelegung gespeichert • laufendes Input-Plugin übernimmt sie live.");}
 void ResetInputBindings_Click(object s,RoutedEventArgs e){foreach(var a in _inputActions){_keyboardBindingValues[a].Text=DefaultBindings[a].key;_controllerBindingValues[a].Text=DefaultBindings[a].pad;}SaveInputProfile();RefreshControllerVisual();Log("Standardbelegung wiederhergestellt.");}
 void ApplyInputProfileEnvironment(){Environment.SetEnvironmentVariable("TOT_M85_INPUT_CONFIG",InputProfilePath,EnvironmentVariableTarget.Process);}

 void SettingsRenderer_Changed(object s,SelectionChangedEventArgs e){if(!IsLoaded||_session is not null||RendererBox is null||SettingsRendererBox is null)return;RendererBox.SelectedIndex=SettingsRendererBox.SelectedIndex;RefreshHomeWorkspace();}
 void SettingsController_Changed(object s,RoutedEventArgs e){if(!IsLoaded||_session is not null||ControllerBox is null||SettingsControllerBox is null)return;ControllerBox.IsChecked=SettingsControllerBox.IsChecked;RefreshHomeWorkspace();}
 void ShowWorkspace(string workspace){PlayWorkspace.Visibility=workspace=="play"?Visibility.Visible:Visibility.Collapsed;RandomizerPage.Visibility=workspace=="randomizer"?Visibility.Visible:Visibility.Collapsed;TrackerWorkspace.Visibility=workspace=="tracker"?Visibility.Visible:Visibility.Collapsed;HomeWorkspace.Visibility=workspace=="home"?Visibility.Visible:Visibility.Collapsed;SettingsWorkspace.Visibility=workspace=="settings"?Visibility.Visible:Visibility.Collapsed;MapWorkspace.Visibility=workspace=="map"?Visibility.Visible:Visibility.Collapsed;}
 void Home_Click(object s,RoutedEventArgs e){RefreshHomeWorkspace();ShowWorkspace("home");Log("Home Dashboard aktiv.");}void Randomizer_Click(object s,RoutedEventArgs e){ShowWorkspace("randomizer");Log("Original-OoTR Randomizer Workspace aktiv.");}void Play_Click(object s,RoutedEventArgs e){ShowWorkspace("play");Log("Play Workspace aktiv.");}void TrackerPage_Click(object s,RoutedEventArgs e){ShowWorkspace("tracker");if(_lastTrackerSnapshot is not null)RenderTrackerWorkspace(_lastTrackerSnapshot,_lastTrackerReadiness);Log("Tracker Workspace aktiv • dieselben Live-Daten wie im Play-Tracker.");}void Map_Click(object s,RoutedEventArgs e){if(_lastTrackerSnapshot is not null)RenderDashboardWorkspaces(_lastTrackerSnapshot,_lastTrackerReadiness);ShowWorkspace("map");Log("World / Map Workspace aktiv.");}void Settings_Click(object s,RoutedEventArgs e){SyncSettingsFromPlay();ShowWorkspace("settings");Log("Settings Workspace aktiv.");}

 void LanguageSwitch_Click(object s,RoutedEventArgs e){var de=!UiLocalization.German;UiLocalization.SetGerman(de);_randomizerWorkspace?.ApplyLanguage(de);if(_lastTrackerSnapshot is not null){RenderTrackerFrame(_lastTrackerSnapshot,_lastTrackerReadiness);RenderTrackerWorkspace(_lastTrackerSnapshot,_lastTrackerReadiness);RenderDashboardWorkspaces(_lastTrackerSnapshot,_lastTrackerReadiness);}UiLocalization.Apply(this);LanguageSwitchButton.Content=de?"🌐  DE":"🌐  EN";}
 void ThemeSelector_SelectionChanged(object s,SelectionChangedEventArgs e){if(!IsLoaded||ThemeSelector.SelectedItem is not ComboBoxItem item)return;var name=item.Content?.ToString()??"Hyrule";ThemeRuntime.Apply(Application.Current,name);ApplyThemePresentation(name);Background=(Brush)Application.Current.Resources["WindowBackground"];InvalidateVisual();}
 void ApplyThemePresentation(string name)
 {
  var hyrule=name=="Hyrule";
  ThemeBackdropImage.Visibility=hyrule?Visibility.Visible:Visibility.Collapsed; ThemeCornerLeft.Visibility=hyrule?Visibility.Visible:Visibility.Collapsed; ThemeCornerRight.Visibility=hyrule?Visibility.Visible:Visibility.Collapsed; ThemeHeaderImage.Visibility=hyrule?Visibility.Visible:Visibility.Collapsed; ThemeLogoFrame.Visibility=hyrule?Visibility.Visible:Visibility.Collapsed;
  var (header,status,corner,frame)=name switch
  {
   "Hyrule" => (102d,34d,9d,2d), "Zora" => (88d,36d,18d,2d), "Gerudo" => (94d,34d,2d,3d), "Ocarina of Time" => (98d,34d,11d,2d),
   "Nightly" => (84d,32d,12d,1.5d), "Midnight" => (78d,30d,5d,1d), "Simple Dark" => (70d,28d,0d,1d), "Light Parchment" => (92d,36d,7d,3d), _ => (102d,34d,9d,2d)
  };
  ThemeHeaderRow.Height=new GridLength(header); ThemeStatusRow.Height=new GridLength(status); ThemeOuterFrame.CornerRadius=new CornerRadius(corner); ThemeOuterFrame.BorderThickness=new Thickness(frame); ThemeHeaderShell.CornerRadius=new CornerRadius(Math.Max(0,corner-4),Math.Max(0,corner-4),0,0);
  if(System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome) chrome.CornerRadius=new CornerRadius(corner);
 }
}
