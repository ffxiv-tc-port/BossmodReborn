namespace BossMod;

public enum Waymark : byte
{
    A, B, C, D, N1, N2, N3, N4, Count
}

public enum Sign : byte
{
    Attack1,
    Attack2,
    Attack3,
    Attack4,
    Attack5,
    Bind1,
    Bind2,
    Bind3,
    Ignore1,
    Ignore2,
    Square,
    Circle,
    Cross,
    Triangle,
    Attack6,
    Attack7,
    Attack8,
    Count
}

// waymark and sign positions in world; part of the world state structure
public sealed class WaymarkState
{
    private BitMask _setMarkers;
    private readonly Vector3[] _positions = new Vector3[(int)Waymark.Count];

    private BitMask _setSigns;
    private readonly ulong[] _targets = new ulong[(int)Sign.Count];

    public Vector3? GetFieldMark(int id) => this[(Waymark)id];
    public ulong GetSign(int id) => this[(Sign)id];

    public Vector3? this[Waymark wm]
    {
        get => _setMarkers[(int)wm] ? _positions[(int)wm] : null;
        private set
        {
            // 🔴 這個 setter 的其中一個來源不受信任：ReplayParserLog.ParseWaymarkChange
            //    直接把 replay 檔裡的一個 raw byte cast 成 Waymark（`(Waymark)_input.ReadByte(false)`），
            //    完全不驗範圍。檔案壞掉／被截斷時只要那個 byte >= Waymark.Count，
            //    這裡就是 IndexOutOfRangeException，把整份 replay 的載入打斷。
            //    （遊戲內那條路徑是安全的：WorldStateGameSync.UpdateWaymarks 從 Waymark.A 逐一 ++，
            //    走的是 MarkingController 固定長度的 FieldMarkers 陣列，永遠在界內。）
            // ⇒ fail-safe：出界就整個跳過。少認一個航點只會讓 replay 少一個標記，
            //    比整份載不進來好，而且不影響其他航點。
            var idx = (int)wm;
            if ((uint)idx >= (uint)_positions.Length)
                return;
            _setMarkers[idx] = value != null;
            _positions[idx] = value ?? default;
        }
    }

    public ulong this[Sign sgn]
    {
        get => _setSigns[(int)sgn] ? _targets[(int)sgn] : 0;
        private set
        {
            // 🔴 與上面 Waymark 那個 setter 同一個洞,而且這裡原本沒補:
            //    ReplayParserLog.ParseSignChange 同樣把 replay 檔的一個 raw byte 直接
            //    cast 成 Sign(`(Sign)_input.ReadByte(false)`),完全不驗範圍。
            //    檔案壞掉／被截斷時只要那個 byte >= Sign.Count(17),
            //    `_targets[(int)sgn] = value` 就是 IndexOutOfRangeException,整份 replay 載不進來。
            //    (BitMask 那一半是安全的 —— 它明文保證出界 get 回 0、set 是 no-op;
            //    會擲的只有 _targets 這個定長陣列。)
            //    遊戲內那條路徑安全:WorldStateGameSync.UpdateWaymarks 從 Sign 的固定列舉逐一走。
            // ⇒ fail-safe 與 Waymark 那邊一致:出界就整個跳過,少認一個標記好過整份載不進來。
            var idx = (int)sgn;
            if ((uint)idx >= (uint)_targets.Length)
                return;
            _setSigns[idx] = value is not (0 or 0xE0000000);
            _targets[idx] = value;
        }
    }

    public List<WorldState.Operation> CompareToInitial()
    {
        List<WorldState.Operation> waymarks = new(16);
        foreach (var i in _setMarkers.SetBits())
            waymarks.Add(new OpWaymarkChange((Waymark)i, _positions[i]));
        foreach (var i in _setSigns.SetBits())
            waymarks.Add(new OpSignChange((Sign)i, _targets[i]));
        return waymarks;
    }

    // implementation of operations
    public Event<OpWaymarkChange> Changed = new();
    public sealed record class OpWaymarkChange(Waymark ID, Vector3? Pos) : WorldState.Operation
    {
        protected override void Exec(WorldState ws)
        {
            ws.Waymarks[ID] = Pos;
            ws.Waymarks.Changed.Fire(this);
        }
        public override void Write(ReplayRecorder.Output output)
        {
            if (Pos != null)
                output.EmitFourCC("WAY+"u8).Emit((byte)ID).Emit(Pos.Value);
            else
                output.EmitFourCC("WAY-"u8).Emit((byte)ID);
        }
    }

    public Event<OpSignChange> SignChanged = new();
    public sealed record class OpSignChange(Sign ID, ulong Target) : WorldState.Operation
    {
        protected override void Exec(WorldState ws)
        {
            ws.Waymarks[ID] = Target;
            ws.Waymarks.SignChanged.Fire(this);
        }
        public override void Write(ReplayRecorder.Output output)
        {
            if (Target is not (0 or 0xE0000000))
                output.EmitFourCC("SGN+"u8).Emit((byte)ID).EmitActor(Target);
            else
                output.EmitFourCC("SGN-"u8).Emit((byte)ID);
        }
    }
}
