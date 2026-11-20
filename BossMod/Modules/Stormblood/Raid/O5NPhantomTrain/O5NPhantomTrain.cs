namespace BossMod.Stormblood.Raid.O5NPhantomTrain;

sealed class DoomStrike(BossModule module) : Components.SingleTargetCast(module, (uint)AID.DoomStrike);
sealed class AcidRain(BossModule module) : Components.RaidwideCast(module, (uint)AID.AcidRain);
// 追尾是距離衰減＋往車廂後方擊退，全員都會中；後牆擋住擊退，刻意不畫擊退（畫了 AI 會為了不被推出場而站到前面吃重傷）。
sealed class HeadOn(BossModule module) : Components.RaidwideCast(module, (uint)AID.HeadOn);
// 四個怒靈各站在一格 10×8 方格中央；讀條落點在怒靈背後 4 碼，前後兩種讀法都蓋住。
sealed class Encumber(BossModule module) : Components.SimpleAOEs(module, (uint)AID.Encumber, new AOEShapeRect(4f, 5f, 8f));
// 標記到判定的秒數沒有錄影，5 秒只影響 AI 開始散開的時機。
sealed class DiabolicWind(BossModule module) : Components.SpreadFromIcon(module, (uint)IconID.DiabolicWind, (uint)AID.DiabolicWind, 5f, 5d);

// 附加階段會上列車頂、被怒靈抓進乘客車廂；照玩家所在位置換場地（在 Update 換，AI 的尋路範圍取自模組場地）。
// 認不出的地方（列車頂以外的高處）不限制範圍，只以玩家為中心，避免 AI 為了回到別處的場地走下平台。
sealed class AreaSwitch(BossModule module) : BossComponent(module)
{
    public override void Update()
    {
        if (Raid.Player() is not { } pc)
        {
            return;
        }
        var (center, bounds) = O5NPhantomTrain.AreaAt(pc.Position, pc.PosRot.Y, Arena.Center);
        if (Arena.Center != center || !ReferenceEquals(Arena.Bounds, bounds))
        {
            Arena.Bounds = bounds;
            Arena.Center = center;
        }
    }
}

// 魔界前照燈（直線分攤）的目標沒有證據、聖光射線（追人的光圈）的最終落點錄影定不出來、魔界光的衰減範圍沒有證據：都刻意不處理。
sealed class O5NPhantomTrainStates : StateMachineBuilder
{
    public O5NPhantomTrainStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<DoomStrike>()
            .ActivateOnEnter<AcidRain>()
            .ActivateOnEnter<HeadOn>()
            .ActivateOnEnter<Encumber>()
            .ActivateOnEnter<DiabolicWind>()
            .ActivateOnEnter<AreaSwitch>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地都是台服碰撞的地板內縮：車廂 20×40、列車頂 14×47.5、乘客車廂八間各 12×16.5（四角有擺設，取 9×13）。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", Expansion = BossModuleInfo.Expansion.Stormblood, Category = BossModuleInfo.Category.Raid, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 286u, NameID = 7127u, SortOrder = 1)]
public sealed class O5NPhantomTrain(WorldState ws, Actor primary) : BossModule(ws, primary, CarCenter, CarBounds)
{
    public static readonly WPos CarCenter = new(0f, 4f);
    public static readonly ArenaBoundsRect CarBounds = new(9.5f, 19.5f);
    public static readonly WPos RoofCenter = new(-30.5f, 1.1f);
    public static readonly ArenaBoundsRect RoofBounds = new(6.5f, 23.25f);
    public static readonly ArenaBoundsRect RoomBounds = new(4.5f, 6.5f);
    public static readonly ArenaBoundsCircle UnknownBounds = new(30f);
    private static readonly float[] roomX = [144.375f, 215.875f];
    private static readonly float[] roomZ = [-71f, -23.5f, 23.75f, 71.125f];

    public static (WPos Center, ArenaBounds Bounds) AreaAt(WPos pos, float y, WPos currentCenter)
    {
        if (y < -100f)
        {
            return (new(Nearest(roomX, pos.X), Nearest(roomZ, pos.Z)), RoomBounds);
        }
        if (y > 8f)
        {
            if (pos.InRect(RoofCenter, new WDir(0f, 1f), 24f, 24f, 7.5f))
            {
                return (RoofCenter, RoofBounds);
            }
            // 同 SimpleBossModule：中心量化、玩家走開 5 碼以上才移，免得尋路圖每幀跟著抖。
            return (currentCenter.AlmostEqual(pos, 5f) ? currentCenter : pos.Rounded(), UnknownBounds);
        }
        return (CarCenter, CarBounds);
    }

    private static float Nearest(float[] values, float v)
    {
        var best = values[0];
        for (var i = 1; i < values.Length; ++i)
        {
            if (MathF.Abs(values[i] - v) < MathF.Abs(best - v))
            {
                best = values[i];
            }
        }
        return best;
    }

    protected override void DrawEnemies(int pcSlot, Actor pc)
    {
        Arena.Actor(PrimaryActor);
        Arena.Actors(Enemies((uint)OID.PutridPassenger));
    }
}
