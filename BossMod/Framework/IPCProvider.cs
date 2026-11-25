using BossMod.AI;
using BossMod.Autorotation;
using Dalamud.Plugin;
using System.Text.Json;

namespace BossMod;

sealed class IPCProvider : IDisposable
{
    private Action? _disposeActions;

    public IPCProvider(BossModuleManager bossmod, AIHints hints, RotationModuleManager autorotation, ActionManagerEx amex, MovementOverride movement, AIManager ai)
    {
        // 🔴 執行緒：IPC 端點跑在**呼叫端的執行緒**上（CallGate 是直接方法呼叫）。凡是碰得到
        //    「每幀重建的集合」「存檔」「外掛狀態改變」的端點，本檔一律包進 IpcFrameworkGate.Get/Run
        //    交回遊戲主執行緒；已經在主執行緒上呼叫時就地執行，行為與延遲逐字不變。
        //    純值型別／參考比較的唯讀端點不包（包了只是白花一幀）—— 判準見 IpcFrameworkGate.cs。
        Register("HasModuleByDataId", (uint dataId) => BossModuleRegistry.FindByOID(dataId) != null);

        #region 機制感知（回移自上游；端點名稱逐字對齊 upstream/main:BossMod/Framework/IPCProvider.cs）

        // 這一整區都是**唯讀**端點，給外部循環外掛（RotationSolverReborn 之類）問「接下來會發生什麼機制」（它們看不到 StateMachine 與 AIHints）。
        // 失敗語意一律照上游：**沒有 active module／沒有資料 ⇒ float.MaxValue**（＝「沒有機制」），絕不擲例外
        // （IPC 端點擲出去的例外會在呼叫端變成 InvalidOperationException，而呼叫端多半沒有 try/catch）。
        // 時間基準刻意沿用上游的 DateTime.Now（而不是 WorldState.CurrentTime）：那是既有呼叫端所依據的線上契約，
        // 兩者的差異只在重播回放時才顯著。

        Register("HasActiveModule", () => bossmod.ActiveModule?.StateMachine.ActiveState != null);
        Register("ActiveModuleName", () => bossmod.ActiveModule?.PrimaryActor.Name.ToString());

        // 走一遍狀態機並回報看到什麼；純診斷字串，沒有機器可讀契約。
        Register("Debug.TimelineWalk", () =>
        {
            var module = bossmod.ActiveModule;
            if (module == null)
            {
                return "No active module";
            }

            var sm = module.StateMachine;
            if (sm.ActiveState == null)
            {
                return "ActiveState is null";
            }

            var sb = new StringBuilder();
            sb.Append($"Phase={sm.ActivePhaseIndex} State={sm.ActiveState.ID:X}({sm.ActiveState.Name}) Dur={sm.ActiveState.Duration:F1}s Hint={sm.ActiveState.EndHint}");
            var count = 0;
            var next = sm.ActiveState;
            var foundRW = false;
            var foundTB = false;
            while (next != null && count < 20)
            {
                if (!foundRW && next.EndHint.HasFlag(StateMachine.StateHint.Raidwide))
                {
                    foundRW = true;
                    sb.Append($" | RW@{next.ID:X}({next.Name})");
                }
                if (!foundTB && next.EndHint.HasFlag(StateMachine.StateHint.Tankbuster))
                {
                    foundTB = true;
                    sb.Append($" | TB@{next.ID:X}({next.Name})");
                }
                next = next.NextStates?.Length == 1 ? next.NextStates[0] : null;
                count++;
            }
            if (!foundRW)
            {
                sb.Append(" | RW=NONE");
            }

            if (!foundTB)
            {
                sb.Append(" | TB=NONE");
            }

            if (next == null && count < 20)
            {
                sb.Append($" | Chain ended at {count} states");
            }

            if (count >= 20)
            {
                sb.Append(" | Walked 20+ states");
            }

            return sb.ToString();
        });

        // 時間軸類：沿著狀態機往後找第一個帶指定旗標的轉換。
        // 📌 StateMachine.NextTransitionWithFlag 沒有命中時回 DateTime.MaxValue，這裡折成 float.MaxValue。
        float nextTransitionIn(StateMachine.StateHint flag)
        {
            var module = bossmod.ActiveModule;
            if (module?.StateMachine.ActiveState == null)
            {
                return float.MaxValue;
            }

            var next = module.StateMachine.NextTransitionWithFlag(flag);
            return next == DateTime.MaxValue ? float.MaxValue : (float)(next - DateTime.Now).TotalSeconds;
        }

        Register("Timeline.NextRaidwideIn", () => nextTransitionIn(StateMachine.StateHint.Raidwide));
        Register("Timeline.NextTankbusterIn", () => nextTransitionIn(StateMachine.StateHint.Tankbuster));
        Register("Timeline.NextKnockbackIn", () => nextTransitionIn(StateMachine.StateHint.Knockback));
        Register("Timeline.NextDowntimeIn", () => nextTransitionIn(StateMachine.StateHint.DowntimeStart));
        Register("Timeline.NextDowntimeEndIn", () => nextTransitionIn(StateMachine.StateHint.DowntimeEnd));
        Register("Timeline.NextVulnerableIn", () => nextTransitionIn(StateMachine.StateHint.VulnerableStart));
        Register("Timeline.NextVulnerableEndIn", () => nextTransitionIn(StateMachine.StateHint.VulnerableEnd));

        // 預測傷害類：AIHints.PredictedDamage 由 boss 模組每幀重建。
        Register("Hints.NextDamageIn", () => IpcFrameworkGate.Get("Hints.NextDamageIn", () =>
        {
            var predicted = hints.PredictedDamage;
            return predicted.Count == 0 ? float.MaxValue : (float)(predicted[0].Activation - DateTime.Now).TotalSeconds;
        }, float.MaxValue));

        Register("Hints.NextDamageType", () => IpcFrameworkGate.Get("Hints.NextDamageType", () =>
        {
            var predicted = hints.PredictedDamage;
            return predicted.Count == 0 ? 0 : (int)predicted[0].Type;
        }, 0));

        // 分屬性的版本：掃**全部**條目找第一筆吻合的類型（不是只看 [0]）。
        float nextDamageOfType(AIHints.PredictedDamageType type)
        {
            var predicted = hints.PredictedDamage;
            var now = DateTime.Now;
            var count = predicted.Count;
            for (var i = 0; i < count; ++i)
            {
                if (predicted[i].Type == type)
                {
                    return (float)(predicted[i].Activation - now).TotalSeconds;
                }
            }
            return float.MaxValue;
        }

        Register("Hints.NextRaidwideDamageIn", () => IpcFrameworkGate.Get("Hints.NextRaidwideDamageIn", () => nextDamageOfType(AIHints.PredictedDamageType.Raidwide), float.MaxValue));
        Register("Hints.NextTankbusterDamageIn", () => IpcFrameworkGate.Get("Hints.NextTankbusterDamageIn", () => nextDamageOfType(AIHints.PredictedDamageType.Tankbuster), float.MaxValue));
        // 只看打本機玩家（PlayerSlot）的死刑；上面那支不分打誰，給坦克決定要不要開減傷會把打別人的也當成打自己。
        Register("Hints.NextTankbusterOnPlayerIn", () => IpcFrameworkGate.Get("Hints.NextTankbusterOnPlayerIn", () => hints.NextTankbusterOnSlotIn(PartyState.PlayerSlot, bossmod.WorldState.CurrentTime), float.MaxValue));
        Register("Hints.PredictedDamagePlayers", () => IpcFrameworkGate.Get("Hints.PredictedDamagePlayers", () => hints.PredictedDamage.Count == 0 ? 0ul : hints.PredictedDamage[0].Players.Raw, 0ul));

        // 危險度：與 Hints.NextRaidwideDamageIn／NextTankbusterDamageIn 指向同一筆（第一筆該類型）；全體傷害另合併同時結算的預告（AIHints.NextRaidwideSeverity）。
        // 🔴 契約（WrathCombo 照寫）：int 0 未知、1 低、2 中、3 高；沒有預測＝0；SourceAction 0＝元件沒填來源。名稱與型別不可改。
        int firstOfType(AIHints.PredictedDamageType type)
        {
            var predicted = hints.PredictedDamage;
            var count = predicted.Count;
            for (var i = 0; i < count; ++i)
            {
                if (predicted[i].Type == type)
                {
                    return i;
                }
            }
            return -1;
        }
        int severityOf(AIHints.PredictedDamageType type)
        {
            var i = firstOfType(type);
            return i < 0 ? 0 : (int)DamageSeverityTable.Get(bossmod.WorldState.CurrentCFCID, hints.PredictedDamage[i].SourceAction, type);
        }
        Register("Hints.NextRaidwideSeverity", () => IpcFrameworkGate.Get("Hints.NextRaidwideSeverity", () => (int)hints.NextRaidwideSeverity(bossmod.WorldState.CurrentCFCID, out _), 0));
        Register("Hints.NextTankbusterSeverity", () => IpcFrameworkGate.Get("Hints.NextTankbusterSeverity", () => severityOf(AIHints.PredictedDamageType.Tankbuster), 0));
        Register("Hints.NextRaidwideSourceAction", () => IpcFrameworkGate.Get("Hints.NextRaidwideSourceAction", () =>
        {
            hints.NextRaidwideSeverity(bossmod.WorldState.CurrentCFCID, out var i);
            return i < 0 ? 0u : hints.PredictedDamage[i].SourceAction;
        }, 0u));

        // 下一筆值得考慮減傷的全體傷害（AIHints.NextMitigableRaidwide）：低危散開排在前面時，上面三支只看得到散開。
        // 🔴 契約：三支指向同一筆；沒有＝float.MaxValue／0／0；危險度數值同上。名稱與型別不可改。
        int nextMitigable(out DateTime now, out DamageSeverity severity)
        {
            now = bossmod.WorldState.CurrentTime;
            return hints.NextMitigableRaidwide(bossmod.WorldState.CurrentCFCID, now, out severity);
        }
        Register("Hints.NextMitigableRaidwideIn", () => IpcFrameworkGate.Get("Hints.NextMitigableRaidwideIn", () =>
        {
            var i = nextMitigable(out var now, out _);
            return i < 0 ? float.MaxValue : (float)(hints.PredictedDamage[i].Activation - now).TotalSeconds;
        }, float.MaxValue));
        Register("Hints.NextMitigableRaidwideSeverity", () => IpcFrameworkGate.Get("Hints.NextMitigableRaidwideSeverity", () =>
        {
            var i = nextMitigable(out _, out var severity);
            return i < 0 ? 0 : (int)severity;
        }, 0));
        Register("Hints.NextMitigableRaidwideSourceAction", () => IpcFrameworkGate.Get("Hints.NextMitigableRaidwideSourceAction", () =>
        {
            var i = nextMitigable(out _, out _);
            return i < 0 ? 0u : hints.PredictedDamage[i].SourceAction;
        }, 0u));

        Register("Hints.MaxCastTime", () => hints.MaxCastTime);

        // 上游把「取消詠唱」的成因拆成 Mechanic／Other 兩個欄位，我方樹仍是單一 AIHints.ForceCancelCast。
        // 這裡把上游的四個名字全部註冊，Mechanic 與 Other 回同一個 bool：對呼叫端只會偏保守（該取消時一定為 true），不會漏報。
        // 另外保留不帶後綴的舊名，讓既有呼叫端不必跟著改（兩者指向同一個 bool）。
        Register("Hints.ForceCancelCast", () => hints.ForceCancelCast);
        Register("Hints.ForceCancelCastMechanic", () => hints.ForceCancelCast);
        Register("Hints.ForceCancelCastOther", () => hints.ForceCancelCast);
        Register("Hints.ForceCancelCastAI", () => ai.Controller.ForceCancelCast);
        Register("Hints.ForceCancelCastMechanicAI", () => ai.Controller.ForceCancelCast);
        Register("Hints.ForceCancelCastOtherAI", () => ai.Controller.ForceCancelCast);

        Register("Movement.IsMoving", () => hints.ForcedMovement != null);
        Register("Movement.IsMoveRequested", movement.IsMoveRequested);

        Register("Hints.ForbiddenZonesCount", () => hints.ForbiddenZones.Count);
        Register("Hints.ForbiddenZonesNextActivation", () => IpcFrameworkGate.Get("Hints.ForbiddenZonesNextActivation", () => hints.ForbiddenZones.Count == 0 ? float.MaxValue : (float)(hints.ForbiddenZones[0].activation - DateTime.Now).TotalSeconds, float.MaxValue));
        Register("Hints.ForbiddenDirectionsCount", () => hints.ForbiddenDirections.Count);
        Register("Hints.ArenaCenter", () => new Vector2(hints.PathfindMapCenter.X, hints.PathfindMapCenter.Z));
        Register("Hints.ArenaRadius", () => hints.PathfindMapBounds.Radius);
        Register("Hints.ShouldCleansePlayers", () => hints.ShouldCleanse.Raw);
        Register("Hints.InteractWithTargetOID", () => hints.InteractWithTarget?.InstanceID ?? 0ul);
        Register("Hints.RecommendedPositional", () => (int)hints.RecommendedPositional.Pos);

        // 打斷／暈眩目標：把 boss 模組與深宮樓層模組每幀寫進 AIHints.Enemy 的兩個旗標曝光給外部循環外掛。
        // 資料源選 hints.PotentialTargets：與 hints.Enemies[] 裝的是同一批物件參考，但沒有 null 洞，BMR 自己的 AI 讀的也是它。
        // 這裡**不做**「敵人在不在戰鬥中」「詠唱可不可打斷」「距離夠不夠」的過濾：那些是呼叫端的職責，
        // 端點語意就是逐字的「模組說這隻該被打斷／該被暈」。
        // 沒有 active module 不必特別處理：旗標的唯一寫入端就是模組，沒模組就全是 false ⇒ 自然回空陣列。
        static ulong[] flaggedEnemies(List<AIHints.Enemy> targets, Func<AIHints.Enemy, bool> flagged)
        {
            // 先數再填的兩趟掃描是為了讓「一個都沒有」這個絕大多數的情況完全不配置
            // （`[]` 對陣列會被降階成 Array.Empty<ulong>()）。上限是敵人數，≤100。
            var count = targets.Count;
            var n = 0;
            for (var i = 0; i < count; ++i)
            {
                if (flagged(targets[i]))
                {
                    ++n;
                }
            }
            if (n == 0)
            {
                return [];
            }

            var res = new ulong[n];
            var j = 0;
            for (var i = 0; i < count && j < n; ++i)
            {
                var e = targets[i];
                if (flagged(e))
                {
                    res[j++] = e.Actor.InstanceID;
                }
            }
            return j == n ? res : res[..j];
        }

        Register("Hints.ShouldInterruptTargets", () => IpcFrameworkGate.Get<ulong[]>("Hints.ShouldInterruptTargets", () => flaggedEnemies(hints.PotentialTargets, static e => e.ShouldBeInterrupted), []));
        Register("Hints.ShouldStunTargets", () => IpcFrameworkGate.Get<ulong[]>("Hints.ShouldStunTargets", () => flaggedEnemies(hints.PotentialTargets, static e => e.ShouldBeStunned), []));
        // true＝本場眩暈／打斷保留給模組標記的讀條（AIHints.StunReservedFor）：呼叫端只可對上面兩個清單裡的目標出手，其餘一律不用。
        // 只讀一個參考是否為 null，不必走主執行緒閘門；舊版 BMR 沒有這兩個端點＝呼叫端當作沒有保留。
        Register("Hints.StunsReserved", () => hints.StunReservedFor != null);
        Register("Hints.InterruptsReserved", () => hints.InterruptReservedFor != null);
        // 限時必殺目標（AIHints.Enemy.UrgentKill）：沒掛預設集也照樣算，給外部自動化切目標用；順序＝優先度由高到低。
        Register("Hints.UrgentKillTargets", () => IpcFrameworkGate.Get<ulong[]>("Hints.UrgentKillTargets", () => flaggedEnemies(hints.PotentialTargets, static e => e.UrgentKill), []));

        Register("Hints.SpecialModeIn", () => hints.ImminentSpecialMode == default
            ? float.MaxValue
            : (float)(hints.ImminentSpecialMode.activation - DateTime.Now).TotalSeconds);
        Register("Hints.SpecialModeType", () => hints.ImminentSpecialMode == default ? 0 : (int)hints.ImminentSpecialMode.mode);

        // 位移安全性：上游是 ActionPredicate.IsDashSafe，我方樹的同一份幾何檢查叫
        // ActionDefinitions.IsDashDangerous（**回傳值相反**，所以這裡一律取反）。
        // 🔑 刻意用 IsDashDangerous 這支「純幾何」的，不是 DashFixedDistanceCheck 那些條件委派 ——
        //    後者還會看 DashSafety/DashSafetyExtra 設定與 PendingKnockbacks，
        //    那是「我方要不要攔這一發」的策略，不是呼叫端問的「這個落點安不安全」。
        Register("Hints.IsPositionSafe", (Vector3 to) => IpcFrameworkGate.Get("Hints.IsPositionSafe", () =>
        {
            var player = bossmod.WorldState.Party.Player();
            return player != null && !ActionDefinitions.IsDashDangerous(player.Position, new WPos(to.X, to.Z), hints);
        }, false));

        Register("Hints.IsDashSafe", (Vector3 from, Vector3 to) => IpcFrameworkGate.Get("Hints.IsDashSafe", () =>
            !ActionDefinitions.IsDashDangerous(new WPos(from.X, from.Z), new WPos(to.X, to.Z), hints), false));

        // 對齊 DashFixedDistanceCheck 的落點算法：dest = playerPos + playerRotation * range（backwards 時取負）。
        Register("Hints.IsFixedDashSafe", (float range, bool backwards) => IpcFrameworkGate.Get("Hints.IsFixedDashSafe", () =>
        {
            var player = bossmod.WorldState.Party.Player();
            if (player == null)
            {
                return false;
            }

            var dest = player.Position + player.Rotation.ToDirection() * range * (backwards ? -1f : 1f);
            return !ActionDefinitions.IsDashDangerous(player.Position, dest, hints);
        }, false));

        // 對齊 BackdashCheck：dir = normalize(playerPos - enemyPos)，dest = playerPos + dir * range。
        Register("Hints.IsBackdashSafe", (Vector3 enemyPos, float range) => IpcFrameworkGate.Get("Hints.IsBackdashSafe", () =>
        {
            var player = bossmod.WorldState.Party.Player();
            if (player == null)
            {
                return false;
            }

            var dir = (player.Position - new WPos(enemyPos.X, enemyPos.Z)).Normalized();
            var dest = player.Position + dir * range;
            return !ActionDefinitions.IsDashDangerous(player.Position, dest, hints);
        }, false));

        // 依技能 id 問位移技落點安不安全：0＝不知道、1＝安全、2＝危險（判定見 ActionDefinitions.ClassifyDashLanding）。
        // 與上面幾條同理只問事實，不看 DashSafety 等設定與 AI 開關；任何例外都回 0。
        // 外部循環每幀可能問好幾次：已在主執行緒上就直接算，不建閘門要的閉包；別的執行緒才走閘門。
        long dashQueryErrorLoggedAt = -60000;
        int queryDash(uint actionId, ulong targetObjectId)
        {
            try
            {
                return ActionDefinitions.ClassifyDashLanding(bossmod.WorldState, hints, actionId, targetObjectId);
            }
            catch (Exception ex)
            {
                var now = Environment.TickCount64;
                if (now - dashQueryErrorLoggedAt >= 60000)
                {
                    dashQueryErrorLoggedAt = now;
                    Service.Logger.Information($"[BMR IPC] BossMod.Hints.IsActionDashSafe({actionId}, 0x{targetObjectId:X}) 擲例外，回 0（不知道）；60 秒內同類不再記：{ex}");
                }
                return ActionDefinitions.DashQueryUnknown;
            }
        }
        int queryDashViaGate(uint actionId, ulong targetObjectId) => IpcFrameworkGate.Get("Hints.IsActionDashSafe", () => queryDash(actionId, targetObjectId), ActionDefinitions.DashQueryUnknown);
        Register("Hints.IsActionDashSafe", (uint actionId, ulong targetObjectId) => Service.Framework.IsInFrameworkUpdateThread ? queryDash(actionId, targetObjectId) : queryDashViaGate(actionId, targetObjectId));

        Register("AI.IsNavigating", () => ai.Controller.NaviTargetPos != null);
        Register("AI.NaviTargetPos", () =>
        {
            var pos = ai.Controller.NaviTargetPos;
            return pos.HasValue ? new Vector3(pos.Value.X, 0, pos.Value.Z) : (Vector3?)null;
        });
        Register("AI.PlayerSpeed", () => ai.WorldState.Client.MoveSpeed);

        // 冷卻計畫（cooldown planner）：回傳沿著**當前生效分支**解析出來的預定動作。
        // 沒有計畫在跑時回空陣列的 JSON，不是 null —— 呼叫端可以無條件丟給 JSON 解析器。
        Register("Plan.GetUpcomingActions", (float lookAheadSeconds) => IpcFrameworkGate.Get("Plan.GetUpcomingActions", () =>
        {
            var planner = autorotation.Planner;
            if (planner == null)
                return "[]";

            var actions = planner.GetUpcomingPlannedActions(bossmod.WorldState, autorotation.PlayerSlot, lookAheadSeconds);
            return JsonSerializer.Serialize(actions);
        }, "[]"));

        // 推播：生效中的計畫換掉時發一次訊號，讓呼叫端知道該重新問 Plan.GetUpcomingActions。
        // 🔴 這個是唯一在本類別裡「訂閱別人事件」的端點，所以退訂必須掛進 _disposeActions ——
        //    漏掉的話 RotationModuleManager 會一直握著已 Dispose 的 IPCProvider 的委派。
        var plannedActionsChangedProvider = Service.PluginInterface.GetIpcProvider<object>("BossMod.Plan.ActionsChanged");
        void OnPlannedActionsChanged() => plannedActionsChangedProvider.SendMessage();
        autorotation.PlannedActionsChanged += OnPlannedActionsChanged;
        _disposeActions += () => autorotation.PlannedActionsChanged -= OnPlannedActionsChanged;

        #endregion

        // 推播：實機 WorldState 的 DirectorUpdate 原樣轉發 (DirectorID, UpdateID, Param1..Param4)，讓其他插件不必再掛一份 ActorControl hook。
        //   只訂 bossmod.WorldState（實機那份）；重播／模擬器各有自己的 WorldState，不會進來。hook 沒裝成＝沒有事件，不另補救。
        // 🔴 訂閱端擲例外時 Dalamud 的 SendMessage 會往上拋並跳過其餘訂閱者；這裡跑在 BMR 執行 ops 的迴圈裡，不攔會截斷這一幀後面的 ops。
        var directorUpdateProvider = Service.PluginInterface.GetIpcProvider<uint, uint, uint, uint, uint, uint, object>("BossMod.WorldState.DirectorUpdate");
        long directorUpdateErrorLoggedAt = -60000;
        void OnDirectorUpdate(WorldState.OpDirectorUpdate op)
        {
            if (directorUpdateProvider.SubscriptionCount == 0)
                return;
            try
            {
                directorUpdateProvider.SendMessage(op.DirectorID, op.UpdateID, op.Param1, op.Param2, op.Param3, op.Param4);
            }
            catch (Exception ex)
            {
                var now = Environment.TickCount64;
                if (now - directorUpdateErrorLoggedAt >= 60000)
                {
                    directorUpdateErrorLoggedAt = now;
                    Service.Logger.Information($"[BMR IPC] BossMod.WorldState.DirectorUpdate 的訂閱端擲例外，已略過；60 秒內同類不再記：{ex}");
                }
            }
        }
        var directorUpdateSubscription = bossmod.WorldState.DirectorUpdate.Subscribe(OnDirectorUpdate);
        _disposeActions += directorUpdateSubscription.Dispose;

        Register("Configuration", (List<string> args, bool save) => IpcFrameworkGate.Get<List<string>>("Configuration", () =>
        {
            var result = Service.Config.ConsoleCommand(args.AsSpan(), save);
            ai.NoteState(AIManager.SrcIpcConfiguration);
            return result;
        }, []));

        DateTime lastModified = DateTime.Now;
        Service.Config.Modified.Subscribe(() => lastModified = DateTime.Now);
        Register("Configuration.LastModified", () => lastModified);

        // 執行期覆寫（不寫進設定檔），給其他插件取代會存檔的 /bmr cfg；鍵與語意見 TransientConfigOverrides。
        //   SetTransientBool(owner, key, value) -> bool：owner＝呼叫端自己的 InternalName，須是已載入的插件；未知鍵回 false。
        //   ClearTransient(owner, key) -> bool（有清到才 true）／ClearAllTransient(owner) -> int（清掉幾筆）。
        // 擁有者卸載時自動清掉它的覆寫；表自帶鎖、讀取端無鎖，所以不走 IpcFrameworkGate。
        Register("Configuration.SetTransientBool", (string owner, string key, bool value) =>
        {
            // 插件清單在鎖外取：Dalamud 送卸載事件時持有它自己的清單鎖，鎖內再取會反向等待。
            var plugin = owner == null ? null : Service.PluginInterface.InstalledPlugins.FirstOrDefault(p => p.InternalName == owner);
            var ok = TransientConfigOverrides.Set(owner!, key, value, () => plugin?.IsLoaded == true);
            Service.Logger.Information(ok
                ? $"[BMR IPC] {owner} 暫時覆寫 {key}＝{value}（只在執行期生效，不寫進設定檔）。"
                : $"[BMR IPC] 拒絕 {owner} 的暫時覆寫 {key}＝{value}：鍵不在允許清單內，或擁有者不是已載入的插件。");
            return ok;
        });
        //   SetTransientString(owner, key, value) -> bool：字串鍵（目前只有 AI 自動循環預設）；value 空白＝覆寫成「不選」。
        //   AI 預設的覆寫在框架執行緒上呼叫時當場套到執行中的 AI（AI.GetPreset 立即看得到），否則下一幀套。
        Register("Configuration.SetTransientString", (string owner, string key, string value) =>
        {
            var plugin = owner == null ? null : Service.PluginInterface.InstalledPlugins.FirstOrDefault(p => p.InternalName == owner);
            var before = TransientConfigOverrides.AIAutorotPresetName;
            var ok = TransientConfigOverrides.SetString(owner!, key, value, () => plugin?.IsLoaded == true);
            if (!ok)
                Service.Logger.Information($"[BMR IPC] 拒絕 {owner} 的暫時覆寫 {key}＝「{value}」：鍵不在允許清單內，或擁有者不是已載入的插件。");
            else if (before == null || before.Owner != owner || before.Value != TransientConfigOverrides.AIAutorotPresetName?.Value)
                Service.Logger.Information($"[BMR IPC] {owner} 暫時覆寫 {key}＝「{value}」（只在執行期生效，不寫進設定檔）。");
            applyPresetOverrideNow();
            return ok;
        });
        Register("Configuration.ClearTransient", (string owner, string key) =>
        {
            var ok = TransientConfigOverrides.Clear(owner, key);
            if (ok)
                Service.Logger.Information($"[BMR IPC] {owner} 取消暫時覆寫 {key}。");
            applyPresetOverrideNow();
            return ok;
        });
        Register("Configuration.ClearAllTransient", (string owner) => clearOwnerOverrides(owner, "呼叫 ClearAllTransient"));

        void applyPresetOverrideNow()
        {
            if (Service.Framework.IsInFrameworkUpdateThread)
                ai.ApplyTransientAIPresetIfChanged();
        }
        int clearOwnerOverrides(string owner, string why)
        {
            var n = TransientConfigOverrides.ClearOwner(owner);
            if (n > 0)
                Service.Logger.Information($"[BMR IPC] {owner} {why}，清掉它的 {n} 筆暫時覆寫。");
            applyPresetOverrideNow();
            return n;
        }
        void onActivePluginsChanged(IActivePluginsChangedEventArgs e)
        {
            if (e.Kind != PluginListInvalidationKind.Unloaded)
                return;
            foreach (var name in e.AffectedInternalNames)
            {
                clearOwnerOverrides(name, "已卸載");
                if (ExternalMovementHold.Release(name) > 0)
                    Service.Logger.Information($"[BMR IPC] {name} 已卸載，清掉它的暫停自動移動租約。");
            }
        }
        Service.PluginInterface.ActivePluginsChanged += onActivePluginsChanged;
        _disposeActions += () => Service.PluginInterface.ActivePluginsChanged -= onActivePluginsChanged;

        Register("Rotation.ActionQueue.HasEntries", () => IpcFrameworkGate.Get("Rotation.ActionQueue.HasEntries", () =>
        {
            var entries = CollectionsMarshal.AsSpan(autorotation.Hints.ActionsToExecute.Entries);
            var len = entries.Length;
            for (var i = 0; i < len; ++i)
            {
                ref readonly var e = ref entries[i];
                if (!e.Manual)
                {
                    return true;
                }
            }
            return false;
        }, false));

        // 完整 IPC 名稱:BossMod.ActionQueue.UseManualQueueEnabled
        // 回「手動佇列接管是否啟用」。唯讀、無副作用,每次呼叫讀設定現值(使用者中途改也拿得到新值)。
        // 🔑 給別的外掛判斷「我直接呼叫 ActionManager::UseAction 會不會被 BMR 收進它自己的佇列」——
        //    這個開關為 true 時會,為 false 時不會(見 ActionManagerEx.UseActionDetour)。
        Register("ActionQueue.UseManualQueueEnabled", () => Service.Config.Get<ActionTweaksConfig>().UseManualQueue);

        Register("Presets.Get", (string name) => IpcFrameworkGate.Get<string?>("Presets.Get", () =>
        {
            var preset = autorotation.Database.Presets.FindPresetByName(name);
            return preset != null ? JsonSerializer.Serialize(preset, Serialization.BuildSerializationOptions()) : null;
        }, null));
        Register("Presets.Create", (string presetSerialized, bool overwrite) =>
        {
            var p = JsonSerializer.Deserialize<Preset>(presetSerialized, Serialization.BuildSerializationOptions());
            if (p == null)
                return false;
            // 🔴 外部 IPC 直接反序列化外部字串就寫入,繞過 UI 的空名閘門(CheckNameConflict 把空白名判為衝突)。
            //    空名 preset 會讓清單/下拉/深層迷宮設定的字串比對全部退化(見 CheckNameConflict 註解),
            //    必須在寫入前擋下。回傳 false 即失敗訊號(本端點回傳型別是 bool,呼叫端已在處理)。
            if (string.IsNullOrWhiteSpace(p.Name))
            {
                // 要使用者回報的診斷;具名說明是哪個 IPC 端點拒絕了什麼。
                Service.Logger.Information("[BMR] IPC Presets.Create 被拒:preset 名稱為空白。");
                return false;
            }
            // 查重用與 FindPresetByName/CheckNameConflict 同一個比較器（見 PresetDatabase.NameComparison）。
            // 🔑 上面的反序列化與空名檢查是純受管理的工作，刻意留在呼叫端的執行緒上
            //    （外部字串可能很大，沒必要佔用遊戲主執行緒的一幀）。從這裡開始才需要閘門：
            //    UserPresets 是 framework 執行緒會改動的裸 List，Modify 之後還會 Save()（檔案 I/O）
            //    並 Fire PresetModified（訂閱者 RotationModuleManager 會 Dispose 全部生效中的循環模組）。
            Preset created = p;
            return IpcFrameworkGate.Get("Presets.Create", () =>
            {
                var index = autorotation.Database.Presets.UserPresets.FindIndex(x => string.Equals(x.Name, created.Name, PresetDatabase.NameComparison));
                if (index >= 0 && !overwrite)
                    return false;
                autorotation.Database.Presets.Modify(index, created);
                return true;
            }, false);
        });
        Register("Presets.Delete", (string name) => IpcFrameworkGate.Get("Presets.Delete", () =>
        {
            var index = autorotation.Database.Presets.UserPresets.FindIndex(x => string.Equals(x.Name, name, PresetDatabase.NameComparison));
            if (index < 0)
                return false;
            autorotation.Database.Presets.Modify(index, null);
            return true;
        }, false));

        Register("Presets.GetActive", () => autorotation.Preset?.Name);
        Register("Presets.SetActive", (string name) => IpcFrameworkGate.Get("Presets.SetActive", () =>
        {
            var preset = autorotation.Database.Presets.FindPresetByName(name);
            if (preset == null)
                return false;
            autorotation.Preset = preset;
            return true;
        }, false));
        Register("Presets.ClearActive", () => IpcFrameworkGate.Get("Presets.ClearActive", () =>
        {
            if (autorotation.Preset == null)
                return false;
            autorotation.Preset = null;
            return true;
        }, false));
        Register("Presets.GetForceDisabled", () => autorotation.Preset == RotationModuleManager.ForceDisable);
        Register("Presets.SetForceDisabled", () => IpcFrameworkGate.Get("Presets.SetForceDisabled", () =>
        {
            if (autorotation.Preset == RotationModuleManager.ForceDisable)
                return false;
            autorotation.Preset = RotationModuleManager.ForceDisable;
            return true;
        }, false));

        bool addTransientStrategy(string presetName, string moduleTypeName, string trackName, string value, StrategyTarget target = StrategyTarget.Automatic, int targetParam = 0)
        {
            var mt = Type.GetType(moduleTypeName);
            if (mt == null || !RotationModuleRegistry.Modules.TryGetValue(mt, out var md))
                return false;
            var iTrack = md.Definition.Configs.FindIndex(td => td.InternalName == trackName);
            if (iTrack < 0)
                return false;
            StrategyValue tempValue;
            switch (md.Definition.Configs[iTrack])
            {
                case StrategyConfigTrack tr:
                    var iOpt = tr.Options.FindIndex(od => od.InternalName == value);
                    if (iOpt < 0)
                        return false;
                    tempValue = new StrategyValueTrack() { Option = iOpt, Target = target, TargetParam = targetParam };
                    break;
                case StrategyConfigFloat sc:
                    if (!float.TryParse(value, out var fv))
                        return false;
                    tempValue = new StrategyValueFloat() { Value = Math.Clamp(fv, sc.MinValue, sc.MaxValue) };
                    break;
                case StrategyConfigInt si:
                    if (!long.TryParse(value, out var lv))
                        return false;
                    tempValue = new StrategyValueInt() { Value = Math.Clamp(lv, si.MinValue, si.MaxValue) };
                    break;
                default:
                    return false;
            }
            var ms = autorotation.Database.Presets.FindPresetByName(presetName)?.Modules.Find(m => m.Type == mt);
            if (ms == null)
                return false;
            var setting = new Preset.ModuleSetting(default, iTrack, tempValue);
            var index = ms.TransientSettings.FindIndex(s => s.Track == iTrack);
            if (index < 0)
                ms.TransientSettings.Add(setting);
            else
                ms.TransientSettings[index] = setting;
            return true;
        }
        Register("Presets.AddTransientStrategy", (string presetName, string moduleTypeName, string trackName, string value) => IpcFrameworkGate.Get("Presets.AddTransientStrategy", () => addTransientStrategy(presetName, moduleTypeName, trackName, value), false));
        Register("Presets.AddTransientStrategyTargetEnemyOID", (string presetName, string moduleTypeName, string trackName, string value, int oid) => IpcFrameworkGate.Get("Presets.AddTransientStrategyTargetEnemyOID", () => addTransientStrategy(presetName, moduleTypeName, trackName, value, StrategyTarget.EnemyByOID, oid), false));

        Register("Presets.ClearTransientStrategy", (string presetName, string moduleTypeName, string trackName) => IpcFrameworkGate.Get("Presets.ClearTransientStrategy", () =>
        {
            var mt = Type.GetType(moduleTypeName);
            if (mt == null || !RotationModuleRegistry.Modules.TryGetValue(mt, out var md))
                return false;
            var iTrack = md.Definition.Configs.FindIndex(td => td.InternalName == trackName);
            if (iTrack < 0)
                return false;
            var ms = autorotation.Database.Presets.FindPresetByName(presetName)?.Modules.Find(m => m.Type == mt);
            if (ms == null)
                return false;
            var index = ms.TransientSettings.FindIndex(s => s.Track == iTrack);
            if (index < 0)
                return false;
            ms.TransientSettings.RemoveAt(index);
            return true;
        }, false));
        Register("Presets.ClearTransientModuleStrategies", (string presetName, string moduleTypeName) => IpcFrameworkGate.Get("Presets.ClearTransientModuleStrategies", () =>
        {
            var mt = Type.GetType(moduleTypeName);
            if (mt == null || !RotationModuleRegistry.Modules.TryGetValue(mt, out var md))
                return false;
            var ms = autorotation.Database.Presets.FindPresetByName(presetName)?.Modules.Find(m => m.Type == mt);
            if (ms == null)
                return false;
            ms.TransientSettings.Clear();
            return true;
        }, false));
        Register("Presets.ClearTransientPresetStrategies", (string presetName) => IpcFrameworkGate.Get("Presets.ClearTransientPresetStrategies", () =>
        {
            var preset = autorotation.Database.Presets.FindPresetByName(presetName);
            if (preset == null)
                return false;
            foreach (var ms in preset.Modules)
                ms.TransientSettings.Clear();
            return true;
        }, false));

        // 🔑 preset 名查找統一走 PresetDatabase.NameComparison（見該常數，跨外掛 IPC 契約的唯一比較器）。
        //    這裡兩側都 .Trim() 是 SetPreset 這個呼叫點的區域輸入正規化（去掉呼叫方傳來的 preset 名頭尾空白），
        //    與大小寫敏感度是兩件事——刻意保留在此呼叫點，不提進 canonical 比較器：那會回頭讓已出貨的
        //    Presets.Create/Delete/FindPresetByName 也開始 Trim，可能把本來區分得開的名字折在一起。
        Register("AI.SetPreset", (string name) => IpcFrameworkGate.Run("AI.SetPreset", () =>
        {
            ai.SetAIPreset(autorotation.Database.Presets.AllPresets.FirstOrDefault(x => x.Name.Trim().Equals(name.Trim(), PresetDatabase.NameComparison)));
            ai.NoteState(AIManager.SrcIpcSetPreset);
        }));
        Register("AI.GetPreset", () => ai.GetAIPreset);

        // 🔑 給「全艦隊急停」用（TCToolbox 一鍵關掉所有會自己動的外掛）：外部外掛要能直接開關 AI。
        //    走的是 /bmrai on|off 完全同一條路徑（AIManager.EnableConfig），不複製邏輯；
        //    關閉時 SwitchToIdle() 本身就會 Controller.Clear()，導航目標一併清掉，不必另外停。
        // 🔴 這是「改變外掛狀態」的端點（會 Dispose 生效中的循環模組、清導航、動 Autorot.Preset），
        //    一定要交回主執行緒；逾時回「呼叫前的狀態」，讓急停的呼叫端看得出來它沒生效。
        Register("AI.SetEnabled", (bool enable) =>
        {
            var before = ai.IsEnabled;
            return IpcFrameworkGate.Get("AI.SetEnabled", () =>
            {
                ai.EnableConfig(enable);
                ai.NoteState(AIManager.SrcIpcSetEnabled);
                var now = ai.IsEnabled;
                Service.Logger.Information($"[BMR IPC] AI 由 IPC {(enable ? "開啟" : "關閉")}，目前狀態：{(now ? "開啟" : "關閉")}。");
                return now;
            }, before);
        });
        // 唯讀且只是參考比較（Beh != null），照本檔慣例不包閘門。
        Register("AI.IsEnabled", () => ai.IsEnabled);

        // 暫停自動移動與閃避的租約（語意見 ExternalMovementHold）：
        //   SuppressMovement(owner, seconds, minHpRatio) -> bool：同一 owner 重送＝續期；秒數上限 15；owner 須是已載入插件的 InternalName。
        //   ReleaseMovementSuppression(owner) -> bool（有清到才 true）；IsMovementSuppressed() -> bool＝上一幀實際有沒有生效。
        // 表自帶鎖、每幀判定讀快照，所以同 Configuration.SetTransient* 不走 IpcFrameworkGate；呼叫端每秒續期，拒收的 log 自己節流。
        long suppressRejectLoggedAt = -60000;
        Register("AI.SuppressMovement", (string owner, float seconds, float minHpRatio) =>
        {
            var plugin = owner == null ? null : Service.PluginInterface.InstalledPlugins.FirstOrDefault(p => p.InternalName == owner);
            var ok = ExternalMovementHold.Hold(owner!, seconds, minHpRatio, Environment.TickCount64, () => plugin?.IsLoaded == true);
            var now = Environment.TickCount64;
            if (!ok && now - System.Threading.Interlocked.Read(ref suppressRejectLoggedAt) >= 60000)
            {
                System.Threading.Interlocked.Exchange(ref suppressRejectLoggedAt, now);
                Service.Logger.Information($"[BMR IPC] 拒絕 {owner} 的暫停自動移動要求（{seconds} 秒、血量門檻 {minHpRatio}）：擁有者不是已載入的插件，或秒數／門檻不是正常數值；60 秒內同類不再記。");
            }
            return ok;
        });
        Register("AI.ReleaseMovementSuppression", (string owner) => ExternalMovementHold.Release(owner) > 0);
        Register("AI.IsMovementSuppressed", () => ExternalMovementHold.Effective);
        _disposeActions += ExternalMovementHold.Reset;

        // 深宮「拉這隻會不會一次帶上太多隻」；快照由 AutoClear 在框架執行緒每幀發佈，端點只讀快照、不經 IpcFrameworkGate。
        Register("DeepDungeon.IsPullSafe", (ulong gameObjectId) => Global.DeepDungeon.DeepDungeonPullSafety.IsPullSafe(gameObjectId));
        // 深宮模組這一幀在一般層標記打斷、且「一般層只標記必要的打斷」開著；同上只讀快照。WrathCombo 用它決定要不要只打標記的讀條。
        Register("DeepDungeon.SelectiveInterruptActive", () => Global.DeepDungeon.DeepDungeonSelectiveInterruptState.IsActive());
    }

    public void Dispose() => _disposeActions?.Invoke();

    private void Register<TRet>(string name, Func<TRet> func)
    {
        var p = Service.PluginInterface.GetIpcProvider<TRet>("BossMod." + name);
        p.RegisterFunc(func);
        _disposeActions += p.UnregisterFunc;
    }

    private void Register<T1, TRet>(string name, Func<T1, TRet> func)
    {
        var p = Service.PluginInterface.GetIpcProvider<T1, TRet>("BossMod." + name);
        p.RegisterFunc(func);
        _disposeActions += p.UnregisterFunc;
    }

    private void Register<T1, T2, TRet>(string name, Func<T1, T2, TRet> func)
    {
        var p = Service.PluginInterface.GetIpcProvider<T1, T2, TRet>("BossMod." + name);
        p.RegisterFunc(func);
        _disposeActions += p.UnregisterFunc;
    }

    private void Register<T1, T2, T3, TRet>(string name, Func<T1, T2, T3, TRet> func)
    {
        var p = Service.PluginInterface.GetIpcProvider<T1, T2, T3, TRet>("BossMod." + name);
        p.RegisterFunc(func);
        _disposeActions += p.UnregisterFunc;
    }

    private void Register<T1, T2, T3, T4, TRet>(string name, Func<T1, T2, T3, T4, TRet> func)
    {
        var p = Service.PluginInterface.GetIpcProvider<T1, T2, T3, T4, TRet>("BossMod." + name);
        p.RegisterFunc(func);
        _disposeActions += p.UnregisterFunc;
    }

    private void Register<T1, T2, T3, T4, T5, TRet>(string name, Func<T1, T2, T3, T4, T5, TRet> func)
    {
        var p = Service.PluginInterface.GetIpcProvider<T1, T2, T3, T4, T5, TRet>("BossMod." + name);
        p.RegisterFunc(func);
        _disposeActions += p.UnregisterFunc;
    }

    //private void Register(string name, Action func)
    //{
    //    var p = Service.PluginInterface.GetIpcProvider<object>("BossMod." + name);
    //    p.RegisterAction(func);
    //    _disposeActions += p.UnregisterAction;
    //}

    private void Register<T1>(string name, Action<T1> func)
    {
        var p = Service.PluginInterface.GetIpcProvider<T1, object>("BossMod." + name);
        p.RegisterAction(func);
        _disposeActions += p.UnregisterAction;
    }
}
