#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectWeapon.VfxRarityLab
{
    public sealed class VfxRarityLab : MonoBehaviour
    {
        public const int Grades = 5;
        public const int Frames = 5;
        public const int PixelsPerUnit = 32;
        internal const float FrameSeconds = 0.072f;
        private const int Tile = 88;
        private static readonly int[] Centers = { 144, 232, 320, 408, 496 };

        [SerializeField] private Texture2D room = null!;
        [SerializeField] private Texture2D target = null!;
        [SerializeField] private Texture2D hero = null!;
        [SerializeField] private Texture2D atlas = null!;
        [SerializeField] private Material spriteMaterial = null!;

        private readonly SpriteRenderer[] effects = new SpriteRenderer[Grades];
        private readonly SpriteRenderer[] actors = new SpriteRenderer[Grades];
        private readonly Sprite[,,] sprites = new Sprite[2, Grades, Frames];
        private readonly Sprite[] poses = new Sprite[4];
        private readonly List<Sprite> ownedSprites = new List<Sprite>();
        private float elapsed;
        private int solo = -1;
        private bool reduced;
        private bool measuring;

        internal void BeginMeasurement() => measuring = true;
        internal int ActiveEffects
        {
            get
            {
                int active = 0;
                foreach (SpriteRenderer effect in effects)
                    if (effect.enabled)
                        active++;
                return active;
            }
        }

        private void Awake()
        {
            MakeRenderer("Dungeon and HUD", Entire(room), 0, 320, 144);
            Sprite enemy = Entire(target);
            for (int frame = 0; frame < poses.Length; frame++)
                poses[frame] = MakeSprite(hero, new Rect(frame * 32, hero.height - 48, 32, 48));

            for (int grade = 0; grade < Grades; grade++)
            {
                int center = Centers[grade];
                MakeRenderer($"Target {grade + 1}", enemy, 1, center + 23, 142);
                actors[grade] = MakeRenderer($"Wanderer {grade + 1}", poses[0], 2, center - 18, 136);
                effects[grade] = MakeRenderer($"Slash {grade + 1}", null, 3, center, 137);
                effects[grade].enabled = false;

                for (int mode = 0; mode < sprites.GetLength(0); mode++)
                    for (int frame = 0; frame < Frames; frame++)
                        sprites[mode, grade, frame] = MakeSprite(atlas, new Rect((mode * Frames + frame) * Tile,
                            atlas.height - (grade + 1) * Tile, Tile, Tile));
            }
        }

        private void Update()
        {
            if (measuring)
                return;

            Keyboard? keys = Keyboard.current;
            if (keys?.spaceKey.wasPressedThisFrame == true)
                Replay(-1);
            if (keys?.rKey.wasPressedThisFrame == true)
            {
                reduced = !reduced;
                Replay(solo);
            }
            if (keys?.digit1Key.wasPressedThisFrame == true) Replay(0);
            if (keys?.digit2Key.wasPressedThisFrame == true) Replay(1);
            if (keys?.digit3Key.wasPressedThisFrame == true) Replay(2);
            if (keys?.digit4Key.wasPressedThisFrame == true) Replay(3);
            if (keys?.digit5Key.wasPressedThisFrame == true) Replay(4);

            if (elapsed < 0f)
                return;
            int frame = (int)(elapsed / FrameSeconds);
            if (frame >= Frames)
            {
                elapsed = -1f;
                Pose(0);
                Show(-1, reduced);
                return;
            }
            Pose(frame);
            Show(frame, reduced, solo);
            elapsed += Time.unscaledDeltaTime;
        }

        private void Replay(int selected)
        {
            solo = selected;
            elapsed = 0f;
        }

        internal void Pose(int frame)
        {
            Sprite pose = poses[Mathf.Min(poses.Length - 1, frame)];
            foreach (SpriteRenderer actor in actors)
                if (actor.sprite != pose)
                    actor.sprite = pose;
        }

        internal void Show(int frame, bool lowerOverdraw, int onlyGrade = -1)
        {
            for (int grade = 0; grade < Grades; grade++)
            {
                SpriteRenderer effect = effects[grade];
                bool show = frame >= 0 && (onlyGrade < 0 || grade == onlyGrade);
                if (effect.enabled != show)
                    effect.enabled = show;
                if (!show)
                    continue;

                Sprite sprite = sprites[lowerOverdraw ? 1 : 0, grade, frame];
                if (effect.sprite != sprite)
                    effect.sprite = sprite;
            }
        }

        private Sprite Entire(Texture2D texture) => MakeSprite(texture, new Rect(0, 0, texture.width, texture.height));

        private Sprite MakeSprite(Texture2D texture, Rect rect)
        {
            Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), PixelsPerUnit);
            ownedSprites.Add(sprite);
            return sprite;
        }

        private SpriteRenderer MakeRenderer(string objectName, Sprite? sprite, int order, int x, int y)
        {
            var child = new GameObject(objectName, typeof(SpriteRenderer));
            child.transform.SetParent(transform, false);
            child.transform.localPosition = new Vector3((x - 320f) / PixelsPerUnit, (144f - y) / PixelsPerUnit);
            var renderer = child.GetComponent<SpriteRenderer>();
            renderer.sharedMaterial = spriteMaterial;
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            return renderer;
        }

        private void OnDestroy()
        {
            foreach (Sprite sprite in ownedSprites)
                Destroy(sprite);
        }
    }
}
