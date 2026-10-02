using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrackerOfTime.V2.M8_5.Desktop;

internal static class UiLocalization
{
    internal static bool German { get; private set; } = true;

    // Exact translations are intentionally presentation-only. OoTR setting keys,
    // option JSON values, tracker IDs and all frozen contracts remain English/canonical.
    static readonly Dictionary<string,string> De = new(StringComparer.Ordinal)
    {
      ["TRACKER OF TIME V2"]="TRACKER OF TIME V2",
      ["Home"]="Start", ["Play"]="Spielen", ["Tracker"]="Tracker", ["Map"]="Karte", ["Settings"]="Einstellungen",
      ["Generate New Seed"]="Neuen Seed erzeugen", ["Generate From Patch File"]="Aus Patch-Datei erzeugen", ["SETTINGS STRING"]="EINSTELLUNGS-STRING", ["SEED"]="SEED", ["PATCH FILE"]="PATCH-DATEI",
      ["COPY"]="KOPIEREN", ["IMPORT"]="IMPORTIEREN", ["SELECT FILE"]="DATEI WÄHLEN", ["Override Original Cosmetics"]="Original-Kosmetik überschreiben", ["⚒  GENERATE!"]="⚒  ERZEUGEN!",
      ["↻ Reload Original"]="↻ Original neu laden", ["✓ Check Settings"]="✓ Einstellungen prüfen", ["Cancel"]="Abbrechen", ["None"]="Keine", ["All"]="Alle", ["Browse"]="Durchsuchen", ["Load"]="Laden", ["Save"]="Speichern", ["Remove"]="Entfernen", ["Preset"]="Voreinstellung", ["Presets"]="Voreinstellungen",
      ["Checked"]="Erledigt", ["Unchecked"]="Offen", ["Unknown"]="Unbekannt", ["Evidence"]="Nachweis", ["Snapshot"]="Snapshot", ["Region"]="Region",
      ["Theme"]="Design", ["Language"]="Sprache", ["Active design is applied to the interface immediately."]="Aktives Design wird sofort auf die Oberfläche angewendet.", ["All eight designs have their own material, frame and shell geometry and can be switched at runtime."]="Alle acht Designs besitzen eigene Material-, Rahmen- und Shell-Geometrie und können zur Laufzeit gewechselt werden.", ["Ready"]="Bereit", ["Running"]="Läuft", ["Profile"]="Profil", ["Renderer"]="Renderer", ["Status"]="Status",
      ["ROM Options"]="ROM-Optionen", ["Main Rules"]="Hauptregeln", ["Detailed Logic"]="Detaillierte Logik", ["Starting Inventory"]="Startinventar", ["Other"]="Sonstiges", ["Cosmetics"]="Kosmetik", ["SFX"]="Soundeffekte",
      ["Base ROM"]="Basis-ROM", ["Output Directory"]="Ausgabeordner", ["Generation Count"]="Anzahl Generierungen", ["Create Spoiler Log"]="Spoiler-Log erstellen", ["Create Cosmetics Log"]="Kosmetik-Log erstellen", ["Output Types"]="Ausgabeformate",
      ["Enable Plandomizer (Advanced)"]="Plandomizer aktivieren (Erweitert)", ["Enable Cosmetic Plandomizer (Advanced)"]="Kosmetik-Plandomizer aktivieren (Erweitert)", ["Plandomizer File"]="Plandomizer-Datei", ["Cosmetic Plandomizer File"]="Kosmetik-Plandomizer-Datei",
      ["Show Seed Info on File Screen"]="Seed-Informationen im Dateibildschirm anzeigen", ["User-Configurable Message"]="Benutzerdefinierte Nachricht", ["Open Output Directory"]="Ausgabeordner öffnen", ["Open App Directory"]="Programmordner öffnen",
      ["Multi-World Generation"]="Multi-World-Generierung", ["Player Count"]="Spieleranzahl", ["Player ID"]="Spieler-ID", ["Settings Presets"]="Einstellungsvoreinstellungen",
      ["Randomize Main Rule Settings"]="Hauptregel-Einstellungen zufällig wählen", ["Logic Rules"]="Logikregeln", ["Glitchless"]="Ohne Glitches", ["Advanced"]="Erweitert", ["No Logic"]="Keine Logik",
      ["Open"]="Offen", ["Forest"]="Wald", ["Open Forest"]="Offener Wald", ["Closed Deku"]="Deku geschlossen", ["Closed Forest"]="Wald geschlossen", ["Kakariko Gate"]="Kakariko-Tor", ["Open Gate"]="Offenes Tor", ["Closed Gate"]="Geschlossenes Tor",
      ["Door of Time"]="Tor der Zeit", ["Song of Time"]="Hymne der Zeit", ["Spiritual Stones"]="Heilige Steine", ["Medallions"]="Medaillons", ["Dungeon Rewards"]="Dungeon-Belohnungen", ["Gold Skulltula Tokens"]="Goldene Skulltula-Symbole", ["Hearts"]="Herzen", ["Random"]="Zufällig",
      ["Default Behavior"]="Standardverhalten", ["Default Behavior (Closed)"]="Standardverhalten (geschlossen)", ["Open For Adult"]="Für Erwachsenen offen", ["Always Open"]="Immer offen", ["Off"]="Aus", ["Specific Dungeons"]="Bestimmte Dungeons", ["All Dungeons"]="Alle Dungeons", ["Random Dungeons"]="Zufällige Dungeons",
      ["Dungeon Boss Shortcuts Mode"]="Dungeon-Boss-Abkürzungen", ["Dungeon Boss Shortcuts"]="Dungeon-Boss-Abkürzungen", ["Rainbow Bridge Requirement"]="Voraussetzung für die Regenbogenbrücke", ["Vanilla Requirements"]="Original-Voraussetzungen",
      ["Deku Tree"]="Deku-Baum", ["Dodongo's Cavern"]="Dodongos Höhle", ["Jabu Jabu's Belly"]="Jabu-Jabus Bauch", ["Forest Temple"]="Waldtempel", ["Fire Temple"]="Feuertempel", ["Water Temple"]="Wassertempel", ["Shadow Temple"]="Schattentempel", ["Spirit Temple"]="Geistertempel",
      ["Japanese"]="Japanisch", ["English"]="Englisch", ["checked"]="aktiviert", ["unchecked"]="deaktiviert", ["Custom Color"]="Benutzerdefinierte Farbe", ["Custom color (#RRGGBB)"]="Benutzerdefinierte Farbe (#RRGGBB)",
      ["Filter available"]="Verfügbare filtern", ["Filter selected"]="Ausgewählte filtern", ["All tags"]="Alle Tags", ["Selected: {count}"]="Ausgewählt: {count}", ["Create new preset"]="Neue Voreinstellung erstellen", ["[New Preset]"]="[Neue Voreinstellung]",
      ["Loading Original OoTR Electron schema …"]="Originales OoTR-Electron-Schema wird geladen …", ["Loading exact Original OoTR Electron schema …"]="Exaktes originales OoTR-Electron-Schema wird geladen …", ["Original OoTR Electron generator schema"]="Originales OoTR-Electron-Generator-Schema", ["Original OoTR Electron schema loaded."]="Originales OoTR-Electron-Schema geladen.",
      ["Settings String synchronized by Original OoTR."]="Einstellungs-String durch Original-OoTR synchronisiert.", ["Generation cancelled."]="Generierung abgebrochen.", ["Operation cancelled."]="Vorgang abgebrochen.", ["Previous output, if any, must be regenerated."]="Eine vorhandene Ausgabe muss neu generiert werden.",
      ["Select Base ROM in ROM Options first."]="Wähle zuerst die Basis-ROM unter ROM-Optionen aus.", ["Select Output Directory in ROM Options first."]="Wähle zuerst den Ausgabeordner unter ROM-Optionen aus.", ["Select a valid OoTR patch file first."]="Wähle zuerst eine gültige OoTR-Patch-Datei aus.", ["Enter a settings string first."]="Gib zuerst einen Einstellungs-String ein.",
      ["ADD >"]="HINZUFÜGEN >", ["< REMOVE"]="< ENTFERNEN", ["ALL >>"]="ALLE >>", ["<< NONE"]="<< KEINE",
      ["TRACKER WORKSPACE"]="TRACKER-ARBEITSBEREICH", ["Waiting for a real live snapshot."]="Wartet auf Live-Daten.", ["LIVE PROGRESS"]="LIVE FORTSCHRITT",
      ["DUNGEONS & KEYS"]="DUNGEONS & SCHLÜSSEL", ["INVENTORY • EQUIPMENT • QUEST • SONGS"]="INVENTAR • AUSRÜSTUNG • QUEST • SONGS", ["Equipment"]="Ausrüstung",
      ["This view shows read-only live game data; clicks never change game state."]="Die Anzeige zeigt ausschließlich gelesene Live-Daten; Klicks verändern keinen Spielzustand.",
      ["Session dashboard • real states from Play, GameCore and Tracker"]="Session-Dashboard • echte Zustände aus Play, GameCore und Tracker", ["GAME & SESSION"]="SPIEL & SESSION",
      ["No ROM selected"]="Keine ROM ausgewählt", ["Session: ready"]="Session: bereit", ["Open Play workspace"]="Zum Play Workspace", ["No live snapshot yet"]="Noch keine Live-Daten",
      ["Open tracker"]="Tracker öffnen", ["QUICK ACCESS"]="DIREKTZUGRIFF",
      ["Home creates no game state of its own. All displays reflect the existing session/snapshot data."]="Home erzeugt keine eigenen Spielzustände. Alle Anzeigen spiegeln ausschließlich die vorhandenen Live-Daten.",
      ["Emulator and input settings can be changed before starting a session."]="Emulator- und Eingabeeinstellungen können vor dem Sessionstart geändert werden.", ["INPUT PROFILE"]="EINGABEPROFIL",
      ["Save & apply bindings"]="Belegung speichern & anwenden", ["Default bindings"]="Standardbelegung", ["CONTROLLER & KEYBOARD BINDINGS"]="CONTROLLER & TASTATURBELEGUNG",
      ["Configure controller"]="Controller konfigurieren", ["Configure keyboard"]="Tastatur konfigurieren", ["N64 CONTROLLER • CURRENT BINDINGS"]="N64 CONTROLLER • AKTUELLE BELEGUNG", ["APPEARANCE"]="DARSTELLUNG",
      ["Global overview • live region and check state from the same game session"]="Globale Übersicht • Live-Region und Checkstatus aus derselben Spielsitzung",
      ["The display shows read-only live game data; clicks do not change game state."]="Die Anzeige zeigt ausschließlich gelesene Live-Daten; Klicks verändern keinen Spielzustand.",
      ["The binding is stored persistently in the UserData settings folder and loaded by the real Mupen64Plus input plugin on the next emulator start."]="Die Belegung wird persistent im UserData-Settings-Ordner gespeichert und beim nächsten Emulatorstart vom echten Mupen64Plus-Input-Plugin geladen.",
      ["Start once, then enter the complete N64 binding in sequence. The N64 binding is visualized live below."]="Einmal starten, dann die komplette N64-Belegung der Reihe nach eingeben. Die N64-Belegung wird unten live visualisiert.",
      ["Use Direct-XInput"]="Direct-XInput verwenden", ["Save & apply binding"]="Belegung speichern & anwenden", ["Default binding"]="Standardbelegung", ["CURRENT BINDING"]="AKTUELLE BELEGUNG",
      ["Select Nintendo 64 ROM"]="Nintendo 64 ROM auswählen", ["All files"]="Alle Dateien", ["Unnamed N64 ROM"]="Unbenannte N64 ROM", ["ROM check"]="ROM-Prüfung",
      ["Tracker candidate detected; waiting for live snapshot."]="Tracker-Kandidat erkannt; wartet auf Live-Daten.", ["Emulator-only profile."]="Emulator-only Profil.", ["ROM detected:"]="ROM erkannt:",
      ["GameCore: running"]="GameCore: läuft", ["Mupen64Plus: running"]="Mupen64Plus: läuft", ["unknown"]="unbekannt",
      ["Session stopped cleanly; emulator window detached."]="Session sauber gestoppt; Emulatorfenster detached.", ["Tracker: stopped"]="Tracker: gestoppt",
      ["MAP\nwaiting for live snapshot"]="KARTE\nwartet auf Live-Daten", ["Session: running"]="Session: läuft", ["Profile:"]="Profil:", ["CURRENT"]="AKTUELL", ["No live region"]="Keine Live-Region",
      ["REGION STATUS"]="REGIONSTATUS", ["Green = complete • orange = partial • red = open. Unknown reachability data is never fabricated."]="Grün = vollständig • Orange = teilweise • Rot = offen. Es werden keine unbekannten Reachability-Daten erfunden.",
      ["LIVE CHECKS OF CURRENT MINIMAP"]="LIVE CHECKS DER AKTUELLEN MINIMAP", ["No game selected"]="Kein Spiel ausgewählt", ["Select ROM"]="ROM auswählen", ["ROM LOADED"]="ROM GELADEN", ["Change ROM …"]="ROM wechseln …",
      ["Play time:"]="Spielzeit:", ["not started"]="nicht gestartet", ["Start game"]="Spiel starten", ["Stop game"]="Spiel stoppen", ["Start tracker"]="Tracker starten",
      ["GAME DISPLAY"]="SPIELANZEIGE", ["Emulator surface"]="Emulatorfläche", ["Find host again"]="Host erneut suchen", ["After starting the game, the real native Mupen window is embedded here."]="Nach Spielstart wird hier das echte native Mupen-Fenster eingebettet.",
      ["CURRENT HINTS / LOG"]="AKTUELLE HINWEISE / LOG", ["V2 workspace ready. Select a ROM and start the session."]="V2 Workspace bereit. ROM auswählen und Session starten.", ["WORLD MAP"]="WELTKARTE", ["REGIONS"]="REGIONEN", ["REGIONS / CHECKS"]="REGIONEN / CHECKS",
      ["waiting for check data"]="wartet auf Check-Daten", ["ready"]="bereit", ["not connected"]="nicht verbunden", ["offline"]="offline",
      ["No live region yet"]="Noch keine Live-Region", ["No reliable check points are available for the current minimap."]="Für die aktuelle Minimap liegen keine belastbaren Checkpunkte vor.",
      ["Keyboard"]="Tastatur", ["Controller"]="Controller", ["N64 controller profile saved."]="N64-Controllerprofil gespeichert.", ["Position:"]="Position:",
      ["PROGRESS"]="FORTSCHRITT", ["Items"]="Gegenstände", ["Songs"]="Lieder", ["Dungeons"]="Dungeons", ["Checks"]="Checks", ["GameCore: ready"]="GameCore: bereit", ["Input: not started"]="Input: nicht gestartet", ["Tracker: offline"]="Tracker: offline",
      ["Replaces the cosmetic and sound settings generated in the patch file with those selected on this page."]="Ersetzt die in der Patch-Datei erzeugten Kosmetik- und Soundeinstellungen durch die auf dieser Seite ausgewählten Einstellungen.",
      ["Generating with Original OoTR …"]="Generierung mit Original-OoTR …", ["Generation complete with Original OoTR"]="Generierung mit Original-OoTR abgeschlossen", ["Imported settings checked and normalized by Original OoTR."]="Importierte Einstellungen wurden durch Original-OoTR geprüft und normalisiert.",
      ["Original OoTR defaults restored; settings string refreshed."]="Original-OoTR-Standardwerte wiederhergestellt; Einstellungs-String aktualisiert.", ["Settings accepted and normalized by Original OoTR."]="Einstellungen wurden durch Original-OoTR akzeptiert und normalisiert."
    };

    // OoTR has hundreds of option labels/tooltips. Exact entries above cover terms
    // where a literal replacement would be ambiguous; these ordered phrases cover
    // the remaining presentation prose while preserving names, numbers and tokens.
    static readonly (string En,string De)[] Phrases =
    {
      ("Starting Inventory","Startinventar"),("Starting Items","Startgegenstände"),("Starting Songs","Startlieder"),("Starting Equipment","Startausrüstung"),
      ("Randomize","Zufällig wählen"),("Shuffle","Mischen"),("Requirement","Voraussetzung"),("Requirements","Voraussetzungen"),("Settings","Einstellungen"),("Setting","Einstellung"),
      ("Dungeon","Dungeon"),("Dungeons","Dungeons"),("Boss Key","Boss-Schlüssel"),("Small Keys","Kleine Schlüssel"),("Key Rings","Schlüsselringe"),("Keys","Schlüssel"),
      ("Enabled","Aktiviert"),("Disabled","Deaktiviert"),("Enable","Aktivieren"),("Disable","Deaktivieren"),("Default","Standard"),("Vanilla","Original"),
      ("Open","Offen"),("Closed","Geschlossen"),("Always","Immer"),("Never","Nie"),("Adult","Erwachsener"),("Child","Kind"),("Both","Beide"),
      ("Items","Gegenstände"),("Item","Gegenstand"),("Locations","Fundorte"),("Location","Fundort"),("Rewards","Belohnungen"),("Reward","Belohnung"),
      ("Songs","Lieder"),("Song","Lied"),("Tokens","Symbole"),("Hearts","Herzen"),("Heart","Herz"),("Stones","Steine"),("Stone","Stein"),
      ("Entrance","Eingang"),("Entrances","Eingänge"),("Overworld","Oberwelt"),("Interior","Innenbereich"),("Interiors","Innenbereiche"),("Grotto","Grotte"),("Grottos","Grotten"),
      ("Hints","Hinweise"),("Hint","Hinweis"),("Logic","Logik"),("Tricks","Tricks"),("Trick","Trick"),("Cosmetic","Kosmetik"),("Cosmetics","Kosmetik"),
      ("Music","Musik"),("Sound Effects","Soundeffekte"),("Sound Effect","Soundeffekt"),("Color","Farbe"),("Colors","Farben"),("Random","Zufällig"),
      ("Select","Auswählen"),("Amount","Anzahl"),("Count","Anzahl"),("Maximum","Maximum"),("Minimum","Minimum"),("Additional","Zusätzliche"),
      ("Top Left","Oben links"),("Top Right","Oben rechts"),("Bottom Left","Unten links"),("Bottom Right","Unten rechts"),("Left","Links"),("Right","Rechts"),("Top","Oben"),("Bottom","Unten"),
      ("Chest","Truhe"),("Pot","Krug"),("Crate","Kiste"),("House","Haus"),("Shop","Laden"),("Near","Nahe"),("Behind","Hinter"),("Above","Über"),("Basement","Keller"),("Bridge","Brücke"),("Room","Raum"),("Maze","Labyrinth"),("Torch","Fackel"),("Tree","Baum"),
      ("Green Rupee","Grüner Rubin"),("Blue Rupee","Blauer Rubin"),("Red Rupee","Roter Rubin"),("Big Poe","Großer Irrlichtgeist"),("Cow","Kuh"),("Dog","Hund"),("Mask","Maske"),("Sword","Schwert"),("Shield","Schild"),("Boots","Stiefel"),("Tunic","Rüstung"),
      ("Create","Erstellen"),("Output","Ausgabe"),("File","Datei"),("Directory","Ordner"),("Language","Sprache"),("Player","Spieler"),("World","Welt"),
      ("This setting","Diese Einstellung"),("This option","Diese Option"),("will be","wird"),("can be","kann"),("must be","muss"),("is required","ist erforderlich"),
      ("Select the amount of","Wähle die Anzahl der"),("Select which","Wähle, welche"),("Choose which","Wähle, welche"),("Allows","Erlaubt"),("Prevents","Verhindert"),
      ("available","verfügbar"),("required","erforderlich"),("generated","generiert"),("generation","Generierung"),("game","Spiel"),("seed","Seed")
    };


    static readonly Dictionary<string,string> WordFallback = new(StringComparer.OrdinalIgnoreCase)
    {
      ["the"]="der",["to"]="zu",["of"]="von",["and"]="und",["with"]="mit",["in"]="in",["without"]="ohne",["can"]="kann",["you"]="du",["is"]="ist",["from"]="von",["for"]="für",["this"]="dies",["or"]="oder",["as"]="als",["it"]="es",["on"]="auf",["into"]="in",["through"]="durch",["by"]="durch",["will"]="wird",["be"]="sein",["not"]="nicht",["that"]="dass",["when"]="wenn",["if"]="wenn",["only"]="nur",["at"]="bei",["are"]="sind",["after"]="nach",["before"]="vor",["then"]="dann",["also"]="auch",["some"]="einige",["more"]="mehr",["very"]="sehr",["all"]="alle",["no"]="kein",
      ["Temple"]="Tempel",["Castle"]="Schloss",["Water"]="Wasser",["Fire"]="Feuer",["Shadow"]="Schatten",["Spirit"]="Geister",["Forest"]="Wald",["Ice"]="Eis",["Cavern"]="Höhle",["Graveyard"]="Friedhof",["Trial"]="Prüfung",["Room"]="Raum",["Door"]="Tür",["Gate"]="Tor",["Bridge"]="Brücke",["Market"]="Markt",["Fairy"]="Fee",["Fountain"]="Quelle",["Pillar"]="Säule",["Boulder"]="Fels",["Hallway"]="Gang",["Zone"]="Zone",["Platform"]="Plattform",["Statue"]="Statue",["Ceiling"]="Decke",["Spikes"]="Stacheln",["Wall"]="Wand",["Ground"]="Boden",["Basement"]="Keller",["Lobby"]="Lobby",["Switch"]="Schalter",["Block"]="Block",["Torch"]="Fackel",["Torches"]="Fackeln",
      ["Rupee"]="Rubin",["Rupees"]="Rubine",["Silver"]="Silber",["Gold"]="Gold",["Red"]="Rot",["Blue"]="Blau",["Green"]="Grün",["Invisible"]="Unsichtbar",["Freestanding"]="Freistehend",["Upper"]="Oben",["First"]="Erste",["Center"]="Mitte",["Front"]="Vorne",["Back"]="Hinten",
      ["Damage"]="Schaden",["Jump"]="Sprung",["Skip"]="Überspringen",["Reach"]="Erreichen",["Access"]="Zugang",["Enter"]="Betreten",["Kill"]="Besiegen",["Use"]="Benutzen",["Requires"]="Benötigt",["Possible"]="Möglich",["Precise"]="Präzise",["Change"]="Ändern",["Removes"]="Entfernt",["Moving"]="Beweglich",["Push"]="Schieben",["Load"]="Laden",["Loading"]="Laden",["Explosives"]="Sprengstoff",["Bow"]="Bogen",["Hammer"]="Hammer",["Boots"]="Stiefel",["Chest"]="Truhe",["Pots"]="Krüge",["Pot"]="Krug",["Key"]="Schlüssel",["Boss"]="Boss",["Sound"]="Sound",["Light"]="Licht",["Truth"]="Wahrheit",["Race"]="Rennen",["Requirement"]="Voraussetzung",["Requirements"]="Voraussetzungen",["Enabled"]="Aktiviert",["Advanced"]="Erweitert"
    };
    internal static void SetGerman(bool german) => German = german;
    internal static string T(string? text)
    {
      if(string.IsNullOrEmpty(text)) return text ?? string.Empty;
      var source=text;
      if(German)
      {
        if(De.TryGetValue(source,out var exact)) return exact;
        if(source.StartsWith("Selected: ",StringComparison.OrdinalIgnoreCase)) return "Ausgewählt: "+source[10..];
        foreach(var (en,de) in Phrases) source=ReplacePhrase(source,en,de);
        source=TranslateFallbackWords(source);
        return source;
      }
      // English mode also translates German-first shell text. This is essential
      // because the frozen M8.5 presentation historically contained a DE/EN mix.
      foreach(var pair in De) if(string.Equals(source,pair.Value,StringComparison.Ordinal)) return pair.Key;
      if(source.StartsWith("Ausgewählt: ",StringComparison.OrdinalIgnoreCase)) return "Selected: "+source[11..];
      foreach(var (en,de) in Phrases.Reverse()) source=ReplacePhrase(source,de,en);
      return source;
    }
    static string ReplacePhrase(string input,string from,string to) => Regex.Replace(input,$@"(?<![A-Za-z]){Regex.Escape(from)}(?![A-Za-z])",to,RegexOptions.CultureInvariant);
    static string TranslateFallbackWords(string input) => Regex.Replace(input,@"(?<![A-Za-z])[A-Za-z]+(?![A-Za-z])",m=>WordFallback.TryGetValue(m.Value,out var v)?MatchCase(m.Value,v):m.Value,RegexOptions.CultureInvariant);
    static string MatchCase(string source,string translated) => source.Length>0 && char.IsUpper(source[0]) && translated.Length>0 ? char.ToUpperInvariant(translated[0])+translated[1..] : translated;
    internal static void Apply(DependencyObject root)
    {
      if(root is TextBlock tb) ApplyText(tb, tb.Text, v=>tb.Text=v);
      else if(root is ContentControl cc && cc.Content is string c) ApplyText(cc,c,v=>cc.Content=v);
      else if(root is HeaderedContentControl hc && hc.Header is string h) ApplyText(hc,h,v=>hc.Header=v);
      if(root is FrameworkElement fe && fe.ToolTip is string tip) ApplyToolTip(fe,tip);
      for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) Apply(VisualTreeHelper.GetChild(root,i));
    }
    static void ApplyText(DependencyObject owner,string current,Action<string> set) => set(T(current));
    static void ApplyToolTip(FrameworkElement owner,string current) => owner.ToolTip=T(current);
}
