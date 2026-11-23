namespace BossMod.Heavensward.Dungeon.D16GreatGubalLibraryHard.D162LiquidFlame;

public enum OID : uint
{
    Boss = 0x176B, // 液態火焰
    LiquidFlameB = 0x176C, // 液態火焰（另一個模型）
    LiquidFlameC = 0x176D // 液態火焰（另一個模型）
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    Bibliocide = 6469, // Boss->self, 3.0s cast, range 100 circle
    Rush = 6471, // Boss->player, 5.0s cast, width 4 rect charge
    SealOfNightAndDay = 6473, // Boss->self, 6.0s cast, range 100 circle
    Ferrofluid = 6475, // Boss->self, 6.0s cast, range 100 circle
    Magnetism = 6476, // Boss->player, no cast, single-target
    Repel = 6477 // Boss->player, no cast, single-target
}

public enum IconID : uint
{
    FerrofluidA = 48, // player/Boss
    FerrofluidB = 49 // player/Boss
}

sealed class Bibliocide(BossModule module) : Components.RaidwideCast(module, (uint)AID.Bibliocide);
sealed class SealOfNightAndDay(BossModule module) : Components.CastHint(module, (uint)AID.SealOfNightAndDay, "Sunseal: red sigil, Moonseal: blue sigil", true);

// 突進：讀條時點一名玩家，判定時衝到他當下的位置 ⇒ 範圍每幀從首領畫到被點的人，不是讀條開始時的那一點。
// 刻意不報死刑：被點的不一定是坦克，傷害也只是一般技能的量。
sealed class Rush(BossModule module) : Components.GenericBaitAway(module, (uint)AID.Rush)
{
    private const float HalfWidth = 2f;

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == WatchedAction && WorldState.Actors.Find(spell.TargetID) is Actor target)
            CurrentBaits.Add(new(caster, target, new AOEShapeRect((target.Position - caster.Position).Length(), HalfWidth), Module.CastFinishAt(spell)));
    }

    public override void Update()
    {
        var baits = CollectionsMarshal.AsSpan(CurrentBaits);
        for (var i = 0; i < baits.Length; ++i)
        {
            ref var b = ref baits[i];
            var length = (b.Target.Position - b.Source.Position).Length();
            if (b.Shape is AOEShapeRect rect && Math.Abs(rect.LengthFront - length) > 0.5f)
                b.Shape = new AOEShapeRect(length, HalfWidth);
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == WatchedAction)
            CurrentBaits.RemoveAll(b => b.Source == caster);
    }
}

// 磁石：首領和玩家頭上各一個 +／- 標記；和首領同號靠近首領、異號遠離（cactbot、攻略）。
// 只給文字提示：吸／推的距離沒有證據，畫不出安全範圍。
sealed class Ferrofluid(BossModule module) : BossComponent(module)
{
    private uint _bossIcon;
    private DateTime _expire;
    private readonly uint[] _playerIcons = new uint[PartyState.MaxAllies];

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID is not ((uint)IconID.FerrofluidA or (uint)IconID.FerrofluidB))
            return;
        _expire = WorldState.FutureTime(15d); // 吸／推事件沒來時不讓提示一直掛著
        var slot = Raid.FindSlot(actor.InstanceID);
        if (slot >= 0)
            _playerIcons[slot] = iconID;
        else if (actor.Type != ActorType.Player)
            _bossIcon = iconID;
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.Magnetism or (uint)AID.Repel)
        {
            _bossIcon = 0;
            Array.Clear(_playerIcons);
        }
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        var mine = _playerIcons[slot];
        if (_bossIcon == 0 || mine == 0 || WorldState.CurrentTime >= _expire)
            return;
        hints.Add(mine == _bossIcon ? "Same mark as boss: go close!" : "Different mark from boss: go away!", false);
    }
}

sealed class D162LiquidFlameStates : StateMachineBuilder
{
    public D162LiquidFlameStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<Bibliocide>()
            .ActivateOnEnter<Rush>()
            .ActivateOnEnter<SealOfNightAndDay>()
            .ActivateOnEnter<Ferrofluid>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 196, NameID = 5218)]
public sealed class D162LiquidFlame(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);

// 液態火焰在 Garland 有三個 BNpcBase（同名、模型不同），沒有錄影分不出開打時是哪一個 ⇒ 三個都註冊、共用同一套狀態。
// 推錯的 OID 只是永遠不載入；限定這個 CFC，避免同 OID 在別處誤啟用。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 196, NameID = 5218,
    PrimaryActorOID = (uint)OID.LiquidFlameB, StatesType = typeof(D162LiquidFlameStates))]
public sealed class D162LiquidFlameB(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    protected override bool CheckPull() => base.CheckPull() && WorldState.CurrentCFCID == 196u;
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 196, NameID = 5218,
    PrimaryActorOID = (uint)OID.LiquidFlameC, StatesType = typeof(D162LiquidFlameStates))]
public sealed class D162LiquidFlameC(WorldState ws, Actor primary) : SimpleBossModule(ws, primary)
{
    protected override bool CheckPull() => base.CheckPull() && WorldState.CurrentCFCID == 196u;
}
