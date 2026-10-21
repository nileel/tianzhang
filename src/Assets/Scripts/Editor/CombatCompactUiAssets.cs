using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    /// <summary>The V4 asset kit belongs only to Adventure's combat UI.</summary>
    internal static class CombatCompactUiAssets
    {
        internal const string Folder = "Assets/Art/UI/Guanzhong/CombatCompact/";
        private const string Source = "../assets/generated-scene-art/guanzhong-first-bounty/compact-ui-20261021/";
        private static Sprite[] sprites;
        [Serializable] private sealed class Manifest { public Slice[] slices; }
        [Serializable] private sealed class Slice
        {
            public string name;
            public float[] rectUnityBottomLeft;
            public float[] borderLTRB;
            public float[] pivot;
        }

        internal static void Import()
        {
            Directory.CreateDirectory(Folder);
            foreach (string file in new[] { "compact-ui-atlas.png", "portraits/enemy_shijiahou.png", "portraits/enemy_lieshijia.png" })
            {
                string target = Folder + Path.GetFileName(file);
                File.Copy(Source + file, target, true);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(target);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = file.StartsWith("portraits/") ? SpriteImportMode.Single : SpriteImportMode.Multiple;
                importer.spritePixelsPerUnit = 100;
                importer.sRGBTexture = true;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                if (importer.spriteImportMode == SpriteImportMode.Multiple)
                {
                    Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Source + "manifest.json"));
                    // Same importer API as the project's other atlas importer; no runtime SVG dependency.
#pragma warning disable CS0618
                    importer.spritesheet = manifest.slices.Select(slice => new SpriteMetaData
                    {
                        name = slice.name,
                        rect = new Rect(slice.rectUnityBottomLeft[0], slice.rectUnityBottomLeft[1], slice.rectUnityBottomLeft[2], slice.rectUnityBottomLeft[3]),
                        border = new Vector4(slice.borderLTRB[0], slice.borderLTRB[3], slice.borderLTRB[2], slice.borderLTRB[1]),
                        alignment = (int)SpriteAlignment.Custom,
                        pivot = new Vector2(slice.pivot[0], slice.pivot[1]),
                    }).ToArray();
#pragma warning restore CS0618
                }
                importer.SaveAndReimport();
            }
            sprites = null;
        }

        internal static Sprite Sprite(string name)
        {
            sprites ??= AssetDatabase.LoadAllAssetsAtPath(Folder + "compact-ui-atlas.png").OfType<Sprite>().ToArray();
            return sprites.Single(sprite => sprite.name == name);
        }

        internal static void Panel(GameObject panel)
        {
            Image image = panel.GetComponent<Image>();
            image.sprite = Sprite("panel_dark");
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1;
            image.color = Color.white;
        }

        internal static void Button(Button button, bool endTurn = false)
        {
            button.image.sprite = Sprite(endTurn ? "button_end" : "button_normal");
            button.image.type = Image.Type.Sliced;
            button.image.pixelsPerUnitMultiplier = 1;
            button.image.color = Color.white;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = Sprite("button_hover"), pressedSprite = Sprite("button_pressed"),
                selectedSprite = Sprite("button_selected"), disabledSprite = Sprite("button_disabled"),
            };
            Text label = button.GetComponentInChildren<Text>(true);
            label.fontSize = 24;
            label.color = new Color32(239, 233, 211, 255);
            label.raycastTarget = false;
        }
    }
}
