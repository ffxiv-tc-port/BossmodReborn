using System;
using System.Runtime.InteropServices;

namespace BossMod;

// 本檔只吃「程式碼位元組＋掃描結果」（另加 kernel32 查 .pdata），不依賴 Dalamud／CS，離線驗證直接編譯同一份原始碼。
// 失敗一律擲 InvalidOperationException；呼叫端在插件建構子的同步路徑，擲出＝插件載入失敗。
internal static unsafe partial class NativeLayoutGate
{
    /// <summary>回傳 [address, address+length) 的程式碼位元組；超出 .text 的部分截掉（可能比 length 短）。</summary>
    public delegate ReadOnlySpan<byte> CodeReader(nint address, int length);

    /// <summary>
    /// 轉向內插收尾：旗標非零時把「移動容器+Y」當角度交給 GameObject.SetRotation(owner＝[容器+O])。
    /// </summary>
    internal const string RotationApplySignature = "40 38 B8 ?? ?? ?? ?? 74 ?? F3 0F 10 88 ?? ?? ?? ?? 48 8B 88 ?? ?? ?? ?? E8";
    private const int RotationApplyLength = 29;

    /// <summary>
    /// Character 初始化：以 (this+X, this) 初始化移動容器，緊接著以 (this+N, this) 初始化下一個子物件。
    /// </summary>
    internal const string CharacterInitSignature = "48 8D 8B ?? ?? ?? ?? 48 8B D3 E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? 48 8B D3 E8";
    private const int CharacterInitLength = 25;

    /// <summary>
    /// 推導 Character 內「轉向內插目標角度」的位移（＝移動容器位置 X ＋ 容器內 DesiredRotation 位置 Y）。
    /// </summary>
    /// <remarks>
    /// 每一項都要從遊戲碼成立：收尾點唯一且呼叫的正是 CS 的 GameObject.SetRotation；
    /// 初始化點中，被呼叫的容器初始化函式會寫入同一個 owner 欄位 O 的恰好一處；
    /// 推得的寫入位置落在容器範圍內（X+Y+4 ≤ N）。刻意不比對 CS 的 Character 版面：只驗跟這個欄位直接相關的碼。
    /// </remarks>
    internal static int DeriveDesiredRotationOffset(Func<string, nint[]> scanAll, CodeReader read, Func<nint, int> functionLength,
        nint setRotation, out string evidence)
    {
        var apply = scanAll(RotationApplySignature);
        if (apply.Length != 1)
            throw Fail($"轉向內插收尾點應唯一命中，實得 {apply.Length} 個");
        var a = ReadExact(read, apply[0], RotationApplyLength);
        var desired = I32(a, 13);
        var owner = I32(a, 20);
        var applyCallee = apply[0] + RotationApplyLength + I32(a, 25);
        if (setRotation == 0 || applyCallee != setRotation)
            throw Fail($"轉向內插收尾點呼叫的是 0x{applyCallee:X}，不是 GameObject.SetRotation（0x{setRotation:X}）");

        int container = 0, next = 0, matched = 0;
        nint init = 0;
        var initHits = scanAll(CharacterInitSignature);
        foreach (var hit in initHits)
        {
            var c = ReadExact(read, hit, CharacterInitLength);
            var callee = hit + 15 + I32(c, 11);
            var calleeLength = functionLength(callee);
            if (calleeLength <= 0 || !StoresQwordAt(read(callee, calleeLength), owner))
                continue;
            ++matched;
            container = I32(c, 3);
            next = I32(c, 18);
            init = callee;
        }
        if (matched != 1)
            throw Fail($"會寫入 owner 欄位 +0x{owner:X} 的移動容器初始化點應唯一，{initHits.Length} 個候選中符合 {matched} 個");

        // next 上限 64 KB（Character 遠小於此）兼擋整數溢位：通過後 container+desired 必在 int 範圍內。
        if (desired <= 0 || (desired & 3) != 0 || container <= 0 || owner <= 0 || next <= 0 || next > 0x10000
            || (long)container + desired + 4 > next || (long)container + owner + 8 > next)
            throw Fail($"推得的位置超出移動容器範圍：容器 0x{container:X}、目標角度 +0x{desired:X}、owner +0x{owner:X}、下一個子物件 0x{next:X}");

        evidence = $"Character+0x{container + desired:X}（移動容器 0x{container:X} ＋ 目標角度 0x{desired:X}；owner +0x{owner:X}；下一個子物件 0x{next:X}；收尾點 0x{apply[0]:X}、容器初始化 0x{init:X}）";
        return container + desired;
    }

    /// <summary>
    /// 走路輸入的附加取樣閘門：<c>cmp byte [base+X], r8b..r15b</c> 或 <c>cmp byte [base+X], 0</c>，緊接 <c>je rel32</c>（各 13 bytes）。
    /// </summary>
    internal static readonly string[] SpinningGateSignatures = ["44 38 ?? ?? ?? ?? ?? 0F 84", "80 ?? ?? ?? ?? ?? 00 0F 84"];
    private const int SpinningGateLength = 13;
    private const int SpinningCallWindow = 0x60;

    /// <summary>
    /// 推導走路輸入函式 self 內「旋轉移動」旗標的位移 X。
    /// </summary>
    /// <remarks>
    /// 成立條件：恰好一個閘門的 je 跳過的區段內有對 RMIWalk 的呼叫，呼叫前把第 7 引數設成 1（<c>mov byte [rsp+30h],1</c>）
    /// 並把 rcx 設成閘門的基底暫存器 ⇒ 遊戲自己在呼叫前讀同一個 self 的 byte [self+X]，插件讀它與遊戲同樣安全。
    /// </remarks>
    internal static int DeriveSpinningOffset(Func<string, nint[]> scanAll, CodeReader read, nint rmiWalk, out string evidence)
    {
        int offset = 0, matched = 0, candidates = 0;
        nint gate = 0, call = 0;
        foreach (var signature in SpinningGateSignatures)
        {
            foreach (var hit in scanAll(signature))
            {
                ++candidates;
                var g = read(hit, SpinningGateLength);
                if (g.Length != SpinningGateLength)
                    continue;
                var rexForm = g[0] == 0x44;
                var modrm = rexForm ? g[2] : g[1];
                var disp = I32(g, rexForm ? 3 : 2);
                var baseReg = modrm & 7;
                if (modrm >> 6 != 2 || baseReg == 4 || !rexForm && (modrm & 0x38) != 0x38)
                    continue;
                var body = hit + SpinningGateLength;
                // 只看 je 跳過的區段：呼叫在裡面＝旗標為 0 時不會呼叫。
                var span = read(body, Math.Clamp(I32(g, 9), 0, SpinningCallWindow));
                var callAt = FindCallTo(span, body, rmiWalk);
                if (callAt < 0 || !Contains(span[..callAt], [0xC6, 0x44, 0x24, 0x30, 0x01])
                    || !(Contains(span[..callAt], [0x48, 0x8B, (byte)(0xC8 | baseReg)]) || Contains(span[..callAt], [0x48, 0x89, (byte)(0xC1 | baseReg << 3)])))
                    continue;
                ++matched;
                offset = disp;
                gate = hit;
                call = body + callAt;
            }
        }
        if (matched != 1)
            throw Fail($"跳過「附加走路取樣」呼叫的旗標閘門應唯一，{candidates} 個候選中符合 {matched} 個（走路輸入函式 0x{rmiWalk:X}）");
        if (offset <= 0 || offset >= 0x10000)
            throw Fail($"旋轉移動旗標位移 0x{offset:X} 不合理");

        evidence = $"self+0x{offset:X}（閘門 0x{gate:X}、附加取樣呼叫 0x{call:X} → 0x{rmiWalk:X}）";
        return offset;
    }

    // E8 rel32 且目標＝target 的第一個位置；逐位元組找，目標位址完全相符才算，誤認機率可忽略。
    private static int FindCallTo(ReadOnlySpan<byte> code, nint codeAddress, nint target)
    {
        for (var i = 0; i + 5 <= code.Length; ++i)
            if (code[i] == 0xE8 && codeAddress + i + 5 + I32(code, i + 1) == target)
                return i;
        return -1;
    }

    private static bool Contains(ReadOnlySpan<byte> code, ReadOnlySpan<byte> needle) => code.IndexOf(needle) >= 0;

    /// <summary>
    /// 飛行輸入函式是否把三個移動軸以 movss 寫到 result+0／+4／+8（Forward／Left／Up）。
    /// </summary>
    /// <remarks>
    /// result 是第二個引數（rdx）；序言常先搬到別的暫存器（REX.W 8B /r 或 89 /r，mod=11），找不到就當它留在 rdx。
    /// 與 GatherBuddyReborn 的 FlyInputLayoutCheck 同一套判準（同一支遊戲函式）。
    /// </remarks>
    internal static bool FlyInputWritesAxes(ReadOnlySpan<byte> code, out string evidence)
    {
        var baseReg = 2;
        var prologue = Math.Min(code.Length, 0x40);
        for (var i = 0; i + 3 <= prologue; ++i)
        {
            int rex = code[i], op = code[i + 1], modrm = code[i + 2];
            if ((rex & 0xF8) != 0x48 || modrm >> 6 != 3)
                continue;
            var reg = ((modrm >> 3) & 7) | ((rex & 4) << 1);
            var rm = (modrm & 7) | ((rex & 1) << 3);
            if (op == 0x8B && rm == 2)
            {
                baseReg = reg;
                break;
            }
            if (op == 0x89 && reg == 2)
            {
                baseReg = rm;
                break;
            }
        }

        // rsp／r12 要 SIB、rbp／r13 的 mod=00 是 rip 相對：不認這兩種形狀，當成驗不過。
        if ((baseReg & 7) is 4 or 5)
        {
            evidence = $"result 搬到不支援的暫存器 r{baseReg}";
            return false;
        }

        var seen = 0;
        for (var i = 0; i + 4 <= code.Length; ++i)
        {
            if (code[i] != 0xF3)
                continue;
            var j = i + 1;
            var rex = 0;
            if ((code[j] & 0xF0) == 0x40)
                rex = code[j++];
            if (j + 3 > code.Length || code[j] != 0x0F || code[j + 1] != 0x11)
                continue;
            var modrm = code[j + 2];
            if (((modrm & 7) | ((rex & 1) << 3)) != baseReg)
                continue;
            var disp = (modrm >> 6) switch
            {
                0 => 0,
                1 when j + 4 <= code.Length => (sbyte)code[j + 3],
                _ => -1,
            };
            if (disp is 0 or 4 or 8)
                seen |= 1 << (disp >> 2);
        }

        evidence = $"result 暫存器 r{baseReg}，+0/+4/+8 的 movss 寫入遮罩 0b{Convert.ToString(seen, 2).PadLeft(3, '0')}，函式長度 0x{code.Length:X}";
        return seen == 7;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeFunction
    {
        public uint BeginAddress;
        public uint EndAddress;
        public uint UnwindData;
    }

    [LibraryImport("kernel32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial RuntimeFunction* RtlLookupFunctionEntry(ulong controlPc, out ulong imageBase, nint historyTable);

    private const byte UnwFlagChainInfo = 0x4;

    /// <summary>從進入點沿 .pdata 走過緊接著的延續區塊（UNW_FLAG_CHAININFO），回傳本體長度；不是函式進入點回 0。</summary>
    internal static int FunctionLength(nint entry)
    {
        var start = (ulong)entry;
        var pc = start;
        while (true)
        {
            var rf = RtlLookupFunctionEntry(pc, out var imageBase, 0);
            if (rf == null || imageBase + rf->BeginAddress != pc)
                break;
            if (pc != start && (rf->UnwindData & 1) == 0 && ((*(byte*)(imageBase + rf->UnwindData) >> 3) & UnwFlagChainInfo) == 0)
                break;
            pc = imageBase + rf->EndAddress;
        }
        return (int)Math.Min(pc - start, 0x10000);
    }

    // REX.W 89 /r，mod=10（disp32）、無 SIB：mov qword [reg+disp], reg。
    private static bool StoresQwordAt(ReadOnlySpan<byte> code, int disp)
    {
        for (var i = 0; i + 7 <= code.Length; ++i)
        {
            if ((code[i] & 0xF8) != 0x48 || code[i + 1] != 0x89)
                continue;
            var modrm = code[i + 2];
            if (modrm >> 6 == 2 && (modrm & 7) != 4 && I32(code, i + 3) == disp)
                return true;
        }
        return false;
    }

    private static ReadOnlySpan<byte> ReadExact(CodeReader read, nint address, int length)
    {
        var span = read(address, length);
        if (span.Length != length)
            throw Fail($"0x{address:X} 讀不到 {length} 個位元組");
        return span;
    }

    private static int I32(ReadOnlySpan<byte> b, int offset) => b[offset] | b[offset + 1] << 8 | b[offset + 2] << 16 | b[offset + 3] << 24;

    private static InvalidOperationException Fail(string what) => new($"BossModReborn 原生版面閘門：{what}。遊戲改版後版面可能變了，為避免寫壞遊戲記憶體而停止載入。");
}
