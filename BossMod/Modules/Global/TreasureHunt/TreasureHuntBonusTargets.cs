namespace BossMod.Global.TreasureHunt;

// 寶物庫全區（含房間之間、沒有首領模組的時候）：獎勵怪的優先度一律蓋過首領模組與普通怪。
// 曼德拉戰隊只讓「下一隻該打的」排最前，其餘列為不可範圍波及（打錯順序＝後面的逃走、獎勵沒了）。
// 契約：鎖定的那隻只存 InstanceID、每幀從 PotentialTargets 重查；死了、不可選取了就換下一隻。
// ZoneModuleConfig.TreasureHuntLockBonusTarget 開著才寫 ForcedTarget（Plugin.ExecuteHints 每幀設成遊戲目標）；
// 關著時只改優先度，只影響 BMR 自己的 AI 選目標。
public abstract class TreasureHuntBonusTargets(WorldState ws, uint[] mandragoras, uint[] others) : ZoneModule(ws)
{
    public const int PriorityLocked = 50;
    public const int PriorityBonus = 40;

    private ulong _lockedId;

    public override void CalculateAIHints(int playerSlot, Actor player, AIHints hints) => Apply(player, hints);
    public override void CalculateAIHintsDuringBossModule(int playerSlot, Actor player, AIHints hints) => Apply(player, hints);

    private int MandragoraRank(uint oid) => Array.IndexOf(mandragoras, oid);
    private int OtherRank(uint oid) => Array.IndexOf(others, oid);

    private void Apply(Actor player, AIHints hints)
    {
        var targets = hints.PotentialTargets;
        var count = targets.Count;
        var nextMandragora = int.MaxValue;
        var anyBonus = false;
        for (var i = 0; i < count; ++i)
        {
            var oid = targets[i].Actor.OID;
            var rank = MandragoraRank(oid);
            if (rank >= 0)
            {
                anyBonus = true;
                if (rank < nextMandragora)
                    nextMandragora = rank;
            }
            else if (OtherRank(oid) >= 0)
                anyBonus = true;
        }
        if (!anyBonus)
        {
            _lockedId = 0;
            return;
        }

        var locked = FindEligible(targets, _lockedId, nextMandragora);
        locked ??= FindEligible(targets, player.TargetID, nextMandragora);
        locked ??= PickNext(targets, player, nextMandragora);
        _lockedId = locked?.Actor.InstanceID ?? 0;

        for (var i = 0; i < count; ++i)
        {
            var e = targets[i];
            var rank = MandragoraRank(e.Actor.OID);
            if (e == locked)
                e.Priority = PriorityLocked;
            else if (rank >= 0)
                e.Priority = rank == nextMandragora ? PriorityBonus : Math.Min(e.Priority, AIHints.Enemy.PriorityUndesirable);
            else if (OtherRank(e.Actor.OID) >= 0)
                e.Priority = PriorityBonus;
        }

        if (locked != null && ZoneModuleManager.Config.TreasureHuntLockBonusTarget)
            hints.ForcedTarget ??= locked.Actor;
    }

    // 只有「下一隻曼德拉」或其他獎勵怪算數；排在後面的曼德拉就算正被選著也不收，免得打亂順序。
    private AIHints.Enemy? FindEligible(List<AIHints.Enemy> targets, ulong instanceId, int nextMandragora)
    {
        if (instanceId == 0)
            return null;
        for (var i = 0; i < targets.Count; ++i)
        {
            var e = targets[i];
            if (e.Actor.InstanceID != instanceId)
                continue;
            if (e.Actor.IsDead)
                return null;
            var rank = MandragoraRank(e.Actor.OID);
            return rank >= 0 ? (rank == nextMandragora ? e : null) : OtherRank(e.Actor.OID) >= 0 ? e : null;
        }
        return null;
    }

    // 曼德拉優先於其他獎勵怪（有順序、全部出現時就該開始照順序打）；同一級取最近的。
    private AIHints.Enemy? PickNext(List<AIHints.Enemy> targets, Actor player, int nextMandragora)
    {
        AIHints.Enemy? best = null;
        var bestRank = int.MaxValue;
        var bestDistSq = float.MaxValue;
        for (var i = 0; i < targets.Count; ++i)
        {
            var e = targets[i];
            if (e.Actor.IsDead)
                continue;
            var m = MandragoraRank(e.Actor.OID);
            var o = OtherRank(e.Actor.OID);
            int rank;
            if (m >= 0)
            {
                if (m != nextMandragora)
                    continue;
                rank = -1;
            }
            else if (o >= 0)
                rank = o;
            else
                continue;
            var distSq = (e.Actor.Position - player.Position).LengthSq();
            if (rank < bestRank || rank == bestRank && distSq < bestDistSq)
            {
                best = e;
                bestRank = rank;
                bestDistSq = distSq;
            }
        }
        return best;
    }
}

// 每個寶物庫的曼德拉戰隊 OID 依擊殺順序（洋蔥、茄子、蒜頭、番茄、王后）排列；其他獎勵怪依各首領模組原本的優先度高到低排列。
[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 268)]
public sealed class LostCanalsOfUznairBonus(WorldState ws) : TreasureHuntBonusTargets(ws, [], [0x1EBF]);

[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 276)]
public sealed class HiddenCanalsOfUznairBonus(WorldState ws) : TreasureHuntBonusTargets(ws, [0x1FCD, 0x1FCE, 0x1FCF, 0x1FD0, 0x1FD1], [0x2063, 0x2064]);

[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 586)]
public sealed class ShiftingAltarsOfUznairBonus(WorldState ws) : TreasureHuntBonusTargets(ws, [0x2546, 0x2547, 0x2548, 0x2549, 0x254A], [0x2544, 0x2545]);

[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 688)]
public sealed class DungeonsOfLyheGhiahBonus(WorldState ws) : TreasureHuntBonusTargets(ws, [0x2A06, 0x2A07, 0x2A08, 0x2A09, 0x2A0A], [0x2A05]);

[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 745)]
public sealed class ShiftingOubliettesOfLyheGhiahBonus(WorldState ws) : TreasureHuntBonusTargets(ws, [0x301D, 0x301E, 0x301F, 0x3020, 0x3021], [0x3033, 0x3034]);

[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 819)]
public sealed class Excitatron6000Bonus(WorldState ws) : TreasureHuntBonusTargets(ws, [0x3808, 0x3809, 0x380A, 0x380B, 0x380C], []);

[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 909)]
public sealed class ShiftingGymnasionAgononBonus(WorldState ws) : TreasureHuntBonusTargets(ws, [0x3D4F, 0x3D50, 0x3D51, 0x3D52, 0x3D53], [0x3D4E, 0x3D4D]);

[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 993)]
public sealed class CenoteJaJaGuralBonus(WorldState ws) : TreasureHuntBonusTargets(ws, [0x4300, 0x4301, 0x4302, 0x4303, 0x4304], [0x42FE, 0x42FF]);

[ZoneModuleInfo(BossModuleInfo.Maturity.Contributed, 1060)]
public sealed class VaultOneironBonus(WorldState ws) : TreasureHuntBonusTargets(ws, [0x48B9, 0x48BA, 0x48BB, 0x48BC, 0x48BD], [0x48B7, 0x48B8]);
