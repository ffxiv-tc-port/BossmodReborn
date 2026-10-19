namespace BossMod.Components;

// generic temporary misdirection component
public abstract class TemporaryMisdirection(BossModule module, uint aid, string hint = "Applies temporary misdirection") : CastHint(module, aid, hint)
{
    private BitMask mask;

    public override void OnStatusGain(Actor actor, ActorStatus status)
    {
        if (status.ID is 1422u or 2936u or 3694u or 3909u)
            mask[Raid.FindSlot(actor.InstanceID)] = true;
    }

    public override void OnStatusLose(Actor actor, ActorStatus status)
    {
        if (status.ID is 1422u or 2936u or 3694u or 3909u)
            mask[Raid.FindSlot(actor.InstanceID)] = false;
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (mask[slot] != default)
            hints.AddSpecialMode(AIHints.SpecialMode.Misdirection, default);
    }
}

// 旋轉移動（狀態 2973）：只能轉向、會一直往前走。這裡只加禁區；轉向哪裡由自動移動／AI 依目的地寫 AIHints.SpinDirection。
public abstract class Spinning(BossModule module, uint aid, bool createforbiddenzones = true, uint statusID = 2973u, string hint = "Applies spinning") : CastHint(module, aid, hint)
{
    protected BitMask mask;
    private readonly uint StatusID = statusID;
    private const float SpinningLookahead = 5.5f;

    public override void OnStatusGain(Actor actor, ActorStatus status)
    {
        if (status.ID == StatusID && Raid.FindSlot(actor.InstanceID) is var slot && slot >= 0)
            mask.Set(slot);
    }

    public override void OnStatusLose(Actor actor, ActorStatus status)
    {
        if (status.ID == StatusID && Raid.FindSlot(actor.InstanceID) is var slot && slot >= 0)
            mask.Clear(slot);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (createforbiddenzones && mask[slot])
        {
            var pos = actor.Position;
            var rot = actor.Rotation;
            // 模擬被迫前進：前方一塊 2 秒後生效、背後錐形永久禁止；矩形刻意不以玩家為中心（那會讓尋路失常）
            hints.AddForbiddenZone(ShapeDistance.Rect(pos, rot, SpinningLookahead, SpinningLookahead + 2f, SpinningLookahead + 2f), WorldState.FutureTime(2d));
            hints.AddForbiddenZone(ShapeDistance.Cone(pos, 100f, rot + 180f.Degrees(), 45f.Degrees()), DateTime.MaxValue);
        }
    }
}
