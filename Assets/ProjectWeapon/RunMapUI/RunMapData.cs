#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Run;
using UnityEngine;

[assembly: InternalsVisibleTo("ProjectWeapon.RunMapUI.Tests")]

namespace ProjectWeapon.RunMapUI
{
    public readonly struct RunMapRoom
    {
        public RunMapRoom(string roomId, Vector2Int gridCell)
        {
            RoomId = roomId;
            GridCell = gridCell;
        }

        public string RoomId { get; }
        public Vector2Int GridCell { get; }
    }

    public readonly struct RunMapConnection
    {
        public RunMapConnection(string from, string to)
        {
            From = from;
            To = to;
        }

        public string From { get; }
        public string To { get; }
    }

    /// <summary>Room history supplied by the current run's layout owner.</summary>
    public sealed class RunMapData
    {
        public RunMapData(RunId runId, int zoneId, IEnumerable<RunMapRoom> layout,
            IEnumerable<string> visitedRoomIds, IEnumerable<RunMapConnection> confirmedConnections, string? currentRoomId)
        {
            if (!runId.IsValid)
                throw new ArgumentException("A valid run ID is required.", nameof(runId));
            if (zoneId <= 0)
                throw new ArgumentOutOfRangeException(nameof(zoneId));

            RunId = runId;
            ZoneId = zoneId;
            Layout = Copy(layout, nameof(layout));
            VisitedRoomIds = Copy(visitedRoomIds, nameof(visitedRoomIds));
            ConfirmedConnections = Copy(confirmedConnections, nameof(confirmedConnections));
            CurrentRoomId = currentRoomId;
        }

        public RunId RunId { get; }
        public int ZoneId { get; }
        public IReadOnlyList<RunMapRoom> Layout { get; }
        public IReadOnlyList<string> VisitedRoomIds { get; }
        public IReadOnlyList<RunMapConnection> ConfirmedConnections { get; }
        public string? CurrentRoomId { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string name)
            => Array.AsReadOnly((values ?? throw new ArgumentNullException(name)).ToArray());
    }

    internal sealed class RunMapDisplay
    {
        private RunMapDisplay(RunSession session, string zoneName, IReadOnlyList<RunMapRoom> rooms,
            IReadOnlyList<RunMapConnection> connections, string? currentRoom)
        {
            ZoneId = session.ZoneId;
            ZoneName = zoneName;
            Rooms = rooms;
            Connections = connections;
            CurrentRoomId = currentRoom;
        }

        public int ZoneId { get; }
        public string ZoneName { get; }
        public IReadOnlyList<RunMapRoom> Rooms { get; }
        public IReadOnlyList<RunMapConnection> Connections { get; }
        public string? CurrentRoomId { get; }

        public static RunSession? LiveSession(RunState run)
            => run.Phase is RunPhase.Exploring or RunPhase.Extracting ? run.CurrentRun : null;

        public static RunMapDisplay? Read(RunState run, ZoneCatalog catalog, RunMapData? data)
        {
            RunSession? session = LiveSession(run);
            if (session == null)
                return null;

            string zoneName = catalog.GetZones().FirstOrDefault(zone => zone.ZoneId == session.ZoneId)?.Theme ?? string.Empty;
            if (data == null || data.RunId != session.Id || data.ZoneId != session.ZoneId)
                return new RunMapDisplay(session, zoneName, Array.Empty<RunMapRoom>(), Array.Empty<RunMapConnection>(), null);

            var visited = new HashSet<string>(data.VisitedRoomIds, StringComparer.Ordinal);
            var shown = new HashSet<string>(StringComparer.Ordinal);
            var cells = new HashSet<Vector2Int>();
            var rooms = new List<RunMapRoom>();
            foreach (RunMapRoom room in data.Layout)
            {
                if (string.IsNullOrWhiteSpace(room.RoomId) || !visited.Contains(room.RoomId)
                    || shown.Contains(room.RoomId) || !cells.Add(room.GridCell))
                    continue;
                shown.Add(room.RoomId);
                rooms.Add(room);
            }

            var unique = new HashSet<(string, string)>();
            var connections = new List<RunMapConnection>();
            foreach (RunMapConnection link in data.ConfirmedConnections)
            {
                if (link.From == link.To || !shown.Contains(link.From) || !shown.Contains(link.To))
                    continue;
                var key = string.CompareOrdinal(link.From, link.To) < 0 ? (link.From, link.To) : (link.To, link.From);
                if (unique.Add(key))
                    connections.Add(link);
            }

            string? current = data.CurrentRoomId is string id && shown.Contains(id) ? id : null;
            return new RunMapDisplay(session, zoneName, rooms.AsReadOnly(), connections.AsReadOnly(), current);
        }
    }
}
