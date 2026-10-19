namespace BossMod.Util;

// TC 7.20: 對齊 FFXIVClientStructs.FFXIV.Client.Game.Camera(pin 版)。
// 舊值(Size 0x2B0 / DirH 0x130)是 7.15 時期的配置,7.20 起整批往後移 0x10,
// 0x130 現在是 FoV,照舊值讀會拿到視角而不是方位角。
[StructLayout(LayoutKind.Explicit, Size = 0x2C0)]
public unsafe struct CameraEx
{
    [FieldOffset(0x140)] public float DirH; // 0 is north, increases CW
    [FieldOffset(0x144)] public float DirV; // 0 is horizontal, positive is looking up, negative looking down
    [FieldOffset(0x148)] public float InputDeltaHAdjusted;
    [FieldOffset(0x14C)] public float InputDeltaVAdjusted;
    [FieldOffset(0x150)] public float InputDeltaH;
    [FieldOffset(0x154)] public float InputDeltaV;
    [FieldOffset(0x158)] public float DirVMin; // -85deg by default
    [FieldOffset(0x15C)] public float DirVMax; // +45deg by default
}
