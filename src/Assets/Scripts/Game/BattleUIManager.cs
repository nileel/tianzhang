using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TianZhang.Combat;
using TianZhang.Entity;
using GameplayCombatCommandHandler = TianZhang.Gameplay.Contracts.ICombatCommandHandler;

namespace TianZhang.Game
{
    public class BattleUIManager : MonoBehaviour
    {
        [Header("引用")]
        private GameplayCombatCommandHandler combatCommandHandler;
        private string playerCombatantId = string.Empty;
        private string targetCombatantId = string.Empty;
        private string[] spellProfileIds = System.Array.Empty<string>();
        private string[] skillProfileIds = System.Array.Empty<string>();
        private string swapProfileId = string.Empty;

        [Header("Canvas 设置")]
        public float panelMargin = 16f;
        public float barHeight = 18f;

        private Font sharedFont;
        private static Sprite whiteSprite; // Bar填充用

        // ---- 玩家面板 ----
        private GameObject playerPanel;
        private Text playerNameText;
        private Image playerHPFill;
        private Text playerHPText;
        private Image playerMPFill;
        private Text playerMPText;
        private Image playerCTFill;
        private Text playerCTText;
        private Text playerStatusText;

        // ---- 敌方面板 ----
        private GameObject enemyPanel;
        private Text enemyNameText;
        private Image enemyHPFill;
        private Text enemyHPText;
        private Image enemyMPFill;
        private Text enemyMPText;
        private Image enemyCTFill;
        private Text enemyCTText;
        private Text enemyStatusText;
        // 五行元素标签
        private Text playerElementText;
        private Text enemyElementText;

        // ---- 回合提示 ----
        private Text turnBanner;

        // ---- 动作按钮 ----
        private List<GameObject> spellButtons = new List<GameObject>();
        private List<GameObject> skillButtons = new List<GameObject>();
        private Button attackButton;
        private Button guardButton;
        private Button waitButton;
        private Button swapButton;
        private Transform actionBarParent; // 动作栏父节点

        // ---- 战斗日志 ----
        private GameObject logPanel;
        private ScrollRect logScroll;
        private Text logText;
        private int logLineCount;
        private const int MaxLogLines = 200;

        // ---- 帮助面板 ----
        private GameObject helpPanel;
        private bool helpVisible;

        public void SetCombatCommandHandler(GameplayCombatCommandHandler handler)
        {
            combatCommandHandler = handler;
        }

        public void SetCombatCommandContext(
            string playerId,
            string targetId,
            string[] spellIds,
            string[] skillIds,
            string nextSwapProfileId)
        {
            playerCombatantId = playerId ?? string.Empty;
            targetCombatantId = targetId ?? string.Empty;
            spellProfileIds = spellIds ?? System.Array.Empty<string>();
            skillProfileIds = skillIds ?? System.Array.Empty<string>();
            swapProfileId = nextSwapProfileId ?? string.Empty;
        }

        private void Awake()
        {
            sharedFont = Font.CreateDynamicFontFromOSFont("Arial", 14);
            if (whiteSprite == null) { var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply(); whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f)); }
            BuildUI();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame) ToggleHelp();
        }

        // ==================== 构建 UI ====================

        private void BuildUI()
        {
            // 确保 EventSystem 存在（否则 IsPointerOverUI 失效）
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(InputSystemUIInputModule));
                esGo.transform.SetParent(transform);
            }

            var canvas = CreateCanvas();
            BuildPlayerPanel(canvas.transform);
            BuildEnemyPanel(canvas.transform);
            BuildTurnBanner(canvas.transform);
            BuildActionBar(canvas.transform);
            BuildLogPanel(canvas.transform);
            BuildHelpPanel(canvas.transform);

            // 默认隐藏（进入探索/战斗后再按需显示）
            if (enemyPanel != null) enemyPanel.SetActive(false);
            if (actionBarParent != null) actionBarParent.gameObject.SetActive(false);
        }

        private GameObject CreateCanvas()
        {
            var go = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            // Canvas 作为根级对象，不挂在 BattleUIManager 下
            go.transform.SetParent(null);
            if (Application.isPlaying)
                UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return go;
        }

        private GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, Color? bgColor = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            if (bgColor.HasValue) { var img = go.AddComponent<Image>(); img.color = bgColor.Value; }
            return go;
        }

        private Text CreateText(Transform parent, string name, string defaultText, int fontSize,
            TextAnchor alignment, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            var text = go.AddComponent<Text>();
            text.text = defaultText;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color ?? Color.white;
            text.font = sharedFont;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private (Image fill, Text label) CreateBar(Transform parent, string name, Color fillColor, Color bgColor)
        {
            var bgGo = new GameObject(name + "_BG", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(parent, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0, 0.5f);
            bgRt.anchorMax = new Vector2(1, 0.5f);
            bgRt.sizeDelta = new Vector2(0, barHeight);
            bgRt.anchoredPosition = Vector2.zero;
            bgGo.GetComponent<Image>().color = bgColor;

            var fillGo = new GameObject(name + "_Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(bgGo.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = Vector2.zero;
            fillRt.pivot = new Vector2(0, 0.5f);
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.color = fillColor;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.sprite = whiteSprite;

            var label = CreateText(bgGo.transform, name + "_Txt", "0/0", 11, TextAnchor.MiddleCenter);
            return (fillImg, label);
        }

        private Button CreateButton(Transform parent, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(110, 40);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.3f, 0.9f);
            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = new Color(0.25f, 0.25f, 0.35f, 1f);
            colors.highlightedColor = new Color(0.4f, 0.4f, 0.5f, 1f);
            colors.pressedColor = new Color(0.15f, 0.15f, 0.25f, 1f);
            btn.colors = colors;

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;
            var txt = txtGo.AddComponent<Text>();
            txt.text = label;
            txt.fontSize = 14;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.font = sharedFont;
            return btn;
        }

        // ---- 玩家面板（左上）----

        private void BuildPlayerPanel(Transform canvas)
        {
            playerPanel = CreatePanel(canvas, "PlayerPanel",
                new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(40, -200), new Vector2(260, -40),
                new Color(0, 0, 0, 0.6f));

            var layout = playerPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 4;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            playerNameText = CreateText(playerPanel.transform, "Name", "玩家", 20, TextAnchor.UpperLeft, Color.cyan);
            playerNameText.rectTransform.sizeDelta = new Vector2(0, 28);

            playerElementText = CreateText(playerPanel.transform, "Element", "", 16, TextAnchor.UpperLeft, Color.yellow);
            playerElementText.rectTransform.sizeDelta = new Vector2(0, 22);

            var (hpFill, hpLabel) = CreateBar(playerPanel.transform, "HP", Color.red, new Color(0.3f, 0, 0));
            playerHPFill = hpFill; playerHPText = hpLabel;

            var (mpFill, mpLabel) = CreateBar(playerPanel.transform, "MP", Color.blue, new Color(0, 0, 0.3f));
            playerMPFill = mpFill; playerMPText = mpLabel;

            var (ctFill, ctLabel) = CreateBar(playerPanel.transform, "CT", Color.yellow, new Color(0.3f, 0.3f, 0));
            playerCTFill = ctFill; playerCTText = ctLabel;

            playerStatusText = CreateText(playerPanel.transform, "Status", "", 14, TextAnchor.UpperLeft, Color.gray);
            playerStatusText.rectTransform.sizeDelta = new Vector2(0, 22);
        }

        // ---- 敌方面板（右上）----

        private void BuildEnemyPanel(Transform canvas)
        {
            enemyPanel = CreatePanel(canvas, "EnemyPanel",
                new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-260, -200), new Vector2(-40, -40),
                new Color(0, 0, 0, 0.6f));

            var layout = enemyPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 4;
            layout.childAlignment = TextAnchor.UpperRight;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            enemyNameText = CreateText(enemyPanel.transform, "Name", "敌方", 20, TextAnchor.UpperRight, Color.red);
            enemyNameText.rectTransform.sizeDelta = new Vector2(0, 28);

            enemyElementText = CreateText(enemyPanel.transform, "Element", "", 16, TextAnchor.UpperRight, Color.yellow);
            enemyElementText.rectTransform.sizeDelta = new Vector2(0, 22);

            var (hpFill, hpLabel) = CreateBar(enemyPanel.transform, "HP", Color.red, new Color(0.3f, 0, 0));
            enemyHPFill = hpFill; enemyHPText = hpLabel;

            var (mpFill, mpLabel) = CreateBar(enemyPanel.transform, "MP", Color.blue, new Color(0, 0, 0.3f));
            enemyMPFill = mpFill; enemyMPText = mpLabel;

            var (ctFill, ctLabel) = CreateBar(enemyPanel.transform, "CT", Color.yellow, new Color(0.3f, 0.3f, 0));
            enemyCTFill = ctFill; enemyCTText = ctLabel;

            enemyStatusText = CreateText(enemyPanel.transform, "Status", "", 14, TextAnchor.UpperRight, Color.gray);
            enemyStatusText.rectTransform.sizeDelta = new Vector2(0, 22);
        }

        // ---- 回合提示 ----

        private void BuildTurnBanner(Transform canvas)
        {
            var go = CreatePanel(canvas, "TurnBanner",
                new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-250, -48), new Vector2(250, -panelMargin));
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 36);
            turnBanner = CreateText(go.transform, "BannerText", "准备战斗", 22, TextAnchor.MiddleCenter, Color.white);
            turnBanner.rectTransform.sizeDelta = new Vector2(0, 36);
        }

        // ---- 动作按钮栏（底部）----

        private void BuildActionBar(Transform canvas)
        {
            var bar = CreatePanel(canvas, "ActionBar",
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(panelMargin, 0), new Vector2(-360, 140),
                new Color(0, 0, 0, 0.5f));
            actionBarParent = bar.transform;

            var layout = bar.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            attackButton = CreateButton(bar.transform, "BtnAttack", "普攻 [A]");
            attackButton.onClick.AddListener(RequestBasicAttack);

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var btn = CreateButton(bar.transform, "BtnSpell" + i, "术法" + (i + 1) + " [" + (idx + 1) + "]");
                btn.onClick.AddListener(() => RequestSpell(idx));
                spellButtons.Add(btn.gameObject);
            }

            swapButton = CreateButton(bar.transform, "BtnSwap", "换法 [S]");
            swapButton.onClick.AddListener(RequestSwapSpell);

            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                var btn = CreateButton(bar.transform, "BtnSkill" + i, "神通" + (i + 1) + " [" + (idx + 5) + "]");
                btn.onClick.AddListener(() => RequestSkill(idx));
                skillButtons.Add(btn.gameObject);
            }

            guardButton = CreateButton(bar.transform, "BtnGuard", "防御 [G]");
            guardButton.onClick.AddListener(RequestGuard);

            waitButton = CreateButton(bar.transform, "BtnWait", "待机 [W]");
            waitButton.onClick.AddListener(RequestWait);
        }

        private void RequestBasicAttack()
        {
            combatCommandHandler?.RequestBasicAttack(playerCombatantId, targetCombatantId);
        }

        private void RequestGuard()
        {
            combatCommandHandler?.RequestGuard(playerCombatantId);
        }

        private void RequestWait()
        {
            combatCommandHandler?.RequestWait(playerCombatantId);
        }

        private void RequestSwapSpell()
        {
            combatCommandHandler?.RequestSwapSpell(playerCombatantId, 0, swapProfileId);
        }

        private void RequestSpell(int index)
        {
            if (index >= 0 && index < spellProfileIds.Length)
                combatCommandHandler?.RequestArt(playerCombatantId, targetCombatantId, spellProfileIds[index]);
        }

        private void RequestSkill(int index)
        {
            if (index >= 0 && index < skillProfileIds.Length)
                combatCommandHandler?.RequestDivine(playerCombatantId, targetCombatantId, skillProfileIds[index]);
        }

        // ---- 战斗日志（右侧）----

        private void BuildLogPanel(Transform canvas)
        {
            logPanel = CreatePanel(canvas, "LogPanel",
                new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(-340, 250), new Vector2(-panelMargin, -250),
                new Color(0, 0, 0, 0.5f));

            // 标题
            var titleRt = new GameObject("LogTitle", typeof(RectTransform)).GetComponent<RectTransform>();
            titleRt.SetParent(logPanel.transform, false);
            titleRt.anchorMin = new Vector2(0, 1);
            titleRt.anchorMax = new Vector2(1, 1);
            titleRt.sizeDelta = new Vector2(0, 28);
            titleRt.anchoredPosition = Vector2.zero;
            var titleTxt = titleRt.gameObject.AddComponent<Text>();
            titleTxt.text = "—— 战斗日志 ——";
            titleTxt.fontSize = 14;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.color = new Color(0.7f, 0.7f, 0.3f);
            titleTxt.font = sharedFont;

            // ScrollRect 容器
            var scrollGo = new GameObject("LogScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(logPanel.transform, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0, 0);
            scrollRt.anchorMax = new Vector2(1, 1);
            scrollRt.offsetMin = new Vector2(8, 8);
            scrollRt.offsetMax = new Vector2(-8, -36);
            scrollGo.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.8f);
            logScroll = scrollGo.GetComponent<ScrollRect>();

            // Viewport（遮罩裁剪区域）
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(scrollGo.transform, false);
            var vpRt = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.sizeDelta = Vector2.zero;
            viewport.GetComponent<Image>().color = Color.clear;

            // 日志文本（直接作为 ScrollRect 的内容，自动扩展高度）
            var logGo = new GameObject("LogText", typeof(RectTransform));
            logGo.transform.SetParent(viewport.transform, false);
            var logRt = logGo.GetComponent<RectTransform>();
            logRt.anchorMin = new Vector2(0, 1);
            logRt.anchorMax = new Vector2(1, 1);
            logRt.pivot = new Vector2(0.5f, 1);
            logRt.anchoredPosition = Vector2.zero;
            logRt.sizeDelta = new Vector2(0, 100);

            logText = logGo.AddComponent<Text>();
            logText.text = "";
            logText.fontSize = 12;
            logText.alignment = TextAnchor.UpperLeft;
            logText.color = new Color(0.8f, 0.8f, 0.7f);
            logText.font = sharedFont;
            logText.horizontalOverflow = HorizontalWrapMode.Wrap;
            logText.verticalOverflow = VerticalWrapMode.Overflow;

            var fitter = logGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            logScroll.viewport = vpRt;
            logScroll.content = logRt;
            logScroll.horizontal = false;
            logScroll.vertical = true;
            logScroll.movementType = ScrollRect.MovementType.Clamped;
        }

        // ---- 帮助面板 ----

        private void BuildHelpPanel(Transform canvas)
        {
            helpPanel = CreatePanel(canvas, "HelpPanel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-250, -180), new Vector2(250, 180),
                new Color(0, 0, 0, 0.85f));

            var txt = CreateText(helpPanel.transform, "HelpText",
                "=== 操作说明 ===\n\n" +
                "鼠标点击 → 移动/选择目标\n" +
                "键盘 [A] 普攻  [G] 防御  [W] 待机\n" +
                "键盘 [1-4] 术法  [5-6] 神通\n" +
                "键盘 [H] 显隐帮助  [Esc] 退出\n\n" +
                "六角格朝向系统:\n" +
                "正面 -15%命中 | 侧面正常 | 背面 +15%命中 +30%伤害",
                15, TextAnchor.UpperLeft, Color.white);
            txt.rectTransform.offsetMin = new Vector2(12, 12);
            txt.rectTransform.offsetMax = new Vector2(-12, -12);

            helpPanel.SetActive(false);
        }

        private void ToggleHelp()
        {
            helpVisible = !helpVisible;
            helpPanel.SetActive(helpVisible);
        }

        // ==================== 公开更新方法 ====================

        public void UpdatePlayerInfo(CombatantPanelState state)
        {
            if (state == null) return;
            if (playerNameText != null) playerNameText.text = state.Name;
            if (playerElementText != null) playerElementText.text = ResolveElementDisplay(state.Element);
            UpdateBar(playerHPFill, playerHPText, state.CurrentHP, state.MaxHP, "HP");
            UpdateBar(playerMPFill, playerMPText, state.CurrentMP, state.MaxMP, "MP");
            UpdateBar(playerCTFill, playerCTText, state.CTRatio, 1f, "CT");
            if (playerStatusText != null)
                playerStatusText.text = string.IsNullOrEmpty(state.Status) ? "" : "状态: " + state.Status;
        }

        public void UpdateEnemyInfo(CombatantPanelState state)
        {
            if (state == null) return;
            if (enemyNameText != null) enemyNameText.text = state.Name;
            if (enemyElementText != null) enemyElementText.text = ResolveElementDisplay(state.Element);
            UpdateBar(enemyHPFill, enemyHPText, state.CurrentHP, state.MaxHP, "HP");
            UpdateBar(enemyMPFill, enemyMPText, state.CurrentMP, state.MaxMP, "MP");
            UpdateBar(enemyCTFill, enemyCTText, state.CTRatio, 1f, "CT");
            if (enemyStatusText != null)
                enemyStatusText.text = string.IsNullOrEmpty(state.Status) ? "" : "状态: " + state.Status;
        }

        private static string ResolveElementDisplay(string elem)
        {
            if (string.IsNullOrEmpty(elem)) return "";
            return "[" + elem + "]";
        }

        public void SetTurnBanner(string text, Color? color = null)
        {
            if (turnBanner != null)
            {
                turnBanner.text = text;
                turnBanner.color = color ?? Color.white;
            }
        }

        public void AddLog(string message)
        {
            if (logText == null || logScroll == null) return;

            // 战斗日志原样呈现：不做整条日志的 Language 键全局替换，避免误改玩家自定义名称或
            // 技术日志中的合法内容；稳定 ID / 原因继续保留在日志文本、Debug 日志与结果对象中。
            // 档案键只在已证明的显示字段（术法/神通按钮标签）由源端解析为中文。

            logLineCount++;
            if (logLineCount > MaxLogLines)
            {
                var lines = logText.text.Split('\n');
                var trimmed = new System.Text.StringBuilder();
                int start = Mathf.Max(0, lines.Length - MaxLogLines / 2);
                for (int i = start; i < lines.Length; i++)
                    trimmed.AppendLine(lines[i]);
                logText.text = trimmed.ToString();
                logLineCount = lines.Length - start;
            }

            logText.text += message + "\n";

            Canvas.ForceUpdateCanvases();
            logScroll.verticalNormalizedPosition = 0f;
        }

        public void ClearLog()
        {
            if (logText != null) logText.text = "";
            logLineCount = 0;
        }

        public void RefreshSpellButtons(string[] spellNames, int[] cooldowns, int currentMP, int[] mpCosts, int maxSlots = -1, string[] elements = null)
        {
            RefreshButtonRow(spellButtons, spellNames, cooldowns, currentMP, mpCosts, maxSlots, elements);
        }

        public void RefreshSkillButtons(string[] skillNames, int[] cooldowns, int currentMP, int[] mpCosts, int maxSlots = -1, string[] elements = null)
        {
            RefreshButtonRow(skillButtons, skillNames, cooldowns, currentMP, mpCosts, maxSlots, elements);
        }

        public void RefreshSkillButtons(string[] skillNames, int[] cooldowns, int currentMP, int[] mpCosts)
        {
            RefreshButtonRow(skillButtons, skillNames, cooldowns, currentMP, mpCosts, -1, null);
        }


        private GameObject CreateActionButton(string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(actionBarParent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100, 36);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
            var btn = go.GetComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(onClick);
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;
            var labelTxt = labelGo.GetComponent<Text>();
            labelTxt.text = label; labelTxt.fontSize = 13; labelTxt.color = Color.white;
            labelTxt.alignment = TextAnchor.MiddleCenter; labelTxt.font = sharedFont;
            return go;
        }
        private void RefreshButtonRow(List<GameObject> buttons, string[] names, int[] cooldowns, int currentMP, int[] mpCosts, int maxSlots = -1, string[] elements = null)
        {
            int equippedCount = names?.Length ?? 0;
            int showCount = maxSlots > 0 ? Mathf.Max(equippedCount, maxSlots) : equippedCount;

            // 确保按钮数量足够
            while (buttons.Count < showCount)
            {
                var newBtn = CreateActionButton("Slot" + buttons.Count, "", null);
                buttons.Add(newBtn);
            }

            for (int i = 0; i < buttons.Count; i++)
            {
                var btn = buttons[i];
                bool active = i < showCount;
                btn.SetActive(active);
                if (!active) continue;

                var txt = btn.GetComponentInChildren<Text>();
                var button = btn.GetComponent<Button>();
                if (txt == null) continue;

                bool isEmptySlot = i >= equippedCount;

                if (isEmptySlot)
                {
                    // 空槽显示为灰色"+"
                    txt.text = "[空]";
                    txt.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                    if (button != null) button.interactable = false;
                }
                else
                {
                    bool onCooldown = cooldowns != null && i < cooldowns.Length && cooldowns[i] > 0;
                    bool noMP = mpCosts != null && i < mpCosts.Length && currentMP < mpCosts[i];
                    string cdStr = onCooldown ? " (CD" + cooldowns[i] + ")" : "";
                    string slotLabel = showCount > 1 ? (i + 1) + ": " : "";
                    string elemTag = (elements != null && i < elements.Length && !string.IsNullOrEmpty(elements[i])) ? " [" + elements[i] + "]" : "";
                    txt.text = slotLabel + names[i] + elemTag + cdStr;
                    txt.color = (onCooldown || noMP) ? Color.gray : Color.white;
                    if (button != null) button.interactable = !onCooldown && !noMP;
                }
            }
        }

        /// <summary>控制敌方面板显隐（无战斗时隐藏）</summary>
        public void ShowEnemyPanel(bool visible)
        {
            if (enemyPanel != null)
                enemyPanel.SetActive(visible);
        }

        /// <summary>控制战斗动作栏显隐（探索模式隐藏）</summary>
        public void SetActionBarVisible(bool visible)
        {
            if (actionBarParent != null)
                actionBarParent.gameObject.SetActive(visible);
        }

        public void SetActionButtonsInteractable(bool interactable)
        {
            if (attackButton != null) attackButton.interactable = interactable;
            if (guardButton != null) guardButton.interactable = interactable;
            if (waitButton != null) waitButton.interactable = interactable;
            if (swapButton != null) swapButton.interactable = interactable;
            foreach (var btn in spellButtons)
                if (btn.TryGetComponent<Button>(out var b)) b.interactable = interactable;
            foreach (var btn in skillButtons)
                if (btn.TryGetComponent<Button>(out var b)) b.interactable = interactable;
        }

        public bool IsPointerOverUI()
        {
            if (UnityEngine.EventSystems.EventSystem.current == null) return false;
            return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }

        // ---- 内部工具 ----

        private void UpdateBar(Image fill, Text label, float current, float max, string prefix)
        {
            if (fill == null) return;
            float pct = max > 0 ? Mathf.Clamp01(current / max) : 0;
            fill.fillAmount = pct;
            if (label != null)
            {
                if (max >= 1)
                    label.text = prefix + ": " + Mathf.RoundToInt(current) + "/" + Mathf.RoundToInt(max);
                else
                    label.text = prefix + ": " + (pct * 100).ToString("F0") + "%";
            }
        }
    }
}
