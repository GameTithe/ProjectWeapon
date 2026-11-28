#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Run;
using UnityEditor;
using UnityEngine;

namespace ProjectWeapon.RunMapUI.Tests
{
    public sealed class RunMapDisplayTests
    {
        private TimeSpan now;
        private RunState run = null!;
        private RunId id;
        private ZoneCatalog catalog = null!;

        [SetUp]
        public void Prepare()
        {
            now = TimeSpan.Zero;
            run = new RunState(() => now);
            id = RunId.New();
            catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>("Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
        }

        [Test]
        public void OnlyVisitedRoomsAndConfirmedConnectionsAppear()
        {
            Start(1);
            var data = new RunMapData(id, 1,
                new[] { Room("entry", 0, 0), Room("hall", 1, 0), Room("guard", 1, 1), Room("hidden", 99, 99) },
                new[] { "entry", "hall", "guard" },
                new[] { Edge("entry", "hall"), Edge("hall", "hidden"), Edge("hall", "entry") }, "hall");

            RunMapDisplay display = Read(data);
            Assert.That(display.ZoneId, Is.EqualTo(1));
            Assert.That(display.ZoneName, Is.EqualTo(catalog.GetZone(1).Theme));
            Assert.That(display.Rooms.Select(room => room.RoomId), Is.EqualTo(new[] { "entry", "hall", "guard" }));
            Assert.That(display.Connections.Select(edge => (edge.From, edge.To)), Is.EqualTo(new[] { ("entry", "hall") }));
            Assert.That(display.CurrentRoomId, Is.EqualTo("hall"));
        }

        [Test]
        public void UnknownCurrentRoomAndMalformedConnectionsDoNotExposeAnyAdditionalGeometry()
        {
            Start(1);
            var data = new RunMapData(id, 1,
                new[] { default, Room("entry", 0, 0), Room("overlap", 0, 0), Room("secret", 4, 3) },
                new[] { "entry", "unmodeled", "overlap" },
                new[] { default, Edge("entry", "entry"), Edge("entry", "secret"), Edge("entry", "unmodeled") }, "secret");

            RunMapDisplay display = Read(data);
            Assert.That(display.Rooms.Select(room => room.RoomId), Is.EqualTo(new[] { "entry" }));
            Assert.That(display.Connections, Is.Empty);
            Assert.That(display.CurrentRoomId, Is.Null);
        }

        [Test]
        public void MissingOrMismatchedMapKeepsTheLiveZoneHeading()
        {
            Start(2);
            var staleZone = new RunMapData(id, 1, new[] { Room("old", 0, 0) }, new[] { "old" },
                Array.Empty<RunMapConnection>(), "old");
            var staleRun = new RunMapData(RunId.New(), 2, new[] { Room("old", 0, 0) }, new[] { "old" },
                Array.Empty<RunMapConnection>(), "old");

            foreach (RunMapData? data in new[] { null, staleZone, staleRun })
            {
                RunMapDisplay display = Read(data);
                Assert.That(display.ZoneId, Is.EqualTo(2));
                Assert.That(display.ZoneName, Is.EqualTo(catalog.GetZone(2).Theme));
                Assert.That(display.Rooms, Is.Empty);
                Assert.That(display.Connections, Is.Empty);
                Assert.That(display.CurrentRoomId, Is.Null);
            }
        }

        [Test]
        public void DataIsAnImmutableRoomVisitSampleAndSupportsOneRoomWithoutEdges()
        {
            Start(1);
            var visited = new[] { "entry" };
            var layout = new[] { Room("entry", int.MinValue, int.MaxValue) };
            var data = new RunMapData(id, 1, layout, visited, Array.Empty<RunMapConnection>(), "entry");
            visited[0] = "new";
            layout[0] = Room("new", 0, 0);

            RunMapDisplay display = Read(data);
            Assert.That(display.Rooms.Single().RoomId, Is.EqualTo("entry"));
            Assert.That(display.CurrentRoomId, Is.EqualTo("entry"));
            Assert.That(display.Connections, Is.Empty);
        }

        [Test]
        public void MapGraphicProducesRoomMarkersAndLinksOnItsCanvasRenderer()
        {
            Start(1);
            RunMapDisplay display = Read(new RunMapData(id, 1,
                new[] { Room("left", 0, 0), Room("right", 1, 0), Room("secret", 2, 0) },
                new[] { "left", "right" }, new[] { Edge("left", "right"), Edge("right", "secret") }, "right"));
            var canvas = new GameObject("Map Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var target = new GameObject("Map Graphic", typeof(RectTransform));
                target.transform.SetParent(canvas.transform, false);
                var graphic = target.AddComponent<RunMapGraphic>();
                graphic.rectTransform.sizeDelta = new Vector2(400, 200);
                graphic.Render(display);
                graphic.Rebuild(UnityEngine.UI.CanvasUpdate.PreRender);
                var renderer = target.GetComponent<CanvasRenderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.GetMesh().vertexCount, Is.EqualTo(24));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        [Test]
        public void ExtractionCanReadMapButTerminalAndNextRunCannotReuseIt()
        {
            Assert.That(RunMapDisplay.Read(run, catalog, null), Is.Null);
            Start(1);
            var oldData = new RunMapData(id, 1, new[] { Room("entry", 0, 0) }, new[] { "entry" },
                Array.Empty<RunMapConnection>(), "entry");
            Assert.That(run.TryBeginExtraction(id, true), Is.Not.Null);
            Assert.That(Read(oldData).CurrentRoomId, Is.EqualTo("entry"));

            now = TimeSpan.FromSeconds(5);
            Assert.That(run.ProcessSignals(Array.Empty<RunSignal>())?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(RunMapDisplay.Read(run, catalog, oldData), Is.Null);
            Assert.That(run.TryReturnToTown(id), Is.True);

            id = RunId.New();
            Start(2);
            Assert.That(Read(oldData).Rooms, Is.Empty);
            Assert.That(Read(oldData).ZoneId, Is.EqualTo(2));
        }

        private RunMapDisplay Read(RunMapData? data) => RunMapDisplay.Read(run, catalog, data)!;

        private void Start(int zoneId)
            => Assert.That(run.TryStartRun(id, zoneId, TimeSpan.FromSeconds(5)), Is.True);

        private static RunMapRoom Room(string room, int column, int row)
            => new RunMapRoom(room, new Vector2Int(column, row));

        private static RunMapConnection Edge(string from, string to) => new RunMapConnection(from, to);
    }
}
