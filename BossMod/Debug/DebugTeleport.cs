using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using BossMod.Util;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace BossMod;

sealed class DebugTeleport
{
    private bool EnableNoClip;
    private float NoClipSpeed = 0.001f;
    private Vector3 inputCoordinates;

    public unsafe void Draw()
    {
        ImGui.BeginGroup();
        ImGui.Checkbox("No Clip", ref EnableNoClip);
        if (EnableNoClip)
        {
            Enable();
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            ImGui.InputFloat("No Clip Speed", ref NoClipSpeed, 0.001f);
        }
        else
        {
            Disable();
        }
        ImGui.Separator();
        ImGui.EndGroup();
        ImGui.BeginGroup();
        ImGui.Text("Current Player Coordinates:");
        ImGui.Text("X: " + PlayerEx.Position.X.ToString("F3"));
        ImGui.Text("Y: " + PlayerEx.Position.Y.ToString("F3"));
        ImGui.Text("Z: " + PlayerEx.Position.Z.ToString("F3"));
        ImGui.EndGroup();
        ImGui.Separator();
        ImGui.BeginGroup();
        ImGui.Text("Enter Target Coordinates:");
        if (ImGui.Button("Set Position"))
        {
            SetPlayerPosition(inputCoordinates);
        }
        ImGui.SetNextItemWidth(150);
        ImGui.InputFloat("X Coordinate", ref inputCoordinates.X, 1f);
        ImGui.SetNextItemWidth(150);
        ImGui.InputFloat("Y Coordinate", ref inputCoordinates.Y, 1f);
        ImGui.SetNextItemWidth(150);
        ImGui.InputFloat("Z Coordinate", ref inputCoordinates.Z, 1f);
        ImGui.EndGroup();
    }

    private void SetPlayerPosition(Vector3 position)
    {
        try
        {
            if (Service.ObjectTable.LocalPlayer != null)
            {
                // Assuming PlayerEx.SetPosition accepts a Vector3
                PlayerEx.SetPosition = position;
                Service.Log($"Player position set to: X = {position.X}, Y = {position.Y}, Z = {position.Z}");
            }
            else
            {
                Service.Log("LocalPlayer is null. Unable to set position.");
            }
        }
        catch (Exception ex)
        {
            Service.Log($"An error occurred while setting position: {ex.Message}");
        }
    }

    private void Enable()
    {
        Service.Framework.Update += OnUpdate;
    }

    private void Disable()
    {
        Service.Framework.Update -= OnUpdate;
    }

    private unsafe void OnUpdate(IFramework framework)
    {
        if (!EnableNoClip)
            return;

        // 🔴 Client.System.Framework.Framework 的 Instance() 是 [StaticAddress(…, isPointer: true)]
        //    ——回傳的是全域指標槽的**內容**，遊戲還沒建好（或已拆掉）Framework 時合法為 null。
        //    拿不到就當「這幀不處理」，不要解參考。
        var fwk = Framework.Instance();
        if (fwk == null || fwk->WindowInactive)
            return;

        if (Service.KeyState.GetRawValue(VirtualKey.SPACE) != 0 || Utils.IsKeyPressed(LimitedKeys.Space))
        {
            Service.KeyState.SetRawValue(VirtualKey.SPACE, 0);
            PlayerEx.SetPosition = (PlayerEx.Object.Position.X, PlayerEx.Object.Position.Y + NoClipSpeed, PlayerEx.Object.Position.Z).ToVector3();
        }
        if (Service.KeyState.GetRawValue(VirtualKey.LSHIFT) != 0 || Utils.IsKeyPressed(LimitedKeys.LeftShiftKey))
        {
            Service.KeyState.SetRawValue(VirtualKey.LSHIFT, 0);
            PlayerEx.SetPosition = (PlayerEx.Object.Position.X, PlayerEx.Object.Position.Y - NoClipSpeed, PlayerEx.Object.Position.Z).ToVector3();
        }

        // 🔴 水平移動要用相機朝向，而 PlayerEx.CameraEx 走的是 GetActiveCamera()，沒有作用中
        //    相機時合法回 null（見 Util/PlayerEx.cs 的說明）。原本四個分支各自直接 ->DirH，
        //    null 時就是 AccessViolation。這裡同一幀只取一次並判空；拿不到相機就這幀不水平移動
        //    （上下移動不需要相機，已在上面處理完）。
        var cameraEx = PlayerEx.CameraEx;
        if (cameraEx == null)
            return;

        if (Service.KeyState.GetRawValue(VirtualKey.W) != 0 || Utils.IsKeyPressed(LimitedKeys.W))
        {
            var newPoint = Utils.RotatePoint(PlayerEx.Object.Position.X, PlayerEx.Object.Position.Z, MathF.PI - cameraEx->DirH, PlayerEx.Object.Position + new Vector3(0, 0, NoClipSpeed));
            Service.KeyState.SetRawValue(VirtualKey.W, 0);
            PlayerEx.SetPosition = newPoint;
        }
        if (Service.KeyState.GetRawValue(VirtualKey.S) != 0 || Utils.IsKeyPressed(LimitedKeys.S))
        {
            var newPoint = Utils.RotatePoint(PlayerEx.Object.Position.X, PlayerEx.Object.Position.Z, MathF.PI - cameraEx->DirH, PlayerEx.Object.Position + new Vector3(0, 0, -NoClipSpeed));
            Service.KeyState.SetRawValue(VirtualKey.S, 0);
            PlayerEx.SetPosition = newPoint;
        }
        if (Service.KeyState.GetRawValue(VirtualKey.A) != 0 || Utils.IsKeyPressed(LimitedKeys.A))
        {
            var newPoint = Utils.RotatePoint(PlayerEx.Object.Position.X, PlayerEx.Object.Position.Z, MathF.PI - cameraEx->DirH, PlayerEx.Object.Position + new Vector3(NoClipSpeed, 0, 0));
            Service.KeyState.SetRawValue(VirtualKey.A, 0);
            PlayerEx.SetPosition = newPoint;
        }
        if (Service.KeyState.GetRawValue(VirtualKey.D) != 0 || Utils.IsKeyPressed(LimitedKeys.D))
        {
            var newPoint = Utils.RotatePoint(PlayerEx.Object.Position.X, PlayerEx.Object.Position.Z, MathF.PI - cameraEx->DirH, PlayerEx.Object.Position + new Vector3(-NoClipSpeed, 0, 0));
            Service.KeyState.SetRawValue(VirtualKey.D, 0);
            PlayerEx.SetPosition = newPoint;
        }
    }
}

