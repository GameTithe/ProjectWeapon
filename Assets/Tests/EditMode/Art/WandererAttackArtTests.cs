using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectWeapon.Art.Tests
{
    public sealed class WandererAttackArtTests
    {
        private const string ArtPath = "Assets/ProjectWeapon/Art/Wanderer/";
        private const string AttackPath = ArtPath + "WandererAttack.png";
        private const string SwordPath = ArtPath + "WornSword.png";

        [Test]
        public void AttackSheetImportsFourDistinctUnclippedFramesPerDirection()
        {
            AssertPixelArtImport(AttackPath, SpriteImportMode.Multiple);
            var frames = AssetDatabase.LoadAllAssetsAtPath(AttackPath).OfType<Sprite>()
                .ToDictionary(sprite => sprite.name);
            Assert.That(frames.Count, Is.EqualTo(16));
            string[] directions = { "south", "east", "north", "west" };

            var source = LoadPixels(AttackPath);
            try
            {
                Assert.That((source.width, source.height), Is.EqualTo((128, 192)));
                for (int row = 0; row < directions.Length; row++)
                {
                    var poses = new List<Color32[]>();
                    for (int column = 0; column < 4; column++)
                    {
                        Rect bounds = new Rect(column * 32, (3 - row) * 48, 32, 48);
                        Sprite sprite = frames[$"wanderer_attack_{directions[row]}_{column + 1}"];
                        Assert.That(sprite.rect, Is.EqualTo(bounds));
                        Assert.That(sprite.pivot, Is.EqualTo(new Vector2(16, 6)));
                        Color32[] pose = ReadFrame(source, bounds);
                        Assert.That(poses.Any(previous => previous.SequenceEqual(pose)), Is.False, sprite.name);
                        poses.Add(pose);
                    }
                }
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void SwordImportsAtItsNativeSizeWithTheGripAsItsPivot()
        {
            AssertPixelArtImport(SwordPath, SpriteImportMode.Single);
            Sprite sword = AssetDatabase.LoadAssetAtPath<Sprite>(SwordPath);
            Assert.That(sword.rect.size, Is.EqualTo(new Vector2(32, 32)));
            Assert.That(sword.pivot, Is.EqualTo(new Vector2(9.5f, 6.5f)));

            var source = LoadPixels(SwordPath);
            try
            {
                ReadFrame(source, sword.rect);
                Assert.That((Color32)source.GetPixel(9, 6), Is.EqualTo(new Color32(152, 103, 88, 255)));
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static void AssertPixelArtImport(string path, SpriteImportMode mode)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.spriteImportMode, Is.EqualTo(mode));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(32));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.mipmapEnabled, Is.False);
        }

        private static Texture2D LoadPixels(string path)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (ImageConversion.LoadImage(image, File.ReadAllBytes(path)))
                return image;

            Object.DestroyImmediate(image);
            throw new IOException($"Unable to read {path}.");
        }

        private static Color32[] ReadFrame(Texture2D texture, Rect bounds)
        {
            int width = (int)bounds.width;
            int height = (int)bounds.height;
            var pixels = new Color32[width * height];
            int visible = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 pixel = texture.GetPixel((int)bounds.x + x, (int)bounds.y + y);
                    pixels[y * width + x] = pixel;
                    if (pixel.a == 0)
                        continue;

                    visible++;
                    Assert.That(x > 0 && x < width - 1 && y > 0 && y < height - 1, Is.True,
                        $"Artwork touches a cell edge at [{x},{y}] in {bounds}.");
                }
            }

            Assert.That(visible, Is.GreaterThan(0), $"Empty sprite cell {bounds}.");
            return pixels;
        }
    }
}
