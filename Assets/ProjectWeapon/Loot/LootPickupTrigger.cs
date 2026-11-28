#nullable enable

using System;
using ProjectWeapon.Inventory;
using UnityEngine;

namespace ProjectWeapon.Loot
{
    [RequireComponent(typeof(CircleCollider2D))]
    [DisallowMultipleComponent]
    public sealed class LootPickupTrigger : MonoBehaviour
    {
        private CircleCollider2D? circle;
        private LootPickupCoordinator? pickup;
        private Func<bool>? canCollect;

        public event Action<WorldLoot?, WeaponInstanceId, InventoryPickupResult>? OutcomeReported;

        public void Initialize(LootPickupCoordinator coordinator) => Initialize(coordinator, null);

        public void Initialize(LootPickupCoordinator coordinator, Func<bool>? canCollect)
        {
            pickup = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            this.canCollect = canCollect;
        }

        public bool TryResolveChoice(WorldLoot? source, InventoryPickupChoice? choice, WeaponInstanceId? selectedId)
        {
            if (pickup == null || choice == null || canCollect?.Invoke() == false)
                return false;

            WorldLoot? matchedSource = source && source!.Item?.Id == choice.Loot.Id ? source : null;
            if (selectedId.HasValue && (matchedSource == null || !matchedSource.isActiveAndEnabled
                || !TryGetRange(out Vector2 center, out float radius)
                || !LootPickupCoordinator.IsWithinRange(center, matchedSource.transform.position, radius)))
                return false;

            if (!pickup.TryResolveChoice(choice, selectedId, out InventoryPickupResult result))
                return false;

            OutcomeReported?.Invoke(matchedSource, choice.Loot.Id, result);
            return true;
        }

        public bool AllowRetry(WeaponInstanceId id) => pickup?.AllowRetry(id) == true;

        private void Awake() => circle = GetComponent<CircleCollider2D>();

        private void OnTriggerEnter2D(Collider2D other) => Observe(other);

        private void OnTriggerStay2D(Collider2D other) => Observe(other);

        private void Observe(Collider2D other)
        {
            if (pickup == null || canCollect?.Invoke() == false || !TryGetRange(out Vector2 center, out float radius))
                return;

            WorldLoot source = other.GetComponentInParent<WorldLoot>();
            if (!source || !source.isActiveAndEnabled || source.Item is not WeaponInstance item)
                return;

            if (pickup.TryObserve(item, center, source.transform.position, radius, out InventoryPickupResult result))
                OutcomeReported?.Invoke(source, item.Id, result);
        }

        private bool TryGetRange(out Vector2 center, out float radius)
        {
            center = default;
            radius = 0;
            if (!isActiveAndEnabled || !circle || !circle!.enabled || !circle.isTrigger)
                return false;

            Vector3 scale = transform.lossyScale;
            float horizontal = Mathf.Abs(scale.x);
            if (horizontal == 0 || !Mathf.Approximately(horizontal, Mathf.Abs(scale.y)))
                return false;

            center = transform.TransformPoint(circle.offset);
            radius = circle.radius * horizontal;
            return true;
        }
    }
}
