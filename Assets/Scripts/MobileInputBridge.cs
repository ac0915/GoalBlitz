using UnityEngine;

public static class MobileInputBridge
{
    public static Vector2 Move;
    public static bool KickHeld;
    public static bool KickPressed;
    public static bool DashPressed;

    public static void ClearFrameFlags()
    {
        KickPressed = false;
        DashPressed = false;
    }
}