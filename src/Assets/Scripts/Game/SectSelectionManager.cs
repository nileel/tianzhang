using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TianZhang.Entity;
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
        public GameManager gameManager;

        private string _selectedSect = "";

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
            if (selectedSectText != null)
                selectedSectText.text = "Selected: " + sectName;
            if (selectedSectDesc != null && Sects.TryGetValue(sectName, out var preset))
                selectedSectDesc.text = preset.desc;
            if (startButton != null)
                startButton.interactable = true;
        }

        private void OnStartGame()
        {
            if (string.IsNullOrEmpty(_selectedSect)) return;
            if (!Sects.TryGetValue(_selectedSect, out var preset)) return;

            var charData = ScriptableObject.CreateInstance<CharacterData>();
            charData.charName = preset.name;
            charData.realmMultiplier = 1.5f;
            charData.rootBone = preset.rootBone;
            charData.physique = preset.physique;
            charData.spirit = preset.spirit;
            charData.mind = preset.mind;
            charData.reaction = preset.reaction;
            charData.talent = preset.talent;
            charData.blockRate = preset.blockRate;
            charData.soulShieldRate = preset.soulShieldRate;
            charData.critRate = preset.critRate;
            charData.critDamage = preset.critDamage;
            charData.gongFaName = preset.gongFaName;
            charData.equippedSpells = preset.startingSpells;
            charData.equippedSkills = new string[0];
            charData.availableSpells = preset.startingSpells;

            if (gameManager == null)
                gameManager = FindObjectOfType<GameManager>();
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

            var exploreCtrl = FindObjectOfType<ExplorationController>();
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
            player.AvailableSpells = charData.availableSpells;

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
                if (gongFaAsset != null)
                {
                    player.ApplyGongFaBonuses(gongFaAsset, "练气");
                    Debug.Log($"[SectSelection] Applied gongFa: {charData.gongFaName} ({gongFaAssetId})");
                }
                else
                {
                    Debug.LogWarning($"[SectSelection] GongFa asset not found: {path}");
                }
            }

            // Update ExplorationController spell/skill assets so UI shows correct names
            UpdateExplorationSpells(exploreCtrl, charData);

            Debug.Log($"[SectSelection] Player reconfigured: {player.Name} HP={player.MaxHP} MP={player.MaxMP} PAtk={player.PhysAtk} MAtk={player.MagAtk}");

            // Hide selection panel after successful reconfiguration
            if (selectionPanel != null)
                selectionPanel.SetActive(false);
        }

        private void UpdateExplorationSpells(ExplorationController ctrl, CharacterData charData)
        {
            if (ctrl == null) return;

            // Load SpellData assets for equipped spells
            var spellList = new System.Collections.Generic.List<Combat.SpellData>();
            if (charData.equippedSpells != null)
            {
                foreach (var spellName in charData.equippedSpells)
                {
                    var id = GetSpellAssetId(spellName);
                    var spath = "Assets/Data/Spells/Spell_spell_" + id + ".asset";
                    var asset = LoadAsset<Combat.SpellData>(spath);
                    if (asset != null)
                    {
                        spellList.Add(asset);
                    }
                    else
                    {
                        Debug.LogWarning($"[SectSelection] Spell asset not found: {spellName} ({spath})");
                    }
                }
            }
            ctrl.playerSpells = spellList.ToArray();
            Debug.Log($"[SectSelection] Loaded {spellList.Count} spells for ExplorationController");
        }

        // Spell Chinese name -> CSV ID mapping
        private static readonly Dictionary<string, string> SpellAssetIds = new()
        {
            ["玄水咒"] = "xuanshuizhou", ["沧浪击"] = "canglangji", ["安神符"] = "anshenfu",
            ["金光破岳"] = "jinguangpoyue", ["流火灵符"] = "liuhuolingfu",
            ["引雷诀"] = "yinleijue", ["苦行剑式"] = "kuxingjianshi", ["剑罡护体"] = "jianganghuti",
            ["铁骨功"] = "tiegugong", ["破阵冲锋"] = "pozhenchongfeng", ["玄甲铁壁"] = "xuanjiatiebi",
            ["暗蚀"] = "tx_anshi", ["幽冥引"] = "youmingyin", ["入梦诀"] = "rumengjue",
            ["暗噬"] = "anshi",
        };

        private static string GetSpellAssetId(string chineseName)
        {
            return SpellAssetIds.TryGetValue(chineseName, out var id) ? id : chineseName;
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
