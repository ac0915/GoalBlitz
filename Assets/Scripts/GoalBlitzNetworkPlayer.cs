using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PlayerController))]
public class GoalBlitzNetworkPlayer : NetworkBehaviour
{
    [Networked] public int Team { get; set; }

    public override void Spawned()
    {
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        if (sprite != null)
        {
            sprite.color = Team == GoalBlitzLobbyState.RedTeam
                ? new Color(1f, 0.32f, 0.32f)
                : new Color(0.3f, 0.65f, 1f);
        }

        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.RegisterPlayer(Team, gameObject);
        }
    }
}
