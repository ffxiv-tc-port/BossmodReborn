// AIDs come from a captured combat log of the Chinese client. Unreal Byakko (幻白虎征魂戰) is a reskin of
// Ex6Byakko (BossMod/Modules/Stormblood/Extreme/Ex6Byakko/) with different AID/OID values; many
// mechanics are cast by short-lived helper clones (Helper), like Ex6's Helper.
// Mechanics with no AID in the log (OminousWind bubbles, AratamaPuddle bait, WhiteHerald, GaleForce/
// VacuumClaw voidzones, FellSwoop) get no fake values here - see UnByakkoStates.cs.
namespace BossMod.Dawntrail.Unreal.UnByakko;

// OIDs are BNpcBase rows (the actor numbers printed in the log are not): values are the ones upstream's
// removed Un1Byakko module used; each row's ModelChara/Scale equals its Ex6Byakko counterpart in BNpcBase.
public enum OID : uint
{
    Boss = 0x4579, // 白虎, NameID 7092, HP ~50.9M - matches Ex6Byakko.OID.Boss
    Hakutei = 0x4581, // 白帝, NameID 7093, HP ~7.74M - matches Ex6Byakko.OID.Hakutei (present whole fight, not spawned fresh)
    IntermissionHakutei = 0x458B, // 白帝, NameID 7093, HP 161938, spawned x3 during intermission - casts 白帝降臨 (ImperialGuard-equivalent line AOE) - matches Ex6Byakko.OID.IntermissionHakutei
    Helper = 0x464D, // R0.5, HP 188300, 白虎 clones x25 + 白帝 clones x4 - cast the exaflare rings and the intermission donut - matches Ex6Byakko.OID.Helper
    AratamaForce = 0x4589, // 荒彈, HP 161938, spawned x140 - name parallels Ex6's AratamaForce/AratamaPuddle actors; exact role unconfirmed
    AramitamaSoul = 0x458A // 荒魂, HP 161938, spawned x16 - name directly parallels Ex6's AramitamaSoul (VoiceOfThunder soakable orbs)
}

public enum AID : uint
{
    AutoAttack = 870, // TODO: not confirmed from log, placeholder by convention

    // --- CONFIRMED boss (白虎, OID.Boss) casts ---
    StormPulse = 39933, // 風雷波動, Boss->self, 3.7s cast, raidwide - matches Ex6.AID.StormPulse
    FireAndLightningBoss = 39930, // 雷火一閃, Boss->self, 3.7s cast, line AOE - matches Ex6.AID.FireAndLightningBoss (Ex6's 10201 carries the same action name)
    StormPulseRepeat = 39964, // 風雷波動, Boss->self, no cast, raidwide - matches Ex6.AID.StormPulseRepeat; not in the log, no component uses it yet
    HeavenlyStrike = 39931, // 天雷掌, Boss->player, 3.7s cast, single-target tankbuster - matches Ex6.AID.HeavenlyStrike
    SweepTheLegBoss = 39932, // 旋體腳, Boss->self, 3.7s cast, wide cone - matches Ex6.AID.SweepTheLegBoss
    DistantClap = 39934, // 遠雷, Boss->self, 4.7s cast, donut - matches Ex6.AID.DistantClap
    StateOfShockGrab = 39937, // 咒縛雷, Boss->player, 3.7s cast, single-target stun/grab - matches Ex6.AID.StateOfShock (grabs a tank before throwing them)
    HighestStakes = 39939, // 乾坤一擲, Boss->location, 4.7s cast, always observed in pairs ~10s apart - matches Ex6.AID.HighestStakes (tower drop x2 per StateOfShock volley)
    UnrelentingAnguishStart = 39950, // 無間地獄, Boss->self, 2.7s cast, visual - matches Ex6.AID.UnrelentingAnguish ("orbs start" marker before the OminousWind/orb sub-mechanic)

    // --- CONFIRMED add (白帝, OID.Hakutei) casts ---
    FireAndLightningAdd = 39935, // 雷火一閃, Hakutei->self, 3.7s cast, line AOE - matches Ex6.AID.FireAndLightningAdd (Ex6's 10206 carries the same action name)
    SteelClaw = 39936, // 鐵爪斬, Hakutei->self, no cast, cleave - matches Ex6.AID.SteelClaw; not in the log, no component uses it yet
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
    // headmarker IDs seen in the log; numerically identical to Ex6's IconID enum
    HighestStakes = 62, // seen on BossHelper (tower marker), matches Ex6.IconID.HighestStakes
    AratamaPuddle = 4, // seen on 2 players simultaneously multiple times, matches Ex6.IconID.AratamaPuddle (spread bait)
    WhiteHerald = 87, // seen on 1 player at a time, matches Ex6.IconID.WhiteHerald (spread)
    Bombogenesis = 101 // seen on 3 players simultaneously, matches Ex6.IconID.Bombogenesis (stack/bait marker)
}
