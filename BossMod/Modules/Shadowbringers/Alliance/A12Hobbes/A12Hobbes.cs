namespace BossMod.Shadowbringers.Alliance.A12Hobbes;

sealed class LaserResistanceTest(BossModule module) : Components.RaidwideCast(module, (uint)AID.LaserResistanceTest);

// 只畫有 Omen（外 20／內 15）的最外圈；內側兩圈的內徑沒有證據，交給後備猜法（內徑 3，比實際寬）。
sealed class RingLaserOuter(BossModule module) : Components.SimpleAOEs(module, (uint)AID.RingLaserOuter, new AOEShapeDonut(15f, 20f));

sealed class ShockingDischarge(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ShockingDischarge, 5f);
sealed class VariableCombatTestCircle(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.VariableCombatTestCircleFirst, (uint)AID.VariableCombatTestCircle], 2f);
sealed class VariableCombatTestDonut(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.VariableCombatTestDonutFirst, (uint)AID.VariableCombatTestDonut], new AOEShapeDonut(7f, 19f));
sealed class VariableCombatTestCone(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.VariableCombatTestConeFirst, (uint)AID.VariableCombatTestCone], new AOEShapeCone(20f, 30f.Degrees()));

// 塌方：整個平台都會挨打但傷害隨距離衰減（近中心三到五成、平台邊緣多半一成以下），畫成半徑 20 會讓 AI 處處危險；
// 只把近中心畫成危險、讓 AI 往邊緣走；自己站在這個平台上才記全體傷害預告（減傷端不看遮罩，別的平台的塌方不能算進來）。
sealed class Impact(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Impact, 14f)
{
    private const float PlatformRadius = 20f;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        var count = Casters.Count;
        if (count == 0)
            return;
        var party = Raid.WithSlot();
        for (var i = 0; i < count; ++i)
        {
            var c = Casters[i];
            if (!actor.Position.InCircle(c.Origin, PlatformRadius))
                continue;
            BitMask mask = default;
            for (var j = 0; j < party.Length; ++j)
                if (party[j].Item2.Position.InCircle(c.Origin, PlatformRadius))
                    mask.Set(party[j].Item1);
            if (mask.Any())
                hints.AddPredictedDamage(mask, c.Activation, AIHints.PredictedDamageType.Raidwide, (uint)AID.Impact);
        }
    }
}

// 崩塌：塌方結束約 2.4 秒後，平台中心往外 4 道互差 90 度的直線（不讀條，施放者落下前 0.1 秒才轉向）。
// 方向由塌方讀條前約 2 秒該平台的場地狀態（EnvControl）決定：平台相對三平台中心的方位，加上狀態對應的 0／22.5／45／67.5 度。
// 狀態值以外的組合不畫（寧可不畫也不畫錯）。
sealed class Collapse(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeRect line = new(20f, 4f);
    private static readonly WPos hub = new(-805f, -240f);
    private const double DelayAfterImpact = 2.4d, NoImpactFallback = 12.2d;
    private readonly List<AOEInstance> _aoes = new(4);
    private WPos _platform;
    private int _resolved;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnEventEnvControl(byte index, uint state)
    {
        WPos? platform = index switch
        {
            0x07 => new(-805f, -270f),
            0x09 => new(-779.02f, -225f),
            0x0A => new(-830.98f, -225f),
            _ => null
        };
        float? offset = state switch
        {
            0x00100010u => 0f,
            0x20002000u => 22.5f,
            0x04000400u => 45f,
            0x00800080u => 67.5f,
            _ => null
        };
        if (platform is not { } p || offset is not { } o)
            return;
        _platform = p;
        _resolved = 0;
        _aoes.Clear();
        var rot = Angle.FromDirection(p - hub) + o.Degrees();
        var activation = WorldState.FutureTime(NoImpactFallback);
        for (var i = 0; i < 4; ++i)
            _aoes.Add(new(line, p, rot + (90f * i).Degrees(), activation));
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.Impact && _aoes.Count != 0 && spell.LocXZ.InCircle(_platform, 3f))
        {
            var activation = Module.CastFinishAt(spell, DelayAfterImpact);
            var aoes = CollectionsMarshal.AsSpan(_aoes);
            for (var i = 0; i < aoes.Length; ++i)
                aoes[i].Activation = activation;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.Collapse && _aoes.Count != 0 && ++_resolved >= 4)
            _aoes.Clear();
    }

    public override void Update()
    {
        if (_aoes.Count != 0 && WorldState.CurrentTime > _aoes[0].Activation.AddSeconds(2d))
            _aoes.Clear();
    }
}

// 耐火焰檢測：塌方平台收到崩塌的場地狀態時，火焰會在「下一個」平台（0x09→0x07→0x0A→0x09）隨塌方判定噴 5 下、約 1 秒一下。
// 方向＝火焰平台相對三平台中心的方位加偏移：扇形 0、兩道牆 ±115（牆從平台外緣往內燒 12 碼）、半邊 ±95。
// 半邊的正負號只在兩組（平台, 狀態）看過，其餘組合不畫（寧可不畫也不畫錯）；扇形角度比實際打到的人略寬。
sealed class FireResistanceTest(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeCone cone = new(41f, 20f.Degrees());
    private static readonly AOEShapeRect band = new(12f, 19f), half = new(22f, 21f);
    private static readonly WPos hub = new(-805f, -240f);
    private const float WallDistance = 20.5f;
    private const double NoImpactFallback = 9.8d, LastTickAfterImpact = 4.8d, TickInterval = 1d;
    private readonly List<AOEInstance> _aoes = new(2);
    private WPos _impactPlatform;
    private DateTime _expire;

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnEventEnvControl(byte index, uint state)
    {
        (WPos impact, WPos fire)? platforms = index switch
        {
            0x07 => (new(-805f, -270f), new(-830.98f, -225f)),
            0x09 => (new(-779.02f, -225f), new(-805f, -270f)),
            0x0A => (new(-830.98f, -225f), new(-779.02f, -225f)),
            _ => null
        };
        if (platforms is not { } p)
            return;
        var (impact, fire) = p;
        var dir = Angle.FromDirection(fire - hub);
        var activation = WorldState.FutureTime(NoImpactFallback);
        switch (state)
        {
            case 0x00100010u:
                _aoes.Clear();
                _aoes.Add(new(cone, fire + WallDistance * dir.ToDirection(), dir + 180f.Degrees(), activation));
                break;
            case 0x00800080u:
                _aoes.Clear();
                AddWall(fire, dir + 115f.Degrees(), activation);
                AddWall(fire, dir - 115f.Degrees(), activation);
                break;
            case 0x04000400u when index == 0x07:
                _aoes.Clear();
                _aoes.Add(new(half, fire, dir + 95f.Degrees(), activation));
                break;
            case 0x20002000u when index == 0x0A:
                _aoes.Clear();
                _aoes.Add(new(half, fire, dir - 95f.Degrees(), activation));
                break;
            default:
                return;
        }
        _impactPlatform = impact;
        _expire = activation.AddSeconds(LastTickAfterImpact);
    }

    private void AddWall(WPos platform, Angle wall, DateTime activation)
        => _aoes.Add(new(band, platform + WallDistance * wall.ToDirection(), wall + 180f.Degrees(), activation));

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.Impact && _aoes.Count != 0 && spell.LocXZ.InCircle(_impactPlatform, 3f))
        {
            var activation = Module.CastFinishAt(spell);
            SetActivation(activation);
            _expire = activation.AddSeconds(LastTickAfterImpact);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.FireResistanceTestCone or (uint)AID.FireResistanceTestBand or (uint)AID.FireResistanceTestHalf && _aoes.Count != 0)
            SetActivation(WorldState.FutureTime(TickInterval));
    }

    private void SetActivation(DateTime activation)
    {
        var aoes = CollectionsMarshal.AsSpan(_aoes);
        for (var i = 0; i < aoes.Length; ++i)
            aoes[i].Activation = activation;
    }

    public override void Update()
    {
        if (_aoes.Count != 0 && WorldState.CurrentTime > _expire)
            _aoes.Clear();
    }
}

sealed class ShortRangeMissile(BossModule module) : Components.SpreadFromCastTargets(module, (uint)AID.ShortRangeMissile, 8f);

// 場地＝三個聯盟各自的圓形平台（碰撞資料半徑約 19.75，內縮到 19.5），彼此不相連；平台之間不放進場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "The Combat Reborn Team, ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 700, NameID = 9143)]
public sealed class A12Hobbes(WorldState ws, Actor primary) : BossModule(ws, primary, arena.Center, arena)
{
    private static readonly ArenaBoundsComplex arena = new([new Circle(new(-805f, -270f), 19.5f), new Circle(new(-779.02f, -225f), 19.5f), new Circle(new(-830.98f, -225f), 19.5f)]);
}
