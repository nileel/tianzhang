using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TianZhang.Game;

namespace TianZhang.Editor
{
    /// <summary>
    /// 门派选择UI快速搭建工具
    /// 使用方法：菜单 → 天章/搭建门派选择UI
    /// </summary>
    public class SectSelectionSetup
    {
        [MenuItem("天章/搭建门派选择UI")]
        static void SetupSectSelectionUI()
        {
            // 确保 GameManager 存在
            var gm = Object.FindFirstObjectByType<GameManager>();
            if (gm == null)
            {
                var gmGo = new GameObject("GameManager", typeof(GameManager));
                gm = gmGo.GetComponent<GameManager>();
                Debug.Log("[Setup] Created GameManager");
            }

            // 移除旧的选择面板
            var existing = GameObject.Find("SectSelectionPanel");
            if (existing != null)
                Object.DestroyImmediate(existing);

            // 查找或创建 UICanvas（与 BattleUIManager 共享）
            var uiCanvas = GameObject.Find("UICanvas");
            Transform canvasParent;
            if (uiCanvas != null)
            {
                canvasParent = uiCanvas.transform;
                Debug.Log("[Setup] Using existing UICanvas");
            }
            else
            {
                uiCanvas = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var canvas = uiCanvas.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
                var scaler = uiCanvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                canvasParent = uiCanvas.transform;
                Debug.Log("[Setup] Created UICanvas");
            }

            var canvasGo = new GameObject("SectSelectionPanel", typeof(RectTransform));

            // 挂到 UICanvas 下
            canvasGo.transform.SetParent(canvasParent, false);
            var panelRt = canvasGo.GetComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero; panelRt.anchorMax = Vector2.one;
            panelRt.sizeDelta = Vector2.zero;

            // 背景遮罩
            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            bgGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);

            // 标题
            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleGo.transform.SetParent(canvasGo.transform, false);
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f); titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0, -60);
            titleRt.sizeDelta = new Vector2(400, 50);
            var title = titleGo.GetComponent<Text>();
            title.text = "选择门派";
            title.fontSize = 36;
            title.color = Color.white;

            // 按钮容器
            var btnContainerGo = new GameObject("ButtonContainer", typeof(RectTransform));
            btnContainerGo.transform.SetParent(canvasGo.transform, false);
            var btnRt = btnContainerGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.5f, 0.5f); btnRt.anchorMax = new Vector2(0.5f, 0.5f);
            btnRt.anchoredPosition = new Vector2(0, 60);
            btnRt.sizeDelta = new Vector2(300, 300);

            // 选中文字
            var selectedGo = new GameObject("SelectedText", typeof(RectTransform), typeof(Text));
            selectedGo.transform.SetParent(canvasGo.transform, false);
            var selRt = selectedGo.GetComponent<RectTransform>();
            selRt.anchorMin = new Vector2(0.5f, 0f); selRt.anchorMax = new Vector2(0.5f, 0f);
            selRt.anchoredPosition = new Vector2(0, 180);
            selRt.sizeDelta = new Vector2(500, 30);
            var selText = selectedGo.GetComponent<Text>();
            selText.text = "";
            selText.fontSize = 18;
            selText.color = Color.yellow;

            // 描述文字
            var descGo = new GameObject("DescText", typeof(RectTransform), typeof(Text));
            descGo.transform.SetParent(canvasGo.transform, false);
            var descRt = descGo.GetComponent<RectTransform>();
            descRt.anchorMin = new Vector2(0.5f, 0f); descRt.anchorMax = new Vector2(0.5f, 0f);
            descRt.anchoredPosition = new Vector2(0, 140);
            descRt.sizeDelta = new Vector2(600, 50);
            var descText = descGo.GetComponent<Text>();
            descText.text = "";
            descText.fontSize = 14;
            descText.color = Color.gray;

            // 开始按钮
            var startGo = new GameObject("StartButton", typeof(RectTransform), typeof(Image), typeof(Button));
            startGo.transform.SetParent(canvasGo.transform, false);
            var startRt = startGo.GetComponent<RectTransform>();
            startRt.anchorMin = new Vector2(0.5f, 0f); startRt.anchorMax = new Vector2(0.5f, 0f);
            startRt.anchoredPosition = new Vector2(0, 80);
            startRt.sizeDelta = new Vector2(200, 50);
            startGo.GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.3f, 1f);
            var startLabelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            startLabelGo.transform.SetParent(startGo.transform, false);
            var startLabelRt = startLabelGo.GetComponent<RectTransform>();
            startLabelRt.anchorMin = Vector2.zero; startLabelRt.anchorMax = Vector2.one;
            startLabelRt.sizeDelta = Vector2.zero;
            var startLabel = startLabelGo.GetComponent<Text>();
            startLabel.text = "开始游戏";
            startLabel.fontSize = 20;
            startLabel.color = Color.white;

            // 添加 SectSelectionManager
            var ssm = canvasGo.AddComponent<SectSelectionManager>();
            var smSo = new SerializedObject(ssm);
            smSo.FindProperty("selectionPanel").objectReferenceValue = canvasGo;
            smSo.FindProperty("buttonContainer").objectReferenceValue = btnContainerGo.transform;
            smSo.FindProperty("startButton").objectReferenceValue = startGo.GetComponent<Button>();
            smSo.FindProperty("selectedSectText").objectReferenceValue = selText;
            smSo.FindProperty("selectedSectDesc").objectReferenceValue = descText;
            smSo.FindProperty("gameManager").objectReferenceValue = gm;
            smSo.ApplyModifiedProperties();

            Debug.Log("[SectSelectionSetup] 门派选择UI搭建完成！运行游戏即可看到");
        }
    }
}
