using System.Text.Json;

namespace BossMod;

[ConfigDisplay(Name = "Boss modules and radar", Order = 1)]
public sealed class BossModuleConfig : ConfigNode
{
    // boss module settings
    [PropertyDisplay("Minimal maturity for the module to be loaded", tooltip: "Some modules will have the \"WIP\" status and will not automatically load unless you change this")]
    public BossModuleInfo.Maturity MinMaturity = BossModuleInfo.Maturity.Contributed;

    [PropertyDisplay("Allow modules to automatically use actions", tooltip: "Example: modules can automatically use anti-knockback abilities before a knockback happens")]
    public bool AllowAutomaticActions = true;

    [PropertyDisplay("Show testing radar and hint window", tooltip: "Useful for configuring your radar and hint windows without being inside of a boss encounter", separator: true)]
    public bool ShowDemo = false;

    // radar window settings
    [PropertyDisplay("Enable radar")]
    public bool Enable = true;

    [PropertyDisplay("Lock radar and hint window movement and mouse interaction")]
    public bool Lock = false;

    [PropertyDisplay("Transparent radar window background", tooltip: "Removes the black window around the radar; this will not work if you move the radar to a different monitor")]
    public bool TrishaMode = true;

    [PropertyDisplay("Add opaque background to the arena in the radar")]
    public bool OpaqueArenaBackground = true;

    [PropertyDisplay("Show outlines and shadows on various radar markings")]
    public bool ShowOutlinesAndShadows = true;

    [PropertyDisplay("Radar arena scale factor", tooltip: "Scale of the arena inside of the radar window")]
    [PropertySlider(0.1f, 10, Speed = 0.1f, Logarithmic = true)]
    public float ArenaScale = 1;

    [PropertyDisplay("Radar element thickness scale factor", tooltip: "Globally scales the outline thickness of radar elements")]
    [PropertySlider(0.1f, 10, Speed = 0.1f, Logarithmic = true)]
    public float ThicknessScale = 1;

    [PropertyDisplay("Rotate radar to match camera orientation")]
    public bool RotateArena = true;

    [PropertyDisplay("Rotate map by 180° if rotating map is off")]
    public bool FlipArena = false;

    [PropertyDisplay("Give radar extra space for rotations", tooltip: "If you are using the above setting, you can give the radar extra space on the sides before the edges are clipped in order to account for rotating your camera during an encounter or to give the cardinal directions space.")]
    [PropertySlider(1, 2, Speed = 0.1f, Logarithmic = true)]
    public float SlackForRotations = 1.5f;

    [PropertyDisplay("Show arena border in radar")]
    public bool ShowBorder = true;

    [PropertyDisplay("Change arena border color if player is at risk", tooltip: "Changes the white border to red when you are standing somewhere you are likely to be hit by a mechanic")]
    public bool ShowBorderRisk = true;

    [PropertyDisplay("Show cardinal direction names on radar")]
    public bool ShowCardinals = false;

    [PropertyDisplay("Cardinal direction font size")]
    [PropertySlider(0.1f, 100, Speed = 1)]
    public float CardinalsFontSize = 17;

    [PropertyDisplay("Waymark font size")]
    [PropertySlider(0.1f, 100, Speed = 1)]
    public float WaymarkFontSize = 22;

    [PropertyDisplay("Actor triangle scale factor")]
    [PropertySlider(0.1f, 10, Speed = 0.1f)]
    public float ActorScale = 1;

    [PropertyDisplay("Show waymarks on radar")]
    public bool ShowWaymarks = false;

    [PropertyDisplay("Always show all alive party members")]
    public bool ShowIrrelevantPlayers = false;

    [PropertyDisplay("Show role-based colors on otherwise uncolored players in the radar")]
    public bool ColorPlayersBasedOnRole = false;

    [PropertyDisplay("Always show focus targeted party member", separator: true)]
    public bool ShowFocusTargetPlayer = false;

    // hint window settings
    [PropertyDisplay("Show text hints in separate window", tooltip: "Separates the radar window from the hints window, allowing you to reposition the hints window")]
    public bool HintsInSeparateWindow = false;

    // legacy on/off transparency toggle, superseded by HintsWindowOpacity below; intentionally left serializable (and without PropertyDisplay, so it no longer shows up in the UI)
    // so that an existing config is migrated rather than silently discarded - see Deserialize below
    public bool HintsInSeparateWindowTransparent = false;

    [PropertyDisplay("Separate hints window background opacity (%)", tooltip: "0 = fully transparent background, same as the old 'Make separate hints window transparent' checkbox; 100 = normal window background")]
    [PropertySlider(0, 100, Speed = 1)]
    public int HintsWindowOpacity = 100;

    [PropertyDisplay("Show mechanic sequence and timer hints")]
    public bool ShowMechanicTimers = true;

    [PropertyDisplay("Show raidwide hints")]
    public bool ShowGlobalHints = true;

    [PropertyDisplay("Show player hints and warnings")]
    public bool ShowPlayerHints = true;

    [PropertyDisplay("Translate hint text", tooltip: "Translates hints when displaying them: whole hints that have a translation are replaced outright, otherwise well-known mechanic vocabulary inside the hint is swapped for its localized term. Display only - the hints themselves are not modified")]
    public bool TranslateHints = true;

    [PropertyDisplay("Mark hints as physical or magical damage", tooltip: "Appends the damage type of the incoming attack to raidwide/tankbuster hints, taken from the action's attack type in the game data.\nAttack types that do not map onto physical or magical (and actions the module does not name) are shown as a greyed out '?' rather than guessed at", separator: true)]
    public bool ShowHintDamageType = true;

    // misc. settings
    [PropertyDisplay("Show movement hints in world", tooltip: "Not used very much, but can show you arrows in the game world to indicate where to move for certain mechanics")]
    public bool ShowWorldArrows = false;

    [PropertyDisplay("Show melee range indicator")]
    public bool ShowMeleeRangeIndicator = false;

    public override void Deserialize(JsonElement j, JsonSerializerOptions ser)
    {
        base.Deserialize(j, ser);

        // backwards compatibility: the 'make separate hints window transparent' checkbox became an opacity slider.
        // if the stored config predates the slider, map the old checked state onto a fully transparent background, which is what it used to do.
        // once the config is written out again it will contain HintsWindowOpacity, and from then on the slider value wins.
        if (HintsInSeparateWindowTransparent && !j.TryGetProperty(nameof(HintsWindowOpacity), out _))
            HintsWindowOpacity = 0;
    }
}
