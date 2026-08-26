using FFXIVClientStructs.FFXIV.Client.Game.Group;

namespace BossMod;

internal static class ClientStructsEx
{
    public static bool IsValidAllianceMember(this PartyMember member) => (member.Flags & 1) != 0;
}

// PlayerMove 是 Character 的別名 view;Size 對齊 pin 版 Character(7.15 的 0x22E0 已過期)。
[StructLayout(LayoutKind.Explicit, Size = 0x2360)]
internal unsafe partial struct PlayerMove
{
    [FieldOffset(0x1E0)] public MoveContainer Move;
}

[StructLayout(LayoutKind.Explicit, Size = 0x430)]
internal unsafe partial struct MoveContainer
{
    [StructLayout(LayoutKind.Explicit, Size = 0x88)]
    public unsafe partial struct InterpolationState
    {
        [FieldOffset(0x10)] public float DesiredRotation;
        [FieldOffset(0x14)] public float OriginalRotation;
        [FieldOffset(0x40)] public bool RotationInterpolationInProgress;
    }

    // 7.15 時是 0x1C0,7.3 世代(TC 7.20)起改為 0x1D0(同上游 main 的修正)。
    // ActionManagerEx.FaceDirection 會往這裡「寫入」DesiredRotation,
    // 位移錯誤等於每次轉向都往 Character 結構的錯誤位置寫 float。
    [FieldOffset(0x1D0)] public InterpolationState Interpolation;
}
