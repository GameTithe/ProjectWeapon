using UnityEngine;
using UnityEngine.UI;

namespace ProjectWeapon.CatalogUI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CatalogGlyph : MaskableGraphic
    {
        private static readonly string[][] Shapes =
        {
            new[]
            {
                ".........##", "........###", ".......####", "......####.", ".....####..", "....####...",
                "..####.....", "...###.....", "..###......", ".##.##.....", "##........."
            },
            new[]
            {
                "........###", "........###", ".......###.", "......###..", "......##...", ".....##....",
                "....##.....", "...##......", "..##.......", ".##........", "##........."
            },
            new[]
            {
                "...####....", "...#...##..", "...#.....#.", "...#......#", "...#......#", "...#......#",
                "...#......#", "...#.....#.", "...#...##..", "...####....", "..........."
            },
            new[]
            {
                "......####.", ".....######", ".....######", ".....######", ".....#####.", "....###....",
                "...###.....", "..###......", ".###.......", "###........", "##........."
            },
            new[]
            {
                "...........", "..........#", "........###", "......####.", "....#####..", "...#####...",
                "..####.....", "...##......", "..####.....", ".##........", "##........."
            },
            new[]
            {
                "........###", "......#####", "....#######", "....#######", ".....#####.", ".....##....",
                "....##.....", "...##......", "..##.......", ".##........", "##........."
            },
            new[]
            {
                "...#####...", ".#########.", "##......##.", "#.......##.", ".......##..", "......##...",
                ".....##....", "....##.....", "...##......", "..##.......", ".##........"
            },
            new[]
            {
                "##.......##", "###.....###", ".###...###.", "..###.###..", "...#####...", "....###....",
                "...#####...", "..#######..", ".##.....##.", "##.......##", "#.........#"
            }
        };

        private WeaponSilhouette shape;

        internal void SetShape(WeaponSilhouette silhouette)
        {
            shape = silhouette;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect bounds = GetPixelAdjustedRect();
            string[] pixels = Shapes[(int)shape];
            int columns = pixels[0].Length;
            float unit = Mathf.Min(bounds.width / columns, bounds.height / pixels.Length);
            Vector2 origin = bounds.center - new Vector2(columns, pixels.Length) * (unit * .5f);
            for (int row = 0; row < pixels.Length; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    if (pixels[row][column] != '#')
                        continue;
                    Vector2 min = origin + new Vector2(column, pixels.Length - row - 1) * unit;
                    int vertex = mesh.currentVertCount;
                    mesh.AddVert(min, color, Vector2.zero);
                    mesh.AddVert(min + Vector2.up * unit, color, Vector2.zero);
                    mesh.AddVert(min + Vector2.one * unit, color, Vector2.zero);
                    mesh.AddVert(min + Vector2.right * unit, color, Vector2.zero);
                    mesh.AddTriangle(vertex, vertex + 1, vertex + 2);
                    mesh.AddTriangle(vertex, vertex + 2, vertex + 3);
                }
            }
        }
    }
}
