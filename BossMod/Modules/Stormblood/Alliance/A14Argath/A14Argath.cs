namespace BossMod.Stormblood.Alliance.A14Argath;

sealed class Raidwides(BossModule module) : Components.RaidwideCasts(module, [(uint)AID.FireIV, (uint)AID.DarkUltima]);
sealed class CripplingBlow(BossModule module) : Components.SingleTargetCast(module, (uint)AID.CripplingBlow);
sealed class CrushWeapon(BossModule module) : Components.SimpleAOEs(module, (uint)AID.CrushWeapon, 6f);
sealed class Soulfix(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Soulfix, 12f);
sealed class Heartless(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Heartless, new AOEShapeRect(61f, 5.5f));

// 神諭：真實的面具＝照指令做，虛假的面具＝反著做；結算是無讀條的神罰，時間軸上固定在面具後約 8 秒。
// 面具或標記任一沒看到就不給要求（猜錯會吃懲罰）；神罰沒來時在預估結算 3 秒後自行清掉。
sealed class CommandState(BossModule module) : BossComponent(module)
{
    public bool? Truth;
    public DateTime Activation;
    public readonly bool[] Flee = new bool[PartyState.MaxAllies];
    public readonly bool[] Turn = new bool[PartyState.MaxAllies];

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID is not (uint)IconID.CommandFlee and not (uint)IconID.CommandTurn)
            return;
        var slot = Raid.FindSlot(actor.InstanceID);
        if (slot < 0)
            return;
        if (iconID == (uint)IconID.CommandFlee)
            Flee[slot] = true;
        else
            Turn[slot] = true;
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.MaskOfTruth:
            case (uint)AID.MaskOfLies:
                Truth = spell.Action.ID == (uint)AID.MaskOfTruth;
                Activation = WorldState.FutureTime(8d);
                break;
            case (uint)AID.TheWord:
                Reset();
                break;
        }
    }

    public override void Update()
    {
        if (Truth != null && WorldState.CurrentTime > Activation.AddSeconds(3d))
            Reset();
    }

    private void Reset()
    {
        Truth = null;
        Activation = default;
        Array.Clear(Flee);
        Array.Clear(Turn);
    }
}

sealed class CommandFlee(BossModule module) : Components.StayMove(module)
{
    private readonly CommandState? _state = module.FindComponent<CommandState>();

    public override void Update()
    {
        if (_state == null)
            return;
        for (var i = 0; i < PlayerStates.Length; ++i)
        {
            if (_state.Truth is bool truth && _state.Flee[i])
            {
                var s = new PlayerState(truth ? Requirement.Move : Requirement.Stay, _state.Activation);
                SetState(i, in s);
            }
            else if (PlayerStates[i].Requirement != Requirement.None)
            {
                ClearState(i);
            }
        }
    }
}

sealed class CommandTurn(BossModule module) : Components.GenericGaze(module)
{
    private readonly CommandState? _state = module.FindComponent<CommandState>();
    private readonly Eye[] _eye = new Eye[1];

    public override ReadOnlySpan<Eye> ActiveEyes(int slot, Actor actor)
    {
        if (_state?.Truth is not bool truth || slot < 0 || slot >= _state.Turn.Length || !_state.Turn[slot])
            return [];
        _eye[0] = new(Module.PrimaryActor.Position, _state.Activation, inverted: !truth);
        return _eye;
    }
}

// 場地＝碰撞資料的正方形地板（中心 (106,-386)、半邊 27），內縮 0.5。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team, ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 281, NameID = 6925)]
public sealed class A14Argath(WorldState ws, Actor primary) : BossModule(ws, primary, new(106f, -386f), new ArenaBoundsSquare(26.5f));
