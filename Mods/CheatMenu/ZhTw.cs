using System.Collections.Generic;

namespace CheatMenu;

/// <summary>
/// Traditional Chinese (zh-TW). Translated by a player (credit name to be confirmed), with the help of ChatGPT, for Cheat Menu 0.5.2.
/// Lines marked "(added)" were written for this release (texts the first translation did not cover, and texts that
/// are now whole sentences instead of pieces glued to numbers); the translator is asked to look them over.
/// The key is the English text exactly as the code uses it.
/// </summary>
static class ZhTw
{
    /// <summary>Skill names, with the words of the game's own Chinese translation.</summary>
    public static readonly Dictionary<string, string> Skills = new()
    {
        ["Woodcutting"] = "伐木", ["Mining"] = "採礦", ["Fishing"] = "釣魚", ["Firemaking"] = "生火", ["Cooking"] = "烹飪",
        ["Smithing"] = "鍛造", ["Crafting"] = "製造", ["Runecrafting"] = "符文鍛造", ["Fletching"] = "弓匠",
        ["Beastcrafting"] = "獸工造物", ["Herblore"] = "草藥學", ["Thieving"] = "扒竊", ["Agility"] = "靈巧",
        ["Astrology"] = "星象學", ["Hunting"] = "狩獵", ["Farming"] = "農務", ["Ranching"] = "畜牧", ["Attack"] = "攻擊",
        ["Strength"] = "力量", ["Defence"] = "防禦", ["Hitpoints"] = "生命值", ["Ranged"] = "遠程", ["Magic"] = "魔法",
        ["Prayer"] = "祝祭", ["Slayer"] = "屠殺者",
    };

    public static readonly Dictionary<string, string> Text = new()
    {
        // ---- settings
        ["Extra bank slots"] = "額外倉庫格數",
        ["Sell price"] = "出售價格",   // (changed: 出售 as everywhere else)
        ["Bone burying reward"] = "埋骨獎勵",
        ["XP multiplier"] = "經驗倍率",
        ["Game speed"] = "遊戲速度",
        ["Skill action speed"] = "技能動作速度",
        ["Farming: normal pace"] = "農務：正常速度", ["Ranching: normal pace"] = "畜牧：正常速度",
        ["Crops and the grain mill keep their normal pace when Game speed or Skill action speed is raised. Farming's own speed (Per skill page) still counts."]
            = "提高「遊戲速度」或「技能動作速度」時，作物和磨坊仍按正常速度進行。農務本身的速度（「各技能」頁面）仍然有效。",
        ["Animals grow, eat and produce at their normal pace when Game speed is raised. Ranching's own speed (Per skill page) still counts."]
            = "提高「遊戲速度」時，動物的成長、進食和產出仍按正常速度進行。畜牧本身的速度（「各技能」頁面）仍然有效。",
        ["Monster respawn speed"] = "怪物重生速度",
        ["Extra offline time limit"] = "額外離線時間上限",
        ["Double product chance"] = "雙倍產物機率",
        ["Keep materials chance"] = "材料保留機率",
        ["Product quantity"] = "產物數量",
        ["Extra products per action"] = "每次額外產物",
        ["Material cost reduction"] = "材料消耗減少",
        ["Woodcutting: instant cut chance"] = "伐木：立即砍倒機率",
        ["Thieving: never stunned"] = "扒竊：不會暈眩",
        ["Thieving: no damage when caught"] = "扒竊：被抓不受傷",
        ["Agility: no damage from a failed obstacle"] = "靈巧：失敗不受傷",
        ["Agility: keep the reward of a failed obstacle"] = "靈巧：失敗保留獎勵",
        ["Astrology: knowledge per study"] = "星象學：每次研究獲得的知識",
        ["God mode"] = "無敵模式",
        ["Damage dealt"] = "造成傷害",
        ["Attack speed"] = "攻擊速度",
        ["Accuracy"] = "精準值",   // (changed: the game's word for the accuracy rating it multiplies)
        ["Maximum hitpoints"] = "最大生命值",
        ["No death penalty"] = "死亡無懲罰",
        ["Double loot chance"] = "雙倍掉落機率",
        ["Keep supplies chance"] = "補給保留機率",
        ["Infinite ammo"] = "無限彈藥",
        ["Ignore immunity"] = "無視免疫",
        ["Infinite lanterns"] = "無限燈籠",
        ["Ranching: animals never die"] = "畜牧：動物永不死亡",
        ["No slayer area penalties"] = "屠殺者區域無懲罰",
        ["Slayer coin reward"] = "屠殺者硬幣獎勵",
        ["Cheap slayer task rerolls"] = "低價重抽屠殺者任務",
        ["Slayer task: most kills"] = "屠殺者任務：最多擊殺數",

        // ---- the line under each setting
        ["Adds bank slots on top of the ones you own."] = "在原有倉庫格數之外增加容量。",
        ["Multiplies what you get for selling items."] = "依倍率提高物品出售所得的金錢。",
        ["Multiplies what you get for burying bones."] = "依倍率提高埋葬骨頭取得的獎勵。",
        ["Multiplies all XP you gain, in every skill."] = "依倍率提高所有技能獲得的經驗值。",
        ["Runs the whole game faster: skills, combat and timers."] = "加速整個遊戲，包括技能、戰鬥與計時。",
        ["Shortens every skill action. Combat is not affected. Multiplies with a skill's own speed on the Per skill page (x2 here and x4 there is x8); together they stop at x20."]
            = "縮短所有技能動作的時間，不影響戰鬥。會與「各技能」頁面中該技能的速度相乘（這裡 x2、那裡 x4 即為 x8），合計最高 x20。",   // (added)
        ["Monsters appear sooner after a kill."] = "擊殺怪物後更快重生。",
        ["Raises the limit of offline progress you can collect."] = "提高可領取的離線進度時間上限。",
        ["Chance that a skill action gives double products."] = "技能動作產出雙倍物品的機率。",
        ["Chance that an action does not use up its materials."] = "動作不消耗材料的機率。",
        ["Multiplies how many items an action produces."] = "依倍率增加每次動作產出的物品數量。",
        ["Adds this many extra items to every action."] = "每次動作額外增加指定數量的物品。",
        ["Recipes need fewer materials."] = "製作配方需要更少材料。",
        ["Chance to fell a tree with a single hit."] = "一擊砍倒樹木的機率。",
        ["A failed pickpocket never stuns you (it can still hurt: see the next setting)."] = "扒竊失敗不會暈眩，但仍可能受傷（見下一項設定）。",
        ["A failed pickpocket no longer hurts you (God mode only covers combat)."] = "扒竊失敗不會受傷；無敵模式只影響戰鬥。",
        ["Failing an agility obstacle no longer hurts you (God mode only covers combat)."] = "靈巧障礙失敗不再受傷；無敵模式只影響戰鬥。",
        ["Failing an agility obstacle no longer loses the item reward it was carrying."] = "靈巧障礙失敗也不會失去物品獎勵。",
        ["Multiplies the Knowledge each study adds to a constellation. Astrology XP is a separate setting."] = "依倍率提高每次研究星座獲得的知識；星象學經驗另行設定。",
        ["You take no damage in combat."] = "戰鬥中不會受到傷害。",
        ["Multiplies all damage you deal."] = "依倍率提高造成的所有傷害。",
        ["You attack this many times faster."] = "依倍率提高攻擊速度。",
        ["Multiplies your accuracy rating."] = "依倍率提高精準值。",
        ["Multiplies your maximum hitpoints."] = "依倍率提高最大生命值。",
        ["Dying no longer costs you an item."] = "死亡不再損失物品。",
        ["Chance that a kill drops double items, coins and bones."] = "擊殺後雙倍掉落物品、金錢與骨頭的機率。",
        ["Chance not to use up food, ammo, runes or prayer points."] = "不消耗食物、彈藥、符文或祝祭點的機率。",   // (changed: the game says 祝祭)
        ["Arrows, bolts, javelins, boomerangs and throwing hammers are never used up: one is enough."] = "箭矢、弩矢、標槍、迴旋鏢和投擲錘永不消耗：一個就夠。",
        ["Any attack style hits monsters that are immune to it (Wind Drifter, for example). Poison and bleeding stick to them too."] = "任何戰鬥風格都能擊中對其免疫的怪物（例如 Wind Drifter）。中毒和流血也會對牠們生效。",
        ["Lit lanterns are never used up in dark areas: one is enough."] = "點亮的燈籠在黑暗區域永不消耗：一個就夠了。",
        ["Ranch animals stop aging: their lifetime stands still, so they never die of old age."] = "牧場動物不再衰老：壽命保持不變，永遠不會老死。",
        ["Slayer areas lose their bad area effects (burn, less healing from food, quicksand...), as if you had bought every Area Effect Removal in the shop."] = "屠殺者區域失去其不利區域效果（灼燒、食物回復減少、流沙……），如同你已在商店買下所有區域效果移除。",
        ["Multiplies the slayer coins earned per task kill."] = "依倍率提高擊殺屠殺者任務目標所得的硬幣。",
        ["Changing your slayer task costs 1 coin (the game's minimum)."] = "重抽屠殺者任務只需 1 枚硬幣（遊戲的最低費用）。",
        ["No slayer task asks for more kills than this, at every master and also when extended (0 keeps the game's own lengths)."] = "屠殺者任務要求的擊殺數不會超過此值，所有大師都一樣，延長後也是（0 表示保留遊戲原本的任務長度）。",

        // ---- the window
        ["CHEAT MENU"] = "修改器選單",
        ["Close  (F8)"] = "關閉 (F8)",
        ["Auto"] = "自動",   // (added)
        ["Language"] = "語言",
        ["Bank"] = "倉庫",   // (added)
        ["XP"] = "經驗",   // (added)
        ["Speed"] = "速度",
        ["Skills"] = "技能",
        ["Combat"] = "戰鬥",   // (added)
        ["Per skill"] = "各技能",
        ["Loot filter"] = "戰利品過濾",
        ["Materials"] = "材料",
        ["Tools"] = "工具",
        ["Changes apply at once and are remembered per character"] = "設定立即生效，並依角色分別記錄",
        ["Reset all settings"] = "重設所有設定",
        ["Click again to confirm"] = "再次點擊確認",
        ["All settings are back to normal."] = "所有設定已恢復預設值。",
        ["Reset"] = "重設",
        ["Back to the game's normal value"] = "恢復遊戲預設值",
        ["back to normal"] = "恢復預設",   // (added)
        ["ON"] = "開啟",   // (added)
        ["OFF"] = "關閉",   // (added)

        // ---- per skill
        ["Each skill's own XP multiplier and action speed. These stack with the settings on the XP and Speed pages."] = "每項技能獨立設定經驗與動作速度倍率，可與經驗及速度頁面的設定疊加。",
        ["SKILL"] = "技能",
        ["SPEED"] = "速度",
        ["All skills at once"] = "一次設定所有技能",
        ["Writes one value into every skill of the table below. XP and speed are set separately."] = "將相同數值套用到下方所有技能；經驗與速度分別設定。",
        ["Apply to all"] = "套用至全部",
        ["{0}: back to normal for all {1} skills"] = "{0}：全部 {1} 項技能已恢復預設值",   // (added)
        ["{0}: {1}x for all {2} skills"] = "{0}：全部 {2} 項技能設為 {1}x",   // (added)
        ["{0} XP"] = "{0}經驗",   // (added)
        ["{0} speed"] = "{0}速度",   // (added)

        // ---- loot filter
        ["Choose what happens to items that monsters and dungeons drop. Only the dropped amount is sold or destroyed; what is already in your bank is never touched. Double-click an item to step through Keep, Sell and Destroy."]
            = "選擇怪物與地城掉落物品的處理方式。只會出售或銷毀掉落的數量，倉庫中原有的物品不受影響。雙擊物品可在保留、出售、銷毀之間切換。",   // (added)
        ["Search drops..."] = "搜尋掉落物...",
        ["All drops"] = "所有掉落物",
        ["Drops of the current monster"] = "目前怪物掉落物",
        ["Only items with a rule"] = "只顯示已設定規則的物品",
        ["Item"] = "物品",   // (added)
        ["When dropped"] = "掉落時",
        ["Selected items:"] = "已選物品：",
        ["Keep"] = "保留",   // (added)
        ["Sell"] = "出售",   // (added)
        ["Destroy"] = "銷毀",
        ["Sell when dropped"] = "掉落時出售",
        ["Destroy when dropped"] = "掉落時銷毀",
        ["Remove all rules"] = "移除所有規則",
        ["Loot filter: every drop is kept again."] = "戰利品過濾：所有掉落物恢復保留。",   // (added)
        ["Select one or more items in the list first."] = "請先從清單選取一個或多個物品。",
        ["{0} items"] = "{0} 項物品",
        ["Loot filter: {0} -> {1}"] = "戰利品過濾：{0} → {1}",
        ["No monster is selected in combat."] = "戰鬥中尚未選取怪物。",
        ["Showing the drops of {0}."] = "顯示「{0}」的掉落物。",
        ["{0} of {1} items shown  |  {2} rule(s)  |  since this character was opened: {3:N0} sold, {4:N0} destroyed"]
            = "顯示 {0} / {1} 項物品  |  {2} 項規則  |  本次開啟角色以來：已出售 {3:N0}，已銷毀 {4:N0}",

        // ---- materials
        ["Pick the materials you no longer want to need. A material set to \"Not needed\" is taken out of every recipe and item upgrade that asks for it: bars without coal, potions without vials. Double-click a material to switch it."]
            = "選擇不想再需要的材料。設為「不再需要」的材料會從所有需要它的配方與物品升級中移除：例如不用煤炭的金屬錠、不用藥瓶的藥水。雙擊材料即可切換。",   // (added)
        ["Search materials..."] = "搜尋材料...",
        ["All materials, most used first"] = "所有材料（依配方數排序）",   // (changed: it counts recipes, not use)
        ["Only the ones not needed"] = "只顯示不需要的材料",
        ["Material"] = "材料",
        ["Asked for by"] = "配方數",   // (changed: the column shows how many recipes ask for it)
        ["In recipes"] = "需求狀態",   // (changed: the column shows Needed / Not needed)
        ["Selected materials:"] = "已選材料：",
        ["Not needed"] = "不再需要",
        ["Needed again"] = "恢復需求",
        ["Everything needed again"] = "全部恢復需求",
        ["Materials: every recipe asks for all of its materials again."] = "材料：所有配方及物品升級已恢復原有材料需求。",   // (added)
        ["Select one or more materials in the list first."] = "請先選取一個或多個材料。",
        ["{0} materials"] = "{0} 種材料",
        ["Materials: {0} -> no longer needed in any recipe."] = "材料：{0} → 所有配方及物品升級不再需要。",
        ["Materials: {0} -> needed again."] = "材料：{0} → 恢復需求。",
        ["1 recipe"] = "1 個配方",
        ["{0} recipes"] = "{0} 個配方",
        ["Needed"] = "需要",
        ["{0} of {1} materials shown  |  {2} not needed"] = "顯示 {0} / {1} 種材料  |  {2} 種不需要",

        // ---- tools
        ["Money and items"] = "金錢與物品",
        ["Give currency"] = "取得貨幣",
        ["Add"] = "加入",   // (added)
        ["Choose an item"] = "選擇物品",
        ["Click and type to search. Items are grouped by the bank's own kinds."] = "點擊並輸入文字搜尋；物品依倉庫分類。",
        ["Give item"] = "取得物品",
        ["Choose an item first."] = "請先選擇物品。",
        ["Choose an item to sell"] = "選擇出售物品",
        ["The items being sold now."] = "目前在自動出售清單中的物品。",
        ["Auto-sell from bank"] = "倉庫自動出售",
        ["Choose the item to sell first."] = "請先選擇要出售的物品。",
        ["Auto-sell: {0} added."] = "自動出售：已加入「{0}」。",   // (added)
        ["Remove"] = "移除",
        ["Items on this list are sold from the bank about once a second, wherever they came from."] = "清單中的物品約每秒從倉庫自動出售一次，不論來源。",
        ["Levels"] = "等級",
        ["Set skill level"] = "設定技能等級",
        ["Set"] = "設定",   // (added)
        ["{0}: level {1}"] = "{0}：等級 {1}",   // (added)
        ["All skills"] = "所有技能",
        ["Set all to the level above"] = "全部設為上方等級",
        ["All skills set."] = "所有技能等級已設定。",
        ["Max all"] = "全部升滿",
        ["All skills are at their maximum level."] = "所有技能已達最高等級。",
        ["Time and quests"] = "時間與任務",
        ["Skip time (as if the game was closed)"] = "跳過時間（模擬離線）",
        ["Run"] = "執行",   // (added)
        ["Quests"] = "任務",
        ["Complete all quests"] = "完成所有任務",
        ["{0} quest(s) completed, rewards given."] = "已完成 {0} 個任務，獎勵已發放。",
        ["{0} reward(s) did not fit in the bank: claim them on the Quests page."] = "有 {0} 份獎勵放不進銀行：請在任務頁面領取。",
        ["{0} quest(s) could not be completed (see the log)."] = "有 {0} 個任務無法完成（見日誌）。",
        ["All quests are already completed."] = "所有任務皆已完成。",

        // ---- the searchable lists
        ["Type to search..."] = "輸入文字搜尋...",
        ["{0} to choose from"] = "共 {0} 個選項",
        ["No matches"] = "沒有符合的項目",
        ["{0} of {1}"] = "{0} / {1}",   // (added)
        ["  (not available now)"] = "  （目前無法使用）",
        ["This choice is not offered by the game right now."] = "遊戲目前不提供此選項。",
        ["Other"] = "其他",   // (added)
        ["{0:N0} in bank"] = "倉庫：{0:N0}",   // (added)
    };
}
