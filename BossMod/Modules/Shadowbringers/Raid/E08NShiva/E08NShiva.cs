namespace BossMod.Shadowbringers.Raid.E08NShiva;

sealed class AbsoluteZero(BossModule module) : Components.RaidwideCast(module, (uint)AID.AbsoluteZero);
sealed class HeavenlyStrike(BossModule module) : Components.RaidwideCast(module, (uint)AID.HeavenlyStrike);
// 擊退距離照錄影量（落地後站定的隊員離首領多了 12.0 碼）；場地邊緣沒有牆，被推出去會掉下去。
sealed class HeavenlyStrikeKnockback(BossModule module) : Components.SimpleKnockbacks(module, (uint)AID.HeavenlyStrike, 12f);
sealed class DiamondFrost(BossModule module) : Components.RaidwideCast(module, (uint)AID.DiamondFrost);
sealed class LightRampant(BossModule module) : Components.RaidwideCast(module, (uint)AID.LightRampant);
sealed class ShatteredWorld(BossModule module) : Components.RaidwideCast(module, (uint)AID.ShatteredWorld);
sealed class DoubleSlap(BossModule module) : Components.SingleTargetCast(module, (uint)AID.DoubleSlap);
// 內徑照 Omen gl_sircle_2004（外 20／內 4）；後備猜法對 CastType 10 一律給內徑 3。
sealed class ScytheKick(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.ScytheKick, (uint)AID.EmbitteredDance, (uint)AID.ReflectedScytheKick, (uint)AID.ReflectedScytheKickSlow], new AOEShapeDonut(4f, 20f));
sealed class FrigidStone(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.FrigidStone, (uint)AID.FrigidStone, 5f, 5.2d);

// 閃光護甲是凝視（看著首領的人會暈）；換裝（閃光）讀條結束後約 5.1 秒由首領位置發動，本身沒有讀條。
sealed class ShiningArmor(BossModule module) : Components.GenericGaze(module)
{
    private readonly List<Eye> _eyes = new(1);

    public override ReadOnlySpan<Eye> ActiveEyes(int slot, Actor actor) => CollectionsMarshal.AsSpan(_eyes);

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.RedressShining)
        {
            _eyes.Clear();
            _eyes.Add(new(caster.Position, WorldState.FutureTime(5.1d)));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ShiningArmor)
        {
            _eyes.Clear();
        }
    }
}

// 世界分斷後全隊被傳到東西兩塊圓形平台，打完再傳回；照玩家所在位置換場地（在 Update 換，AI 的尋路範圍取自模組場地）。
sealed class PlatformSwitch(BossModule module) : BossComponent(module)
{
    public override void Update()
    {
        if (Raid.Player() is not { } pc)
        {
            return;
        }
        var pos = pc.Position;
        var center = pos.InCircle(E08NShiva.WestPlatform, 16f) ? E08NShiva.WestPlatform : pos.InCircle(E08NShiva.EastPlatform, 16f) ? E08NShiva.EastPlatform : E08NShiva.ArenaCenter;
        if (Arena.Center != center)
        {
            Arena.Bounds = center == E08NShiva.ArenaCenter ? E08NShiva.MainBounds : E08NShiva.PlatformBounds;
            Arena.Center = center;
        }
    }
}

// 光之波動的扇形（頭上標記 0x17）角度沒有預兆可查、錄影也定不出來，刻意不處理；冰霜（標記 0x57）是距離衰減，散開半徑沒有證據。
sealed class E08NShivaStates : StateMachineBuilder
{
    public E08NShivaStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<AbsoluteZero>()
            .ActivateOnEnter<HeavenlyStrike>()
            .ActivateOnEnter<HeavenlyStrikeKnockback>()
            .ActivateOnEnter<DiamondFrost>()
            .ActivateOnEnter<LightRampant>()
            .ActivateOnEnter<ShatteredWorld>()
            .ActivateOnEnter<DoubleSlap>()
            .ActivateOnEnter<ScytheKick>()
            .ActivateOnEnter<FrigidStone>()
            .ActivateOnEnter<ShiningArmor>()
            .ActivateOnEnter<PlatformSwitch>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地＝台服碰撞的圓形地板（半徑 20），轉場平台各半徑約 12.75；都內縮 0.5 碼，場外會掉下去。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Shadowbringers, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 728u, NameID = 9353u, SortOrder = 1)]
public sealed class E08NShiva(WorldState ws, Actor primary) : BossModule(ws, primary, ArenaCenter, MainBounds)
{
    public static readonly WPos ArenaCenter = new(100f, 100f);
    public static readonly WPos WestPlatform = new(67f, 168f);
    public static readonly WPos EastPlatform = new(133f, 168f);
    public static readonly ArenaBoundsCircle MainBounds = new(19.5f);
    public static readonly ArenaBoundsCircle PlatformBounds = new(12.25f);
}
