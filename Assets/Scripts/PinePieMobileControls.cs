using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Bridges PinePie JoystickController + Kick/Dash UI buttons into MobileInputBridge.
/// Hang this on the Pitch Canvas. Assign the PinePie joystick and the two buttons.
/// </summary>
public class PinePieMobileControls : MonoBehaviour
{
    [Header("PinePie Joystick")]
    [Tooltip("The panel that has JoystickController (PinePie)")]
    [SerializeField] private JoystickController joystick;

    [Header("Action Buttons")]
    [SerializeField] private Button kickButton;
    [SerializeField] private Button dashButton;

    private void Start()
    {
        if (joystick == null)
        {
            joystick = FindObjectOfType<JoystickController>();
        }

        if (kickButton != null)
        {
            var trigger = kickButton.gameObject.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = kickButton.gameObject.AddComponent<EventTrigger>();
            }

            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ =>
            {
                MobileInputBridge.KickHeld = true;
                MobileInputBridge.KickPressed = true;
            });
            trigger.triggers.Add(down);

            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ =>
            {
                MobileInputBridge.KickHeld = false;
            });
            trigger.triggers.Add(up);
        }

        if (dashButton != null)
        {
            dashButton.onClick.AddListener(() =>
            {
                MobileInputBridge.DashPressed = true;
            });
        }
    }

    private void Update()
    {
        if (joystick == null)
        {
            MobileInputBridge.Move = Vector2.zero;
            return;
        }

        // PinePie API: InputDirection (see PinePie manual example)
        Vector2 dir = joystick.InputDirection;
        MobileInputBridge.Move = dir;
    }

    private void OnDisable()
    {
        MobileInputBridge.Move = Vector2.zero;
        MobileInputBridge.KickHeld = false;
        MobileInputBridge.ClearFrameFlags();
    }
}
