namespace BossMod.Shadowbringers.Raid.E12NEdensPromise;

sealed class Maleficium(BossModule module) : Components.RaidwideCast(module, (uint)AID.Maleficium);
sealed class DiamondDust(BossModule module) : Components.RaidwideCast(module, (uint)AID.DiamondDust);
sealed class EarthenFury(BossModule module) : Components.RaidwideCast(module, (uint)AID.EarthenFury);
sealed class FormlessJudgment(BossModule module) : Components.SingleTargetCast(module, (uint)AID.FormlessJudgment);
sealed class Obliteration(BossModule module) : Components.LineStack(module, aidMarker: default, (uint)AID.Obliteration, range: 40f, halfWidth: 3f);

// 頭上標記 0x3E＝分攤（半徑 6）、0x60＝散開（半徑 5）；判定招式各有好幾個，標記到判定的秒數沒有證據，取 5.1 秒。
// 判定招式沒看到（例如判定 ID 不在列舉裡）時，過了預定時間 10 秒自動清掉，不讓 AI 一直維持分攤／散開。
sealed class StackSpreadIcons(BossModule module) : Components.UniformStackSpread(module, 6f, 5f, alwaysShowSpreads: true)
{
    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.Stack)
        {
            AddStack(actor, WorldState.FutureTime(5.1d));
        }
        else if (iconID == (uint)IconID.FrigidStone)
        {
            AddSpread(actor, WorldState.FutureTime(5.1d));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.IceFloe:
            case (uint)AID.UnderTheWeight:
            case (uint)AID.ClassicalSculpture:
                var stack = Stacks.FindIndex(s => s.Target.InstanceID == spell.MainTargetID);
                if (stack >= 0 || Stacks.Count > 0)
                {
                    Stacks.RemoveAt(Math.Max(stack, 0));
                }
                break;
            case (uint)AID.FrigidStone:
            case (uint)AID.FrigidStoneReach:
                Spreads.RemoveAll(s => s.Target.InstanceID == spell.MainTargetID);
                break;
        }
    }

    public override void Update()
    {
        var expired = WorldState.FutureTime(-10d);
        Stacks.RemoveAll(s => s.Activation < expired || s.Target.IsDead);
        Spreads.RemoveAll(s => s.Activation < expired || s.Target.IsDead);
    }
}

// 五塊圓形平台互不相連、高度不同：第一階段一塊、中場三塊、失樂園之後一塊；照玩家站的那塊換場地（AI 的尋路範圍取自模組場地）。
sealed class PlatformSwitch(BossModule module) : BossComponent(module)
{
    public override void Update()
    {
        if (Raid.Player() is not { } pc)
        {
            return;
        }
        var pos = pc.Position;
        foreach (var (center, bounds) in E12NEdensPromise.Platforms)
        {
            if (pos.InCircle(center, bounds.Radius + 1f))
            {
                if (Arena.Center != center)
                {
                    Arena.Bounds = bounds;
                    Arena.Center = center;
                }
                return;
            }
        }
    }
}

// 拒絕之手（中場的單邊擊退）寬 30 等於平台直徑、距離沒有證據，刻意不處理；列進列舉只為了不讓後備把它畫成直線傷害。
// 激射眼、中場平台 3 的全場擊退、永恆忘卻、失樂園沒有讀條預兆或證據，不處理。
// 制裁之界雷／臨時洋流／瞬燃強襲／凶猛風暴（含儲存後的施放）、愉悅之手、炸彈的爆炸、大地搖動照讀條預兆交給後備。
sealed class E12NEdensPromiseStates : StateMachineBuilder
{
    public E12NEdensPromiseStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Maleficium>()
            .ActivateOnEnter<DiamondDust>()
            .ActivateOnEnter<EarthenFury>()
            .ActivateOnEnter<FormlessJudgment>()
            .ActivateOnEnter<Obliteration>()
            .ActivateOnEnter<StackSpreadIcons>()
            .ActivateOnEnter<PlatformSwitch>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞的五塊圓形地板（半徑 20 兩塊、15 三塊），都內縮 0.5 碼；平台外會掉下去。
public abstract class E12NEdensPromise(WorldState ws, Actor primary) : UnverifiedOidTrialModule(ws, primary, Platforms[0].Center, Platforms[0].Bounds)
{
    public static readonly (WPos Center, ArenaBoundsCircle Bounds)[] Platforms =
    [
        (new(71.329f, -23.176f), new(19.5f)),
        (new(44.084f, 60.676f), new(14.5f)),
        (new(-44.084f, 60.676f), new(14.5f)),
        (new(-71.329f, -23.176f), new(14.5f)),
        (new(default, -75f), new(19.5f)),
    ];
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E12NEdensPromiseStates), PrimaryActorOID = (uint)OID.BossCandidateA, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 758u, NameID = 9813u, SortOrder = 1)]
public sealed class E12NEdensPromiseA(WorldState ws, Actor primary) : E12NEdensPromise(ws, primary);

[ModuleInfo(BossModuleInfo.Maturity.WIP, StatesType = typeof(E12NEdensPromiseStates), PrimaryActorOID = (uint)OID.BossCandidateB, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 758u, NameID = 9813u, SortOrder = 1)]
public sealed class E12NEdensPromiseB(WorldState ws, Actor primary) : E12NEdensPromise(ws, primary);
