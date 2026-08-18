using Dalamud.Game.ClientState.Objects.SubKinds;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace BossMod.Util;
public static unsafe class PlayerEx
{
    public static IPlayerCharacter Object => Service.ObjectTable.LocalPlayer ?? throw new InvalidOperationException("LocalPlayer is null");

    // 📌 這裡的 CameraManager 是 Client.Game.Control.CameraManager（見上面的 using），它的
    //    Instance() 只是 (CameraManager*)Control.Instance()，而 Control.Instance() 標的是
    //    [StaticAddress("4C 8D 35 …", 3)] —— isPointer 省略即 false，取的是全域變數**本身**的
    //    位址（4C 8D 35 = lea r14,[rip+…]）。CS 的產生器對 isPointer:false 產出的是
    //    「解析失敗就 ThrowNullAddress」，成功則回一個恆非 null 的模組內位址。
    //    ⇒ 對 CameraManager.Instance() 判空是**死碼**，不要加。
    // 🔴 真正可能是 null 的是 GetActiveCamera() 的**回傳值**（登入前／讀取畫面／過場時沒有
    //    作用中相機），所以下面兩個屬性刻意原樣把 null 傳出去，由呼叫端判空後才解參考。
    //    AccessViolationException 是 corrupted-state exception，try/catch 攔不到，沒有第二道防線。
    public static unsafe FFXIVClientStructs.FFXIV.Client.Game.Camera* Camera => CameraManager.Instance()->GetActiveCamera();
    public static unsafe CameraEx* CameraEx => (CameraEx*)CameraManager.Instance()->GetActiveCamera();

    public static CSGameObject* GameObject
    {
        get
        {
            var localPlayer = Service.ObjectTable.LocalPlayer;
            return localPlayer != null ? (CSGameObject*)localPlayer.Address : null;
        }
    }

    public static Vector3? SetPosition
    {
        get
        {
            var localPlayer = Service.ObjectTable.LocalPlayer;
            return localPlayer?.Position;
        }
        set
        {
            if (GameObject != null && value.HasValue)
            {
                GameObject->SetPosition(value.Value.X, value.Value.Y, value.Value.Z);
            }
        }
    }

    public static Vector3 Position
    {
        get
        {
            var localPlayer = Service.ObjectTable.LocalPlayer;
            return localPlayer != null ? localPlayer.Position : Vector3.Zero;
        }
    }

    public static void SetPlayerPosition(Vector3 position)
    {
        try
        {
            if (Service.ObjectTable.LocalPlayer != null)
            {
                SetPosition = position;
                Service.Log("Setting player position to: " + position.ToString());

            }
            else
            {
                Service.Log("LocalPlayer is null");
            }
        }
        catch (Exception ex)
        {
            Service.Log("Error in SetPlayerPosition" + ex);
        }
    }
}
