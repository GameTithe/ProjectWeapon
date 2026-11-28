#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectWeapon.RunMapUI
{
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class RunMapGraphic : MaskableGraphic
    {
        private static readonly Color Link = new Color32(115, 132, 154, 255);
        private static readonly Color Room = new Color32(160, 175, 184, 255);
        private static readonly Color Interior = new Color32(61, 58, 79, 255);
        private static readonly Color Current = new Color32(249, 196, 131, 255);

        private RunMapDisplay? display;

        public void Render(RunMapDisplay map)
        {
            display = map;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (display == null || display.Rooms.Count == 0)
                return;

            Rect area = GetPixelAdjustedRect();
            float width = area.width - 52;
            float height = area.height - 52;
            if (width <= 0 || height <= 0)
                return;

            int left = int.MaxValue;
            int right = int.MinValue;
            int bottom = int.MaxValue;
            int top = int.MinValue;
            foreach (RunMapRoom room in display.Rooms)
            {
                left = Math.Min(left, room.GridCell.x);
                right = Math.Max(right, room.GridCell.x);
                bottom = Math.Min(bottom, room.GridCell.y);
                top = Math.Max(top, room.GridCell.y);
            }

            double midX = (left + (double)right) / 2;
            double midY = (bottom + (double)top) / 2;
            double scale = Math.Min(width / Math.Max(1, (double)right - left),
                height / Math.Max(1, (double)top - bottom));
            var centers = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            foreach (RunMapRoom room in display.Rooms)
            {
                Vector2Int cell = room.GridCell;
                centers.Add(room.RoomId, area.center + new Vector2((float)((cell.x - midX) * scale),
                    (float)((cell.y - midY) * scale)));
            }

            foreach (RunMapConnection link in display.Connections)
            {
                Vector2 from = centers[link.From];
                Vector2 to = centers[link.To];
                Vector2 side = Vector2.Perpendicular(to - from).normalized * 2;
                if (side != Vector2.zero)
                    Quad(mesh, from - side, from + side, to + side, to - side, Link);
            }

            foreach (RunMapRoom room in display.Rooms)
            {
                Vector2 center = centers[room.RoomId];
                bool current = room.RoomId == display.CurrentRoomId;
                Box(mesh, center, 29, 25, current ? Current : Room);
                Box(mesh, center, 21, 17, Interior);
                if (current)
                    Box(mesh, center, 7, 7, Current);
            }
        }

        private static void Box(VertexHelper mesh, Vector2 center, float width, float height, Color color)
        {
            Vector2 half = new Vector2(width, height) / 2;
            Vector2 min = center - half;
            Vector2 max = center + half;
            Quad(mesh, min, new Vector2(min.x, max.y), max, new Vector2(max.x, min.y), color);
        }

        private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero);
            mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero);
            mesh.AddVert(d, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
