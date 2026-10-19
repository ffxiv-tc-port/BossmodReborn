using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BossMod;

// Slidecast marker: draws the 'from here on moving no longer interrupts the cast' band onto the tail of the player's own cast bar.
// It lives in BossMod because the band position depends on CastTimeReductionTweak: with a reduction of R the window measured against the visible
// bar is (0.5 - R) wide. Both this and CalculateDesiredOrientation read R from CastTimeReductionTweak.ReductionSeconds, so they cannot disagree.
// The band spans the *predicted* window start to the end of the bar; its colour flips on the *measured* signal (the server's action effect for this cast).
// Rendering is a pure ImGui overlay: no game node is created or modified, the addon pointer is resolved fresh inside Draw, node access is field reads only.
public sealed unsafe class SlidecastMarkerTweak
{
    // the game's slidecast window, in seconds; same constant CalculateDesiredOrientation uses
    public const float SlidecastWindow = 0.5f;

    // node id of the cast bar's progress gauge inside the _CastBar addon; its parent's coordinate origin is the left edge of the bar and the bar
    // spans [0, GaugeWidth] there (taken from DailyRoutines' OptimizedCastBar, which reads the same node and positions against the same origin)
    private const uint GaugeNodeId = 11;
    private const float GaugeWidth = 160f;
    private const float GaugeFallbackHeight = 20f;
    private const int MaxNodeDepth = 32; // parent-chain walks are bounded so a corrupt chain cannot spin forever

    private const uint ColorPendingFill = 0x403030FF;
    private const uint ColorPendingEdge = 0xE04040FF;
    private const uint ColorReadyFill = 0x4030FF30;
    private const uint ColorReadyEdge = 0xE040FF40;

    private readonly ActionTweaksConfig _config = Service.Config.Get<ActionTweaksConfig>();
    private readonly CastTimeReductionTweak _castTimeTweak;
    private bool _loggedGeometry; // one-shot dump of the resolved bar rect, so the hardcoded gauge constants can be checked against a live client

    public SlidecastMarkerTweak(CastTimeReductionTweak castTimeTweak) => _castTimeTweak = castTimeTweak;

    public void Draw()
    {
        if (!_config.ShowSlidecastMarker)
            return;

        // 1. cast state - read from the player's own CastInfo, the exact same source CalculateDesiredOrientation uses, so the band and the movement
        // block are always derived from identical numbers. This is also what restricts the feature to the player: enemy casts live in their own
        // CastInfo and their own addons, neither of which is touched here.
        var gom = GameObjectManager.Instance();
        if (gom == null)
            return; // early login / zoning / title screen
        var player = (Character*)gom->Objects.IndexSorted[0].Value;
        if (player == null)
            return;
        var castInfo = player->GetCastInfo();
        if (castInfo == null || !castInfo->IsCasting)
            return; // not casting, or cast was interrupted/cancelled - nothing to draw, and nothing lingers because we draw nothing this frame

        var total = castInfo->TotalCastTime;
        if (total <= 0)
            return;

        // 2. where the window starts. ReductionSeconds returns 0 unless CastTimeReductionTweak actually shortened this very cast, so with the tweak
        // off this is exactly the plain 0.5s window.
        var window = SlidecastWindow - _castTimeTweak.ReductionSeconds(new((ActionType)castInfo->ActionType, castInfo->ActionId));
        if (window <= 0 || window >= total)
            return; // window swallowed by the reduction, or the whole cast is shorter than the window - nothing meaningful to point at
        var fraction = (total - window) / total;

        // 3. has the server already released this cast? The Response* fields are filled in when the action effect packet for this cast arrives,
        // which is the real start of the window; matching on SourceSequence keeps a previous cast's response from counting for this one.
        // Fall back to the time-based prediction only if the game did not stamp a sequence (should not happen for player-initiated casts).
        var ready = castInfo->SourceSequence != 0
            ? castInfo->ResponseSourceSequence == castInfo->SourceSequence
            : castInfo->CurrentCastTime >= total - window;

        // 4. geometry of the bar, resolved fresh every frame
        var addon = (AtkUnitBase*)Service.GameGui.GetAddonByName("_CastBar").Address;
        if (addon == null || !addon->IsVisible || addon->VisibilityFlags != 0)
            return;
        ref var uld = ref addon->UldManager;
        if (uld.LoadedState != AtkLoadState.Loaded || uld.NodeList == null)
            return;

        AtkResNode* gauge = null;
        for (var i = 0; i < uld.NodeListCount; ++i)
        {
            var node = uld.NodeList[i];
            if (node != null && node->NodeId == GaugeNodeId)
            {
                gauge = node;
                break;
            }
        }
        if (gauge == null)
            return; // gauge node not where we expect it - draw nothing rather than draw somewhere wrong

        var anchor = gauge->ParentNode; // bar-left origin; the gauge itself is resized as the cast progresses, its parent is not
        // visibility is checked on the parent chain, not on the gauge: a gauge node at 0% fill may legitimately hide itself, while its container
        // being hidden is the actual 'bar is not on screen' signal
        if (anchor == null || !ChainVisible(anchor))
            return;

        var scale = ChainScale(anchor);
        var width = GaugeWidth * scale.X;
        var height = (gauge->Height > 0 ? gauge->Height : GaugeFallbackHeight) * scale.Y;
        if (width <= 0 || height <= 0)
            return;

        var left = anchor->ScreenX;
        var top = anchor->ScreenY;
        var markerX = left + fraction * width;

        if (!_loggedGeometry)
        {
            _loggedGeometry = true;
            Service.Logger.Information($"[SlideMarker] gauge #{GaugeNodeId} {gauge->Width}x{gauge->Height}, parent #{anchor->NodeId} {anchor->Width}x{anchor->Height} @ ({left:f1},{top:f1}), scale {scale.X:f3}x{scale.Y:f3}, addon scale {addon->Scale:f3} -> bar {width:f1}x{height:f1}");
        }

        // 5. draw. Same full-screen no-input overlay pattern Camera.DrawWorldPrimitives uses, so the marker lands in the main viewport regardless of
        // ImGui viewport settings; opened only on frames that actually have something to show.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(new Vector2(0, 0));
        ImGui.Begin("bmr_slidecast_marker", ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowSize(ImGui.GetIO().DisplaySize);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(new(markerX, top), new(left + width, top + height), ready ? ColorReadyFill : ColorPendingFill);
        dl.AddLine(new(markerX, top), new(markerX, top + height), ready ? ColorReadyEdge : ColorPendingEdge, 2f);

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private static Vector2 ChainScale(AtkResNode* node)
    {
        var scale = Vector2.One;
        for (var i = 0; node != null && i < MaxNodeDepth; ++i, node = node->ParentNode)
            scale *= new Vector2(node->ScaleX, node->ScaleY);
        return scale;
    }

    private static bool ChainVisible(AtkResNode* node)
    {
        for (var i = 0; node != null && i < MaxNodeDepth; ++i, node = node->ParentNode)
            if ((node->NodeFlags & NodeFlags.Visible) == 0)
                return false;
        return true;
    }
}
