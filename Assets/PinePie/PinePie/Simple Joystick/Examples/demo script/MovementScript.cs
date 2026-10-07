using UnityEngine;

namespace PinePie.SimpleJoystick.Examples.DemoScript
{
    public class MovementScript : MonoBehaviour
    {
        private JoystickController joystickController;
        public float moveSpeed = 5f;
    
        void Start()
        {
            JoystickController[] joysticks = FindObjectsOfType<JoystickController>();
            foreach (var joystick in joysticks)
            {
                if (joystick.name == "PinePie Joystick") joystickController = joystick;
            }
        }
    
        void Update()
        {
            transform.position +=
                moveSpeed * Time.deltaTime * (Vector3)joystickController.InputDirection;
        }
    }
    
}