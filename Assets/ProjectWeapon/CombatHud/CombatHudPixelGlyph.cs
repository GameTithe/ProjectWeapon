#nullable enable

using UnityEngine;

namespace ProjectWeapon.CombatHud
{
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    internal sealed class CombatHudPixelGlyph : UnityEngine.UI.MaskableGraphic
    {
        internal enum Shape { Skill, Aim, Dodge, Exit }

        private static readonly string[][] Pixels =
        {
            new[] { ".......#.", "......##.", ".....###.", "...####..", ".####....",
                "..#.#....", "....#....", "........." },
            new[] { "....#....", "....#....", "..#####..", "..#...#..", "###.#.###",
                "..#...#..", "..#####..", "....#....", "....#...." },
            new[] { "......##.", "......##.", ".###.#...", "....###..", "..###.##.",
                "....#....", "...#.#...", "..#...##.", "........." },
            new[] { "..#####..", ".##...##.", ".#..#..#.", ".#...#.#.", ".#..###..",
                ".#...#.#.", ".#..#..#.", ".##...##.", "..#####.." }
        };

        private Shape shape;

        internal void Show(Shape value)
        {
            if (shape == value)
                return;

            shape = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            string[] rows = Pixels[(int)shape];
            Rect rect = GetPixelAdjustedRect();
            float cell = Mathf.Min(rect.width / rows[0].Length, rect.height / rows.Length);
            float left = rect.center.x - cell * rows[0].Length / 2f;
            float top = rect.center.y + cell * rows.Length / 2f;

            for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                if (rows[y][x] != '#')
                    continue;

                float x0 = left + x * cell;
                float y0 = top - (y + 1) * cell;
                int vertex = mesh.currentVertCount;
                mesh.AddVert(new Vector3(x0, y0), color, Vector2.zero);
                mesh.AddVert(new Vector3(x0, y0 + cell), color, Vector2.zero);
                mesh.AddVert(new Vector3(x0 + cell, y0 + cell), color, Vector2.zero);
                mesh.AddVert(new Vector3(x0 + cell, y0), color, Vector2.zero);
                mesh.AddTriangle(vertex, vertex + 1, vertex + 2);
                mesh.AddTriangle(vertex, vertex + 2, vertex + 3);
            }
        }
    }
}
