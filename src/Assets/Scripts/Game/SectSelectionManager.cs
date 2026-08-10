using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Entity;
using TianZhang.Game.CharacterCreation;
using TianZhang.Map;

namespace TianZhang.Game
{
    public class SectSelectionManager : MonoBehaviour
    {
        [Header("UI")]
        public GameObject selectionPanel;
        public Transform buttonContainer;
        public Button startButton;
        public Text selectedSectText;
        public Text selectedSectDesc;
        public Text innateBudgetText;
        public Text visibleRootText;
        public Text hiddenRootSeedText;
        public Text creationBudgetText;
        public Text craftSkillText;
        public GameManager gameManager;

        private string _selectedSect = "";
        private CharacterCreationDraft _draft;

        private static readonly Dictionary<string, SectPreset> Sects = new()
        {
            ["太一道庭"] = new SectPreset
            {
                name = "太一道庭",
                desc = "法修为主，神魂攻击+守一印记防御，持续作战型。",
                rootBone = 8, physique = 8, spirit = 16, mind = 14, reaction = 10, talent = 12,
                blockRate = 5f, soulShieldRate = 12f, critRate = 5f, critDamage = 20f,
                gongFaName = "抱元守一经",
                startingSpells = new[] { "玄水咒", "沧浪击", "安神符" },
            },
            ["玉清崖"] = new SectPreset
            {
                name = "玉清崖",
                desc = "剑修+雷法，血剑气以HP换高倍率物理伤害。",
                rootBone = 16, physique = 14, spirit = 8, mind = 14, reaction = 8, talent = 10,
                blockRate = 8f, soulShieldRate = 5f, critRate = 10f, critDamage = 20f,
                gongFaName = "苦行剑典",
                startingSpells = new[] { "引雷诀", "苦行剑式", "剑罡护体" },
            },
            ["混元山"] = new SectPreset
            {
                name = "混元山",
                desc = "高防御坦克，土金双属性，含弘光大典肉盾型。",
                rootBone = 14, physique = 16, spirit = 10, mind = 10, reaction = 10, talent = 10,
                blockRate = 12f, soulShieldRate = 8f, critRate = 5f, critDamage = 15f,
                gongFaName = "含弘光大典",
                startingSpells = new[] { "铁骨功", "破阵冲锋", "玄甲铁壁" },
            },
            ["太虚观"] = new SectPreset
            {
                name = "太虚观",
                desc = "暗系神魂专精，debuff清除链+虚化免疫，概率防御型。",
                rootBone = 6, physique = 8, spirit = 18, mind = 16, reaction = 10, talent = 10,
                blockRate = 3f, soulShieldRate = 15f, critRate = 8f, critDamage = 25f,
                gongFaName = "不真自虚法",
                startingSpells = new[] { "暗蚀", "幽冥引", "入梦诀" },
            },
            ["散修"] = new SectPreset
            {
                name = "散修",
                desc = "无门无派，秋水游心经减伤+自愈，均衡发展。",
                rootBone = 12, physique = 10, spirit = 12, mind = 10, reaction = 10, talent = 12,
                blockRate = 5f, soulShieldRate = 8f, critRate = 5f, critDamage = 15f,
                gongFaName = "秋水游心经",
                startingSpells = new[] { "暗噬" },
            },
        };

        private void Start()
        {
            // Parent under existing UICanvas if available
            var uiCanvas = GameObject.Find("UICanvas");
            if (uiCanvas != null && transform.parent == null)
                transform.SetParent(uiCanvas.transform, false);

            // Ensure references exist (self-sufficient if Editor setup not run)
            if (selectionPanel == null)
                selectionPanel = gameObject;
            if (buttonContainer == null)
            {
                var existing = transform.Find("ButtonContainer");
                if (existing != null) buttonContainer = existing;
                else
                {
                    var go = new GameObject("ButtonContainer", typeof(RectTransform));
                    go.transform.SetParent(transform, false);
                    var rt = go.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(0, 60);
                    rt.sizeDelta = new Vector2(300, 300);
                    buttonContainer = go.transform;
                }
            }
            if (startButton == null)
            {
                var existing = transform.Find("StartButton");
                if (existing != null) startButton = existing.GetComponent<Button>();
            }

            EnsureCreationDraft();
            EnsureCreationSummaryTexts();
            UpdateCreationSummary();

            selectionPanel.SetActive(true);
            CreateSectButtons();
        }

        private void CreateSectButtons()
        {
            if (buttonContainer == null) return;

            foreach (var kvp in Sects)
            {
                var sectName = kvp.Key;
                var go = new GameObject("Btn_" + sectName, typeof(RectTransform));
                go.transform.SetParent(buttonContainer, false);
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(260, 50);

                var image = go.AddComponent<Image>();
                image.color = new Color(0.25f, 0.25f, 0.45f, 0.9f);

                var btn = go.AddComponent<Button>();
                var capturedName = sectName;
                btn.onClick.AddListener(() => SelectSect(capturedName));

                var labelGo = new GameObject("Label", typeof(RectTransform));
                labelGo.transform.SetParent(go.transform, false);
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.sizeDelta = Vector2.zero;
                var label = labelGo.AddComponent<Text>();
                label.text = sectName;
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.fontSize = 22;
                label.color = Color.white;
            }

            if (!buttonContainer.TryGetComponent<VerticalLayoutGroup>(out _))
            {
                var vlg = buttonContainer.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 8;
                vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = true;
            }

            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartGame);
                startButton.interactable = false;
            }
        }

        public void SelectSect(string sectName)
        {
            _selectedSect = sectName;
            EnsureCreationDraft();
            _draft.SectRouteId = GetSectRouteId(sectName);
            _draft.CharacterName = sectName;

            if (selectedSectText != null)
                selectedSectText.text = "Selected: " + sectName;
            if (selectedSectDesc != null && Sects.TryGetValue(sectName, out var preset))
                selectedSectDesc.text = preset.desc;
            if (startButton != null)
                startButton.interactable = true;

            UpdateCreationSummary();
        }

        private void EnsureCreationDraft()
        {
            if (_draft == null)
                _draft = CharacterCreationCatalog.CreateDefaultDraft();
        }

        private void EnsureCreationSummaryTexts()
        {
            var parent = selectionPanel != null ? selectionPanel.transform : transform;
            var summary = parent.Find("CharacterCreationSummary");
            if (summary == null)
            {
                var summaryGo = new GameObject("CharacterCreationSummary", typeof(RectTransform));
                summaryGo.transform.SetParent(parent, false);
                var summaryRt = summaryGo.GetComponent<RectTransform>();
                summaryRt.anchorMin = new Vector2(1f, 0.5f);
                summaryRt.anchorMax = new Vector2(1f, 0.5f);
                summaryRt.anchoredPosition = new Vector2(-340, 90);
                summaryRt.sizeDelta = new Vector2(420, 190);

                var layout = summaryGo.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 8;
                layout.childAlignment = TextAnchor.UpperLeft;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                summary = summaryGo.transform;
            }

            if (innateBudgetText == null)
                innateBudgetText = FindOrCreateSummaryText(summary, "InnateBudgetText");
            if (visibleRootText == null)
                visibleRootText = FindOrCreateSummaryText(summary, "VisibleRootText");
            if (hiddenRootSeedText == null)
                hiddenRootSeedText = FindOrCreateSummaryText(summary, "HiddenRootSeedText");
            if (creationBudgetText == null)
                creationBudgetText = FindOrCreateSummaryText(summary, "CreationBudgetText");
            if (craftSkillText == null)
                craftSkillText = FindOrCreateSummaryText(summary, "CraftSkillText");
        }

        private static Text FindOrCreateSummaryText(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null && existing.TryGetComponent<Text>(out var existingText))
                return existingText;

            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(420, 28);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 16;
            text.color = new Color(0.86f, 0.9f, 0.78f, 1f);
            text.alignment = TextAnchor.MiddleLeft;
            return text;
        }

        private void UpdateCreationSummary()
        {
            EnsureCreationDraft();
            var result = CharacterCreationRules.Validate(_draft);
            var visibleRoot = CharacterCreationCatalog.FindVisibleRoot(_draft.VisibleSpiritRootId);
            var hiddenSeed = CharacterCreationCatalog.FindHiddenRootSeed(_draft.HiddenRootSeedId);

            if (innateBudgetText != null)
                innateBudgetText.text = $"先天购买点剩余：{result.InnatePurchasePointsRemaining}/{result.InnatePurchasePointLimit}";
            if (visibleRootText != null)
                visibleRootText.text = $"显性灵根：{(visibleRoot != null ? visibleRoot.DisplayName : "未选择")}";
            if (hiddenRootSeedText != null)
                hiddenRootSeedText.text = $"隐藏灵根种子：{(hiddenSeed != null ? hiddenSeed.DisplayName : "无")}";
            if (creationBudgetText != null)
                creationBudgetText.text = $"创建预算剩余：{result.BudgetAvailable}/{result.BudgetLimit}";
            if (craftSkillText != null)
                craftSkillText.text = $"技艺点剩余：{CharacterCreationCatalog.CraftSkillStartingPoints - result.CraftSkillPointsUsed}/{CharacterCreationCatalog.CraftSkillStartingPoints}";
        }

        private void OnStartGame()
        {
            if (string.IsNullOrEmpty(_selectedSect)) return;
            if (!Sects.ContainsKey(_selectedSect)) return;

            EnsureCreationDraft();
            _draft.SectRouteId = GetSectRouteId(_selectedSect);
            _draft.CharacterName = _selectedSect;
            var validation = CharacterCreationRules.Validate(_draft);
            if (!validation.IsValid)
            {
                var message = string.Join("\n", validation.Errors);
                if (selectedSectDesc != null)
                    selectedSectDesc.text = message;
                Debug.LogError("[SectSelection] Character creation draft invalid: " + message);
                UpdateCreationSummary();
                return;
            }

            var charData = CharacterCreationRules.BuildCharacterData(_draft);

            var flow = SceneFlowManager.Instance;
            if (flow != null)
            {
                if (gameManager == null)
                    gameManager = FindFirstObjectByType<GameManager>();
                if (gameManager != null)
                    gameManager.PlayerCharData = charData;

                flow.StartNewGame(charData);
                if (selectionPanel != null)
                    selectionPanel.SetActive(false);
                return;
            }

            if (gameManager == null)
                gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager != null)
                gameManager.StartGameWithSect(charData);
            else
                Debug.LogError("[SectSelection] GameManager not found!");

            // Run coroutine on GameManager (stays active via DontDestroyOnLoad)
            // Panel will be hidden after reconfiguration completes
            if (gameManager != null)
                gameManager.StartCoroutine(ReconfigurePlayerDelayed(charData));
            else
                StartCoroutine(ReconfigurePlayerDelayed(charData));
        }

        private System.Collections.IEnumerator ReconfigurePlayerDelayed(CharacterData charData)
        {
            yield return new WaitForSeconds(0.5f);

            var exploreCtrl = FindFirstObjectByType<ExplorationController>();
            if (exploreCtrl == null)
            {
                Debug.LogWarning("[SectSelection] ExplorationController not found");
                yield break;
            }

            var player = exploreCtrl.GetPlayer();
            if (player == null)
            {
                Debug.LogWarning("[SectSelection] Player not found");
                yield break;
            }

            // Reconfigure player with sect preset
            player.Name = charData.charName;
            player.RootBone = charData.rootBone;
            player.Physique = charData.physique;
            player.Spirit = charData.spirit;
            player.Mind = charData.mind;
            player.Reaction = charData.reaction;
            player.Talent = charData.talent;
            player.BlockRate = charData.blockRate;
            player.SoulShieldRate = charData.soulShieldRate;
            player.CritRate = charData.critRate;
            player.CritDamage = charData.critDamage;
            player.EquippedSpellIds = charData.equippedSpells;
            player.EquippedSkillIds = charData.equippedSkills;
            player.MainEquipmentBasicAttackProfileId = charData.mainEquipmentBasicAttackProfileId;
            player.UnarmedBasicAttackProfileId = charData.unarmedBasicAttackProfileId;
            if (!string.IsNullOrWhiteSpace(player.MainEquipmentBasicAttackProfileId) &&
                string.IsNullOrWhiteSpace(player.UnarmedBasicAttackProfileId))
            {
                player.BasicAttackProfileId = player.MainEquipmentBasicAttackProfileId;
                player.BasicAttackBindingKind = "main_equipment";
            }
            else if (string.IsNullOrWhiteSpace(player.MainEquipmentBasicAttackProfileId) &&
                     !string.IsNullOrWhiteSpace(player.UnarmedBasicAttackProfileId))
            {
                player.BasicAttackProfileId = player.UnarmedBasicAttackProfileId;
                player.BasicAttackBindingKind = "unarmed_fallback";
            }
            else
            {
                player.BasicAttackProfileId = null;
                player.BasicAttackBindingKind = null;
            }
            player.AvailableSpells = charData.availableSpells;
            player.RealmStage = charData.realmStage;
            player.RealmMultiplier = charData.realmMultiplier;
            player.VisibleRootElement = charData.visibleRootElement;

            // Recalculate derived stats
            float realm = charData.realmMultiplier;
            float hpBase = Mathf.Pow(player.RootBone, 0.75f) * realm * 80f;
            player.MaxHP = Mathf.RoundToInt(hpBase);
            player.CurrentHP = player.MaxHP;
            float mpBase = player.Spirit * realm * 15f;
            player.MaxMP = Mathf.RoundToInt(mpBase);
            player.CurrentMP = player.MaxMP;
            player.PhysAtk = Mathf.RoundToInt(player.RootBone * realm * 5f);
            player.MagAtk = Mathf.RoundToInt(player.Spirit * realm * 5f);
            player.PhysDef = Mathf.RoundToInt(player.Physique * realm * 3.5f);
            player.MagDef = Mathf.RoundToInt(player.Mind * realm * 3.5f);
            player.MovePoints = Mathf.Clamp(Mathf.RoundToInt(player.Reaction / 20f), 2, 8);

            // Apply gongfa bonuses if gongfa asset exists
            if (!string.IsNullOrEmpty(charData.gongFaName))
            {
                var gongFaAssetId = GetGongFaAssetId(charData.gongFaName);
                var path = "Assets/Data/GongFa/" + gongFaAssetId + ".asset";
                var gongFaAsset = LoadAsset<Cultivation.GongFaGrowthData>(path);
                if (gongFaAsset != null && Cultivation.ContentScopePolicy.IsPlayerAvailable(gongFaAsset.contentScope))
                {
                    player.ApplyGongFaBonuses(gongFaAsset, "练气");
                    Debug.Log($"[SectSelection] Applied gongFa: {charData.gongFaName} ({gongFaAssetId})");
                }
                else if (gongFaAsset != null)
                {
                    Debug.LogWarning($"[SectSelection] Excluded non-player gongFa: {charData.gongFaName} ({gongFaAssetId}) scope={gongFaAsset.contentScope}");
                }
                else
                {
                    Debug.LogWarning($"[SectSelection] GongFa asset not found: {path}");
                }
            }

            // Update ExplorationController spell/skill assets so UI shows correct names
            UpdateExplorationAbilities(exploreCtrl, charData, player);

            Debug.Log($"[SectSelection] Player reconfigured: {player.Name} HP={player.MaxHP} MP={player.MaxMP} PAtk={player.PhysAtk} MAtk={player.MagAtk}");

            // Hide selection panel after successful reconfiguration
            if (selectionPanel != null)
                selectionPanel.SetActive(false);
        }

        private void UpdateExplorationAbilities(ExplorationController ctrl, CharacterData charData, Character player)
        {
            if (ctrl == null) return;

            var knownProfiles = new System.Collections.Generic.List<Combat.AttackProfileData>();
            if (ctrl.attackProfiles != null)
                knownProfiles.AddRange(ctrl.attackProfiles.Where(profile => profile != null));

            var spellList = new System.Collections.Generic.List<Combat.AttackProfileData>();
            if (charData.equippedSpells != null)
            {
                foreach (var profileId in charData.equippedSpells)
                {
                    var asset = LoadAttackProfile(profileId);
                    if (asset != null && asset.profileKind == Combat.AttackProfileKind.Art &&
                        TianZhang.Cultivation.ContentScopePolicy.IsPlayerAvailable(asset.contentScope) &&
                        Combat.AbilityRequirementPolicy.IsSatisfied(player, asset.realmRequirementId, asset.elementRequirementId))
                    {
                        spellList.Add(asset);
                        AddKnownProfile(knownProfiles, asset);
                    }
                    else if (asset != null && !TianZhang.Cultivation.ContentScopePolicy.IsPlayerAvailable(asset.contentScope))
                    {
                        Debug.LogWarning($"[SectSelection] Excluded non-player spell: {profileId} scope={asset.contentScope}");
                    }
                    else if (asset != null)
                    {
                        Debug.LogWarning($"[SectSelection] Excluded unresolved art profile: {profileId}");
                    }
                    else
                    {
                        Debug.LogWarning($"[SectSelection] Attack profile not found: {profileId}");
                    }
                }
            }
            ctrl.playerSpells = spellList.ToArray();
            Debug.Log($"[SectSelection] Loaded {spellList.Count} art profiles for ExplorationController");

            var skillList = new System.Collections.Generic.List<Combat.AttackProfileData>();
            if (charData.equippedSkills != null)
            {
                foreach (var profileId in charData.equippedSkills)
                {
                    var asset = LoadAttackProfile(profileId);
                    if (asset != null && asset.profileKind == Combat.AttackProfileKind.Divine &&
                        TianZhang.Cultivation.ContentScopePolicy.IsPlayerAvailable(asset.contentScope) &&
                        Combat.AbilityRequirementPolicy.IsSatisfied(player, asset.realmRequirementId, asset.elementRequirementId))
                    {
                        skillList.Add(asset);
                        AddKnownProfile(knownProfiles, asset);
                    }
                    else if (asset != null && !TianZhang.Cultivation.ContentScopePolicy.IsPlayerAvailable(asset.contentScope))
                    {
                        Debug.LogWarning($"[SectSelection] Excluded non-player skill: {profileId} scope={asset.contentScope}");
                    }
                    else if (asset != null)
                    {
                        Debug.LogWarning($"[SectSelection] Excluded unresolved divine profile: {profileId}");
                    }
                    else
                    {
                        Debug.LogWarning($"[SectSelection] Attack profile not found: {profileId}");
                    }
                }
            }
            ctrl.playerSkills = skillList.ToArray();
            Debug.Log($"[SectSelection] Loaded {skillList.Count} divine profiles for ExplorationController");

            string basicProfileId = player.BasicAttackProfileId;
            if (!string.IsNullOrWhiteSpace(basicProfileId))
            {
                var basicProfile = LoadAttackProfile(basicProfileId);
                if (basicProfile != null)
                    AddKnownProfile(knownProfiles, basicProfile);
            }
            ctrl.attackProfiles = knownProfiles.ToArray();
        }

        private static Combat.AttackProfileData LoadAttackProfile(string attackProfileId)
        {
            if (string.IsNullOrWhiteSpace(attackProfileId))
                return null;
            return LoadAsset<Combat.AttackProfileData>(
                "Assets/Data/AttackProfiles/AttackProfile_" + attackProfileId + ".asset");
        }

        private static void AddKnownProfile(
            System.Collections.Generic.List<Combat.AttackProfileData> profiles,
            Combat.AttackProfileData profile)
        {
            if (profile != null && !profiles.Contains(profile))
                profiles.Add(profile);
        }

        private static string GetSectRouteId(string sectName)
        {
            return sectName switch
            {
                "太一道庭" => "route_taiyi",
                "玉清崖" => "route_yuqing",
                "混元山" => "route_hunyuan",
                "太虚观" => "route_taixu",
                _ => "route_sanxiu",
            };
        }

        // GongFa Chinese name -> CSV asset ID mapping
        private static readonly Dictionary<string, string> GongFaAssetIds = new()
        {
            ["抱元守一经"] = "GongFa_gongfa_baoyuanshouyi",
            ["苦行剑典"] = "GongFa_gongfa_kuxingjiandian",
            ["含弘光大典"] = "GongFa_gongfa_hanhongguangda",
            ["不真自虚法"] = "GongFa_gongfa_buzhenzixu",
            ["秋水游心经"] = "GongFa_gongfa_qiushuiyouxin",
        };

        private static string GetGongFaAssetId(string chineseName)
        {
            return GongFaAssetIds.TryGetValue(chineseName, out var id) ? id : "GongFa_" + chineseName;
        }

#if UNITY_EDITOR
        private static T LoadAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }
#else
        private static T LoadAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            return Resources.Load<T>(assetPath);
        }
#endif

        [System.Serializable]
        public struct SectPreset
        {
            public string name, desc;
            public int rootBone, physique, spirit, mind, reaction, talent;
            public float blockRate, soulShieldRate, critRate, critDamage;
            public string gongFaName;
            public string[] startingSpells;
        }
    }
}
