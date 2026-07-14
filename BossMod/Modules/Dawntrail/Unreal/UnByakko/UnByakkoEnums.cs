// IDs below were extracted from a real captured combat log (Chinese client, ACT-plugin style),
// see d:\BossmodReborn\log\_VPR100_Lo_U_2026_07_14_23_54_41.log.
// The user confirmed the mechanics of Unreal Byakko (幻白虎征魂戰) are structurally identical
// (1:1 reskin) to Ex6Byakko (Extreme Byakko), see BossMod/Modules/Stormblood/Extreme/Ex6Byakko/,
// just with different numeric AID/OID values (Unreal uses the 39900s range, level-100-scaled).
// This second pass captured a full rotation including the add/intermission window and the
// exaflare (HundredfoldHavoc-equivalent) mechanic that the first pass missed by only grepping
// casts where the boss actor itself was the source - many real mechanics are cast by short-lived
// "Helper" clone actors (OID A584C6/A584C7/A584C9) instead of the boss/add directly, exactly like
// Ex6's Helper (OID 0x18D6) pattern.
//
// AIDs marked "CONFIRMED" were seen in CST+ lines in the log, correlated positionally/structurally
// against Ex6ByakkoByakkoStates.cs's SinglePhase sequence (raidwide count/spacing, tankbuster
// single-target framing, donut/cone naming parallels, add-cast RoarOfThunder-equivalent, etc).
// AIDs/mechanics with NO real numeric ID in the log (orb/OminousWind bubble mechanic, AratamaPuddle
// bait, WhiteHerald spread AOE resolution ID, GaleForce/VacuumClaw voidzones, FireAndLightning line
// AOE resolution IDs, FellSwoop raidwide resolution ID) are NOT included as fake AID values here -
// see UnByakkoStates.cs for how those are handled (Timeout-based placeholders referencing the Ex6
// mechanic they structurally correspond to, per explicit instruction not to fabricate AIDs).
namespace BossMod.Dawntrail.Unreal.UnByakko;

public enum OID : uint
{
    Boss = 0xA584C1, // 白虎, NameID 7092, HP ~50.9M - matches Ex6Byakko.OID.Boss
    Hakutei = 0xA584C2, // 白帝, NameID 7093, HP ~7.74M - matches Ex6Byakko.OID.Hakutei (present whole fight, not spawned fresh)
    IntermissionHakutei = 0xA584C9, // 白帝, NameID 7093, HP 161938, spawned x3 during intermission - casts 白帝降臨 (ImperialGuard-equivalent line AOE) - matches Ex6Byakko.OID.IntermissionHakutei
    BossHelper = 0xA584C6, // 白虎 clone, NameID 7092, R0.5, HP 188300, spawned x25 - casts HundredfoldHavoc-equivalent exaflare rings and the intermission donut - matches Ex6Byakko.OID.Helper (boss side)
    AddHelper = 0xA584C7, // 白帝 clone, NameID 7093, R0.5, HP 188300, spawned x4 - matches Ex6Byakko.OID.Helper (add side)
    AratamaForce = 0xA584C3, // 荒彈, HP 161938, spawned x140(!) - name directly parallels Ex6's AratamaForce/AratamaPuddle orb-spawner actors; role not conclusively pinned down beyond "very frequent orb/puddle spawner"
    AramitamaSoul = 0xA584C4 // 荒魂, HP 161938, spawned x16 - name directly parallels Ex6's AramitamaSoul (VoiceOfThunder soakable orbs)
}

public enum AID : uint
{
    AutoAttack = 870, // TODO: not confirmed from log, placeholder by convention

    // --- CONFIRMED boss (白虎, OID.Boss) casts ---
    StormPulse = 39933, // 風雷波動, Boss->self, 3.7s cast, raidwide - matches Ex6.AID.StormPulse, by far the most frequent boss cast just like Ex6
    StormPulseRepeat = 39930, // 雷火一閃, Boss->self, 3.7s cast, raidwide - matches Ex6.AID.StormPulseRepeat (second hit of a double/quadruple raidwide volley); unlike Ex6's instant version this one has its own cast bar
    HeavenlyStrike = 39931, // 天雷掌, Boss->player, 3.7s cast, single-target tankbuster - matches Ex6.AID.HeavenlyStrike
    SweepTheLegBoss = 39932, // 旋體腳, Boss->self, 3.7s cast, wide cone - matches Ex6.AID.SweepTheLegBoss (always seen right after the HundredfoldHavoc-equivalent exaflare window, same as Ex6)
    DistantClap = 39934, // 遠雷, Boss->self, 4.7s cast, donut - matches Ex6.AID.DistantClap
    StateOfShockGrab = 39937, // 咒縛雷, Boss->player, 3.7s cast, single-target stun/grab - matches Ex6.AID.StateOfShock (grabs a tank before throwing them)
    HighestStakes = 39939, // 乾坤一擲, Boss->location, 4.7s cast, always observed in pairs ~10s apart - matches Ex6.AID.HighestStakes (tower drop x2 per StateOfShock volley)
    UnrelentingAnguishStart = 39950, // 無間地獄, Boss->self, 2.7s cast, visual - matches Ex6.AID.UnrelentingAnguish ("orbs start" marker before the OminousWind/orb sub-mechanic)

    // --- CONFIRMED add (白帝, OID.Hakutei) casts ---
    SteelClaw = 39935, // 雷火一閃, Hakutei->self, 3.7s cast - matches Ex6.AID.SteelClaw (add's own cleave)
    RoarOfThunder = 39961, // 雷轟, Hakutei->self, 19.7s cast, raidwide enrage-style - matches Ex6.AID.RoarOfThunder (intermission-ending big raidwide)

    // --- CONFIRMED intermission-only casts ---
    ImperialGuard = 39954, // 白帝降臨, IntermissionHakutei->self, cast x3 per intermission ~5-12s apart - matches Ex6.AID.ImperialGuard (line AOE x3)
    IntermissionSweepTheLeg = 39957, // 旋體腳, BossHelper->self, cast during intermission window - matches Ex6.AID.IntermissionSweepTheLeg (donut)

    // --- CONFIRMED exaflare (HundredfoldHavoc-equivalent) ---
    HundredfoldHavoc = 39942 // 百雷繚亂, BossHelper->self (4 simultaneous casters in a ring, then rotated ~45 degrees), no observable initial-vs-rest split in the log - matches Ex6.AID.HundredfoldHavocFirst/Rest collapsed into one ID
}

public enum SID : uint
{
    // TODO: no boss-specific status effects (stun/OminousWind-bubble-equivalent) were conclusively
    // identified from the captured log; needs another pass with STA+/STA- correlated to mechanic timestamps.
}

public enum IconID : uint
{
    // These headmarker IDs were observed directly in ICON lines and happen to be numerically
    // identical to Ex6's IconID enum (icon IDs are frequently shared across content tiers even
    // when ability IDs differ, since they come from a smaller shared headmarker pool).
    HighestStakes = 62, // seen on BossHelper (tower marker), matches Ex6.IconID.HighestStakes
    AratamaPuddle = 4, // seen on 2 players simultaneously multiple times, matches Ex6.IconID.AratamaPuddle (spread bait)
    WhiteHerald = 87, // seen on 1 player at a time, matches Ex6.IconID.WhiteHerald (spread)
    Bombogenesis = 101 // seen on 3 players simultaneously, matches Ex6.IconID.Bombogenesis (stack/bait marker)
}
