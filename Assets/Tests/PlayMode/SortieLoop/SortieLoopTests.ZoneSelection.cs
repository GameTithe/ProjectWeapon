#if UNITY_EDITOR
#nullable enable

using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Save;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectWeapon.SortieLoop.Tests
{
    public sealed partial class SortieLoopTests
    {
        [UnityTest]
        public IEnumerator ZoneMenuReadsAllCatalogRowsAndClosesWithoutWriting()
        {
            string path = Path.Combine(profileDirectory!, "snapshot.json");
            byte[] saveBefore = File.ReadAllBytes(path);
            ClickByRaycast(ButtonNamed("Open Zones"));
            yield return null;
            Assert.That(host.IsInLobby, Is.False);
            Transform list = VisibleZoneList();

            AssertZoneRow(list, 1, "버려진 폐성", "1", "1~2", "08:00");
            AssertZoneRow(list, 2, "지하 감옥", "2", "1~3", "10:00");
            AssertZoneRow(list, 3, "저주받은 숲", "3", "2~4", "12:00");
            AssertZoneRow(list, 4, "마왕의 요새", "4", "3~5", "12:00");
            AssertZoneRow(list, 5, "심연의 성역", "5", "4~5", "15:00");
            Assert.That(ButtonNamed("Select Zone 1").interactable, Is.True);

            for (int zone = 2; zone <= 5; zone++)
            {
                Assert.That(list.Find($"Zone {zone}/Entry Status").GetComponent<TMP_Text>().text, Is.EqualTo("잠김"));
                Assert.That(VisibleButtons($"Select Zone {zone}"), Is.Empty);
            }
            foreach (TMP_Text text in list.GetComponentsInChildren<TMP_Text>())
                Assert.That(text.text.All(glyph => text.font.HasCharacter(glyph)), Is.True, text.text);

            ClickByRaycast(ButtonNamed("Close Zones"));
            yield return null;
            Assert.That(host.IsInLobby, Is.True);
            Assert.That(host.ActiveManifest, Is.Null);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(saveBefore));
        }

        [UnityTest]
        public IEnumerator ZoneSelectionUsesRealEquipmentAndSortieDespiteLowerGrade()
        {
            ZoneCatalog? fixtureCatalog = null;
            try
            {
                yield return SceneManager.UnloadSceneAsync(lobby);
                yield return LoadLobby(Path.Combine(profileDirectory!, "snapshot.json"), setup =>
                {
                    using var hostFields = new SerializedObject(setup);
                    SerializedProperty zones = hostFields.FindProperty("zones");
                    fixtureCatalog = Object.Instantiate((ZoneCatalog)zones.objectReferenceValue);
                    using var catalogFields = new SerializedObject(fixtureCatalog);
                    SerializedProperty castle = catalogFields.FindProperty("zones").GetArrayElementAtIndex(0);
                    Assert.That(castle.FindPropertyRelative("zoneId").intValue, Is.EqualTo(1));
                    castle.FindPropertyRelative("recommendedGrade").intValue = 2;
                    catalogFields.ApplyModifiedPropertiesWithoutUndo();
                    zones.objectReferenceValue = fixtureCatalog;
                    hostFields.ApplyModifiedPropertiesWithoutUndo();
                });

                ClickByRaycast(ButtonNamed("Open Zones"));
                yield return null;
                Assert.That(VisibleZoneList().Find("Zone 1/Recommended Grade").GetComponent<TMP_Text>().text,
                    Is.EqualTo("2"));
                ClickByRaycast(ButtonNamed("Select Zone 1"));
                yield return null;
                ClickByRaycast(ButtonNamed("Cancel"));
                yield return null;
                Assert.That(VisibleZoneList(), Is.Not.Null);

                ClickByRaycast(ButtonNamed("Select Zone 1"));
                yield return null;
                ClickByRaycast(WarehouseSwordButton());
                UnityEngine.UI.Button confirm = ButtonNamed("Confirm");
                ClickByRaycast(confirm);
                confirm.onClick.Invoke();
                yield return WaitUntilOrFail(() => host.ActiveRoom != null, "Selecting Zone 1 did not load the actual room.");
                Assert.That(host.ActiveRoom!.Run.CurrentRun!.ZoneId, Is.EqualTo(1));
                Assert.That(host.ActiveManifest!.Equipped.Single().Id, Is.EqualTo(sword.Id));
                AssertReserved(sword, host.ActiveRoom.Run.CurrentRun.Id, revision: 2);
            }
            finally
            {
                if (fixtureCatalog)
                    Object.Destroy(fixtureCatalog);
            }
        }

        private IEnumerator AssertReopenedZonesReadDurableUnlock()
        {
            string path = Path.Combine(profileDirectory!, "snapshot.json");
            byte[] saveBefore = File.ReadAllBytes(path);
            ClickByRaycast(ButtonNamed("Open Zones"));
            yield return null;
            Transform list = VisibleZoneList();
            TMP_Text prison = list.Find("Zone 2/Entry Status").GetComponent<TMP_Text>();
            Assert.That(prison.text, Is.EqualTo("해금 · 입장 준비 중"));
            Assert.That(prison.text.All(glyph => prison.font.HasCharacter(glyph)), Is.True);
            Assert.That(VisibleButtons("Select Zone 2"), Is.Empty);
            Assert.That(list.Find("Zone 3/Entry Status").GetComponent<TMP_Text>().text, Is.EqualTo("잠김"));
            Assert.That(host.ActiveManifest, Is.Null);
            ClickByRaycast(ButtonNamed("Close Zones"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(saveBefore));
        }

        private Transform VisibleZoneList() => lobby.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<RectTransform>())
            .Single(item => item.name == "Zone List");

        private static void AssertZoneRow(Transform list, int number, string name, string recommended, string reward, string time)
        {
            Transform row = list.Find($"Zone {number}");
            Assert.That(row.Find("Zone Name").GetComponent<TMP_Text>().text, Is.EqualTo($"{number}. {name}"));
            Assert.That(row.Find("Recommended Grade").GetComponent<TMP_Text>().text, Is.EqualTo(recommended));
            Assert.That(row.Find("Reward Grades").GetComponent<TMP_Text>().text, Is.EqualTo(reward));
            Assert.That(row.Find("Time Limit").GetComponent<TMP_Text>().text, Is.EqualTo(time));
        }
    }
}
#endif
