#nullable enable

using ProjectWeapon.Combat;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.DpsQA
{
    [DisallowMultipleComponent]
    public sealed class DpsQaScene : MonoBehaviour
    {
        [SerializeField] private DpsQaSettings settings = null!;
        [SerializeField] private Transform caster = null!;
        [SerializeField] private Transform target = null!;
        [SerializeField] private TMP_Text results = null!;

        public DpsQaReport? Report { get; private set; }

        private void Start()
        {
            Report = DpsQaRunner.Measure(settings, Point(caster), Point(target));
            results.text = Report.Summary();
            Debug.Log(Report.DetailedLog(), this);
        }

        private static WorldPoint2D Point(Transform marker) => new WorldPoint2D(marker.position.x, marker.position.y);
    }
}
