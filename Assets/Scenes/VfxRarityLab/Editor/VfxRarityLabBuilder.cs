#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProjectWeapon.VfxRarityLab.Editor
{
    public static class VfxRarityLabBuilder
    {
        private const string Folder = "Assets/Scenes/VfxRarityLab/";
        private const string Scene = Folder + "VfxRarityLab.unity";

        public static void Create()
        {
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset)
                throw new InvalidOperationException("The sample uses the project's URP 2D pipeline.");

            Texture2D room = Import("ComparisonRoom");
            Texture2D target = Import("TrainingTarget");
            Texture2D atlas = Import("RaritySlashFrames");
            var hero = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProjectWeapon/Art/Wanderer/WandererAttack.png");
            if (hero == null || hero.filterMode != FilterMode.Point || hero.width != 128 || hero.height != 192)
                throw new InvalidOperationException("The comparison must use the project Wanderer 32-pixel sprite grid.");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Gameplay Camera", typeof(Camera), typeof(PixelPerfectCamera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 270f / 64f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(22, 21, 32, 255);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            var pixels = cameraObject.GetComponent<PixelPerfectCamera>();
            pixels.assetsPPU = 32;
            pixels.refResolutionX = 480;
            pixels.refResolutionY = 270;
            pixels.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            pixels.cropFrame = PixelPerfectCamera.CropFrame.None;

            var previewObject = new GameObject("VFX Rarity Comparison", typeof(VfxRarityLab), typeof(VfxRarityBenchmark));
            using var fields = new SerializedObject(previewObject.GetComponent<VfxRarityLab>());
            fields.FindProperty("room").objectReferenceValue = room;
            fields.FindProperty("target").objectReferenceValue = target;
            fields.FindProperty("hero").objectReferenceValue = hero;
            fields.FindProperty("atlas").objectReferenceValue = atlas;
            fields.FindProperty("spriteMaterial").objectReferenceValue = Material();
            fields.ApplyModifiedPropertiesWithoutUndo();

            if (!EditorSceneManager.SaveScene(scene, Scene))
                throw new InvalidOperationException("Could not save the standalone VFX comparison scene.");
        }

        public static void BuildWindows()
        {
            if (!File.Exists(Scene))
                Create();

            string[] arguments = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(arguments, "-buildOutput");
            if (flag < 0 || flag + 1 >= arguments.Length)
                throw new ArgumentException("Supply the desktop player path using unity build --output.");

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = arguments[flag + 1],
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.StrictMode
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("The VFX lab Windows player build failed: " + report.summary.result);
        }

        private static Texture2D Import(string name)
        {
            string path = Folder + "Art/" + name + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.spriteImportMode = SpriteImportMode.None;
            importer.spritePixelsPerUnit = VfxRarityLab.PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material Material()
        {
            const string path = Folder + "Art/VfxSpriteUnlit.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
                throw new InvalidOperationException("The URP 2D sprite shader is missing.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
