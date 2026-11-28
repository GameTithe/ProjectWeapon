#nullable enable

using System;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.Enemies.Boss
{
    [DisallowMultipleComponent]
    public sealed class BossShockwaveActor : MonoBehaviour
    {
        [SerializeField] private string targetId = "zone1-boss";
        [SerializeField] private BossShockwaveSettings settings = null!;
        [SerializeField] private SpriteRenderer body = null!;
        [SerializeField] private LineRenderer warning = null!;

        private const int Segments = 64;
        private Color idleColor;
        private double damagedUntil;
        private Guid displayedCast;

        public BossShockwave? Boss { get; private set; }

        public void Initialize(RunState run)
        {
            if (Boss != null)
                throw new InvalidOperationException("The boss actor has already been initialized.");
            if (!body || !warning)
                throw new InvalidOperationException("The boss prefab requires its body and telegraph renderers.");

            idleColor = body.color;
            Boss = new BossShockwave(run, targetId, settings);
            warning.enabled = false;
        }

        public void Advance(TimeSpan now, TargetCandidate player)
        {
            if (!isActiveAndEnabled || Boss == null)
                return;

            Vector3 position = transform.position;
            Boss.Advance(now, new WorldPoint2D(position.x, position.y), player);
            DrawWarning();
        }

        public bool ApplyDamage(DamageRequest hit, TimeSpan now)
        {
            if (Boss?.ApplyDamage(hit, now) != true)
                return false;

            damagedUntil = Time.unscaledTimeAsDouble + 0.15;
            DrawWarning();
            UpdateBody();
            return true;
        }

        private void Update()
        {
            if (Boss != null)
                UpdateBody();
        }

        private void OnDisable()
        {
            if (warning)
                warning.enabled = false;
        }

        private void UpdateBody()
            => body.color = Boss!.IsDead ? Color.gray
                : Time.unscaledTimeAsDouble < damagedUntil ? new Color(1f, 0.45f, 0.35f) : idleColor;

        private void DrawWarning()
        {
            warning.enabled = Boss!.ActiveCast.HasValue;
            if (Boss.ActiveCast is not { } cast || cast.Area.ApplicationId == displayedCast)
                return;

            displayedCast = cast.Area.ApplicationId;
            warning.loop = true;
            warning.useWorldSpace = true;
            warning.positionCount = Segments;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * 2f * Mathf.PI / Segments;
                warning.SetPosition(i, new Vector3(cast.Area.Origin.X + Mathf.Cos(angle) * cast.Area.Radius,
                    cast.Area.Origin.Y + Mathf.Sin(angle) * cast.Area.Radius, transform.position.z));
            }
        }
    }
}
