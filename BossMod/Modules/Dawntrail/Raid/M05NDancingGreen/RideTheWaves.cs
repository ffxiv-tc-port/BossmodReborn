namespace BossMod.Dawntrail.Raid.M05NDancingGreen;

sealed class RideTheWaves(BossModule module) : Components.GenericAOEs(module)
{
    private static readonly AOEShapeRect rect = new(15f, 2.5f);
    private readonly List<AOEInstance> _aoes = new(14);
    private int exaflaresStarted;
    private int? safespot;
    private static readonly WDir dir = new(default, 1f);
    private const float ArenaSouthZ = 120f;
    private const float FirstHitZ = 70f; // 每道浪第一下的起點；打過一下就往南移 5 碼
    private static readonly AOEShapeRect rectToWall = new(50f, 2.5f); // 登記起點最北 z=75，50 碼必定蓋到南緣

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void OnEventEnvControl(byte index, uint state)
    {
        if (index == 0x04)
        {
            int[] indices = [];
            switch (state)
            {
                case 0x04000400u:
                    indices = [0, 2, 3, 4, 5, 6, 7];
                    safespot = 1;
                    break;
                case 0x00020002u:
                case 0x08000800u:
                    indices = [0, 1, 3, 4, 5, 6, 7];
                    safespot = 2;
                    break;
                case 0x10001000u:
                case 0x00100010u:
                    indices = [0, 1, 2, 4, 5, 6, 7];
                    safespot = 3;
                    break;
                case 0x20002000u:
                case 0x00200020u:
                    indices = [0, 1, 2, 3, 5, 6, 7];
                    safespot = 4;
                    break;
                case 0x00400040u:
                case 0x40004000u:
                    indices = [0, 1, 2, 3, 4, 6, 7];
                    safespot = 5;
                    break;
                case 0x80008000u:
                    indices = [0, 1, 2, 3, 4, 5, 7];
                    safespot = 6;
                    break;
                case 0x00080004u:
                    ++exaflaresStarted;
                    break;
            }
            if (indices.Length != 0)
            {
                for (var i = 0; i < 7; ++i)
                {
                    _aoes.Add(new(rect, new(82.5f + indices[i] * 5f, 70f), default, WorldState.FutureTime(3d)));
                }
            }
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.RideTheWaves)
        {
            ++NumCasts;
            if (_aoes.Count > 6 && NumCasts is 10 or 16)
            {
                _aoes.RemoveRange(0, 7);
                --exaflaresStarted;
            }
            if (NumCasts is 3 or 7)
                safespot = null;
            if (_aoes.Count == 0)
            {
                NumCasts = 0;
                return;
            }
            var aoes = CollectionsMarshal.AsSpan(_aoes);
            var max = exaflaresStarted == 1 ? 7 : 14;
            var act = WorldState.FutureTime(2d);
            for (var i = 0; i < max; ++i)
            {
                ref var aoe = ref aoes[i];
                var origin = aoe.Origin;
                aoe.Origin = new(origin.X, origin.Z + 5f);
                aoe.Activation = act;
            }
        }
    }

    public override void DrawArenaBackground(int pcSlot, Actor pc)
    {
        base.DrawArenaBackground(pcSlot, pc);
        if (safespot is int index)
        {
            Arena.ZoneRect(new(82.5f + index * 5f, 80f), dir, 5, default, 2.5f, Colors.SafeFromAOE);
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        // 只閃波浪：告訴閃避預判「浪前方整片都會被掃到」，AI 自己挑來得及的缺口或浪後方站，不逼進綠格。
        // 預判關閉時只看得到下一步，會一路往前退到被浪逼死在場邊，才退回舊的強制安全格。
        if (AIHints.LookaheadEnabled)
        {
            AddSweepAhead(hints);
            return;
        }
        if (safespot is not int index)
            return;
        var actindex = exaflaresStarted <= 1 ? 0 : 7;
        hints.AddForbiddenZone(ShapeDistance.InvertedRect(new WPos(82.5f + index * 5f, 80f), dir, 5f, default, 2.5f), _aoes[actindex].Activation.AddSeconds(1d)); // add 1s so it doesn't get merged with other aoes
    }

    // 浪每步往南推 5 碼、約 2 秒一步，一路推到南緣；浪前方整片併成「下一步之後就會被掃到」的同一波。
    // 逐步照實登記時預判會選「一直站在浪前方往後退」（只要缺口還來得及就不動），退到場邊或碰上別的招式就躲不掉。
    // 還沒打過第一下的浪多給一步，讓人有時間等前一道浪過去再橫移，不必貼著浪尾切過去。
    private void AddSweepAhead(AIHints hints)
    {
        var aoes = CollectionsMarshal.AsSpan(_aoes);
        var len = aoes.Length;
        for (var i = 0; i < len; ++i)
        {
            ref readonly var aoe = ref aoes[i];
            var sweepStep = aoe.Origin.Z > FirstHitZ ? 1 : 2;
            for (var step = 1; step <= sweepStep; ++step)
            {
                var origin = new WPos(aoe.Origin.X, aoe.Origin.Z + step * 5f);
                if (origin.Z >= ArenaSouthZ)
                    break;
                hints.AddFutureForbiddenZone(step == sweepStep ? rectToWall : rect, origin, default, aoe.Activation.AddSeconds(step * 2d));
            }
        }
    }
}
