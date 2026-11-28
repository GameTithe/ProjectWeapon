#nullable enable

using NUnit.Framework;
using ProjectWeapon.Art.Wanderer;
using UnityEditor;
using UnityEngine;

namespace ProjectWeapon.Art.Tests
{
    public sealed class WandererLocomotionTests
    {
        private const string ArtPath = "Assets/ProjectWeapon/Art/Wanderer/";
        private GameObject actor = null!;
        private WandererLocomotionView view = null!;
        private SpriteRenderer renderer = null!;
        private WandererLocomotionSet frames = null!;

        [SetUp]
        public void SetUp()
        {
            frames = AssetDatabase.LoadAssetAtPath<WandererLocomotionSet>(ArtPath + "WandererLocomotion.asset");
            Assert.That(frames, Is.Not.Null);
            actor = new GameObject("Wanderer", typeof(SpriteRenderer), typeof(WandererLocomotionView));
            renderer = actor.GetComponent<SpriteRenderer>();
            view = actor.GetComponent<WandererLocomotionView>();
            view.Configure(frames);
        }

        [TearDown]
        public void TearDown()
        {
            if (actor != null)
                Object.DestroyImmediate(actor);
        }

        [TestCase(WandererMotion.Idle, "idle", 2)]
        [TestCase(WandererMotion.Walk, "walk", 6)]
        [TestCase(WandererMotion.Dodge, "dodge", 4)]
        public void ImportedClipsShareFootPivotAndHaveEveryDirection(WandererMotion motion, string action, int count)
        {
            for (int row = 0; row < 4; row++)
            {
                var facing = (WandererFacing)row;
                for (int frame = 0; frame < count; frame++)
                {
                    Sprite sprite = frames.GetFrame(motion, facing, frame)!;
                    Assert.That(sprite, Is.Not.Null);
                    Assert.That(sprite.name, Is.EqualTo($"wanderer_{action}_{facing.ToString().ToLowerInvariant()}_{frame + 1}"));
                    Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(32, 48)));
                    Assert.That(sprite.pivot, Is.EqualTo(new Vector2(16, 6)));
                    Assert.That(sprite.pixelsPerUnit, Is.EqualTo(32));
                    Assert.That(sprite.texture.filterMode, Is.EqualTo(FilterMode.Point));
                }
            }
        }

        [Test]
        public void MoveSamplesChangeFacingAndSnapToIdleWithTheLastDirection()
        {
            Assert.That(renderer.sprite.name, Is.EqualTo("wanderer_idle_south_1"));
            view.Present(new Vector2(1, 1), false, Vector2.zero, 0);
            Assert.That(renderer.sprite.name, Is.EqualTo("wanderer_walk_north_1"));

            view.Present(Vector2.left, false, Vector2.zero, 0);
            view.Present(new Vector2(-1, 1), false, Vector2.zero, 0.1f);
            Assert.That(renderer.sprite.name, Is.EqualTo("wanderer_walk_west_2"));

            view.Present(Vector2.zero, false, Vector2.zero, 0);
            Assert.That(renderer.sprite.name, Is.EqualTo("wanderer_idle_west_1"));
        }

        [Test]
        public void DodgeUsesLockedDirectionAndNeverLeavesARollFrameWhenEnding()
        {
            view.Present(Vector2.up, true, Vector2.right, 0);
            Assert.That(renderer.sprite.name, Is.EqualTo("wanderer_dodge_east_1"));
            view.Present(Vector2.down, true, Vector2.right, 1);
            Assert.That(renderer.sprite.name, Is.EqualTo("wanderer_dodge_east_4"));

            view.Present(Vector2.left, false, Vector2.zero, 0);
            Assert.That(renderer.sprite.name, Is.EqualTo("wanderer_walk_west_1"));
            view.Present(Vector2.zero, true, Vector2.down, 0);
            view.Present(Vector2.zero, false, Vector2.zero, 0);
            Assert.That(renderer.sprite.name, Is.EqualTo("wanderer_idle_south_1"));
        }
    }
}
