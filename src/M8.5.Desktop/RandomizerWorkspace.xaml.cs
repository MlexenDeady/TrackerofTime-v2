using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TrackerOfTime.V2.M7.Workflow;
using TrackerOfTime.V2.M8.Application.Core;
using TrackerOfTime.V2.M8_1.Integration;

namespace TrackerOfTime.V2.M8_5.Desktop;

public partial class RandomizerWorkspace : UserControl
{
    sealed record OptionDef(JsonElement Value, string Text, string Tooltip, string[] DisableSettings, string[] DisableSections, string[] DisableTabs, string[] ConditionalTargets, string[] Tags);
    sealed record SettingDef(string Key, string Text, string Type, string Tooltip, JsonElement Default, IReadOnlyList<OptionDef> Options, bool HideWhenDisabled, bool NoLineBreak, string Size, int? Min, int? Max, int? MaxLength, string Function, JsonElement FileTypes, JsonElement ConditionalControls, bool Dynamic, string[] Tags, bool Shared);
    sealed record SectionDef(string Id, string Text, string Subheader, int ColSpan, int[] RowSpan, bool IsColors, IReadOnlyList<SettingDef> Settings);
    sealed record TabDef(string Id, string Text, IReadOnlyList<SectionDef> Sections);
    sealed record Schema(IReadOnlyList<TabDef> Tabs, IReadOnlyDictionary<string, SettingDef> Settings);

    readonly string _root;
    readonly M8UserDataPaths _paths;
    readonly IM8Diagnostics _diagnostics;
    readonly RandomizerWorkflowService _randomizer;
    readonly OoTRRuntimeWorkspace _runtime;
    readonly Dictionary<string, JsonElement> _draftValues = new(StringComparer.Ordinal);
    readonly HashSet<string> _changedKeys = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _customColors = new(StringComparer.Ordinal);
    readonly Dictionary<string, FrameworkElement> _settingHosts = new(StringComparer.Ordinal);
    readonly Dictionary<string, FrameworkElement> _sectionHosts = new(StringComparer.Ordinal);
    readonly Dictionary<string, ListBoxItem> _tabHosts = new(StringComparer.Ordinal);
    Schema? _schema;
    M8SettingsWorkflowCoordinator? _settings;
    OoTRHostOwner? _host;
    CancellationTokenSource? _generationCts;
    bool _busy;
    CancellationTokenSource? _settingsStringRefreshCts;

    const string ExtractScript = """
import json
import SettingsToJson as S
out={'settingsObj':{},'settingsArray':[],'cosmeticsObj':{},'cosmeticsArray':[],'distroArray':[]}
for tab in S.SettingInfos.setting_map['Tabs']:
    if tab.get('exclude_from_electron',False): continue
    o=S.get_tab_json(tab,False,False); a=S.get_tab_json(tab,False,True)
    out['settingsObj'][tab['name']]=o; out['settingsArray'].append(a)
S.resolve_conditional_control_dependencies(out)
allowed={'general_tab','main_tab','detailed_tab','starting_tab','other_tab','cosmetics_tab','sfx_tab'}
tabs=[]
for tab in out['settingsArray']:
    if tab['name'] not in allowed: continue
    sections=[]
    for sec in tab.get('sections',[]):
        if sec['name'].endswith('_patcher'): continue
        settings=[x for x in sec.get('settings',[]) if x is not None]
        if settings:
            sections.append({'name':sec['name'],'text':sec.get('text',''),'subheader':sec.get('subheader',''),'col_span':sec.get('col_span',1),'row_span':sec.get('row_span',[]),'is_colors':sec.get('is_colors',False),'settings':settings})
    tabs.append({'name':tab['name'],'text':tab.get('text',tab['name']),'sections':sections})
print(json.dumps({'tabs':tabs},separators=(',',':')))
""";

    public RandomizerWorkspace(string root, M8UserDataPaths paths, IM8Diagnostics diagnostics)
    {
        _root = root; _paths = paths; _diagnostics = diagnostics;
        _randomizer = new(diagnostics); _runtime = new(root, paths.Temporary, diagnostics);
        InitializeComponent();
        Loaded += async (_, _) => await LoadSchemaAsync();
    }

    async Task EnsureOoTRAsync()
    {
        if (_host is { Snapshot.Exited: false } && _settings is not null) return;
        using (var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5))) await OoTRStaleHostCleanup.StopAllTrackerOoTRHostsAsync(_diagnostics, cleanup.Token);
        var runtimeRoot = _runtime.Prepare(); _host = new OoTRHostOwner(runtimeRoot, _diagnostics); await _host.StartAsync();
        using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var version = await _randomizer.QueryMetadataAsync(ready.Token);
        if (_settings is null) { var initial = await new OoTRSettingsCatalog().LoadInitialStateAsync(_root, version); _settings = new M8SettingsWorkflowCoordinator(_randomizer, initial); }
    }

    async Task LoadSchemaAsync()
    {
        ReloadButton.IsEnabled = false; StatusText.Text = "Loading exact Original OoTR Electron schema …";
        try
        {
            _schema = await ExtractAsync(); TabList.Items.Clear(); _tabHosts.Clear();
            foreach (var tab in _schema.Tabs)
            {
                var item = new ListBoxItem { Content = UiLocalization.T(tab.Text), Tag = tab };
                TabList.Items.Add(item); _tabHosts[tab.Id] = item;
            }
            CountText.Text = $"{_schema.Settings.Count} Original Electron UI entries • {_schema.Tabs.Count} tabs";
            SourceText.Text = "OoTR Randomizer • Einstellungen und Generator";
            foreach (var def in _schema.Settings.Values) if (!_draftValues.ContainsKey(def.Key)) _draftValues[def.Key] = def.Default.Clone();
            StatusText.Text = "Original OoTR Electron schema loaded.";
            if (TabList.Items.Count > 0) TabList.SelectedIndex = 0;
        }
        catch (Exception ex) { StatusText.Text = "OoTR schema load failed: " + ex.Message; SectionsHost.Children.Clear(); }
        finally { ReloadButton.IsEnabled = true; RefreshButtons(); }
    }

    async Task<Schema> ExtractAsync()
    {
        var ootr = System.IO.Path.Combine(_root, "third_party", "OoTR");
        if (!System.IO.File.Exists(System.IO.Path.Combine(ootr, "SettingsToJson.py"))) throw new System.IO.FileNotFoundException("OoTR-Komponente SettingsToJson.py fehlt.");
        var psi = new ProcessStartInfo("py") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = ootr };
        psi.ArgumentList.Add("-3.13"); psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(ExtractScript);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Python could not start.");
        var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync();
        var output = await stdout; var err = await stderr; if (p.ExitCode != 0) throw new InvalidOperationException($"OoTR Electron projection failed ({p.ExitCode}): {err.Trim()}");
        using var doc = JsonDocument.Parse(output); var tabs = new List<TabDef>(); var all = new Dictionary<string, SettingDef>(StringComparer.Ordinal);
        foreach (var t in doc.RootElement.GetProperty("tabs").EnumerateArray())
        {
            var sections = new List<SectionDef>();
            foreach (var s in t.GetProperty("sections").EnumerateArray())
            {
                var defs = new List<SettingDef>();
                foreach (var x in s.GetProperty("settings").EnumerateArray())
                {
                    var options = new List<OptionDef>();
                    foreach (var o in x.GetProperty("options").EnumerateArray())
                    {
                        options.Add(new OptionDef(o.GetProperty("name").Clone(), GetString(o, "text"), StripHtml(GetString(o, "tooltip")), SplitCsv(o, "controls_visibility_setting"), SplitCsv(o, "controls_visibility_section"), SplitCsv(o, "controls_visibility_tab"), GetStringArray(o, "conditionally_controls_setting"), GetStringArray(o, "tags")));
                    }
                    var key = GetString(x, "name");
                    var def = new SettingDef(key, GetString(x, "text"), GetString(x, "type"), StripHtml(GetString(x, "tooltip")), x.GetProperty("default").Clone(), options, GetBool(x, "hide_when_disabled"), GetBool(x, "no_line_break"), GetString(x, "size"), GetInt(x, "min"), GetInt(x, "max"), GetInt(x, "max_length"), GetString(x, "function"), x.TryGetProperty("file_types", out var ft) ? ft.Clone() : JsonDocument.Parse("[]").RootElement.Clone(), x.TryGetProperty("conditional_controls", out var cc) ? cc.Clone() : JsonDocument.Parse("{}").RootElement.Clone(), GetBool(x, "dynamic"), GetStringArray(x, "tags"), GetBool(x, "shared"));
                    defs.Add(def); all[key] = def;
                }
                var rows = s.TryGetProperty("row_span", out var rs) && rs.ValueKind == JsonValueKind.Array ? rs.EnumerateArray().Select(v => v.GetInt32()).ToArray() : Array.Empty<int>();
                sections.Add(new SectionDef(GetString(s, "name"), GetString(s, "text"), StripHtml(GetString(s, "subheader")), GetInt(s, "col_span") ?? 1, rows, GetBool(s, "is_colors"), defs));
            }
            tabs.Add(new TabDef(GetString(t, "name"), GetString(t, "text"), sections));
        }
        return new Schema(tabs, all);
    }

    static string GetString(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static bool GetBool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    static int? GetInt(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
    static string[] SplitCsv(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : Array.Empty<string>();
    static string[] GetStringArray(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : Array.Empty<string>();
    static string StripHtml(string value) => System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(value), "<[^>]+>", " ").Replace("\n", " ").Trim();

    void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_schema is null || TabList.SelectedItem is not ListBoxItem li || li.Tag is not TabDef tab) return;
        Render(tab);
    }

    void Render(TabDef tab)
    {
        PageTitle.Text = UiLocalization.T(tab.Text); SectionsHost.Children.Clear(); _settingHosts.Clear(); _sectionHosts.Clear();
        var layout = new Grid(); for (var c = 0; c < 4; c++) layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0; var col = 0;
        foreach (var sec in tab.Sections)
        {
            var span = Math.Clamp(sec.ColSpan, 1, 4); if (col + span > 4) { row++; col = 0; } while (layout.RowDefinitions.Count <= row) layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var panel = new StackPanel(); var border = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(63, 53, 25)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(7, 16, 13)), Margin = new Thickness(5), Padding = new Thickness(12), Child = panel };
            _sectionHosts[sec.Id] = border;
            if (!string.IsNullOrWhiteSpace(sec.Text)) panel.Children.Add(new TextBlock { Text = UiLocalization.T(sec.Text), Foreground = new SolidColorBrush(Color.FromRgb(227, 187, 76)), FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 0, 0, 8) });
            if (!string.IsNullOrWhiteSpace(sec.Subheader)) panel.Children.Add(new TextBlock { Text = UiLocalization.T(sec.Subheader), Foreground = new SolidColorBrush(Color.FromRgb(126, 144, 132)), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            for (var i = 0; i < sec.Settings.Count; i++)
            {
                var def = sec.Settings[i]; var host = CreateSetting(def, sec.IsColors); _settingHosts[def.Key] = host;
                if (def.NoLineBreak && i + 1 < sec.Settings.Count)
                {
                    var next = sec.Settings[++i]; var nextHost = CreateSetting(next, sec.IsColors); _settingHosts[next.Key] = nextHost;
                    var pair = new Grid(); pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    pair.Children.Add(host); Grid.SetColumn(nextHost, 1); pair.Children.Add(nextHost); panel.Children.Add(pair);
                }
                else panel.Children.Add(host);
            }
            Grid.SetRow(border, row); Grid.SetColumn(border, col); Grid.SetColumnSpan(border, span); layout.Children.Add(border); col += span; if (col >= 4) { row++; col = 0; }
        }
        SectionsHost.Children.Add(layout); ApplyVisibility();
    }

    FrameworkElement CreateSetting(SettingDef d, bool isColors)
    {
        var current = _draftValues.TryGetValue(d.Key, out var draft) ? draft : d.Default;
        var box = new StackPanel { Margin = new Thickness(0, 3, 0, d.NoLineBreak ? 2 : 7), ToolTip = CreateOoTRToolTip(d.Tooltip) };
        ToolTipService.SetShowDuration(box, 60000);
        FrameworkElement control;
        if (string.IsNullOrWhiteSpace(d.Type)) { box.Visibility = Visibility.Collapsed; return box; }
        if (d.Type == "Textbox") { box.Children.Add(Label(d.Text)); return box; }
        if (d.Type == "Button")
        {
            var b = new Button { Content = UiLocalization.T(d.Text), HorizontalAlignment = HorizontalAlignment.Left, Tag = d };
            b.Click += SpecialButton_Click; box.Children.Add(b); return box;
        }
        if (d.Type == "Checkbutton")
        {
            var c = new CheckBox { Content = UiLocalization.T(d.Text), IsChecked = current.ValueKind == JsonValueKind.True, Foreground = new SolidColorBrush(Color.FromRgb(207, 216, 209)), HorizontalAlignment = HorizontalAlignment.Left };
            c.Checked += (_, _) => DraftChanged(d.Key, JsonSerializer.SerializeToElement(true)); c.Unchecked += (_, _) => DraftChanged(d.Key, JsonSerializer.SerializeToElement(false)); control = c;
        }
        else if (d.Type == "Radiobutton")
        {
            box.Children.Add(Label(d.Text)); var radios = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var o in d.Options)
            {
                var rb = new RadioButton { Content = UiLocalization.T(o.Text), Tag = o.Value.Clone(), GroupName = "ootr_" + d.Key, IsChecked = JsonEquals(o.Value, current), Margin = new Thickness(0,0,12,0), ToolTip = CreateOoTRToolTip(o.Tooltip) };
                rb.Checked += (_, _) => { if (rb.Tag is JsonElement v) DraftChanged(d.Key, v.Clone()); }; radios.Children.Add(rb);
            }
            control = radios;
        }
        else if (d.Type == "Combobox")
        {
            box.Children.Add(Label(d.Text)); var cb = new ComboBox { MinWidth = 150 };
            foreach (var o in d.Options) cb.Items.Add(new ComboBoxItem { Content = UiLocalization.T(o.Text), Tag = o.Value.Clone(), ToolTip = CreateOoTRToolTip(o.Tooltip) });
            cb.SelectedItem = cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => JsonEquals((JsonElement)i.Tag, current));
            var colorPanel = new StackPanel(); colorPanel.Children.Add(cb); TextBox? color = null;
            if (isColors && d.Options.Any(o => o.Value.ValueKind == JsonValueKind.String && o.Value.GetString() == "Custom Color"))
            {
                color = new TextBox { Text = _customColors.GetValueOrDefault(d.Key, "#FFFFFF"), MaxLength = 7, Margin = new Thickness(0,4,0,0), Visibility = current.ValueKind == JsonValueKind.String && current.GetString() == "Custom Color" ? Visibility.Visible : Visibility.Collapsed, ToolTip = "Custom color (#RRGGBB)" };
                color.LostFocus += (_, _) => { var v=color.Text.Trim(); if(System.Text.RegularExpressions.Regex.IsMatch(v,"^#?[0-9A-Fa-f]{6}$")){if(!v.StartsWith("#"))v="#"+v;_customColors[d.Key]=v.ToUpperInvariant();color.Text=_customColors[d.Key];_changedKeys.Add(d.Key);} else color.Text=_customColors.GetValueOrDefault(d.Key,"#FFFFFF");}; colorPanel.Children.Add(color);
            }
            cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is ComboBoxItem i) { var v=((JsonElement)i.Tag).Clone(); DraftChanged(d.Key, v); if(color is not null) color.Visibility=v.ValueKind==JsonValueKind.String && v.GetString()=="Custom Color"?Visibility.Visible:Visibility.Collapsed; } }; control = colorPanel;
        }
        else if (d.Type == "MultipleSelect")
        {
            box.Children.Add(Label(d.Text)); control = CreateMultipleSelect(d, current);
        }
        else if (d.Type == "SearchBox") { control = CreateSearchBox(d, current); }
        else if (d.Type == "Scale") { control = CreateScale(d, current); }
        else if (d.Type is "Numberinput" or "Textinput")
        {
            box.Children.Add(Label(d.Text)); var tb = new TextBox { Text = Display(current), MinWidth = 90, MaxLength = d.MaxLength ?? 260 };
            tb.LostFocus += (_, _) => DraftChanged(d.Key, ParseText(d, tb.Text)); control = tb;
        }
        else if (d.Type is "Fileinput" or "Directoryinput") { control = CreatePathInput(d, current); }
        else if (d.Type == "Presetinput") { control = CreatePresetInput(d); }
        else
        {
            box.Children.Add(Label(d.Text)); var tb = new TextBox { Text = Display(current), IsReadOnly = true }; control = tb;
        }
        box.Children.Add(control); return box;
    }

    static TextBlock Label(string text) => new() { Text = UiLocalization.T(text), Foreground = new SolidColorBrush(Color.FromRgb(207, 216, 209)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };

    FrameworkElement CreateMultipleSelect(SettingDef d, JsonElement current)
    {
        var selected = current.ValueKind == JsonValueKind.Array ? current.EnumerateArray().Select(v => v.GetRawText()).ToHashSet() : new HashSet<string>();
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        var menu = new ContextMenu();
        void RefreshText() { var count = menu.Items.Cast<MenuItem>().Count(x => x.IsChecked); button.Content = count == 0 ? "None" : count == d.Options.Count ? "All" : count <= 2 ? string.Join(", ", menu.Items.Cast<MenuItem>().Where(x => x.IsChecked).Select(x => x.Header?.ToString())) : $"Selected: {count}"; }
        foreach (var o in d.Options)
        {
            var item = new MenuItem { Header = UiLocalization.T(o.Text), IsCheckable = true, IsChecked = selected.Contains(o.Value.GetRawText()), StaysOpenOnClick = true, Tag = o.Value.Clone(), ToolTip = CreateOoTRToolTip(o.Tooltip) };
            item.Click += (_, _) => { DraftChanged(d.Key, JsonSerializer.SerializeToElement(menu.Items.Cast<MenuItem>().Where(x => x.IsChecked).Select(x => JsonElementToObject((JsonElement)x.Tag)).ToArray())); RefreshText(); }; menu.Items.Add(item);
        }
        button.ContextMenu = menu; button.Click += (_, _) => { menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; }; RefreshText(); return button;
    }

    FrameworkElement CreateScale(SettingDef d, JsonElement current)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = Label(d.Text + ":"); label.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(label);
        var value = current.ValueKind == JsonValueKind.Number ? current.GetDouble() : d.Min ?? 0; var tb = new TextBox { Text = value.ToString("0"), Margin = new Thickness(8, 0, 8, 0) }; Grid.SetColumn(tb, 1); grid.Children.Add(tb);
        var slider = new Slider { Minimum = d.Min ?? 0, Maximum = d.Max ?? 100, Value = Math.Clamp(value, d.Min ?? 0, d.Max ?? 100), TickFrequency = 1, IsSnapToTickEnabled = true }; Grid.SetColumn(slider, 2); grid.Children.Add(slider);
        slider.ValueChanged += (_, _) => { tb.Text = ((int)slider.Value).ToString(); DraftChanged(d.Key, JsonSerializer.SerializeToElement((int)slider.Value)); };
        tb.LostFocus += (_, _) => { if (int.TryParse(tb.Text, out var n)) slider.Value = Math.Clamp(n, d.Min ?? n, d.Max ?? n); else tb.Text = ((int)slider.Value).ToString(); };
        return grid;
    }

    FrameworkElement CreatePathInput(SettingDef d, JsonElement current)
    {
        var stack = new StackPanel(); stack.Children.Add(Label(d.Text)); var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tb = new TextBox { Text = Display(current), MinWidth = 120 }; tb.LostFocus += (_, _) => DraftChanged(d.Key, JsonSerializer.SerializeToElement(tb.Text)); grid.Children.Add(tb);
        var b = new Button { Content = "Browse", Tag = (d, tb), Margin = new Thickness(5, 0, 0, 0) }; b.Click += BrowseSetting_Click; Grid.SetColumn(b, 1); grid.Children.Add(b); stack.Children.Add(grid); return stack;
    }

    FrameworkElement CreatePresetInput(SettingDef d)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cb = new ComboBox { MinWidth = 130, Tag = d }; LoadPresetNames(cb); grid.Children.Add(cb);
        var load = new Button { Content = "Load", Margin = new Thickness(4,0,0,0) }; load.Click += (_, _) => LoadPreset(cb);
        var save = new Button { Content = "Save", Margin = new Thickness(4,0,0,0) }; save.Click += (_, _) => SavePreset(cb);
        var remove = new Button { Content = "Remove", Margin = new Thickness(4,0,0,0) }; remove.Click += (_, _) => RemovePreset(cb);
        Grid.SetColumn(load, 1); Grid.SetColumn(save, 2); Grid.SetColumn(remove, 3); grid.Children.Add(load); grid.Children.Add(save); grid.Children.Add(remove); return grid;
    }

    string UserPresetsPath => System.IO.Path.Combine(_paths.Settings, "OoTR-Presets.json");
    Dictionary<string, JsonElement> ReadUserPresets()
    {
        if (!System.IO.File.Exists(UserPresetsPath)) return new(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(UserPresetsPath));
        return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal) : new(StringComparer.Ordinal);
    }
    Dictionary<string, JsonElement> ReadBuiltInPresets()
    {
        // Match Original OoTR Electron: presets_default.json first, then every JSON
        // from data/Presets in filename order. Earlier/system names stay protected.
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var data = System.IO.Path.Combine(_root, "third_party", "OoTR", "data");
        void AddPresetFile(string path)
        {
            if (!System.IO.File.Exists(path)) return;
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var x in doc.RootElement.EnumerateObject())
                if (!result.ContainsKey(x.Name) && x.Name is not "[New Preset]" and not "Default / Beginner") result[x.Name] = x.Value.Clone();
        }
        AddPresetFile(System.IO.Path.Combine(data, "presets_default.json"));
        var extra = System.IO.Path.Combine(data, "Presets");
        if (System.IO.Directory.Exists(extra))
            foreach (var path in System.IO.Directory.EnumerateFiles(extra, "*.json").OrderBy(x => System.IO.Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)) AddPresetFile(path);
        return result;
    }
    void LoadPresetNames(ComboBox cb)
    {
        cb.Items.Clear(); cb.Items.Add("[New Preset]"); cb.Items.Add("Default / Beginner"); foreach (var k in ReadBuiltInPresets().Keys) cb.Items.Add(k); foreach (var k in ReadUserPresets().Keys) if (!cb.Items.Contains(k)) cb.Items.Add(k); cb.SelectedIndex = 0;
    }
    void ApplyPresetObject(JsonElement obj)
    {
        if (_schema is null || obj.ValueKind != JsonValueKind.Object) return;
        // Original OoTR always restores defaults first, then overlays the selected preset.
        // Mark the complete schema dirty so an older/incomplete preset also resets values
        // that were changed before the preset was loaded.
        foreach (var d in _schema.Settings.Values) _draftValues[d.Key] = d.Default.Clone();
        _changedKeys.Clear();
        // Electron schema also contains seven GUI-only helper entries (NoneType).
        // Original OoTR never sends those through Settings; only real settable keys
        // participate in afterSettingChange()/settings-string conversion.
        if (_settings is not null)
            _changedKeys.UnionWith(_settings.State.Metadata.Where(x => x.Value.IsSettable).Select(x => x.Key));
        else
            _changedKeys.UnionWith(_schema.Settings.Keys.Where(k => k is not "output_types" and not "open_output_dir" and not "open_python_dir" and not "tricks_list_msg" and not "model_unavailable_msg" and not "sfx_link_unavailable_msg" and not "custom_music_unavailable_msg"));
        foreach (var p in obj.EnumerateObject()) if (_schema.Settings.ContainsKey(p.Name)) _draftValues[p.Name] = p.Value.Clone();
        if (TabList.SelectedItem is ListBoxItem { Tag: TabDef tab }) Render(tab);
        SettingsStringBox.Text = ""; ApplyVisibility();
    }
    async void LoadPreset(ComboBox cb) => await RunBusyAsync(async () =>
    {
        var name = cb.SelectedItem?.ToString() ?? "";
        if (name == "[New Preset]") { MessageBox.Show("Select a preset to load.", "Preset"); return; }
        if (name == "Default / Beginner")
        {
            if (_schema is not null) ApplyPresetObject(JsonSerializer.SerializeToElement(_schema.Settings.ToDictionary(x=>x.Key,x=>JsonElementToObject(x.Value.Default))));
            await RefreshSettingsStringNowAsync();
            StatusText.Text = "Original OoTR defaults restored; settings string refreshed.";
            return;
        }
        var users = ReadUserPresets();
        if (users.TryGetValue(name, out var u))
        {
            ApplyPresetObject(u); await RefreshSettingsStringNowAsync(); StatusText.Text=$"Preset '{name}' loaded; settings string refreshed."; return;
        }
        var built = ReadBuiltInPresets();
        if (built.TryGetValue(name, out var b))
        {
            ApplyPresetObject(b); await RefreshSettingsStringNowAsync(); StatusText.Text=$"Built-in OoTR preset '{name}' loaded; settings string refreshed.";
        }
    });
    string? PromptPresetName()
    {
        var w=new Window{Title="Create new preset",Width=360,Height=150,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=Window.GetWindow(this),ResizeMode=ResizeMode.NoResize};
        var g=new Grid{Margin=new Thickness(12)}; g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        g.Children.Add(new TextBlock{Text="Enter preset name:",Margin=new Thickness(0,0,0,6)});var tb=new TextBox{MinWidth=300};Grid.SetRow(tb,1);g.Children.Add(tb);var ok=new Button{Content="OK",Width=80,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0),IsDefault=true};ok.Click+=(_,_)=>{w.DialogResult=true;w.Close();};Grid.SetRow(ok,2);g.Children.Add(ok);w.Content=g;return w.ShowDialog()==true?tb.Text:null;
    }

    void SavePreset(ComboBox cb)
    {
        _paths.EnsureCreated(); var selected = cb.SelectedItem?.ToString() ?? "[New Preset]"; var built = ReadBuiltInPresets(); if (selected == "Default / Beginner" || built.ContainsKey(selected)) { MessageBox.Show("Built-in/system presets are protected and cannot be overwritten.", "Preset"); return; }
        var name = selected == "[New Preset]" ? PromptPresetName()?.Trim() : selected;
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("[", StringComparison.Ordinal)) return; var users=ReadUserPresets(); if (selected=="[New Preset]" && users.ContainsKey(name)) { MessageBox.Show("A preset with this name already exists.", "Preset"); return; }
        if (selected != "[New Preset]" && MessageBox.Show($"Overwrite preset '{name}'?", "Preset", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var obj = _draftValues.Where(x => _schema is not null && _schema.Settings.TryGetValue(x.Key, out var def) && def.Shared).ToDictionary(x=>x.Key,x=>JsonElementToObject(x.Value)); users[name]=JsonSerializer.SerializeToElement(obj); System.IO.File.WriteAllText(UserPresetsPath, JsonSerializer.Serialize(users.ToDictionary(x=>x.Key,x=>JsonElementToObject(x.Value)), new JsonSerializerOptions{WriteIndented=true})); LoadPresetNames(cb); cb.SelectedItem=name; StatusText.Text=$"Preset '{name}' saved.";
    }
    void RemovePreset(ComboBox cb)
    {
        var name=cb.SelectedItem?.ToString()??""; if (name is "[New Preset]" or "Default / Beginner" || ReadBuiltInPresets().ContainsKey(name)) { MessageBox.Show("System/built-in presets cannot be deleted.", "Preset"); return; }
        var users=ReadUserPresets(); if (!users.ContainsKey(name)) return; if (MessageBox.Show($"Delete preset '{name}'?", "Preset", MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return; users.Remove(name); System.IO.File.WriteAllText(UserPresetsPath, JsonSerializer.Serialize(users.ToDictionary(x=>x.Key,x=>JsonElementToObject(x.Value)), new JsonSerializerOptions{WriteIndented=true})); LoadPresetNames(cb); StatusText.Text=$"Preset '{name}' removed.";
    }

    FrameworkElement CreateSearchBox(SettingDef d, JsonElement current)
    {
        var outer = new Grid(); outer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto}); outer.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        var filters=new Grid{Margin=new Thickness(0,0,0,5)}; filters.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)}); filters.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto}); filters.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var leftFilter=new TextBox{ToolTip="Filter available",Margin=new Thickness(0,0,5,0)}; var tagFilter=new ComboBox{MinWidth=95,Margin=new Thickness(3,0,3,0)}; tagFilter.Items.Add("All tags"); foreach(var tag in d.Tags) tagFilter.Items.Add(tag); tagFilter.SelectedIndex=0; var rightFilter=new TextBox{ToolTip="Filter selected",Margin=new Thickness(5,0,0,0)}; Grid.SetColumn(tagFilter,1);Grid.SetColumn(rightFilter,2);filters.Children.Add(leftFilter);filters.Children.Add(tagFilter);filters.Children.Add(rightFilter);outer.Children.Add(filters);
        var root = new Grid(); Grid.SetRow(root,1); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var source = new ListBox { SelectionMode = SelectionMode.Extended, MinHeight = 150 }; var dest = new ListBox { SelectionMode = SelectionMode.Extended, MinHeight = 150 }; Grid.SetColumn(dest, 2);
        var selected = current.ValueKind == JsonValueKind.Array ? current.EnumerateArray().Select(v => v.GetRawText()).ToHashSet() : new HashSet<string>();
        foreach (var o in d.Options) { var item = new ListBoxItem { Content = UiLocalization.T(o.Text), Tag = o, Foreground = new SolidColorBrush(Color.FromRgb(244,231,190)), Background = new SolidColorBrush(Color.FromRgb(16,24,21)), ToolTip = CreateOoTRToolTip(o.Tooltip) }; (selected.Contains(o.Value.GetRawText()) ? dest : source).Items.Add(item); }
        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) };
        void Sync() => DraftChanged(d.Key, JsonSerializer.SerializeToElement(dest.Items.Cast<ListBoxItem>().Select(i => JsonElementToObject(((OptionDef)i.Tag).Value)).ToArray()));
        Button Add(string text, Action action) { var b = new Button { Content = text, MinWidth = 58, Margin = new Thickness(0, 2, 0, 2) }; b.Click += (_, _) => { action(); Sync(); RefreshSearchFilters(source,dest,leftFilter.Text,rightFilter.Text,tagFilter.SelectedItem?.ToString()); }; buttons.Children.Add(b); return b; }
        Add("ADD >", () => MoveSelected(source, dest));
        Add("ALL >>", () =>
        {
            MoveAllVisible(source, dest);
            // OoTR 9.1.36 represents an unshuffled adult-trade sequence with one
            // #AdultTrade pool slot. Selecting every adult-trade item as starting
            // inventory therefore asks Plandomizer to remove that single slot many
            // times and ends in KeyError. Original semantics only need the effective
            // furthest trade item. For the UI's Add-All convenience action retain
            // Claim Check and Zelda's Letter as the effective sequence states.
            if (d.Key == "starting_inventory")
            {
                var adultTrade = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Pocket Egg","Pocket Cucco","Cojiro","Odd Mushroom","Odd Potion","Poachers Saw","Poacher's Saw","Broken Sword","Broken Goron's Sword","Prescription","Eyeball Frog","Eyedrops","Eye Drops" };
                var childTrade = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Weird Egg","Chicken","Keaton Mask","Skull Mask","Spooky Mask","Bunny Hood","Goron Mask","Zora Mask","Gerudo Mask","Mask of Truth","Sold Out" };
                foreach (var item in dest.Items.Cast<ListBoxItem>().ToList())
                {
                    var option=(OptionDef)item.Tag; var text=option.Text;
                    if (adultTrade.Contains(text) || childTrade.Contains(text)) { dest.Items.Remove(item); source.Items.Add(item); }
                }
            }
        });
        Add("<< NONE", () => MoveAllVisible(dest, source)); Add("< REMOVE", () => MoveSelected(dest, source));
        void Refresh()=>RefreshSearchFilters(source,dest,leftFilter.Text,rightFilter.Text,tagFilter.SelectedItem?.ToString()); leftFilter.TextChanged+=(_,_)=>Refresh();rightFilter.TextChanged+=(_,_)=>Refresh();tagFilter.SelectionChanged+=(_,_)=>Refresh();
        root.Children.Add(source); Grid.SetColumn(buttons, 1); root.Children.Add(buttons); root.Children.Add(dest); outer.Children.Add(root); Refresh(); return outer;
    }
    static void RefreshSearchFilters(ListBox source,ListBox dest,string left,string right,string? tag)
    {
        foreach(ListBoxItem i in source.Items){var o=(OptionDef)i.Tag; i.Visibility=(string.IsNullOrWhiteSpace(left)||o.Text.Contains(left,StringComparison.OrdinalIgnoreCase)) && (tag is null or "All tags" || o.Tags.Contains(tag,StringComparer.Ordinal)) ? Visibility.Visible:Visibility.Collapsed;}
        foreach(ListBoxItem i in dest.Items){var o=(OptionDef)i.Tag; i.Visibility=string.IsNullOrWhiteSpace(right)||o.Text.Contains(right,StringComparison.OrdinalIgnoreCase)?Visibility.Visible:Visibility.Collapsed;}
    }
    static void MoveSelected(ListBox from,ListBox to){foreach(var i in from.SelectedItems.Cast<ListBoxItem>().ToList()){from.Items.Remove(i);to.Items.Add(i);}}
    static void MoveAllVisible(ListBox from,ListBox to){foreach(var i in from.Items.Cast<ListBoxItem>().Where(x=>x.Visibility==Visibility.Visible).ToList()){from.Items.Remove(i);to.Items.Add(i);}}

    void BrowseSetting_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not ValueTuple<SettingDef, TextBox> pair) return; var (d, tb) = pair;
        if (d.Type == "Directoryinput") { var dlg = new OpenFolderDialog { InitialDirectory = System.IO.Directory.Exists(tb.Text) ? tb.Text : _paths.Outputs }; if (dlg.ShowDialog() == true) { tb.Text = dlg.FolderName; DraftChanged(d.Key, JsonSerializer.SerializeToElement(tb.Text)); } }
        else { var dlg = new OpenFileDialog { Filter = BuildFileFilter(d) }; if (dlg.ShowDialog() == true) { tb.Text = dlg.FileName; DraftChanged(d.Key, JsonSerializer.SerializeToElement(tb.Text)); if (d.Key == "rom") _randomizer.SelectRom(tb.Text); } }
    }

    static string BuildFileFilter(SettingDef d)
    {
        if (d.FileTypes.ValueKind != JsonValueKind.Array) return "All Files|*.*"; var parts = new List<string>();
        foreach (var ft in d.FileTypes.EnumerateArray()) { var name = GetString(ft, "name"); if (!ft.TryGetProperty("extensions", out var exts)) continue; var patterns = exts.EnumerateArray().Select(x => x.GetString() == "*" ? "*.*" : "*." + x.GetString()); parts.Add($"{name}|{string.Join(';', patterns)}"); }
        return parts.Count == 0 ? "All Files|*.*" : string.Join('|', parts);
    }

    void SpecialButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SettingDef d }) return;
        if (d.Function == "openOutputDir" && _draftValues.TryGetValue("output_dir", out var p) && p.ValueKind == JsonValueKind.String && System.IO.Directory.Exists(p.GetString())) Process.Start(new ProcessStartInfo("explorer.exe", p.GetString()!) { UseShellExecute = true });
        else if (d.Function == "openPythonDir") Process.Start(new ProcessStartInfo("explorer.exe", System.IO.Path.Combine(_root, "third_party", "OoTR")) { UseShellExecute = true });
    }

    void DraftChanged(string key, JsonElement value)
    {
        _draftValues[key] = value.Clone();
        // SettingsToJson.py exposes GUI-only helper controls alongside real settings.
        // Keep helpers in the presentation model, but never feed them into the
        if (IsTransportSetting(key))
        {
            _changedKeys.Add(key);
            SettingsStringBox.Text = "";
            OutputText.Text = "Previous output, if any, must be regenerated.";
            ScheduleSettingsStringRefresh();
        }
        ApplyVisibility();
    }

    bool IsTransportSetting(string key)
    {
        if (_settings is not null)
            return _settings.State.Metadata.TryGetValue(key, out var metadata) && metadata.IsSettable;
        return key is not "output_types" and not "open_output_dir" and not "open_python_dir" and not "tricks_list_msg" and not "model_unavailable_msg" and not "sfx_link_unavailable_msg" and not "custom_music_unavailable_msg";
    }

    void ScheduleSettingsStringRefresh()
    {
        _settingsStringRefreshCts?.Cancel();
        _settingsStringRefreshCts?.Dispose();
        var cts = _settingsStringRefreshCts = new CancellationTokenSource();
        _ = RefreshSettingsStringAfterDelayAsync(cts);
    }

    async Task RefreshSettingsStringAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(180, cts.Token);
            if (cts.IsCancellationRequested || _busy) return;
            await EnsureOoTRAsync();
            SyncChangedSettingsIntent();
            var result = await _settings!.ConvertAsync(cts.Token);
            // A later UI edit cancels this refresh. Never paint an older Original-OoTR
            // conversion result over the settings string for a newer draft revision.
            if (!cts.IsCancellationRequested && ReferenceEquals(cts, _settingsStringRefreshCts))
            {
                SettingsStringBox.Text = result.SettingsString;
                StatusText.Text = "Settings String synchronized by Original OoTR.";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) StatusText.Text = "Original OoTR settings-string refresh failed: " + ex.Message;
        }
    }

    async Task RefreshSettingsStringNowAsync()
    {
        await EnsureOoTRAsync();
        SyncChangedSettingsIntent();
        var result = await _settings!.ConvertAsync();
        SettingsStringBox.Text = result.SettingsString;
    }

    void ApplyVisibility()
    {
        if (_schema is null) return;
        var enabled = _schema.Settings.Keys.ToDictionary(k => k, _ => true, StringComparer.Ordinal);
        var disabledSections = new HashSet<string>(StringComparer.Ordinal);
        var disabledTabs = new HashSet<string>(StringComparer.Ordinal);
        for (var pass = 0; pass < 12; pass++)
        {
            var next = _schema.Settings.Keys.ToDictionary(k => k, _ => true, StringComparer.Ordinal);
            var nextSections = new HashSet<string>(StringComparer.Ordinal);
            var nextTabs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in _schema.Settings.Values)
            {
                if (!enabled.GetValueOrDefault(d.Key, true) || !_draftValues.TryGetValue(d.Key, out var value)) continue;
                foreach (var o in SelectedOptions(d, value))
                {
                    foreach (var k in o.DisableSettings) if (next.ContainsKey(k)) next[k] = false;
                    foreach (var sectionId in o.DisableSections)
                    {
                        nextSections.Add(sectionId);
                        foreach (var tab in _schema.Tabs) foreach (var sec in tab.Sections.Where(x => x.Id == sectionId)) foreach (var child in sec.Settings) next[child.Key] = false;
                    }
                    foreach (var tabId in o.DisableTabs)
                    {
                        nextTabs.Add(tabId);
                        foreach (var tab in _schema.Tabs.Where(x => x.Id == tabId)) foreach (var sec in tab.Sections) foreach (var child in sec.Settings) next[child.Key] = false;
                    }
                }
            }
            var same = enabled.All(kv => next.GetValueOrDefault(kv.Key) == kv.Value);
            enabled = next; disabledSections = nextSections; disabledTabs = nextTabs;
            if (same) break;
        }
        foreach (var (key, host) in _settingHosts)
            if (_schema.Settings.TryGetValue(key, out var d)) { var on = enabled.GetValueOrDefault(key, true); host.IsEnabled = on; host.Visibility = !on && d.HideWhenDisabled ? Visibility.Collapsed : Visibility.Visible; }
        foreach (var (key, host) in _sectionHosts) host.IsEnabled = !disabledSections.Contains(key);
        foreach (var (key, host) in _tabHosts) host.IsEnabled = !disabledTabs.Contains(key);
    }

    static IEnumerable<OptionDef> SelectedOptions(SettingDef d, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) { var vals = value.EnumerateArray().Select(v => v.GetRawText()).ToHashSet(); return d.Options.Where(o => vals.Contains(o.Value.GetRawText())); }
        return d.Options.Where(o => JsonEquals(o.Value, value));
    }

    static object? CreateOoTRToolTip(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Original Electron tooltips contain simple HTML (<b>, etc.). WPF ToolTip
        // does not render HTML, so preserve the original wording while removing
        // markup. Localization can translate this source text later without
        // changing the setting semantics.
        var text = System.Text.RegularExpressions.Regex.Replace(raw, "<[^>]+>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text).Trim();
        return new TextBlock
        {
            Text = UiLocalization.T(text),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 430,
            Foreground = new SolidColorBrush(Color.FromRgb(238, 238, 238)),
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Padding = new Thickness(12)
        };
    }

    static bool JsonEquals(JsonElement a, JsonElement b) => a.GetRawText() == b.GetRawText();
    static object? JsonElementToObject(JsonElement e) => e.ValueKind switch { JsonValueKind.String => e.GetString(), JsonValueKind.Number when e.TryGetInt32(out var i) => i, JsonValueKind.Number => e.GetDouble(), JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null, _ => JsonSerializer.Deserialize<object>(e.GetRawText()) };
    static JsonElement ParseText(SettingDef d, string text) => d.Default.ValueKind switch { JsonValueKind.Number when int.TryParse(text, out var i) => JsonSerializer.SerializeToElement(Math.Clamp(i, d.Min ?? i, d.Max ?? i)), _ => JsonSerializer.SerializeToElement(text) };
    static string Display(JsonElement e) => e.ValueKind switch { JsonValueKind.String => e.GetString() ?? "", JsonValueKind.Array => string.Join(", ", e.EnumerateArray().Select(Display)), JsonValueKind.Null => "", _ => e.GetRawText() };

    void SyncChangedSettingsIntent()
    {
        if (_settings is null) throw new InvalidOperationException("OoTR settings state is not initialized.");
        foreach (var key in _changedKeys) if (_draftValues.TryGetValue(key, out var value))
        {
            // Defense in depth: Electron GUI helpers (the seven NoneType entries)
            // must never reach M8SettingsStateEditor/Original OoTR Settings.
            if (!_settings.State.Metadata.TryGetValue(key, out var metadata) || !metadata.IsSettable) continue;
            var transport=value.Clone(); if(value.ValueKind==JsonValueKind.String && value.GetString()=="Custom Color" && _customColors.TryGetValue(key,out var hex)) transport=JsonSerializer.SerializeToElement(hex.TrimStart('#'));
            _settings.SetUserValueIfChanged(key, new M8SettingValue(transport), "M8.5RandomizerUI");
        }
    }

    async void Reload_Click(object sender, RoutedEventArgs e) => await LoadSchemaAsync();
    void CopySettings_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(SettingsStringBox.Text)) Clipboard.SetText(SettingsStringBox.Text); }
    async Task<JsonElement> DecodeSettingsStringLikeOriginalGuiAsync(string settingsString, CancellationToken ct = default)
    {
        // The frozen M3 ConvertSettings contract returns Settings.to_json(), whose modern
        // starting_items dictionary is intentionally different from the Electron GUI's
        // legacy starting_equipment/starting_inventory/starting_songs controls.
        // Use the exact Original OoTR CLI path used by Electron for settings-string import:
        // OoTRandomizer.py --convert_settings --settings_string <string>.
        var ootr = _runtime.OoTRRoot;
        var script = System.IO.Path.Combine(ootr, "OoTRandomizer.py");
        if (!System.IO.File.Exists(script)) throw new FileNotFoundException("Original OoTR runtime is not prepared.", script);

        var psi = new ProcessStartInfo("py")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ootr
        };
        psi.ArgumentList.Add("-3.13");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("--convert_settings");
        psi.ArgumentList.Add("--settings_string");
        psi.ArgumentList.Add(settingsString);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Original OoTR settings-string decoder could not start.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0) throw new InvalidOperationException("Original OoTR settings-string import failed: " + (string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim()));
        using var document = JsonDocument.Parse(stdout);
        return document.RootElement.Clone();
    }

    bool TryNormalizeImportedGuiValueLikeOriginal(SettingDef def, JsonElement incoming, out JsonElement normalized)
    {
        normalized = incoming.Clone();
        // Mirrors GUIGlobal.applySettingsObject(): old/foreign values must not poison
        // the GUI state.  Original Electron validates choice/list/numeric controls while
        // applying an imported settings object.
        if (def.Type == "Combobox" || def.Type == "Radiobutton")
        {
            var option = def.Options.FirstOrDefault(o => JsonEquals(o.Value, incoming));
            if (option is null) return false;
            normalized = option.Value.Clone();
            return true;
        }
        if (def.Type == "SearchBox")
        {
            if (incoming.ValueKind != JsonValueKind.Array) return false;
            var allowed = def.Options.Select(o => o.Value.GetRawText()).ToHashSet(StringComparer.Ordinal);
            var values = incoming.EnumerateArray().Where(v => allowed.Contains(v.GetRawText())).Select(JsonElementToObject).ToArray();
            normalized = JsonSerializer.SerializeToElement(values);
            return true;
        }
        if (def.Type == "MultipleSelect")
        {
            if (incoming.ValueKind != JsonValueKind.Array) return false;
            normalized = incoming.Clone();
            return true;
        }
        if (def.Type is "Scale" or "Numberinput")
        {
            if (incoming.ValueKind != JsonValueKind.Number || !incoming.TryGetInt32(out var number)) return false;
            if (def.Min is int min) number = Math.Max(number, min);
            if (def.Max is int max) number = Math.Min(number, max);
            normalized = JsonSerializer.SerializeToElement(number);
            return true;
        }
        return true;
    }

    async Task<string> ImportSettingsStringCoreAsync(string requestedString)
    {
        if (string.IsNullOrWhiteSpace(requestedString)) throw new InvalidOperationException("Enter a settings string first.");
        await EnsureOoTRAsync();

        _settingsStringRefreshCts?.Cancel();
        _settingsStringRefreshCts?.Dispose();
        _settingsStringRefreshCts = null;

        var decoded = await DecodeSettingsStringLikeOriginalGuiAsync(requestedString);
        if (decoded.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Original OoTR returned no settings object for the imported string.");

        foreach (var property in decoded.EnumerateObject())
        {
            if (_schema is null || !_schema.Settings.TryGetValue(property.Name, out var def)) continue;
            if (_settings is null || !_settings.State.Metadata.TryGetValue(property.Name, out var metadata) || !metadata.IsSettable) continue;
            if (!TryNormalizeImportedGuiValueLikeOriginal(def, property.Value, out var value)) continue;
            _draftValues[property.Name] = value.Clone();
            _changedKeys.Add(property.Name);
            _settings.SetUserValueIfChanged(property.Name, new M8SettingValue(value.Clone()), "M8.5OoTRSettingsStringImport");
        }

        // A settings string intentionally contains seed/shared settings only.  As in
        // Original Electron, cosmetics/SFX and other non-shared GUI state are untouched.
        var normalized = await _settings!.ConvertAsync();
        SettingsStringBox.Text = normalized.SettingsString;
        if (TabList.SelectedItem is ListBoxItem { Tag: TabDef tab }) Render(tab);
        ApplyVisibility();
        return normalized.SettingsString;
    }

    async void ImportSettings_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var requestedString = SettingsStringBox.Text.Trim();
        var normalized = await ImportSettingsStringCoreAsync(requestedString);
        StatusText.Text = string.Equals(requestedString, normalized, StringComparison.Ordinal)
            ? "Settings string imported and round-trip verified by Original OoTR."
            : "Settings string imported; Original OoTR normalized it to the current 9.1.36 schema.";
    });

    async void Convert_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        await EnsureOoTRAsync();
        var typed = SettingsStringBox.Text.Trim();
        var current = _settings!.State.HasCurrentSettingsString ? _settings.State.SettingsString!.SettingsString : "";

        // V2's Check Settings button must never silently discard a string the user just
        // pasted.  If the text differs from the revision-bound string, treat it exactly
        // like Original Electron's Import first, then validate/re-encode that imported state.
        if (!string.IsNullOrWhiteSpace(typed) && !string.Equals(typed, current, StringComparison.Ordinal))
        {
            var normalizedImported = await ImportSettingsStringCoreAsync(typed);
            SettingsStringBox.Text = normalizedImported;
            StatusText.Text = "Imported settings checked and normalized by Original OoTR.";
            return;
        }

        SyncChangedSettingsIntent();
        var result = await _settings.ConvertAsync();
        SettingsStringBox.Text = result.SettingsString;
        StatusText.Text = "Settings accepted and normalized by Original OoTR.";
    });

    void SelectPatchFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg=new OpenFileDialog{Filter="Patch File Archive|*.zpfz;*.zpf;*.patch|All Files|*.*"}; if(dlg.ShowDialog()==true) PatchFileBox.Text=dlg.FileName;
    }
    async void GeneratePatch_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var rom=GetDraftString("rom"); var outputDir=GetDraftString("output_dir"); if(string.IsNullOrWhiteSpace(rom)) throw new InvalidOperationException("Select Base ROM in ROM Options first."); if(string.IsNullOrWhiteSpace(outputDir)) throw new InvalidOperationException("Select Output Directory in ROM Options first."); if(!System.IO.File.Exists(PatchFileBox.Text)) throw new InvalidOperationException("Select a valid OoTR patch file first.");
        var run=System.IO.Path.Combine(outputDir,"Patch-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));System.IO.Directory.CreateDirectory(run); var settings=_draftValues.ToDictionary(x=>x.Key,x=>JsonElementToObject(x.Value)); settings["rom"]=rom;settings["output_dir"]=run;settings["patch_file"]=PatchFileBox.Text;settings["repatch_cosmetics"]=RepatchCosmeticsBox.IsChecked==true;
        foreach(var kv in _customColors) if(_draftValues.TryGetValue(kv.Key,out var v)&&v.ValueKind==JsonValueKind.String&&v.GetString()=="Custom Color") settings[kv.Key]=kv.Value.TrimStart('#');
        var temp=System.IO.Path.Combine(_paths.Temporary,"ootr-patch-"+Guid.NewGuid().ToString("N")+".json");System.IO.Directory.CreateDirectory(_paths.Temporary);await System.IO.File.WriteAllTextAsync(temp,JsonSerializer.Serialize(settings));
        try { var ootr=System.IO.Path.Combine(_root,"third_party","OoTR");var psi=new ProcessStartInfo("py"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=ootr};psi.ArgumentList.Add("-3.13");psi.ArgumentList.Add("OoTRandomizer.py");psi.ArgumentList.Add("--settings");psi.ArgumentList.Add(temp);psi.ArgumentList.Add("--no_log");using var proc=Process.Start(psi)??throw new InvalidOperationException("Original OoTR patch process could not start.");_generationCts=new();CancelButton.IsEnabled=true;StatusText.Text="Generating from Original OoTR patch file …";var so=proc.StandardOutput.ReadToEndAsync();var se=proc.StandardError.ReadToEndAsync();await proc.WaitForExitAsync(_generationCts.Token);var err=await se;if(proc.ExitCode!=0)throw new InvalidOperationException("Original OoTR patch generation failed: "+err.Trim());OutputText.Text=$"Patch output: {run}";StatusText.Text="Patch generation complete with Original OoTR."; }
        finally { try{System.IO.File.Delete(temp);}catch{} }
    });

    async void Generate_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(async () =>
    {
        var rom = GetDraftString("rom"); var outputDir = GetDraftString("output_dir"); var worlds = GetDraftInt("world_count", 1);
        if (string.IsNullOrWhiteSpace(rom)) throw new InvalidOperationException("Select Base ROM in ROM Options first.");
        if (string.IsNullOrWhiteSpace(outputDir)) throw new InvalidOperationException("Select Output Directory in ROM Options first.");
        await EnsureOoTRAsync(); _runtime.ClearRomCache(); _randomizer.SelectRom(rom); var validation = await _randomizer.ValidateAsync(); if (!validation.Valid) throw new InvalidOperationException(validation.Detail);
        SyncChangedSettingsIntent(); var converted = await _settings!.ConvertAsync(); SettingsStringBox.Text = converted.SettingsString;
        System.IO.Directory.CreateDirectory(outputDir); var run = System.IO.Path.Combine(outputDir, "Run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")); System.IO.Directory.CreateDirectory(run);
        _generationCts = new(); CancelButton.IsEnabled = true;
        var output = new M7OutputOptions(run, GetDraftBool("create_patch_file"), GetDraftBool("create_compressed_rom"), GetDraftBool("create_uncompressed_rom"), GetDraftBool("create_spoiler"));
        var seed = string.IsNullOrWhiteSpace(SeedBox.Text) ? null : SeedBox.Text.Trim(); StatusText.Text = "Generating with Original OoTR …";
        var result = await _settings.GenerateAsync(rom, seed, worlds, output, _generationCts.Token); SettingsStringBox.Text = result.SettingsString; SeedBox.Text = result.Seed; OutputText.Text = $"Seed {result.Seed} • {result.OutputFiles.Count} file(s) • {result.OutputDirectory}"; StatusText.Text = $"Generation complete with Original OoTR {result.RandomizerVersion}.";
    });

    string GetDraftString(string key) => _draftValues.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    int GetDraftInt(string key, int fallback) => _draftValues.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
    bool GetDraftBool(string key) => _draftValues.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.True;

    async void Cancel_Click(object sender, RoutedEventArgs e) { try { await _randomizer.CancelGenerationAsync(); _generationCts?.Cancel(); StatusText.Text = "Generation cancelled."; } catch (Exception ex) { StatusText.Text = "Cancel failed: " + ex.Message; } finally { CancelButton.IsEnabled = false; } }
    async Task RunBusyAsync(Func<Task> action) { if (_busy) return; _busy = true; RefreshButtons(); try { await action(); } catch (OperationCanceledException) { StatusText.Text = "Operation cancelled."; } catch (Exception ex) { StatusText.Text = ex.Message; MessageBox.Show(ex.Message, "Randomizer", MessageBoxButton.OK, MessageBoxImage.Warning); } finally { _generationCts?.Dispose(); _generationCts = null; _busy = false; CancelButton.IsEnabled = false; RefreshButtons(); } }
    void RefreshButtons() { ConvertButton.IsEnabled = !_busy && _schema is not null; GenerateButton.IsEnabled = !_busy && _schema is not null; GeneratePatchButton.IsEnabled = !_busy && _schema is not null; ReloadButton.IsEnabled = !_busy; }
    public async Task ShutdownAsync() { _settingsStringRefreshCts?.Cancel(); _settingsStringRefreshCts?.Dispose(); _settingsStringRefreshCts = null; _generationCts?.Cancel(); if (_host is not null) { await _host.DisposeAsync(); _host = null; } _runtime.Cleanup(); }

    public void ApplyLanguage(bool german)
    {
        UiLocalization.SetGerman(german);
        // Rebuild the currently visible Original OoTR page from the immutable
        // English schema. This prevents mixed DE/EN controls created after the
        // first visual-tree localization pass. Internal keys/values are untouched.
        if (_schema is not null && IsLoaded)
        {
            foreach (var pair in _tabHosts)
                if (pair.Value.Tag is TabDef tab) pair.Value.Content = UiLocalization.T(tab.Text);
            if (TabList.SelectedItem is ListBoxItem li && li.Tag is TabDef selected) Render(selected);
        }
        UiLocalization.Apply(this);
    }
}
