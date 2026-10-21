using Dalamud.Bindings.ImGui;

namespace BossMod;

// 沒有專屬模組的討伐戰用的雷達：畫 AI 這一幀用的尋路範圍、敵我位置、AIHintsBuilder 從讀條猜出來的危險區。
// 契約：只讀 AIHints／AIHintsBuilder，不寫回，也不改它們持有的邊界實例（顯示用的邊界自己建）。
// 只在主執行緒、AIHintsBuilder.Update 之後呼叫；Draw 只在 ShouldShow 為 true 的那一幀呼叫。
public sealed class GenericTrialRadar
{
    private const uint ContentTypeTrial = 4; // ContentFinderCondition.ContentType：討伐殲滅戰
    private static readonly BossModuleConfig _config = Service.Config.Get<BossModuleConfig>();
    private static readonly AI.AIConfig _aiConfig = Service.Config.Get<AI.AIConfig>();
    private static readonly List<RelTriangle> _noTriangles = [];

    private sealed class Zone
    {
        public AOEShape? Shape;
        public WPos Origin;
        public WPos End;
        public Angle Rotation;
        public int Generation;
        public int Frame;
        public List<RelTriangle>? Triangles; // null＝這個形狀沒有免配置的畫法，改走 AOEShape.Draw
    }

    private readonly WorldState _ws;
    private readonly AIHints _hints;
    private readonly AIHintsBuilder _builder;
    private readonly MiniArena _arena;
    private readonly Dictionary<ulong, Zone> _zones = []; // 鍵＝施放者；三角化結果留到形狀、位置或場地變了才重算
    private readonly List<WDir> _border = []; // 場地外框各輪廓的頂點（相對顯示中心），依 _borderEnds 分段
    private readonly List<int> _borderEnds = [];

    // 資格快取：副本或成熟度門檻變了才重查
    private ushort _cfc;
    private BossModuleInfo.Maturity _minMaturity;
    private bool _eligible;
    private bool _faulted;

    // 顯示邊界的來源快照：任何一項變了才重建
    private Bitmap? _srcBitmap;
    private float _srcHalfWidth = -1f;
    private float _srcHalfHeight;
    private Angle _srcRotation;
    private bool _walls; // 外框是障礙物圖的牆（多邊形），不是尋路範圍的方框
    private WDir _centerOffset; // 顯示中心相對尋路中心
    private float _screenHalfSize;
    private int _generation;
    private int _frame;

    public GenericTrialRadar(WorldState ws, AIHints hints, AIHintsBuilder builder)
    {
        _ws = ws;
        _hints = hints;
        _builder = builder;
        _arena = new(default, new ArenaBoundsSquare(1f));
    }

    // 在討伐戰裡、這個副本沒有會載入的模組、而且還有東西可看（戰鬥中或場上有敵人）
    public bool ShouldShow()
    {
        var cfc = _ws.CurrentCFCID;
        var minMaturity = _config.MinMaturity;
        if (cfc != _cfc || minMaturity != _minMaturity)
        {
            _cfc = cfc;
            _minMaturity = minMaturity;
            _faulted = false;
            _eligible = cfc != 0 && IsTrialWithoutModule(cfc, minMaturity);
            if (_eligible)
                Service.Logger.Information($"[GenericTrialRadar] 副本 {cfc} 是討伐戰而且沒有會載入的模組：場上有敵人或戰鬥中時顯示通用雷達。");
        }
        if (!_eligible || _faulted)
            return false;
        var player = _ws.Party.Player();
        return player != null && (player.InCombat || _hints.PotentialTargets.Count != 0);
    }

    public static bool IsTrialWithoutModule(uint cfc, BossModuleInfo.Maturity minMaturity)
    {
        try
        {
            if (Service.LuminaRow<Lumina.Excel.Sheets.ContentFinderCondition>(cfc)?.ContentType.RowId != ContentTypeTrial)
                return false;
            foreach (var info in BossModuleRegistry.RegisteredModules.Values)
                if (info.GroupID == cfc && info.Maturity >= minMaturity && info.GroupType is BossModuleInfo.GroupType.CFC or BossModuleInfo.GroupType.RemovedUnreal
                    && !info.ModuleType.IsSubclassOf(typeof(UnverifiedOidTrialModule))) // 推錯的 OID 永遠不載入，不能因此讓位；推對時模組載入就會蓋過通用雷達
                    return false;
            return true;
        }
        catch (Exception ex)
        {
            Service.Logger.Error(ex, $"[GenericTrialRadar] 查不到副本 {cfc} 的類型，這個副本不顯示通用雷達。");
            return false;
        }
    }

    public void Draw(Angle cameraAzimuth)
    {
        var pc = _ws.Party.Player();
        if (pc == null || _faulted)
            return;
        try
        {
            UpdateArena();
        }
        catch (Exception ex)
        {
            Fault(ex);
            return;
        }
        _arena.Begin(cameraAzimuth);
        try
        {
            DrawContents(pc);
        }
        catch (Exception ex)
        {
            ImGui.GetWindowDrawList().PathClear();
            Fault(ex);
        }
        finally
        {
            MiniArena.End();
        }
    }

    // 畫壞一次就整個副本不再畫（ShouldShow 回 false、視窗關掉），換副本才重試
    private void Fault(Exception ex)
    {
        _faulted = true;
        Service.Logger.Error(ex, $"[GenericTrialRadar] 副本 {_cfc} 的通用雷達繪製失敗，這個副本不再顯示。");
    }

    private void UpdateArena()
    {
        var src = _hints.PathfindMapBounds;
        var obstacles = _hints.PathfindMapObstacles;
        // 整張圖都在尋路視窗裡（視窗不跟著玩家捲動）才把牆畫出來；刻意不畫會捲動的大圖：每走一格就要重建一次外框
        var bitmap = _aiConfig.DisableObstacleMaps ? null : obstacles.Bitmap;
        if (bitmap != null && (obstacles.Rect.Left > 0 || obstacles.Rect.Top > 0 || obstacles.Rect.Right < bitmap.Width || obstacles.Rect.Bottom < bitmap.Height))
            bitmap = null;
        var rect = src as ArenaBoundsRect;
        var halfWidth = rect?.HalfWidth ?? src.Radius;
        var halfHeight = rect?.HalfHeight ?? src.Radius;
        var rotation = rect?.Rotation ?? default;
        if (!ReferenceEquals(bitmap, _srcBitmap) || halfWidth != _srcHalfWidth || halfHeight != _srcHalfHeight || rotation != _srcRotation)
        {
            _srcBitmap = bitmap;
            _srcHalfWidth = halfWidth;
            _srcHalfHeight = halfHeight;
            _srcRotation = rotation;
            RebuildBounds(bitmap, obstacles.Rect, src.MapResolution);
        }
        var center = _hints.PathfindMapCenter + _centerOffset;
        if (_arena.Center != center)
        {
            _arena.Center = center;
            ++_generation;
        }
        if (_screenHalfSize != _arena.ScreenHalfSize)
        {
            _screenHalfSize = _arena.ScreenHalfSize;
            ++_generation;
        }
    }

    private void RebuildBounds(Bitmap? bitmap, Bitmap.Rect view, float resolution)
    {
        ++_generation;
        _border.Clear();
        _borderEnds.Clear();
        if (bitmap != null && _srcRotation == default && BuildWalkableArea(bitmap, view, resolution) is (var poly, var radius, var offset))
        {
            _walls = true;
            _centerOffset = offset;
            _arena.Bounds = new ArenaBoundsCustom(radius, poly);
            var parts = poly.Parts;
            for (var i = 0; i < parts.Count; ++i)
            {
                var part = parts[i];
                AddBorderContour(part.Exterior);
                var holes = part.Holes;
                for (var j = 0; j < holes.Length; ++j)
                    AddBorderContour(part.Interior(holes[j]));
            }
            return;
        }
        _walls = false;
        _centerOffset = default;
        var bounds = new ArenaBoundsRect(_srcHalfWidth, _srcHalfHeight, _srcRotation);
        _arena.Bounds = bounds;
        AddBorderContour(CurveApprox.Rect(bounds.Orientation, _srcHalfWidth, _srcHalfHeight));
    }

    private void AddBorderContour(ReadOnlySpan<WDir> contour)
    {
        _border.AddRange(contour);
        _borderEnds.Add(_border.Count);
    }

    // 尋路視窗裡沒被障礙物圖擋住的格子 → 多邊形（相對於這些格子外接矩形的中心）。圖外的格子可走，與 AIHints.InitPathfindMap 一致。
    // 視窗裡沒有被擋的格子、或全部被擋時回 null（退回畫方框）。
    private static (RelSimplifiedComplexPolygon poly, float radius, WDir offset)? BuildWalkableArea(Bitmap bitmap, Bitmap.Rect view, float resolution)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        var blocked = false;
        for (var y = view.Top; y < view.Bottom; ++y)
        {
            for (var x = view.Left; x < view.Right; ++x)
            {
                if (Blocked(bitmap, x, y))
                {
                    blocked = true;
                    continue;
                }
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }
        if (!blocked || maxX < minX)
            return null;

        // 每一列的每一段連續可走格各加一個矩形，交給 clipper 聯集
        var originX = (minX + maxX + 1) * 0.5f;
        var originY = (minY + maxY + 1) * 0.5f;
        var operand = new PolygonClipper.Operand();
        Span<WDir> quad = stackalloc WDir[4];
        for (var y = minY; y <= maxY; ++y)
        {
            var z0 = (y - originY) * resolution;
            var z1 = z0 + resolution;
            for (var x = minX; x <= maxX; ++x)
            {
                if (Blocked(bitmap, x, y))
                    continue;
                var start = x;
                while (x < maxX && !Blocked(bitmap, x + 1, y))
                    ++x;
                var x0 = (start - originX) * resolution;
                var x1 = (x + 1 - originX) * resolution;
                quad[0] = new(x0, z0);
                quad[1] = new(x1, z0);
                quad[2] = new(x1, z1);
                quad[3] = new(x0, z1);
                operand.AddContour((ReadOnlySpan<WDir>)quad);
            }
        }
        var poly = new PolygonClipper().Simplify(operand);
        if (poly.Parts.Count == 0)
            return null;
        var radius = Math.Max(maxX - minX + 1, maxY - minY + 1) * 0.5f * resolution;
        var viewCenterX = (view.Left + view.Right) * 0.5f;
        var viewCenterY = (view.Top + view.Bottom) * 0.5f;
        return (poly, radius, new WDir(originX - viewCenterX, originY - viewCenterY) * resolution);
    }

    private static bool Blocked(Bitmap bitmap, int x, int y) => x >= 0 && y >= 0 && x < bitmap.Width && y < bitmap.Height && bitmap[x, y];

    private void DrawContents(Actor pc)
    {
        ++_frame;
        var risk = DrawGuessedAOEs(pc);
        foreach (var gaze in _builder.GuessedGazes)
            risk |= GazeState(pc, gaze.Caster, gaze.Target, gaze.Shape, out _) == GazeFacing;

        if (_config.ShowBorder)
            DrawBorder(risk && _config.ShowBorderRisk ? Colors.Enemy : Colors.Border);
        if (_config.ShowCardinals)
            _arena.CardinalNames();
        if (_config.ShowWaymarks)
            BossModule.DrawWaymarks(_arena, _ws);

        DrawPartyMembers();

        foreach (var gaze in _builder.GuessedGazes)
        {
            var state = GazeState(pc, gaze.Caster, gaze.Target, gaze.Shape, out var eye);
            if (state == GazeNone)
                continue;
            // 與模組的凝視畫法（Components.GenericGaze）相同：眼睛圖示，玩家在範圍內時再畫出玩家面向的那一段弧
            Components.GenericGaze.DrawEye(EyeScreenPos(eye), state == GazeFacing);
            if (state == GazeOutOfRange)
                continue;
            _arena.PathArcTo(pc.Position, 1f, (pc.Rotation - 45f.Degrees()).Rad, (pc.Rotation + 45f.Degrees()).Rad);
            MiniArena.PathStroke(false, Colors.Enemy);
        }

        var targets = _hints.PotentialTargets;
        var count = targets.Count;
        if (_config.ShowMeleeRangeIndicator && _config.MeleeRangeIndicatorOnTarget)
            BossModule.DrawMeleeRangeOnTarget(_arena, _ws, pc);
        else if (_config.ShowMeleeRangeIndicator)
        {
            for (var i = 0; i < count; ++i)
            {
                var a = targets[i].Actor;
                if (a.InCombat)
                {
                    _arena.ZoneDonut(a.Position, a.HitboxRadius + 2.6f, a.HitboxRadius + 2.9f, Colors.MeleeRangeIndicator);
                    break;
                }
            }
        }
        for (var i = 0; i < count; ++i)
            DrawActor(targets[i].Actor, Colors.Enemy, false);
        DrawActor(pc, Colors.PC, true);
    }

    // 回傳玩家是否站在任何一個猜出來的範圍裡。形狀與位置的取法逐項對齊 AIHintsBuilder.CalculateAutoHints。
    private bool DrawGuessedAOEs(Actor pc)
    {
        var risk = false;
        var live = 0;
        foreach (var aoe in _builder.GuessedAOEs)
        {
            if (aoe.Caster.CastInfo is not { } cast)
                continue;
            ++live;
            var isCharge = aoe.IsCharge;
            var origin = isCharge ? aoe.Caster.Position.Quantized() : cast.LocXZ;
            var end = isCharge ? cast.LocXZ : default;
            var rotation = cast.Rotation;
            ref var zone = ref CollectionsMarshal.GetValueRefOrAddDefault(_zones, aoe.Caster.InstanceID, out _);
            zone ??= new();
            if (zone.Generation != _generation || !ReferenceEquals(zone.Shape, aoe.Shape) || zone.Origin != origin || zone.End != end || zone.Rotation != rotation)
            {
                zone.Shape = aoe.Shape;
                zone.Origin = origin;
                zone.End = end;
                zone.Rotation = rotation;
                zone.Generation = _generation;
                zone.Triangles = Triangulate(aoe.Shape, origin, end, rotation, isCharge);
            }
            zone.Frame = _frame;
            if (zone.Triangles != null)
                _arena.Zone(zone.Triangles);
            else
                aoe.Shape.Draw(_arena, origin, rotation);
            risk |= isCharge ? pc.Position.InRect(origin, end, ((AOEShapeRect)aoe.Shape).HalfWidth) : aoe.Shape.Check(pc.Position, origin, rotation);
        }
        if (_zones.Count > live)
            foreach (var (id, zone) in _zones)
                if (zone.Frame != _frame)
                    _zones.Remove(id);
        return risk;
    }

    private List<RelTriangle>? Triangulate(AOEShape shape, WPos origin, WPos end, Angle rotation, bool isCharge)
    {
        var bounds = _arena.Bounds;
        var offset = origin - _arena.Center;
        if (isCharge)
            return (end - origin).LengthSq() < 0.01f ? _noTriangles : bounds.ClipAndTriangulateRect(offset, end - _arena.Center, ((AOEShapeRect)shape).HalfWidth);
        return shape switch
        {
            AOEShapeCircle s => bounds.ClipAndTriangulateCircle(offset, s.Radius),
            AOEShapeCone s => bounds.ClipAndTriangulateCone(offset, default, s.Radius, rotation + s.DirectionOffset, s.HalfAngle),
            AOEShapeDonut s => bounds.ClipAndTriangulateDonut(offset, s.InnerRadius, s.OuterRadius),
            AOEShapeRect s => bounds.ClipAndTriangulateRect(offset, rotation + s.DirectionOffset, s.LengthFront, s.LengthBack, s.HalfWidth),
            AOEShapeCross s => TriangulateCross(bounds, offset, (rotation + s.DirectionOffset).ToDirection(), s.Length, s.HalfWidth),
            _ => null
        };
    }

    // 十字的 12 個頂點，順序與 AOEShapeCross 的輪廓相同
    private static List<RelTriangle> TriangulateCross(ArenaBounds bounds, WDir offset, WDir dx, float length, float halfWidth)
    {
        var dy = dx.OrthoL();
        WDir xl = dx * length, xw = dx * halfWidth, yl = dy * length, yw = dy * halfWidth;
        Span<WDir> contour =
        [
            offset + xl - yw, offset + xw - yw, offset + xw - yl, offset - xw - yl, offset - xw - yw, offset - xl - yw,
            offset - xl + yw, offset - xw + yw, offset - xw + yl, offset + xw + yl, offset + xw + yw, offset + xl + yw
        ];
        return bounds.ClipAndTriangulate((ReadOnlySpan<WDir>)contour);
    }

    private const int GazeNone = 0, GazeOutOfRange = 1, GazeInRange = 2, GazeFacing = 3;

    // 眼睛位置與「在不在範圍內」的判定和 CalculateAutoHints 加禁止面向的條件相同；GazeNone＝讀條已結束
    private static int GazeState(Actor pc, Actor caster, Actor? target, AOEShape shape, out WPos eye)
    {
        eye = default;
        if (caster.CastInfo is not { } cast)
            return GazeNone;
        eye = target?.Position ?? cast.LocXZ;
        if (!shape.Check(pc.Position, eye, cast.Rotation))
            return GazeOutOfRange;
        return Components.GenericGaze.HitByEye(ref pc, new(eye)) ? GazeFacing : GazeInRange;
    }

    private Vector2 EyeScreenPos(WPos eye)
    {
        if (_arena.InBounds(eye))
            return _arena.WorldPositionToScreenPosition(eye);
        var dir = (eye - _arena.Center).Normalized();
        return _arena.ScreenCenter + _arena.RotatedCoords(dir.ToVec2()) * (_arena.ScreenHalfSize + _arena.ScreenMarginSize * 0.5f);
    }

    private void DrawBorder(uint color)
    {
        var dl = ImGui.GetWindowDrawList();
        var center = _arena.Center;
        var start = 0;
        var contours = _borderEnds.Count;
        for (var i = 0; i < contours; ++i)
        {
            var end = _borderEnds[i];
            for (var j = start; j < end; ++j)
                dl.PathLineTo(_arena.WorldPositionToScreenPosition(center + _border[j]));
            dl.PathStroke(color, ImDrawFlags.Closed, 2f);
            start = end;
        }
    }

    // 沒有元件能判斷誰和機制有關，所以隊友一律照「無關」處理：只在設定要求顯示時才畫，顏色規則與模組雷達相同
    private void DrawPartyMembers()
    {
        var showAll = _config.ShowIrrelevantPlayers;
        var focusId = _config.ShowFocusTargetPlayer || showAll ? _ws.Client.FocusTargetId : 0;
        if (!showAll && focusId == 0)
            return;
        for (var slot = 0; slot < PartyState.MaxAllies; ++slot)
        {
            var player = slot != PartyState.PlayerSlot ? _ws.Party[slot] : null;
            if (player == null || player.IsDead)
                continue;
            var isFocus = focusId != 0 && player.InstanceID == focusId;
            if (!showAll && !isFocus)
                continue;
            var color = Colors.PlayerGeneric;
            if (isFocus)
                color = Colors.Focus;
            else if (_config.ColorPlayersBasedOnRole)
                color = player.ClassCategory switch
                {
                    ClassCategory.Tank => Colors.Tank,
                    ClassCategory.Healer => Colors.Healer,
                    ClassCategory.Melee => Colors.Melee,
                    ClassCategory.Caster => Colors.Caster,
                    ClassCategory.PhysRanged => Colors.PhysRanged,
                    _ => color
                };
            DrawActor(player, color, false);
        }
    }

    // 與 MiniArena.Actor 相同，只是牆外的角色貼到最近的外框上時不經過 ArenaBoundsCustom.ClampToBounds（它每次呼叫都會配置）
    private void DrawActor(Actor actor, uint color, bool allowDeadAndUntargetable)
    {
        if (actor.IsDestroyed || !allowDeadAndUntargetable && (!actor.IsTargetable || actor.IsDead))
            return;
        var position = actor.Position;
        if (_arena.InBounds(position))
            _arena.ActorInsideBounds(position, actor.Rotation, color);
        else
            _arena.ActorOutsideBounds(_walls ? ClampToBorder(position) : _arena.ClampToBounds(position), actor.Rotation, color);
    }

    private WPos ClampToBorder(WPos position)
    {
        var p = position - _arena.Center;
        var best = p;
        var bestDistSq = float.MaxValue;
        var start = 0;
        var contours = _borderEnds.Count;
        for (var i = 0; i < contours; ++i)
        {
            var end = _borderEnds[i];
            var a = _border[end - 1];
            for (var j = start; j < end; ++j)
            {
                var b = _border[j];
                var ab = b - a;
                var lenSq = ab.LengthSq();
                var t = lenSq > 0f ? Math.Clamp((p - a).Dot(ab) / lenSq, 0f, 1f) : 0f;
                var q = a + t * ab;
                var distSq = (q - p).LengthSq();
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = q;
                }
                a = b;
            }
            start = end;
        }
        return _arena.Center + best;
    }
}
