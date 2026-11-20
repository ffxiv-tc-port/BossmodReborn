namespace BossMod.RealmReborn.Dungeon.D26SunkenTempleOfQarnHard.D263VicegerentToTheWarden;

public enum OID : uint
{
    Boss = 0xD15, // 審理神代言者
    TempleMummy = 0xCAB // 聖堂木乃伊
}

// 沒有列技能：怨恨的斬擊 3110（矩形 40 寬 4、無 Omen）後備猜法已能照 EXD 畫出，交給 UnclaimedCastAOEs。

// 被木乃伊碰到會疊詛咒、疊滿 4 層被木乃伊化 ⇒ 木乃伊身邊當常駐危險區。碰觸距離沒有證據，取模型半徑外加 1 碼。
sealed class TempleMummies(BossModule module) : BossComponent(module)
{
    private readonly List<Actor> _mummies = module.Enemies((uint)OID.TempleMummy);
    private AOEShapeCircle? _shape;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var count = _mummies.Count;
        for (var i = 0; i < count; ++i)
        {
            var m = _mummies[i];
            if (!m.IsDead)
                hints.AddForbiddenZone(_shape ??= new(m.HitboxRadius + 1f), m.Position);
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        var count = _mummies.Count;
        for (var i = 0; i < count; ++i)
        {
            var m = _mummies[i];
            if (!m.IsDead)
                Arena.AddCircle(m.Position, m.HitboxRadius + 1f, Colors.Danger);
        }
    }
}

sealed class D263VicegerentToTheWardenStates : StateMachineBuilder
{
    public D263VicegerentToTheWardenStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<TempleMummies>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 26, NameID = 3075)]
public sealed class D263VicegerentToTheWarden(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
