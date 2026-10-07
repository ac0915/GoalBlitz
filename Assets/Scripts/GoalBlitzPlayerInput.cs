using Fusion;
using UnityEngine;

public struct GoalBlitzPlayerInput : INetworkInput
{
    public Vector2 Move;
    public NetworkBool KickHeld;
    public NetworkBool KickPressed;
    public NetworkBool DashPressed;
}
