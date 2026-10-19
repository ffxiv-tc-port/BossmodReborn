using Dalamud.Interface.ImGuiNotification;

namespace BossMod;

// 載入時有遊戲函式沒掛上（降級運作）時讓使用者看得見：一則通知，加上登入後聊天視窗一行，每次載入各只一次。
// 契約：Update／OnLogin／Dispose 都在主執行緒（Plugin 的每幀更新、ClientState.Login、插件卸載）。
// 沒有缺項時 Create 回 null：不建立任何東西、不訂閱任何事件。
sealed class DegradedLoadNotice : IDisposable
{
    public readonly record struct Text(string Title, string Content, string Chat);

    private readonly Text _text;
    private bool _started;

    private DegradedLoadNotice(Text text) => _text = text;

    // 純函式：缺項清單（可重複，照出現順序去重）→ 要顯示的文字；沒有缺項回 null。
    public static Text? Compose(IReadOnlyList<string> missing)
    {
        var items = missing.Distinct().ToList();
        if (items.Count == 0)
            return null;
        var list = string.Join("、", items);
        return new(
            "BossMod Reborn 降級運作",
            $"遊戲改版後有 {items.Count} 項遊戲功能接不上，這次是降級運作，相關的機制提示與 AI 判斷停用或不準：\n{list}\n細節看 dalamud.log（搜尋「特徵碼」），請等插件更新。",
            $"[BMR] 這次是降級運作：{items.Count} 項遊戲功能接不上（{list}），相關的機制提示與 AI 判斷停用或不準。細節看 dalamud.log（搜尋「特徵碼」），請等插件更新。");
    }

    public static DegradedLoadNotice? Create(IReadOnlyList<string> missing) => Compose(missing) is { } text ? new(text) : null;

    // 第一次呼叫送出通知，並決定聊天那一行現在印還是等登入事件；之後的呼叫只是一個布林判斷。
    public void Update()
    {
        if (_started)
            return;
        _started = true;
        Service.Notifications.AddNotification(new()
        {
            Title = _text.Title,
            Content = _text.Content,
            Type = NotificationType.Warning,
            InitialDuration = TimeSpan.FromSeconds(60d),
            Minimized = false,
        });
        if (Service.ClientState.IsLoggedIn)
            Service.ChatGui.Print(_text.Chat);
        else
            Service.ClientState.Login += OnLogin;
    }

    private void OnLogin()
    {
        Service.ClientState.Login -= OnLogin;
        Service.ChatGui.Print(_text.Chat);
    }

    public void Dispose() => Service.ClientState.Login -= OnLogin;
}
