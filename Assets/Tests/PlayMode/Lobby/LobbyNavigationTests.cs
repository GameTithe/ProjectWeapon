#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectWeapon.Lobby.Tests
{
    public sealed class LobbyNavigationTests
    {
        private GameObject root = null!;
        private LobbyNavigation navigation = null!;
        private GameObject home = null!;
        private LobbyNavigation.Page[] pages = null!;
        private PointerEventData click = null!;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Lobby canvas", typeof(RectTransform), typeof(Canvas));
            var input = Object.FindAnyObjectByType<EventSystem>() ?? root.AddComponent<EventSystem>();
            click = new PointerEventData(input) { button = PointerEventData.InputButton.Left };
            navigation = root.AddComponent<LobbyNavigation>();
            home = Child(root.transform, "Home");
            pages = Enum.GetValues(typeof(LobbyDestination)).Cast<LobbyDestination>().Select(destination =>
            {
                GameObject content = Child(root.transform, destination.ToString());
                return new LobbyNavigation.Page(destination, content, Button(home.transform, "Open " + destination),
                    Button(content.transform, "Return"));
            }).ToArray();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void AllFiveRealButtonsNavigateAndEachBackReturnsToTheSameHome()
        {
            var requested = new List<LobbyDestination>();
            navigation.Initialize(home, pages, destination => { requested.Add(destination); return true; });

            foreach (LobbyNavigation.Page page in pages)
            {
                Assert.That(home.activeSelf, Is.True);
                page.Open.OnPointerClick(click);
                Assert.That(navigation.CurrentDestination, Is.EqualTo(page.Destination));
                Assert.That(home.activeSelf, Is.False);
                Assert.That(pages.Where(candidate => candidate.Content.activeInHierarchy), Is.EquivalentTo(new[] { page }));

                page.Back.OnPointerClick(click);
                Assert.That(navigation.CurrentDestination, Is.Null);
                Assert.That(pages.All(candidate => !candidate.Content.activeInHierarchy), Is.True);
                Assert.That(home.activeSelf, Is.True);
            }

            Assert.That(requested, Is.EqualTo(pages.Select(page => page.Destination)));
        }

        [Test]
        public void RejectedServiceLeavesTheHomeAndAllowsARepeatedChoice()
        {
            var reports = new List<LobbyDestination>();
            int attempts = 0;
            navigation.OpenRejected += reports.Add;
            navigation.Initialize(home, pages, _ => ++attempts > 1);
            LobbyNavigation.Page draw = pages.Single(page => page.Destination == LobbyDestination.WeaponDraw);
            draw.Open.OnPointerClick(click);
            Assert.That(reports, Is.EqualTo(new[] { LobbyDestination.WeaponDraw }));
            Assert.That(home.activeSelf, Is.True);
            Assert.That(navigation.CurrentDestination, Is.Null);

            draw.Open.OnPointerClick(click);
            Assert.That(draw.Content.activeSelf, Is.True);
            Assert.That(navigation.TryOpen(LobbyDestination.Warehouse), Is.False);
            Assert.That(attempts, Is.EqualTo(2));
        }

        [Test]
        public void MissingOrDuplicateBindingsAreRejectedBeforeVisibilityChanges()
        {
            Assert.Throws<ArgumentException>(() => navigation.Initialize(home, pages.Take(4).ToArray(), _ => true));
            Assert.Throws<ArgumentException>(() => navigation.Initialize(home,
                pages.Take(4).Append(pages[0]).ToArray(), _ => true));
            Assert.That(home.activeSelf && pages.All(page => page.Content.activeSelf), Is.True);
            Assert.That(navigation.TryReturnToLobby(), Is.False);
        }

        [UnityTest]
        public IEnumerator CanvasRaycastSelectsTheVisibleLobbyButton()
        {
            navigation.Initialize(home, pages, _ => true);
            var raycaster = root.AddComponent<GraphicRaycaster>();
            var desired = pages.Single(page => page.Destination == LobbyDestination.EquippedWeapons);
            desired.Open.targetGraphic = desired.Open.gameObject.AddComponent<Image>();
            yield return null;
            Canvas.ForceUpdateCanvases();

            click.position = RectTransformUtility.WorldToScreenPoint(null, desired.Open.transform.position);
            var hits = new List<RaycastResult>();
            raycaster.Raycast(click, hits);
            GameObject selected = hits.First().gameObject;
            Assert.That(selected, Is.SameAs(desired.Open.gameObject));
            ExecuteEvents.ExecuteHierarchy(selected, click, ExecuteEvents.pointerClickHandler);
            Assert.That(navigation.CurrentDestination, Is.EqualTo(LobbyDestination.EquippedWeapons));
        }

        private static GameObject Child(Transform parent, string name)
        {
            var instance = new GameObject(name, typeof(RectTransform));
            instance.transform.SetParent(parent, false);
            return instance;
        }

        private static Button Button(Transform parent, string name)
            => Child(parent, name).AddComponent<Button>();
    }
}
