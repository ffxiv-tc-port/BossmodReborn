using FFXIVClientStructs.FFXIV.Client.Game.Gauge;
using DrgAID = BossMod.DRG.AID;
using MnkAID = BossMod.MNK.AID;
using NinAID = BossMod.NIN.AID;
using RprAID = BossMod.RPR.AID;
using RprSID = BossMod.RPR.SID;
using SamAID = BossMod.SAM.AID;
using VprAID = BossMod.VPR.AID;
using VprSID = BossMod.VPR.SID;

namespace BossMod.Autorotation.MiscAI;

// GoToPositional 的「Automatic」判定:依玩家職業推導「下一個方位技」要側面還是背面。
//
// 🔴 資訊源刻意限制在 BMR 既有職業模組已經在讀的東西 —— World.Client 的連擊狀態與職業量譜、
//    自己身上的增益、AIHints 已經算好的目標數。不新增任何原生記憶體讀取。
// 🔴 輸出只有 Hints.RecommendedPositional 與 Hints.GoalZones,不碰目標選取(不寫 ForcedTarget)。
//
// 判定規則抄自 BossMod.Autorotation.xan 各近戰模組的 GetNextPositional/GetPositional,但拿掉了
// 對 NextGCD 的依賴 —— 本模組不規劃循環(出招是別的外掛在做),只能用客觀的遊戲狀態去推。
// 推不出來一律回 Positional.Any 代表「不知道」,呼叫端那一幀就什麼都不推。
public sealed partial class GoToPositional
{
    // 判定源(量譜/增益/連段)在同一個 GCD 內可能連續變動,直接跟著翻會讓人在目標兩側來回跑。
    // 遲滯:已經認定的方位要維持,換邊必須連續成立這麼久才接受。取值明顯短於一個 GCD,
    // 所以正常的「連段推進 -> 換邊」不會被拖慢,擋掉的是單幀抖動。
    private const float AutoSwitchDelay = 0.3f;

    private Positional _autoCommitted;
    private Positional _autoPending;
    private DateTime _autoPendingSince;
    private ulong _autoTarget;

    private void ResetAutoPositional()
    {
        _autoCommitted = Positional.Any;
        _autoPending = Positional.Any;
        _autoTarget = 0;
    }

    private Positional UpdateAutoPositional(Actor target)
    {
        // 換目標就重來,不要把上一個目標的判定帶過去
        if (_autoTarget != target.InstanceID)
        {
            ResetAutoPositional();
            _autoTarget = target.InstanceID;
        }

        var raw = PredictPositional(target);

        // 「不知道」不動搖已經認定的方位,但這一幀呼叫端不會推任何東西
        if (raw is not (Positional.Flank or Positional.Rear))
            return Positional.Any;

        if (raw == _autoCommitted)
        {
            _autoPending = Positional.Any;
            return _autoCommitted;
        }

        // 還沒認定過任何方位 -> 直接採用(沒有「翻回去」的風險,不需要等遲滯)
        if (_autoCommitted is not (Positional.Flank or Positional.Rear))
        {
            _autoCommitted = raw;
            _autoPending = Positional.Any;
            return _autoCommitted;
        }

        if (_autoPending != raw)
        {
            _autoPending = raw;
            _autoPendingSince = World.CurrentTime;
        }
        else if ((World.CurrentTime - _autoPendingSince).TotalSeconds >= AutoSwitchDelay)
        {
            _autoCommitted = raw;
            _autoPending = Positional.Any;
        }

        return _autoCommitted;
    }

    private Positional PredictPositional(Actor target) => Player.Class switch
    {
        Class.MNK or Class.PGL => AutoMNK(),
        Class.DRG or Class.LNC => AutoDRG(),
        Class.NIN or Class.ROG => AutoNIN(target),
        Class.SAM => AutoSAM(),
        Class.RPR => AutoRPR(target),
        Class.VPR => AutoVPR(),
        _ => Positional.Any // 其餘職業沒有方位技
    };

    // 近戰 AOE 連段沒有方位需求,擠到側背只是白跑。目標數用 AIHints 已經算好的優先目標,
    // 半徑與 xan 的 NumMeleeAOETargets 一致(5y)。
    private bool AutoInAOE => Hints.NumPriorityTargetsInAOECircle(Player.Position, 5f) > 2;

    // 與 Basexan.StatusLeft 同式(不看 pending 狀態)
    private float AutoSelfStatus<SID>(SID sid) where SID : Enum
        => Player.FindStatus(sid) is ActorStatus s ? StatusDuration(s.ExpireAt) : 0f;

    // 與 Basexan.GetCurrentPositional 同式
    private Positional AutoCurrentPositional(Actor target)
        => (Player.Position - target.Position).Normalized().Dot(target.Rotation.ToDirection()) switch
        {
            < -0.7071068f => Positional.Rear,
            < 0.7071068f => Positional.Flank,
            _ => Positional.Front
        };

    // 武僧:豹形的方位技二選一 —— 破碎拳(背面)/崩拳(側面)。
    // 依量譜的豹之力堆疊決定:堆疊為 0 時循環會打破碎拳把堆疊補回來,有堆疊時打崩拳消耗。
    // 對照 xan MNK.NextPositional。
    private Positional AutoMNK()
    {
        if (AutoInAOE || !ActionUnlocked(MnkAID.SnapPunch))
            return Positional.Any;

        return ActionUnlocked(MnkAID.Demolish) && World.Client.GetGauge<MonkGauge>().CoeurlStacks == 0
            ? Positional.Rear
            : Positional.Flank;
    }

    // 龍騎士:連段位置決定下一個方位技。
    // ⚠️ xan 的 predictNext(靠 DoT/戰吼剩餘時間猜循環會走「櫻花繚亂線」還是「蒼天刺線」)
    //    這裡一律回「不知道」—— 出招是別的外掛在做,猜它會挑哪條分支不可靠,猜錯就是走錯邊。
    private Positional AutoDRG()
    {
        if (AutoInAOE || !ActionUnlocked(DrgAID.ChaosThrust))
            return Positional.Any;

        // 龍牙龍爪之前只有櫻花怒放一個方位技
        if (!ActionUnlocked(DrgAID.FangAndClaw))
            return Positional.Rear;

        return (DrgAID)World.Client.ComboState.Action switch
        {
            // 開膛槍/螺旋擊 -> 櫻花怒放/櫻花繚亂(背面)
            DrgAID.Disembowel or DrgAID.SpiralBlow => Positional.Rear,
            // 櫻花繚亂 -> 龍尾大迴旋(背面)
            DrgAID.ChaoticSpring => Positional.Rear,
            DrgAID.ChaosThrust => ActionUnlocked(DrgAID.WheelingThrust) ? Positional.Rear : Positional.Any,
            // 貫通刺/前衝刺 -> 直刺/蒼天刺 -> 龍牙龍爪(側面)
            DrgAID.VorpalThrust or DrgAID.LanceBarrage => Positional.Flank,
            DrgAID.HeavensThrust or DrgAID.FullThrust => Positional.Flank,
            _ => Positional.Any
        };
    }

    // 忍者:連段結尾二選一 —— 旋風刃(背面)/強甲破點突(側面)。
    // 對照 xan NIN.GetComboEnder:風魔手裏劍量譜(Kazematoi)空了要補強甲破點突,
    // 滿(4)了打旋風刃,中間則挑離自己近的那一邊(等於「不用移動」)。
    private Positional AutoNIN(Actor target)
    {
        if (AutoInAOE || !ActionUnlocked(NinAID.AeolianEdge))
            return Positional.Any;

        if (!ActionUnlocked(NinAID.ArmorCrush))
            return Positional.Rear;

        var kazematoi = World.Client.GetGauge<NinjaGauge>().Kazematoi;
        if (kazematoi == 0)
            return Positional.Flank; // 強甲破點突
        if (kazematoi >= 4)
            return Positional.Rear; // 旋風刃

        return AutoCurrentPositional(target) == Positional.Rear ? Positional.Rear : Positional.Flank;
    }

    // 奪魂者:絞決(側面)/縊殺(背面)由增益指定;兩個增益都沒有時挑離自己近的那一邊。
    // 對照 xan RPR.GetNextPositional。
    private Positional AutoRPR(Actor target)
    {
        if (AutoInAOE || !ActionUnlocked(RprAID.Gibbet))
            return Positional.Any;

        if (AutoSelfStatus(RprSID.EnhancedGallows) > 0f)
            return Positional.Rear; // 縊殺效果提高
        if (AutoSelfStatus(RprSID.EnhancedGibbet) > 0f)
            return Positional.Flank; // 絞決效果提高

        return AutoCurrentPositional(target) == Positional.Rear ? Positional.Rear : Positional.Flank;
    }

    // 武士:陣風 -> 月光(背面)、士風 -> 花車(側面)。
    // 連段不在那兩步時退回看雪月花量譜:缺「花」就要打花車(側面),缺「月」就要打月光(背面)。
    // 對照 xan SAM.GetNextPositional 的 default 分支 —— xan 前面那段靠自己排出來的 NextGCD,
    // 本模組沒有循環規劃,所以只用得到這個分支。
    private Positional AutoSAM()
    {
        if (AutoInAOE || !ActionUnlocked(SamAID.Gekko))
            return Positional.Any;

        var combo = (SamAID)World.Client.ComboState.Action;
        if (combo == SamAID.Jinpu)
            return Positional.Rear;
        if (combo == SamAID.Shifu)
            return Positional.Flank;

        var sen = World.Client.GetGauge<SamuraiGauge>().SenFlags;
        if (ActionUnlocked(SamAID.Kasha) && !sen.HasFlag(SenFlags.Ka))
            return Positional.Flank;
        if (!sen.HasFlag(SenFlags.Getsu))
            return Positional.Rear;

        return Positional.Any;
    }

    // 毒蛇劍士:
    //  - 蛇連段(毒蛇尖牙系)由量譜的 DreadCombo 指定下一步:貳之蛇【猛襲】=側面、貳之蛇【疾速】=背面。
    //  - 一般連段由連段狀態指定:貳之牙【猛襲】後接參之牙【側擊/側裂】,貳之牙【疾速】後接【背擊/背裂】。
    //  - 都不成立時看「疾速」與「猛襲」誰快到期 —— 循環會先補快到期的那個,而補疾速的那條線走背面。
    // 對照 xan VPR.GetPositional(含它把 DreadCombo 判定排在 AOE 判定之前的順序)。
    private Positional AutoVPR()
    {
        if (!ActionUnlocked(VprAID.FlankstingStrike))
            return Positional.Any;

        var swiftscaled = AutoSelfStatus(VprSID.Swiftscaled);
        var instinct = AutoSelfStatus(VprSID.HuntersInstinct);

        switch (World.Client.GetGauge<ViperGauge>().DreadCombo)
        {
            case DreadCombo.Dreadwinder:
                return swiftscaled < instinct ? Positional.Rear : Positional.Flank;
            case DreadCombo.HuntersCoil:
                return Positional.Rear;
            case DreadCombo.SwiftskinsCoil:
                return Positional.Flank;
            case DreadCombo.HuntersDen:
            case DreadCombo.SwiftskinsDen:
            case DreadCombo.PitOfDread:
                return Positional.Any;
        }

        if (AutoInAOE)
            return Positional.Any;

        return (VprAID)World.Client.ComboState.Action switch
        {
            VprAID.HuntersSting => Positional.Flank,
            VprAID.SwiftskinsSting => Positional.Rear,
            _ => swiftscaled < instinct ? Positional.Rear : Positional.Flank
        };
    }
}
