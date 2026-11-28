#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using NUnit.Framework;
using ProjectWeapon.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectWeapon.Art.EnemyCues.Tests
{
    public sealed class EnemyCueViewTests
    {
        private GameObject instance = null!;
        private EnemyCueView view = null!;
        private LineRenderer outline = null!;
        private SpriteRenderer spark = null!;

        [SetUp]
        public void Setup()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectWeapon/Art/EnemyCues/EnemyCue.prefab");
            Assert.That(prefab, Is.Not.Null);
            instance = Object.Instantiate(prefab);
            view = instance.GetComponent<EnemyCueView>();
            outline = instance.GetComponent<LineRenderer>();
            spark = instance.GetComponentInChildren<SpriteRenderer>(includeInactive: true);
        }

        [TearDown]
        public void Teardown()
        {
            if (instance != null)
                Object.DestroyImmediate(instance);
        }

        [Test]
        public void SectorUsesActualOriginRadiusAndBothLockedAngles()
        {
            var origin = new WorldPoint2D(2f, 3f);
            AreaDamageRequest attack = AreaDamageRequest.Sector(Guid.NewGuid(), "melee", origin,
                1.4f, 8f, new WorldPoint2D(1f, 0f), 90f);

            Assert.That(view.ShowSector(attack), Is.True);
            Assert.That(outline.enabled && outline.useWorldSpace && outline.loop, Is.True);
            Assert.That(outline.GetPosition(0), Is.EqualTo(new Vector3(origin.X, origin.Y, 0f)));
            float offset = 1.4f * Mathf.Sqrt(0.5f);
            Vector3 first = outline.GetPosition(1);
            Vector3 last = outline.GetPosition(outline.positionCount - 1);
            Assert.That(first.x, Is.EqualTo(origin.X + offset).Within(0.0001f));
            Assert.That(first.y, Is.EqualTo(origin.Y - offset).Within(0.0001f));
            Assert.That(last.x, Is.EqualTo(origin.X + offset).Within(0.0001f));
            Assert.That(last.y, Is.EqualTo(origin.Y + offset).Within(0.0001f));
        }

        [Test]
        public void RangedUsesTheFourWorldCornersWithAnOutlineBehindTheHitMarker()
        {
            var corners = new[]
            {
                new WorldPoint2D(2f, 3.35f), new WorldPoint2D(6f, 3.35f),
                new WorldPoint2D(6f, 2.65f), new WorldPoint2D(2f, 2.65f)
            };
            Assert.That(view.ShowStrip(corners), Is.True);
            Assert.That(outline.positionCount, Is.EqualTo(4));
            for (int index = 0; index < corners.Length; index++)
                Assert.That(outline.GetPosition(index), Is.EqualTo(new Vector3(corners[index].X, corners[index].Y, 0f)));

            Assert.That(outline.startWidth, Is.EqualTo(0.0625f));
            Assert.That(outline.sortingOrder, Is.GreaterThan(0).And.LessThan(2));
            Assert.That(spark.sortingOrder, Is.GreaterThan(2));
            Assert.That(outline.startColor.a, Is.LessThan(1f));
            Assert.That(spark.sprite.pixelsPerUnit, Is.EqualTo(32));
            Assert.That(spark.sprite.rect.width / 32f, Is.EqualTo(0.5f));
            view.ClearTelegraph();
            Assert.That(outline.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator PulseDoesNotReplaceAnAttackOutlineAndDisableClearsBoth()
        {
            Assert.That(view.ShowStrip(new[]
            {
                new WorldPoint2D(0, 1), new WorldPoint2D(4, 1),
                new WorldPoint2D(4, 0), new WorldPoint2D(0, 0)
            }), Is.True);

            view.FlashHit(new WorldPoint2D(1, 1));
            Assert.That(spark.enabled && outline.enabled, Is.True);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(spark.enabled, Is.False);
            Assert.That(outline.enabled, Is.True);

            view.FlashHit(new WorldPoint2D(1, 1));
            instance.SetActive(false);
            Assert.That(spark.enabled || outline.enabled, Is.False);
        }
    }
}
#endif
