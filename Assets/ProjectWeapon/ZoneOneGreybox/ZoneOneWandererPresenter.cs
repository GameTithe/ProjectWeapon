#nullable enable

using ProjectWeapon.Art.Wanderer;
using UnityEngine;

namespace ProjectWeapon.ZoneOneGreybox
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WandererLocomotionView))]
    public sealed class ZoneOneWandererPresenter : MonoBehaviour
    {
        [SerializeField] private ZoneOneGreybox room = null!;

        private WandererLocomotionView view = null!;

        private void Awake() => view = GetComponent<WandererLocomotionView>();

        private void LateUpdate()
        {
            var movement = room.Movement;
            if (movement == null || !movement.isActiveAndEnabled || room.Run.Result != null)
            {
                view.Present(Vector2.zero, false, Vector2.zero, Time.deltaTime);
                return;
            }

            view.Present(movement.SampleDirection(), movement.IsDodging, movement.DodgeDirection, Time.deltaTime);
        }
    }
}
