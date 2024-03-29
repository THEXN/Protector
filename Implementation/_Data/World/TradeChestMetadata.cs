﻿using System;
using System.Collections.Generic;
using TShockAPI;

namespace Terraria.Plugins.CoderCow.Protector
{
    using JournalEntry = Tuple<string, DateTime>;

    public class TradeChestMetadata
    {
        private const string JournalEntryFormat = "{0} 使用 {2} 购买了 {1}";
        private const int JournalEntryMax = 20;

        public int ItemToSellAmount { get; set; }
        public int ItemToSellId { get; set; }
        public int ItemToPayAmount { get; set; }
        public int ItemToPayId { get; set; }
        public string ItemToPayGroup { get; set; }
        public int LootLimitPerPlayer { get; set; }
        public Dictionary<int, int> LootersTable { get; }
        public LinkedList<JournalEntry> TransactionJournal;

        public TradeChestMetadata()
        {
            this.LootersTable = new Dictionary<int, int>();
            this.TransactionJournal = new LinkedList<JournalEntry>();
        }

        public void AddJournalEntry(string buyingPlayer, Item buyItem, Item payItem)
        {
            string entry = string.Format(JournalEntryFormat, buyingPlayer, TShock.Utils.ItemTag(buyItem), TShock.Utils.ItemTag(payItem));
            this.TransactionJournal.AddFirst(new JournalEntry(entry, DateTime.UtcNow));

            if (this.TransactionJournal.Count > JournalEntryMax)
                this.TransactionJournal.RemoveLast();
        }

        public void AddOrUpdateLooter(int lootingPlayerId)
        {
            int lootAmount;
            if (this.LootersTable.TryGetValue(lootingPlayerId, out lootAmount))
            {
                if (this.LootLimitPerPlayer > 0 && lootAmount >= this.LootLimitPerPlayer)
                    throw new InvalidOperationException("玩家战利品已达上限。");

                this.LootersTable[lootingPlayerId] = lootAmount + 1;
            }
            else
                this.LootersTable[lootingPlayerId] = 1;
        }
    }
}
