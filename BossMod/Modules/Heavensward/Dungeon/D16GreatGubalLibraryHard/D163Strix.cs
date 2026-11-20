namespace BossMod.Heavensward.Dungeon.D16GreatGubalLibraryHard.D163Strix;

public enum OID : uint
{
    Boss = 0x176E, // 博學林鴞
    BehemothWard = 0x176F // 凶惡巨獸護衛
}

// 契約：這個列舉＝模組知道的技能。列舉以外的敵方讀條由 Components.UnclaimedCastAOEs 照沒有模組時的猜法處理，
// 所以刻意不把其他技能列進來（列了就不再被猜）。
public enum AID : uint
{
    PropertiesOfDarknessII = 6485, // Boss->self, 3.0s cast, range 100 circle
    PropertiesOfQuakes = 6486, // Boss->self, 7.0s cast, range 100 circle
    PropertiesOfTornados = 6487, // Boss->self, 7.0s cast, single-target
    PropertiesOfImps = 6489, // Boss->self, 3.0s cast, range 100 circle
    PropertiesOfThunderIII = 6490, // Boss->self, 6.0s cast, single-target
    PropertiesOfThunderIIIAOE = 6491, // Boss->player, no cast, range 3 circle
    EclipticMeteor = 6493, // BehemothWard->self, 24.0s cast, range 100 circle
    MeteorImpact = 6494 // BehemothWard->location, 5.0s cast, range 52 circle with falloff
}

sealed class PropertiesOfDarknessII(BossModule module) : Components.RaidwideCast(module, (uint)AID.PropertiesOfDarknessII);
sealed class PropertiesOfQuakes(BossModule module) : Components.CastHint(module, (uint)AID.PropertiesOfQuakes, "Stand in a white circle (Levitation)", true);
sealed class PropertiesOfTornados(BossModule module) : Components.CastHint(module, (uint)AID.PropertiesOfTornados, "Stand in a black circle (Leaden)", true);
sealed class PropertiesOfImps(BossModule module) : Components.CastHint(module, (uint)AID.PropertiesOfImps, "Green circle toggles Imp");
// 隕石要躲在岩石後面；岩石的 OID 與半徑沒有證據，只給提示不畫視線遮蔽。
sealed class EclipticMeteor(BossModule module) : Components.CastHint(module, (uint)AID.EclipticMeteor, "Hide behind a boulder!", true);
// 距離衰減（Omen 是 shipimpact）；後備猜法遇到 52 碼圓會當全場而不畫。衰減沒有量過，15 碼沿用 BMR 慣例值。
sealed class MeteorImpact(BossModule module) : Components.SimpleAOEs(module, (uint)AID.MeteorImpact, 15f);

// 大雷電之章：讀條結束後每個人身上各落一個 3 碼圓（攻略「Marks everyone … then hits each of them」）⇒ 全員散開。
sealed class PropertiesOfThunderIII(BossModule module) : Components.UniformStackSpread(module, default, 3f, alwaysShowSpreads: true)
{
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.PropertiesOfThunderIII)
            AddSpreads(Raid.WithoutSlot(true, true, true), Module.CastFinishAt(spell, 1d));
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.PropertiesOfThunderIIIAOE)
            Spreads.RemoveAll(s => s.Target.InstanceID == spell.MainTargetID);
    }

    public override void Update()
    {
        // 落雷事件沒來（被打斷、角色離開）時不讓散開一直掛著。
        if (Spreads.Count != 0 && WorldState.CurrentTime > Spreads[0].Activation.AddSeconds(5d))
            Spreads.Clear();
    }
}

sealed class D163StrixStates : StateMachineBuilder
{
    public D163StrixStates(BossModule module) : base(module)
    {
        TrivialPhase()
            .ActivateOnEnter<PropertiesOfDarknessII>()
            .ActivateOnEnter<PropertiesOfQuakes>()
            .ActivateOnEnter<PropertiesOfTornados>()
            .ActivateOnEnter<PropertiesOfImps>()
            .ActivateOnEnter<PropertiesOfThunderIII>()
            .ActivateOnEnter<EclipticMeteor>()
            .ActivateOnEnter<MeteorImpact>()
            .ActivateOnEnter<Components.UnclaimedCastAOEs>();
    }
}

// 場地與尋路沿用沒有模組時的做法（跟著玩家走的方框）：這個副本沒有障礙物圖，也沒有錄影可量場地。
[ModuleInfo(BossModuleInfo.Maturity.WIP, Contributors = "ffxiv-tc-port", GroupType = BossModuleInfo.GroupType.CFC, GroupID = 196, NameID = 5219)]
public sealed class D163Strix(WorldState ws, Actor primary) : SimpleBossModule(ws, primary);
