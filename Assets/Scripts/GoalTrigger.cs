using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class GoalTrigger : MonoBehaviour
{
    [Header("Which team receives the point?")]
    public MatchManager.Team scoringTeam;

    private void Awake()
    {
        BoxCollider2D trigger = GetComponent<BoxCollider2D>();
        trigger.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Ball"))
        {
            return;
        }

        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.RegisterGoal(
                scoringTeam,
                other.transform.position
            );
        }
    }
}