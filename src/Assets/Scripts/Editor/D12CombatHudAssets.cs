using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    /// <summary>Only the D12 Adventure HUD consumes the approved V3 SVG/CSS exports.</summary>
    internal static class D12CombatHudAssets
    {
        private const string Folder = "Assets/Art/UI/Guanzhong/CombatV3/";
        internal static void Import()
        {
            foreach (string path in Directory.GetFiles(Folder, "*.png"))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.spritePixelsPerUnit = 100;
                importer.spriteBorder = Path.GetFileName(path) is "panel.png" or "button.png" or "confirm.png" or "end.png"
                    ? Vector4.one * 2 : Vector4.zero;
                importer.SaveAndReimport();
            }
        }
        internal static Sprite Sprite(string name) => SceneBuildSupport.RequireAsset<Sprite>(Folder + name + ".png");
        internal static void Surface(Image image, string name)
        {
            image.sprite = name == null ? null : Sprite(name);
            image.type = name is "panel" or "button" or "confirm" or "end" ? Image.Type.Sliced : Image.Type.Simple;
            image.color = name == null ? Color.clear : Color.white;
        }
        internal static void Button(Button button, string surface = "button")
        {
            Surface(button.image, surface);
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock { normalColor = Color.white, highlightedColor = new Color(1.15f, 1.15f, 1.1f),
                pressedColor = new Color(.75f, .8f, .8f), selectedColor = Color.white,
                disabledColor = new Color(.65f, .65f, .65f, .8f), colorMultiplier = 1, fadeDuration = .08f };
            button.image.raycastTarget = true;
            Text label = button.GetComponentInChildren<Text>(true);
            label.font = SettlementSceneBuilder.UiFont;
            label.fontStyle = FontStyle.Normal; label.resizeTextForBestFit = false;
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.one * 2; label.rectTransform.offsetMax = Vector2.one * -2;
            label.color = surface == "confirm" ? new Color32(11, 28, 34, 255) : new Color32(238, 231, 210, 255);
        }
    }
}
