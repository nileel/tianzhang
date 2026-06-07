// ============================================================
// 太玄界 - 战斗数值模拟器 v1.0
// 用途：验证角色数值设计，模拟CTB充能制战棋战斗
// ============================================================

// ============ 游戏常量 ============

// 境界系数（用于伤害公式）
const REALM_COEFFICIENTS = {
  "凡人": 1.0, "练气": 1.5, "筑基": 3.0, "金丹": 6.0,
  "元婴": 12.0, "化神": 25.0, "炼虚": 50.0
};

// 境界基础值
const REALM_BASE = {
  "凡人": { HP:30, MP:0,  肉攻:5,   神攻:5,   肉防:3,   神防:3,   反应:5,  移力:2, 神识:3 },
  "练气": { HP:100,MP:10, 肉攻:25,  神攻:25,  肉防:20,  神防:20,  反应:15, 移力:3, 神识:5 },
  "筑基": { HP:400,MP:100,肉攻:120, 神攻:120, 肉防:100, 神防:100, 反应:50, 移力:4, 神识:8 },
  "金丹": { HP:1500,MP:1000,肉攻:500,神攻:500,肉防:400,神防:400,反应:150,移力:5,神识:12 },
  "元婴": { HP:6000,MP:6000,肉攻:2000,神攻:2000,肉防:1500,神防:1500,反应:500,移力:6,神识:18 },
  "化神": { HP:25000,MP:20000,肉攻:8000,神攻:8000,肉防:6000,神防:6000,反应:1500,移力:7,神识:25 },
  "炼虚": { HP:100000,MP:80000,肉攻:30000,神攻:30000,肉防:25000,神防:25000,反应:5000,移力:8,神识:35 }
};

// 境界系数（每点先天属性贡献）
const REALM_FACTOR = {
  "凡人": { HP:4,   MP:0.5, 攻:1,   防:0.8, 反应:0.6, 移力:0.08, 神识:0.15 },
  "练气": { HP:8,   MP:2,   攻:3,   防:2,   反应:1.5, 移力:0.10, 神识:0.20 },
  "筑基": { HP:12,  MP:5,   攻:5,   防:4,   反应:3,   移力:0.12, 神识:0.25 },
  "金丹": { HP:20,  MP:8,   攻:8,   防:6,   反应:5,   移力:0.15, 神识:0.30 },
  "元婴": { HP:35,  MP:12,  攻:14,  防:10,  反应:8,   移力:0.18, 神识:0.35 },
  "化神": { HP:60,  MP:20,  攻:25,  防:18,  反应:14,  移力:0.20, 神识:0.40 },
  "炼虚": { HP:100, MP:35,  攻:45,  防:35,  反应:25,  移力:0.25, 神识:0.50 }
};

// 灵根修正
const SPIRIT_ROOT_MOD = { "凡品":0.70, "下品":0.85, "中品":1.00, "上品":1.20, "极品":1.50 };

// 功法品级 → 每子等级一级属性增长
const TECH_GROWTH = {
  "极品": { HP:65, MP:45, 肉攻:22, 神攻:22, 肉防:18, 神防:18, 反应:12 },
  "上品": { HP:40, MP:25, 肉攻:12, 神攻:12, 肉防:10, 神防:10, 反应:6  },
  "中品": { HP:20, MP:12, 肉攻:6,  神攻:6,  肉防:5,  神防:5,  反应:4  },
  "下品": { HP:10, MP:5,  肉攻:3,  神攻:3,  肉防:2,  神防:2,  反应:2  },
  "凡品": { HP:5,  MP:3,  肉攻:1,  神攻:1,  肉防:1,  神防:1,  反应:1  }
};

// 功法品级 → 每子等级先天总增长
const TECH_INNATE_GROWTH = { "极品":5, "上品":4, "中品":3, "下品":2, "凡品":1 };

// 子等级计数（凡人→炼虚，含当前境界已获得的子等级）
const SUBLEVELS_PER_REALM = { "凡人":1, "练气":9, "筑基":4, "金丹":4, "元婴":4, "化神":4, "炼虚":4 };
const REALM_ORDER = ["凡人","练气","筑基","金丹","元婴","化神","炼虚"];
const SUBLEVEL_NAMES = {
  "凡人": ["凡人"],
  "练气": ["一层","二层","三层","四层","五层","六层","七层","八层","九层"],
  "筑基": ["初期","中期","后期","圆满"],
  "金丹": ["初期","中期","后期","圆满"],
  "元婴": ["初期","中期","后期","圆满"],
  "化神": ["初期","中期","后期","圆满"],
  "炼虚": ["初期","中期","后期","圆满"]
};

// ============ 辅助函数 ============

function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }
function rand() { return Math.random(); }
function randInt(min, max) { return Math.floor(Math.random() * (max - min + 1)) + min; }

// 计算到达某境界某子等级前，累积的所有子等级数（含当前境界已完成，不含当前子等级本身）
function totalSublevelsBefore(realm, subIndex) {
  let total = 0;
  for (const r of REALM_ORDER) {
    if (r === realm) { total += subIndex; break; }
    total += SUBLEVELS_PER_REALM[r];
  }
  return total;
}

// 计算到达某境界某子等级（含当前子等级）的总子等级数
function totalSublevelsUpTo(realm, subIndex) {
  return totalSublevelsBefore(realm, subIndex) + 1;
}

// ============ 角色构建 ============

function buildCharacter(name, config) {
  // config: { innate:{根骨,魂魄,神识,资质,气运}, realm, subIndex, techGrade, techWeights:{根骨,魂魄,神识,资质,气运}, spiritRootGrade, battleStyle }
  
  const c = {
    name, config,
    realm: config.realm,
    subIndex: config.subIndex,
    battleStyle: config.battleStyle || "physical", // "physical" or "spiritual"
    
    // 先天属性（含子等级增长）
    innate: { ...config.innate },
    
    // 一级属性（待计算）
    primary: {},
    
    // 二级属性（待计算）
    secondary: {},
    
    // 战斗状态
    HP: 0, maxHP: 0,
    MP: 0, maxMP: 0,
    CT: 0,
    alive: true
  };
  
  // 计算子等级带来的先天属性增长
  applySublevelInnateGrowth(c);
  
  // 计算一级属性
  calcPrimaryStats(c);
  
  // 计算二级属性
  calcSecondaryStats(c);
  
  // 初始化战斗状态
  c.HP = c.primary.HP;
  c.maxHP = c.primary.HP;
  c.MP = c.primary.MP;
  c.maxMP = c.primary.MP;
  
  return c;
}

function applySublevelInnateGrowth(c) {
  const totalSub = totalSublevelsUpTo(c.realm, c.subIndex);
  const perLevel = TECH_INNATE_GROWTH[c.config.techGrade];
  const totalPoints = totalSub * perLevel;
  
  // 计算权重总和
  const w = c.config.techWeights;
  const totalW = w.根骨 + w.魂魄 + w.神识 + w.资质 + w.气运;
  
  // 分配
  const keys = ["根骨","魂魄","神识","资质","气运"];
  const allocated = {};
  for (const k of keys) {
    allocated[k] = Math.round(totalPoints * w[k] / totalW);
  }
  
  // 调整确保总和正确
  const sumAlloc = Object.values(allocated).reduce((a,b)=>a+b,0);
  const diff = totalPoints - sumAlloc;
  if (diff !== 0) {
    // 加到权重最高的
    const maxKey = keys.reduce((a,b) => w[a] > w[b] ? a : b);
    allocated[maxKey] += diff;
  }
  
  for (const k of keys) {
    c.innate[k] += allocated[k];
  }
}

function calcPrimaryStats(c) {
  const base = REALM_BASE[c.realm];
  const factor = REALM_FACTOR[c.realm];
  const w = c.config.techWeights;
  const mod = SPIRIT_ROOT_MOD[c.config.spiritRootGrade];
  const totalSub = totalSublevelsUpTo(c.realm, c.subIndex);
  const growth = TECH_GROWTH[c.config.techGrade];
  
  // 先天转化部分
  const innatePart = {};
  innatePart.HP     = Math.round(base.HP   + c.innate.根骨 * factor.HP   * w.根骨);
  innatePart.MP     = Math.round(base.MP   + c.innate.魂魄 * factor.MP   * w.魂魄);
  innatePart.肉攻   = Math.round(base.肉攻 + c.innate.根骨 * factor.攻   * w.根骨);
  innatePart.神攻   = Math.round(base.神攻 + c.innate.魂魄 * factor.攻   * w.魂魄);
  innatePart.肉防   = Math.round(base.肉防 + c.innate.根骨 * factor.防   * w.根骨);
  innatePart.神防   = Math.round(base.神防 + c.innate.神识 * factor.防   * w.神识);
  innatePart.反应   = Math.round(base.反应 + c.innate.神识 * factor.反应 * w.神识);
  innatePart.移力   = Math.round(base.移力 + c.innate.气运 * factor.移力 * w.气运);
  innatePart.神识   = Math.round(base.神识 + c.innate.神识 * factor.神识 * w.神识);
  
  // 子等级积累部分
  const realmIdx = REALM_ORDER.indexOf(c.realm);
  const movesFromRealms = realmIdx; // 已完成的境界数
  
  c.primary = {};
  c.primary.HP   = Math.round((innatePart.HP   + totalSub * growth.HP)   * mod);
  c.primary.MP   = Math.round((innatePart.MP   + totalSub * growth.MP)   * mod);
  c.primary.肉攻 = Math.round((innatePart.肉攻 + totalSub * growth.肉攻) * mod);
  c.primary.神攻 = Math.round((innatePart.神攻 + totalSub * growth.神攻) * mod);
  c.primary.肉防 = Math.round((innatePart.肉防 + totalSub * growth.肉防) * mod);
  c.primary.神防 = Math.round((innatePart.神防 + totalSub * growth.神防) * mod);
  c.primary.反应 = Math.round((innatePart.反应 + totalSub * growth.反应) * mod);
  c.primary.移力 = Math.round(innatePart.移力 + movesFromRealms); // 每境界+1移力
  c.primary.神识 = Math.round(innatePart.神识 + movesFromRealms); // 每境界+1神识
  
  // 保存先天部分用于后续功法转化计算
  c._innatePart = innatePart;
  c._subLevelPart = totalSub;
}

function calcSecondaryStats(c) {
  const s = {};
  
  s.生命恢复率  = clamp(1.0 + c.innate.根骨 * 0.05, 0, 6);
  s.生命恢复    = Math.round(c.primary.HP * s.生命恢复率 / 100);
  s.格挡率      = clamp(c.innate.根骨 * 0.3, 0, 40);
  s.物理抗性    = clamp(c.innate.根骨 * 0.4, 0, 50);
  
  s.灵力恢复率  = clamp(0.5 + c.innate.魂魄 * 0.05, 0, 5);
  s.灵力恢复    = Math.round(c.primary.MP * s.灵力恢复率 / 100);
  s.神魂抗性    = clamp(c.innate.魂魄 * 0.4, 0, 50);
  s.暴击伤害    = clamp(150 + c.innate.魂魄 * 1.0, 150, 300);
  
  s.暴击率      = clamp(c.innate.神识 * 0.25, 0, 40);
  s.命中率      = clamp(c.innate.神识 * 0.30, 0, 50);
  
  s.修炼速度    = 1.0 + c.innate.资质 * 0.02;
  s.悟性        = clamp(0.5 + c.innate.资质 * 0.015, 0.5, 3.0);
  s.灵根亲和    = clamp(c.innate.资质 * 0.3, 0, 50);
  
  s.闪避率      = clamp(c.innate.气运 * 0.3, 0, 50);
  s.幸运值      = c.innate.气运 * 0.4;
  s.魅力值      = c.innate.气运 * 0.4;
  
  c.secondary = s;
}

// ============ 伤害公式 ============

function calcPhysicalDamage(attacker, defender, skillMultiplier, direction) {
  const atk = attacker.primary.肉攻;
  const def = defender.primary.肉防;
  const realmRatio = REALM_COEFFICIENTS[attacker.realm] / REALM_COEFFICIENTS[defender.realm];
  const defFactor = atk / (atk + def);
  
  // 抗性（受境界系数影响）
  const resistRaw = defender.secondary.物理抗性 / 100;
  const resistEff = resistRaw * Math.sqrt(1 / realmRatio);
  
  // 朝向修正
  let dmgBonus = 1.0;
  if (direction === "侧面") dmgBonus = 1.10;
  else if (direction === "背面") dmgBonus = 1.25;
  
  let dmg = atk * skillMultiplier * realmRatio * defFactor * Math.max(0, (1 - resistEff)) * dmgBonus;
  
  return Math.max(0, Math.round(dmg));
}

function calcSpiritualDamage(attacker, defender, skillMultiplier, direction) {
  const atk = attacker.primary.神攻;
  const def = defender.primary.神防;
  const realmRatio = REALM_COEFFICIENTS[attacker.realm] / REALM_COEFFICIENTS[defender.realm];
  const defFactor = atk / (atk + def);
  
  const resistRaw = defender.secondary.神魂抗性 / 100;
  const resistEff = resistRaw * Math.sqrt(1 / realmRatio);
  
  // 神魂伤害不受朝向影响? 不，文档说朝向影响伤害加成，神魂也受
  let dmgBonus = 1.0;
  if (direction === "侧面") dmgBonus = 1.10;
  else if (direction === "背面") dmgBonus = 1.25;
  
  let dmg = atk * skillMultiplier * realmRatio * defFactor * Math.max(0, (1 - resistEff)) * dmgBonus;
  
  return Math.max(0, Math.round(dmg));
}

// ============ 战斗引擎 ============

const MAX_TICKS = 200;

const DIRECTION_NAMES = ["正面","正面","正面","正面","侧面","背面"];

function simulateBattle(charA, charB, maxTicks) {
  maxTicks = maxTicks || MAX_TICKS;
  
  // 重置战斗状态
  [charA, charB].forEach(c => {
    c.HP = c.maxHP;
    c.MP = c.maxMP;
    c.CT = rand() * 100;
    c.alive = true;
  });
  
  const log = [];
  const stats = { ticks: 0, actionsA: 0, actionsB: 0, totalDmgA: 0, totalDmgB: 0, winner: null };
  
  function useSkill(attacker, defender, skillDef) {
    const direction = DIRECTION_NAMES[randInt(0, 5)];
    let dmg = 0;
    
    if (skillDef.type === "physical") {
      dmg = calcPhysicalDamage(attacker, defender, skillDef.multiplier, direction);
      // 格挡判定（仅物理）
      if (direction !== "背面" && rand() * 100 < defender.secondary.格挡率) {
        dmg = Math.round(dmg / 2);
      }
    } else {
      dmg = calcSpiritualDamage(attacker, defender, skillDef.multiplier, direction);
    }
    
    // 闪避判定
    const hitRate = attacker.secondary.命中率;
    const dodgeRate = defender.secondary.闪避率;
    const effectiveDodge = Math.max(0, dodgeRate - hitRate);
    if (rand() * 100 < effectiveDodge) {
      dmg = 0;
    }
    
    // 暴击判定
    let isCrit = false;
    if (dmg > 0 && rand() * 100 < attacker.secondary.暴击率) {
      dmg = Math.round(dmg * attacker.secondary.暴击伤害 / 100);
      isCrit = true;
    }
    
    defender.HP -= dmg;
    if (defender.HP <= 0) {
      defender.HP = 0;
      defender.alive = false;
    }
    
    return { dmg, direction, isCrit };
  }
  
  for (let tick = 1; tick <= maxTicks; tick++) {
    stats.ticks = tick;
    
    // 恢复
    [charA, charB].forEach(c => {
      if (c.alive) {
        c.HP = Math.min(c.maxHP, c.HP + c.secondary.生命恢复);
        c.MP = Math.min(c.maxMP, c.MP + c.secondary.灵力恢复);
      }
    });
    
    // CT充能
    [charA, charB].forEach(c => {
      if (c.alive) {
        c.CT += c.primary.反应;
      }
    });
    
    // 检查行动
    const actOrder = [];
    for (const c of [charA, charB]) {
      if (c.alive && c.CT >= 100) actOrder.push(c);
    }
    actOrder.sort((a,b) => b.primary.反应 - a.primary.反应);
    
    for (const actor of actOrder) {
      if (!actor.alive) continue;
      if (actor.CT < 100) continue;
      
      const defender = (actor === charA) ? charB : charA;
      if (!defender.alive) continue;
      
      // 选择攻击类型
      const style = actor.battleStyle;
      const skillDef = { type: style === "physical" ? "physical" : "spiritual", multiplier: 1.0 };
      
      const result = useSkill(actor, defender, skillDef);
      
      if (actor === charA) {
        stats.actionsA++;
        stats.totalDmgA += result.dmg;
      } else {
        stats.actionsB++;
        stats.totalDmgB += result.dmg;
      }
      
      // CT归零 + 冷却
      actor.CT = 0;
      
      if (!defender.alive) break;
    }
    
    // 检查结束
    if (!charA.alive || !charB.alive) {
      stats.winner = charA.alive ? "A" : "B";
      break;
    }
  }
  
  return stats;
}

// ============ 批量模拟 ============

function runBatchSimulation(charA, charB, count) {
  count = count || 1000;
  const results = {
    winsA: 0, winsB: 0, draws: 0,
    avgTicks: 0, avgActionsA: 0, avgActionsB: 0,
    avgDmgPerHitA: 0, avgDmgPerHitB: 0,
    avgTTK_A: 0, avgTTK_B: 0,
    totalTicks: 0, totalActionsA: 0, totalActionsB: 0,
    totalDmgA: 0, totalDmgB: 0
  };
  
  for (let i = 0; i < count; i++) {
    // 深拷贝角色
    const a = JSON.parse(JSON.stringify(charA));
    const b = JSON.parse(JSON.stringify(charB));
    // 恢复函数引用
    a._innatePart = charA._innatePart;
    b._innatePart = charB._innatePart;
    a.config = charA.config;
    b.config = charB.config;
    
    const r = simulateBattle(a, b);
    
    if (r.winner === "A") results.winsA++;
    else if (r.winner === "B") results.winsB++;
    else results.draws++;
    
    results.totalTicks += r.ticks;
    results.totalActionsA += r.actionsA;
    results.totalActionsB += r.actionsB;
    results.totalDmgA += r.totalDmgA;
    results.totalDmgB += r.totalDmgB;
  }
  
  results.avgTicks = results.totalTicks / count;
  results.avgActionsA = results.totalActionsA / count;
  results.avgActionsB = results.totalActionsB / count;
  results.avgDmgPerHitA = results.totalDmgA / Math.max(1, results.totalActionsA);
  results.avgDmgPerHitB = results.totalDmgB / Math.max(1, results.totalActionsB);
  results.avgTTK_A = results.totalActionsA / count;
  results.avgTTK_B = results.totalActionsB / count;
  
  return results;
}

// ============ 打印角色信息 ============

function printCharacter(c) {
  console.log(`\n=== ${c.name} ===`);
  console.log(`  境界: ${c.realm}·${SUBLEVEL_NAMES[c.realm][c.subIndex]} | 灵根: ${c.config.spiritRootGrade} | 功法: ${c.config.techGrade}`);
  console.log(`  战斗风格: ${c.battleStyle}`);
  
  console.log(`\n  [先天属性]`);
  console.log(`    根骨:${c.innate.根骨}  魂魄:${c.innate.魂魄}  神识:${c.innate.神识}  资质:${c.innate.资质}  气运:${c.innate.气运}`);
  
  console.log(`\n  [一级属性]`);
  console.log(`    HP:${c.primary.HP}  MP:${c.primary.MP}  肉攻:${c.primary.肉攻}  神攻:${c.primary.神攻}`);
  console.log(`    肉防:${c.primary.肉防}  神防:${c.primary.神防}  反应:${c.primary.反应}  移力:${c.primary.移力}  神识:${c.primary.神识}`);
  
  console.log(`\n  [二级属性]`);
  console.log(`    生命恢复:${c.secondary.生命恢复}/回合(${c.secondary.生命恢复率.toFixed(1)}%)  灵力恢复:${c.secondary.灵力恢复}/回合(${c.secondary.灵力恢复率.toFixed(1)}%)`);
  console.log(`    格挡:${c.secondary.格挡率.toFixed(1)}%  物抗:${c.secondary.物理抗性.toFixed(1)}%  神魂抗性:${c.secondary.神魂抗性.toFixed(1)}%`);
  console.log(`    暴击率:${c.secondary.暴击率.toFixed(1)}%  暴击伤害:${c.secondary.暴击伤害}%  命中:${c.secondary.命中率.toFixed(1)}%  闪避:${c.secondary.闪避率.toFixed(1)}%`);
}

// ============ 战斗报告 ============

function printBattleReport(results, count) {
  console.log(`\n═══════════════════════════════════════`);
  console.log(`  战斗模拟报告（${count}次）`);
  console.log(`═══════════════════════════════════════`);
  console.log(`  胜率: A ${(results.winsA/count*100).toFixed(1)}% | B ${(results.winsB/count*100).toFixed(1)}% | 平 ${(results.draws/count*100).toFixed(1)}%`);
  console.log(`  平均回合数(ticks): ${results.avgTicks.toFixed(1)}`);
  console.log(`  平均行动次数: A ${results.avgActionsA.toFixed(1)} | B ${results.avgActionsB.toFixed(1)}`);
  console.log(`  平均每击伤害: A ${results.avgDmgPerHitA.toFixed(0)} | B ${results.avgDmgPerHitB.toFixed(0)}`);
  console.log(`  平均击杀所需击中数: A→B ${results.avgTTK_A.toFixed(1)} | B→A ${results.avgTTK_B.toFixed(1)}`);
}

// ============ 主程序 ============

function main() {
  const CURRENT_REALM = "筑基";
  const SUB_INDEX = 2; // 后期 (0=初期,1=中期,2=后期,3=圆满)
  
  // ---- 角色A: 体修 ----
  const charA = buildCharacter("⚔ 体修", {
    innate: { 根骨:40, 魂魄:15, 神识:25, 资质:20, 气运:15 },
    realm: CURRENT_REALM,
    subIndex: SUB_INDEX,
    techGrade: "上品",
    techWeights: { 根骨:0.8, 魂魄:0.8, 神识:0.7, 资质:0.6, 气运:0.5 },
    spiritRootGrade: "中品",
    battleStyle: "physical"
  });
  
  // ---- 角色B: 魂修 ----
  const charB = buildCharacter("🔮 魂修", {
    innate: { 根骨:20, 魂魄:40, 神识:20, 资质:20, 气运:15 },
    realm: CURRENT_REALM,
    subIndex: SUB_INDEX,
    techGrade: "上品",
    techWeights: { 根骨:0.6, 魂魄:1.0, 神识:0.7, 资质:0.6, 气运:0.5 },
    spiritRootGrade: "中品",
    battleStyle: "spiritual"
  });
  
  printCharacter(charA);
  printCharacter(charB);
  
  // 伤害验算
  console.log(`\n--- 伤害验算（正面普攻）---`);
  const dmgAB = calcPhysicalDamage(charA, charB, 1.0, "正面");
  const dmgBA = calcSpiritualDamage(charB, charA, 1.0, "正面");
  console.log(`  A→B 物理: ${dmgAB}  (B有效HP: ${charB.primary.HP}, 需 ${(charB.primary.HP / dmgAB).toFixed(1)} 击)`);
  console.log(`  B→A 神魂: ${dmgBA}  (A有效HP: ${charA.primary.HP}, 需 ${(charA.primary.HP / dmgBA).toFixed(1)} 击)`);
  
  console.log(`\n--- 背面攻击伤害 ---`);
  const dmgAB_back = calcPhysicalDamage(charA, charB, 1.0, "背面");
  const dmgBA_back = calcSpiritualDamage(charB, charA, 1.0, "背面");
  console.log(`  A→B 物理(背面): ${dmgAB_back}  (需 ${(charB.primary.HP / dmgAB_back).toFixed(1)} 击)`);
  console.log(`  B→A 神魂(背面): ${dmgBA_back}  (需 ${(charA.primary.HP / dmgBA_back).toFixed(1)} 击)`);
  
  // 批量模拟
  const SIM_COUNT = 2000;
  console.log(`\n--- 批量模拟 ${SIM_COUNT} 次 ---`);
  const results = runBatchSimulation(charA, charB, SIM_COUNT);
  printBattleReport(results, SIM_COUNT);
  
  // ---- 验证区间 ----
  console.log(`\n--- 区间验证 (筑基: HP 400~2000, 攻 120~600, 防 100~450, 反应 50~200) ---`);
  console.log(`  A.HP=${charA.primary.HP} (400~2000)${charA.primary.HP>=400&&charA.primary.HP<=2000?' ✓':' ✗'}`);
  console.log(`  B.HP=${charB.primary.HP} (400~2000)${charB.primary.HP>=400&&charB.primary.HP<=2000?' ✓':' ✗'}`);
  console.log(`  A.肉攻=${charA.primary.肉攻} (120~600)${charA.primary.肉攻>=120&&charA.primary.肉攻<=600?' ✓':' ✗'}`);
  console.log(`  B.神攻=${charB.primary.神攻} (120~600)${charB.primary.神攻>=120&&charB.primary.神攻<=600?' ✓':' ✗'}`);
  console.log(`  A.肉防=${charA.primary.肉防} (100~450)${charA.primary.肉防>=100&&charA.primary.肉防<=450?' ✓':' ✗'}`);
  console.log(`  B.神防=${charB.primary.神防} (100~450)${charB.primary.神防>=100&&charB.primary.神防<=450?' ✓':' ✗'}`);
  console.log(`  A.反应=${charA.primary.反应} (50~200)${charA.primary.反应>=50&&charA.primary.反应<=200?' ✓':' ✗'}`);
  console.log(`  B.反应=${charB.primary.反应} (50~200)${charB.primary.反应>=50&&charB.primary.反应<=200?' ✓':' ✗'}`);
}

main();
