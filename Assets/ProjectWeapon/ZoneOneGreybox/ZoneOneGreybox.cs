#nullable enable

using System;
using ProjectWeapon.Combat;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Extraction;
using ProjectWeapon.Run;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectWeapon.ZoneOneGreybox
{
    [DisallowMultipleComponent]
    public sealed class ZoneOneGreybox : MonoBehaviour
    {
        [SerializeField] private ZoneCatalog catalog = null!;
        [SerializeField] private Rigidbody2D player = null!;
        [SerializeField] private SpriteRenderer extractionMarker = null!;
        [SerializeField, Min(0.01f)] private float extractionRadius = 0.75f;

        private readonly RunId runId = new RunId(Guid.NewGuid());
        private RunState run = null!;
        private ZoneRunCountdown countdown = null!;
        private ExtractionAccess access = null!;
        private InputAction? interact;

        public ZoneDefinition Zone => catalog.GetZone(1);
        public Rigidbody2D Player => player;
        public Vector2 ExtractionCenter => extractionMarker.transform.position;
        public float ExtractionRadius => extractionRadius;
        public RunPhase Phase => run.Phase;
        public bool CanStartExtraction => access.CanStart(runId, PlayerPoint());

        private void Awake()
        {
            double startedAt = Time.timeAsDouble;
            run = new RunState(() => TimeSpan.FromSeconds(Time.timeAsDouble - startedAt));
            countdown = new ZoneRunCountdown(catalog, run);
            Vector2 center = ExtractionCenter;
            access = new ExtractionAccess(run, Zone.ZoneId, new WorldPoint2D(center.x, center.y), extractionRadius);
            interact = InputSystem.actions?.FindAction("Player/Interact");
            countdown.TryStartRun(runId, Zone.ZoneId);
        }

        private void Update()
        {
            if (interact?.WasPressedThisFrame() == true)
                TryStartExtraction();

            countdown.ProcessFrame(Array.Empty<RunSignal>());
            extractionMarker.color = run.Phase switch
            {
                RunPhase.Succeeded => new Color(0.62f, 1f, 0.64f),
                RunPhase.Extracting => new Color(0.90f, 0.95f, 1f),
                _ => CanStartExtraction ? Color.white : new Color(0.60f, 0.68f, 0.78f)
            };
        }

        public bool TryStartExtraction() => access.TryStart(runId, PlayerPoint()) != null;

        private WorldPoint2D PlayerPoint()
        {
            Vector2 position = player.position;
            return new WorldPoint2D(position.x, position.y);
        }

        private void OnDrawGizmosSelected()
        {
            if (extractionMarker == null)
                return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(extractionMarker.transform.position, extractionRadius);
        }
    }
}
