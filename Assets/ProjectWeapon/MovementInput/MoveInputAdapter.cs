using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectWeapon.MovementInput
{
    [DisallowMultipleComponent]
    public sealed class MoveInputAdapter : MonoBehaviour
    {
        [SerializeField] private InputActionReference moveAction;

        // The project-wide action map owns activation and can also serve the UI.
        public Vector2 Direction
        {
            get
            {
                if (!isActiveAndEnabled || moveAction == null)
                {
                    return Vector2.zero;
                }

                InputAction action = moveAction.action;
                return action != null && action.enabled
                    ? Vector2.ClampMagnitude(action.ReadValue<Vector2>(), 1f)
                    : Vector2.zero;
            }
        }
    }
}
