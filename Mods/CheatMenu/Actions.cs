using System;
using System.Collections.Generic;
using System.Linq;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Experience;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Entities.Quests;

namespace CheatMenu;

/// <summary>One-shot actions (the "Tools" tab). Each works directly on the open character.</summary>
public static class Actions
{
    public static BankEntity? Bank(Game game) =>
        game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? e) ? e as BankEntity : null;

    public static List<Currency> Currencies(Game game) =>
        game.Data.GetDataRegistryOutput<Currency>().GetAllObjects().ToList();

    /// <summary>
    /// Every game item, by name. One Style's rare items ("Os..." local ids) are left out: each one only exists with
    /// One Style's own record of it, so a given copy would be broken (a sold one) or a duplicate (a unique).
    /// </summary>
    public static List<Item> Items(Game game) =>
        game.Data.GetDataRegistryOutput<Item>().GetAllObjects()
            .Where(i => !i.LocalId.StartsWith("Os", StringComparison.Ordinal))
            .OrderBy(ItemName, StringComparer.OrdinalIgnoreCase).ToList();

    public static string ItemName(Item item) => item.DisplayName.GetPluralText(1);

    public static List<ILeveledEntity<ExperienceComponent>> Skills(Game game) => game.LeveledEntities.ToList();

    public static void AddCurrency(Game game, Currency currency, long amount) => Bank(game)?.Wallet.AddQuantity(currency, amount);

    public static void AddItem(Game game, Item item, int quantity) => Bank(game)?.Inventory.AddQuantity(item, quantity);

    /// <summary>True when the player has locked this item's stack in the bank (the game's own "do not sell" mark).</summary>
    public static bool IsLocked(BankEntity bank, Item item) => bank.Inventory.Stacks.Any(s => s.Item == item && s.IsLocked);

    /// <summary>Sells through the game's own sale code and releases the price lookup it needs afterwards.</summary>
    public static void Sell(BankEntity bank, Item item, int quantity)
    {
        var stats = bank.ItemSales.GetItemStats(item);
        try { bank.ItemSales.SellItem(item, stats, quantity); }
        finally { stats.Destroy(); }
    }

    public static void SetLevel(ILeveledEntity<ExperienceComponent> skill, int level)
    {
        ExperienceComponent xp = skill.Experience;
        xp.SetXp(xp.Table.GetXpForLevel(Math.Clamp(level, 1, xp.LevelCap)));
    }

    public static void SetAllLevels(Game game, int level)
    {
        foreach (var skill in game.LeveledEntities) SetLevel(skill, level);
    }

    public static void MaxAllLevels(Game game)
    {
        foreach (var skill in game.LeveledEntities) SetLevel(skill, skill.Experience.LevelCap);
    }

    public static void RunOfflineHours(Game game, double hours) => game.RequestOfflineHours(hours);

    /// <summary>What <see cref="CompleteAllQuests"/> did.</summary>
    public readonly record struct QuestResult(int Done, int Waiting, int Failed);

    /// <summary>
    /// Every quest of the character completed and its rewards claimed, the way a player does it. The game's own
    /// "complete all" (its debug menu's) marks quests as claimed without ever giving the rewards, so it is not used.
    /// Per quest: started if not yet (the quest page's Start button), each step finished in turn (the game then
    /// starts the next and finally calls the quest completed), then the rewards claimed with the game's own claim
    /// (items and currency into the bank). Steps' item costs are not taken. A quest whose rewards do not fit in the
    /// bank stays completed with its rewards waiting: the quest page's Claim button gives them later.
    /// </summary>
    public static QuestResult CompleteAllQuests(Game game)
    {
        QuestsComponent? quests = game.Quests?.Quests;
        if (quests == null) return default;
        int done = 0, waiting = 0, failed = 0;
        foreach (Quest data in quests.Config.Quests)
        {
            if (!quests.ActiveQuests.TryGetValue(data, out ActiveQuest? quest) || quest.State == QuestState.Claimed) continue;
            try
            {
                if (quest.State == QuestState.Unstarted) quest.Start();
                foreach (ActiveQuestStep step in quest.ActiveSteps)
                    if (quest.State == QuestState.InProgress && step.State != QuestStepState.Complete) step.SetCompleted();
            }
            catch (Exception e) { Engine.Log($"Quest {data.Id} could not be finished step by step: {e.Message}"); }
            if (quest.State != QuestState.Completed)
            {
                // Something in the quest did not go the usual way: the game's own debug completion, rewards by hand.
                try
                {
                    if (quest.State != QuestState.Claimed) quest.SetCompleted();
                    GiveRewards(quests, quest);
                    done++;
                }
                catch (Exception e) { failed++; Engine.Log($"Quest {data.Id} could not be completed: {e.Message}"); }
                continue;
            }
            if (quests.ClaimRewards(quest)) done++;
            else waiting++;
        }
        return new QuestResult(done, waiting, failed);
    }

    static void GiveRewards(QuestsComponent quests, ActiveQuest quest)
    {
        foreach (ItemQuantity item in quest.Rewards.Items) quests.Inventory.AddQuantity(item.Item, item.Quantity);
        foreach (CurrencyQuantity currency in quest.Rewards.Currency) quests.Wallet.AddQuantity(currency.Currency, currency.Quantity);
    }
}
