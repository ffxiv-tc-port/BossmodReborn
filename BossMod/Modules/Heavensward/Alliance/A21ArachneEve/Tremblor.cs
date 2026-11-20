namespace BossMod.Heavensward.Alliance.A21ArachneEve;

// 地震由內往外三段：圓 10.5 → 甜甜圈 10.5-20.5 → 20.5-30.5（台服預兆 general_1／gl_sircle_2010／gl_sircle_3020）。
sealed class Tremblor(BossModule module) : Components.ConcentricAOEs(module, [new AOEShapeCircle(10.5f), new AOEShapeDonut(10.5f, 20.5f), new AOEShapeDonut(20.5f, 30.5f)])
{
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var order = OrderOf(spell.Action.ID);
        if (order == 0)
        {
            AddSequence(spell.LocXZ, Module.CastFinishAt(spell));
        }
        else if (order > 0)
        {
            var sequences = CollectionsMarshal.AsSpan(Sequences);
            for (var i = 0; i < sequences.Length; ++i)
            {
                ref var s = ref sequences[i];
                if (s.NumCastsDone == order && s.Origin.AlmostEqual(spell.LocXZ, 1f))
                {
                    s.NextActivation = Module.CastFinishAt(spell);
                }
            }
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (Sequences.Count != 0)
        {
            AdvanceSequence(OrderOf(spell.Action.ID), spell.LocXZ, WorldState.FutureTime(2d));
        }
    }

    // 下一段的讀條沒對上（施放位置不同、讀條沒出現）時序列不會前進；過了預定時間 5 秒就丟掉，免得危險區一直掛著。
    public override void Update()
    {
        var expire = WorldState.CurrentTime.AddSeconds(-5d);
        for (var i = Sequences.Count - 1; i >= 0; --i)
        {
            if (Sequences[i].NextActivation < expire)
            {
                Sequences.RemoveAt(i);
            }
        }
    }

    private static int OrderOf(uint aid) => aid switch
    {
        (uint)AID.Tremblor1 => 0,
        (uint)AID.Tremblor2 => 1,
        (uint)AID.Tremblor3 => 2,
        _ => -1
    };
}
