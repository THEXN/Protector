﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using Terraria.ID;
using Terraria.Plugins.Common;
using TShockAPI;
using TShockAPI.DB;
using DPoint = System.Drawing.Point;

namespace Terraria.Plugins.CoderCow.Protector
{
    public class ChestManager
    {
        // Terraria 世界宝箱的最后一个宝箱用于伪装保护者世界元数据中存储的所有其他宝箱。
        public static readonly int DummyChestIndex = Main.chest.Length - 1;
        public static readonly Chest DummyChest;

        private Configuration config;

        public PluginTrace PluginTrace { get; }
        public WorldMetadata WorldMetadata { get; }
        public ServerMetadataHandler ServerMetadataHandler { get; }
        public PluginCooperationHandler CooperationHandler { get; }
        public TimerManager RefillTimers { get; }
        public Func<TimerBase, bool> RefillTimerCallbackHandler { get; }

        public Configuration Config
        {
            get { return this.config; }
            set
            {
                if (value == null) throw new ArgumentNullException();
                this.config = value;
            }
        }

        static ChestManager()
        {
            ChestManager.DummyChest = Main.chest[ChestManager.DummyChestIndex] = new Chest();
            ChestManager.DummyChest.item = new Item[Chest.maxItems];
        }

        public ChestManager(
          PluginTrace pluginTrace, Configuration config, ServerMetadataHandler serverMetadataHandler, WorldMetadata worldMetadata,
          PluginCooperationHandler cooperationHandler
        )
        {
            this.PluginTrace = pluginTrace;
            this.config = config;
            this.ServerMetadataHandler = serverMetadataHandler;
            this.WorldMetadata = worldMetadata;
            this.CooperationHandler = cooperationHandler;

            this.RefillTimers = new TimerManager(pluginTrace);
            this.RefillTimerCallbackHandler = this.RefillChestTimer_Callback;
        }

        /// <returns>
        ///   返回一个布尔值，如果在指定位置已经存在一个补充宝箱，并且只是更新了它的补充时间，则返回<c>false</c>；  
        ///   如果实际上创建了一个新的补充宝箱，则返回<c>true</c>。  
        /// </returns>
        public bool SetUpRefillChest(
          TSPlayer player, DPoint tileLocation, TimeSpan? refillTime, bool? oneLootPerPlayer = null, int? lootLimit = null,
          bool? autoLock = null, bool? autoEmpty = null, bool fairLoot = false, bool checkPermissions = false
        )
        {
            if (player == null) throw new ArgumentNullException();
            if (!(TerrariaUtils.Tiles[tileLocation] != null)) throw new ArgumentException("tileLocation");
            if (!(TerrariaUtils.Tiles[tileLocation].active())) throw new ArgumentException("tileLocation");
            if (!(lootLimit == null || lootLimit >= -1)) throw new ArgumentOutOfRangeException();

            ITile tile = TerrariaUtils.Tiles[tileLocation];
            if (tile.type != TileID.Containers && tile.type != TileID.Containers2 && tile.type != TileID.Dressers)
                throw new InvalidBlockTypeException(tile.type);

            if (checkPermissions && !player.Group.HasPermission(ProtectorPlugin.SetRefillChests_Permission))
                throw new MissingPermissionException(ProtectorPlugin.SetRefillChests_Permission);

            DPoint chestLocation = TerrariaUtils.Tiles.MeasureObject(tileLocation).OriginTileLocation;
            ProtectionEntry protection;
            lock (this.WorldMetadata.Protections)
                if (!this.WorldMetadata.Protections.TryGetValue(chestLocation, out protection))
                    throw new NoProtectionException(chestLocation);

            if (protection.BankChestKey != BankChestDataKey.Invalid)
                throw new ChestIncompatibilityException();

            RefillChestMetadata refillChestData;
            if (protection.RefillChestData != null)
            {
                refillChestData = protection.RefillChestData;

                if (refillTime != null)
                    refillChestData.RefillTimer.TimeSpan = refillTime.Value;
                if (oneLootPerPlayer != null)
                    refillChestData.OneLootPerPlayer = oneLootPerPlayer.Value;
                if (lootLimit != null)
                    refillChestData.RemainingLoots = lootLimit.Value;
                if (autoLock != null)
                    refillChestData.AutoLock = autoLock.Value;
                if (autoEmpty != null)
                    refillChestData.AutoEmpty = autoEmpty.Value;

                if (refillChestData.OneLootPerPlayer || refillChestData.RemainingLoots > 0)
                    if (refillChestData.Looters == null)
                        refillChestData.Looters = new Collection<int>();
                    else
                        refillChestData.Looters = null;

                this.RefillTimers.RemoveTimer(refillChestData.RefillTimer);

                return false;
            }

            IChest chest = this.ChestFromLocation(chestLocation);
            if (chest == null)
                throw new NoChestDataException(chestLocation);

            TimeSpan actualRefillTime = TimeSpan.Zero;
            if (refillTime != null)
                actualRefillTime = refillTime.Value;

            refillChestData = new RefillChestMetadata(player.Account.ID);
            refillChestData.RefillTimer = new Timer(actualRefillTime, refillChestData, this.RefillTimerCallbackHandler);
            if (oneLootPerPlayer != null)
                refillChestData.OneLootPerPlayer = oneLootPerPlayer.Value;
            if (lootLimit != null)
                refillChestData.RemainingLoots = lootLimit.Value;

            if (refillChestData.OneLootPerPlayer || refillChestData.RemainingLoots > 0)
                refillChestData.Looters = new Collection<int>();
            else
                refillChestData.Looters = null;

            if (autoLock != null)
                refillChestData.AutoLock = autoLock.Value;

            if (autoEmpty != null)
                refillChestData.AutoEmpty = autoEmpty.Value;

            bool fairLootPutItem = fairLoot;
            for (int i = 0; i < Chest.maxItems; i++)
            {
                ItemData item = chest.Items[i];
                if (item.StackSize == 0 && fairLootPutItem)
                {
                    try
                    {
                        bool isLocked;
                        int keyItemType = TerrariaUtils.Tiles.GetItemTypeFromChestStyle(TerrariaUtils.Tiles.GetChestStyle(tile, out isLocked));
                        chest.Items[i] = new ItemData(keyItemType);
                    }
                    catch (ArgumentException) { }

                    fairLootPutItem = false;
                }

                refillChestData.RefillItems[i] = item;
            }

            protection.RefillChestData = refillChestData;

            return true;
        }

        public void SetUpBankChest(TSPlayer player, DPoint tileLocation, int bankChestIndex, bool checkPermissions = false)
        {
            if (player == null) throw new ArgumentNullException();
            if (!(TerrariaUtils.Tiles[tileLocation] != null)) throw new ArgumentException("tileLocation");
            if (!(TerrariaUtils.Tiles[tileLocation].active())) throw new ArgumentException("tileLocation");
            if (!(bankChestIndex >= 1)) throw new ArgumentOutOfRangeException("bankChestIndex");

            ITile tile = TerrariaUtils.Tiles[tileLocation];
            if (tile.type != TileID.Containers && tile.type != TileID.Containers2 && tile.type != TileID.Dressers)
                throw new InvalidBlockTypeException(tile.type);

            if (checkPermissions && !player.Group.HasPermission(ProtectorPlugin.SetBankChests_Permission))
                throw new MissingPermissionException(ProtectorPlugin.SetBankChests_Permission);

            if (
              checkPermissions && !player.Group.HasPermission(ProtectorPlugin.NoBankChestLimits_Permission)
            )
            {
                if (bankChestIndex > this.Config.MaxBankChestsPerPlayer)
                    throw new ArgumentOutOfRangeException("bankChestIndex", this.Config.MaxBankChestsPerPlayer, "全局保险宝箱已达上限。");

                int byGroupLimit;
                if (
                  this.Config.MaxBankChests.TryGetValue(player.Group.Name, out byGroupLimit) &&
                  bankChestIndex > byGroupLimit
                )
                {
                    throw new ArgumentOutOfRangeException("bankChestIndex", byGroupLimit, "用户组保险宝箱已达上限。");
                }
            }

            DPoint chestLocation = TerrariaUtils.Tiles.MeasureObject(tileLocation).OriginTileLocation;
            ProtectionEntry protection;
            lock (this.WorldMetadata.Protections)
                if (!this.WorldMetadata.Protections.TryGetValue(chestLocation, out protection))
                    throw new NoProtectionException(chestLocation);

            if (protection.RefillChestData != null || protection.TradeChestData != null)
                throw new ChestIncompatibilityException();

            IChest chest = this.ChestFromLocation(chestLocation);
            if (chest == null)
                throw new NoChestDataException(chestLocation);

            if (protection.BankChestKey != BankChestDataKey.Invalid)
                throw new ChestTypeAlreadyDefinedException();

            BankChestDataKey bankChestKey = new BankChestDataKey(player.Account.ID, bankChestIndex);
            lock (this.WorldMetadata.Protections)
            {
                if (this.WorldMetadata.Protections.Values.Any(p => p.BankChestKey == bankChestKey))
                    throw new BankChestAlreadyInstancedException();
            }

            if (checkPermissions && !player.Group.HasPermission(ProtectorPlugin.BankChestShare_Permission))
                protection.Unshare();

            BankChestMetadata bankChest = this.ServerMetadataHandler.EnqueueGetBankChestMetadata(bankChestKey).Result;
            if (bankChest == null)
            {
                bankChest = new BankChestMetadata();
                for (int i = 0; i < Chest.maxItems; i++)
                    bankChest.Items[i] = chest.Items[i];

                this.ServerMetadataHandler.EnqueueAddOrUpdateBankChest(bankChestKey, bankChest);
            }
            else
            {
                for (int i = 0; i < Chest.maxItems; i++)
                    if (chest.Items[i].StackSize > 0)
                        throw new ChestNotEmptyException(chestLocation);

                for (int i = 0; i < Chest.maxItems; i++)
                    chest.Items[i] = bankChest.Items[i];
            }

            protection.BankChestKey = bankChestKey;
        }

        public void SetUpTradeChest(TSPlayer player, DPoint tileLocation, int sellAmount, int sellItemId, int payAmount, object payItemIdOrGroup, int lootLimit = 0, bool checkPermissions = false)
        {
            if (player == null) throw new ArgumentNullException();
            if (!(TerrariaUtils.Tiles[tileLocation] != null)) throw new ArgumentException("tileLocation");
            if (!(TerrariaUtils.Tiles[tileLocation].active())) throw new ArgumentException("tileLocation");
            if (!(sellAmount > 0)) throw new ArgumentOutOfRangeException("sellAmount");
            if (!(payAmount > 0)) throw new ArgumentOutOfRangeException("payAmount");

            Item itemInfo = new Item();
            itemInfo.netDefaults(sellItemId);
            if (sellAmount > itemInfo.maxStack)
                throw new ArgumentOutOfRangeException(nameof(sellAmount));

            bool isPayItemGroup = payItemIdOrGroup is string;
            if (!isPayItemGroup)
            {
                itemInfo.netDefaults((int)payItemIdOrGroup);
                if (payAmount > itemInfo.maxStack)
                    throw new ArgumentOutOfRangeException(nameof(payAmount));
            }

            ITile tile = TerrariaUtils.Tiles[tileLocation];
            if (tile.type != TileID.Containers && tile.type != TileID.Containers2 && tile.type != TileID.Dressers)
                throw new InvalidBlockTypeException(tile.type);

            if (checkPermissions && !player.Group.HasPermission(ProtectorPlugin.SetTradeChests_Permission))
                throw new MissingPermissionException(ProtectorPlugin.SetTradeChests_Permission);

            DPoint chestLocation = TerrariaUtils.Tiles.MeasureObject(tileLocation).OriginTileLocation;
            ProtectionEntry protection;
            lock (this.WorldMetadata.Protections)
                if (!this.WorldMetadata.Protections.TryGetValue(chestLocation, out protection))
                    throw new NoProtectionException(chestLocation);

            if (protection.BankChestKey != BankChestDataKey.Invalid)
                throw new ChestIncompatibilityException();

            IChest chest = this.ChestFromLocation(chestLocation);
            if (chest == null)
                throw new NoChestDataException(chestLocation);

            bool isNewTradeChest = (protection.TradeChestData == null);
            if (isNewTradeChest && checkPermissions && this.CooperationHandler.IsSeconomyAvailable && !player.Group.HasPermission(ProtectorPlugin.FreeTradeChests_Permission))
            {
#if SEconomy
        if (this.CooperationHandler.Seconomy_GetBalance(player.Name) < this.Config.TradeChestPayment)
          throw new PaymentException(this.Config.TradeChestPayment);

        this.CooperationHandler.Seconomy_TransferToWorld(player.Name, this.Config.TradeChestPayment, "Trade Chest", "Setup Price");
#endif
            }

            protection.TradeChestData = protection.TradeChestData ?? new TradeChestMetadata();
            protection.TradeChestData.ItemToSellAmount = sellAmount;
            if (protection.TradeChestData.ItemToSellId != sellItemId)
                protection.TradeChestData.LootersTable.Clear();

            protection.TradeChestData.ItemToSellId = sellItemId;
            protection.TradeChestData.ItemToPayAmount = payAmount;
            if (isPayItemGroup)
                protection.TradeChestData.ItemToPayGroup = (payItemIdOrGroup as string).ToLowerInvariant();
            else
                protection.TradeChestData.ItemToPayId = (int)payItemIdOrGroup;
            protection.TradeChestData.LootLimitPerPlayer = lootLimit;

            // TODO: uncomment
            //this.PluginTrace.WriteLineVerbose($"{player.Name} just setup a trade chest selling {sellAmount}x {sellItemId} for {payAmount}x {payItemId} with a limit of {lootLimit} at {tileLocation}");
        }

        public bool TryRefillChest(DPoint chestLocation, RefillChestMetadata refillChestData)
        {
            IChest chest = this.ChestFromLocation(chestLocation);
            if (chest == null)
                throw new InvalidOperationException("在指定位置未找到宝箱数据。");

            return this.TryRefillChest(chest, refillChestData);
        }

        public bool TryRefillChest(IChest chest, RefillChestMetadata refillChestData)
        {
            for (int i = 0; i < Chest.maxItems; i++)
                chest.Items[i] = refillChestData.RefillItems[i];

            if (
              refillChestData.AutoLock && refillChestData.RefillTime != TimeSpan.Zero &&
              !TerrariaUtils.Tiles.IsChestLocked(TerrariaUtils.Tiles[chest.Location])
            )
            {
                TerrariaUtils.Tiles.LockChest(chest.Location);
            }

            return true;
        }

        public IChest PlaceChest(ushort tileType, int style, DPoint placeLocation)
        {
            if (!(tileType == TileID.Containers || tileType == TileID.Containers2 || tileType == TileID.Dressers))
                throw new ArgumentException();

            IChest chest;
            bool isDresser = (tileType == TileID.Dressers);
            int chestIndex = WorldGen.PlaceChest(placeLocation.X, placeLocation.Y, tileType, false, style);
            bool isWorldFull = (chestIndex == -1 || chestIndex == ChestManager.DummyChestIndex);

            if (!isWorldFull)
            {
                chest = new ChestAdapter(chestIndex, Main.chest[chestIndex]);
            }
            else
            {
                lock (this.WorldMetadata.ProtectorChests)
                {
                    isWorldFull = (this.WorldMetadata.ProtectorChests.Count >= this.Config.MaxProtectorChests);
                    if (isWorldFull)
                        throw new LimitEnforcementException();

                    if (isDresser)
                        WorldGen.PlaceDresserDirect(placeLocation.X, placeLocation.Y, tileType, style, chestIndex);
                    else
                        WorldGen.PlaceChestDirect(placeLocation.X, placeLocation.Y, tileType, style, chestIndex);

                    DPoint chestLocation = TerrariaUtils.Tiles.MeasureObject(placeLocation).OriginTileLocation;
                    chest = new ProtectorChestData(chestLocation);
                    this.WorldMetadata.ProtectorChests.Add(chestLocation, (ProtectorChestData)chest);

                    chestIndex = ChestManager.DummyChestIndex;
                }
            }

            int storageType = 0;
            if (isDresser)
                storageType = 2;
            if (tileType == TileID.Containers2)
                storageType = 4;

            TSPlayer.All.SendData(PacketTypes.PlaceChest, string.Empty, storageType, placeLocation.X, placeLocation.Y, style, chestIndex);
            // 客户端总是会对最新的宝箱索引显示打开/关闭动画。但是，当存在多个ID为999的宝箱时，  
            // 这会看起来很奇怪，因此，我们告诉客户端在玩家永远不会注意到动画的位置上，存在另一个ID为999的宝箱。
            if (chestIndex == ChestManager.DummyChestIndex)
                TSPlayer.All.SendData(PacketTypes.PlaceChest, string.Empty, storageType, 0, 0, style, chestIndex);

            return chest;
        }

        public IChest CreateChestData(DPoint chestLocation)
        {
            int chestIndex = Chest.CreateChest(chestLocation.X, chestLocation.Y);
            bool isWorldFull = (chestIndex == -1 || chestIndex == ChestManager.DummyChestIndex);

            if (!isWorldFull)
            {
                return new ChestAdapter(chestIndex, Main.chest[chestIndex]);
            }
            else
            {
                lock (this.WorldMetadata.ProtectorChests)
                {
                    isWorldFull = (this.WorldMetadata.ProtectorChests.Count >= this.Config.MaxProtectorChests);
                    if (isWorldFull)
                        throw new LimitEnforcementException();

                    IChest chest = new ProtectorChestData(chestLocation);
                    this.WorldMetadata.ProtectorChests.Add(chestLocation, (ProtectorChestData)chest);
                    return chest;
                }
            }
        }

        public IChest ChestFromLocation(DPoint chestLocation, TSPlayer reportToPlayer = null)
        {
            ITile tile = TerrariaUtils.Tiles[chestLocation];
            if (!tile.active() || (tile.type != TileID.Containers && tile.type != TileID.Containers2 && tile.type != TileID.Dressers))
            {
                reportToPlayer?.SendErrorMessage("这个位置没有宝箱。");
                return null;
            }

            IChest chest = null;
            int chestIndex = Chest.FindChest(chestLocation.X, chestLocation.Y);
            bool isWorldDataChest = (chestIndex != -1 && chestIndex != ChestManager.DummyChestIndex);

            if (isWorldDataChest)
            {
                Chest tChest = Main.chest[chestIndex];
                if (tChest != null)
                    chest = new ChestAdapter(chestIndex, Main.chest[chestIndex]);
                else
                    reportToPlayer?.SendErrorMessage($"预计此宝箱（ID：{chestIndex}）的世界数据应该存在，但并未找到");
            }
            else
            {
                lock (this.WorldMetadata.ProtectorChests)
                {
                    ProtectorChestData protectorChest;
                    if (this.WorldMetadata.ProtectorChests.TryGetValue(chestLocation, out protectorChest))
                        chest = protectorChest;
                    else
                        reportToPlayer?.SendErrorMessage("这个宝箱的数据记录在世界数据和保护者数据中均缺失。");
                }
            }

            return chest;
        }

        public void DestroyChest(DPoint anyTileLocation)
        {
            DPoint chestLocation = TerrariaUtils.Tiles.MeasureObject(anyTileLocation).OriginTileLocation;
            this.DestroyChest(this.ChestFromLocation(chestLocation));
        }

        public IEnumerable<IChest> EnumerateAllChests()
        {
            for (int i = 0; i < Main.chest.Length; i++)
            {
                if (Main.chest[i] != null && i != ChestManager.DummyChestIndex)
                    yield return new ChestAdapter(i, Main.chest[i]);
            }

            lock (this.WorldMetadata.ProtectorChests)
            {
                foreach (ProtectorChestData protectorChest in this.WorldMetadata.ProtectorChests.Values)
                    yield return protectorChest;
            }
        }

        public IEnumerable<IChest> EnumerateProtectorChests()
        {
            lock (this.WorldMetadata.ProtectorChests)
            {
                foreach (ProtectorChestData protectorChest in this.WorldMetadata.ProtectorChests.Values)
                    yield return protectorChest;
            }
        }

        public void DestroyChest(IChest chest)
        {
            if (chest != null)
            {
                int chestIndex;
                if (chest.IsWorldChest)
                {
                    Main.chest[chest.Index] = null;
                    chestIndex = chest.Index;
                }
                else
                {
                    lock (this.WorldMetadata.ProtectorChests)
                        this.WorldMetadata.ProtectorChests.Remove(chest.Location);

                    chestIndex = ChestManager.DummyChestIndex;
                }

                WorldGen.KillTile(chest.Location.X, chest.Location.Y);
                TSPlayer.All.SendData(PacketTypes.PlaceChest, string.Empty, 1, chest.Location.X, chest.Location.Y, 0, chestIndex);
            }
        }

        public bool EnsureRefillChest(ProtectionEntry protection)
        {
            if (protection.RefillChestData == null)
                return false;

            if (this.ChestFromLocation(protection.TileLocation) == null)
            {
                protection.RefillChestData = null;
                return false;
            }

            protection.RefillChestData.RefillTimer.Data = protection.RefillChestData;
            protection.RefillChestData.RefillTimer.Callback = this.RefillTimerCallbackHandler;
            this.RefillTimers.ContinueTimer(protection.RefillChestData.RefillTimer);
            return true;
        }

        public bool EnsureBankChest(ProtectionEntry protection, bool resetContent)
        {
            if (protection.BankChestKey == BankChestDataKey.Invalid)
                return false;

            BankChestMetadata bankChest = this.ServerMetadataHandler.EnqueueGetBankChestMetadata(protection.BankChestKey).Result;
            if (bankChest == null)
            {
                protection.BankChestKey = BankChestDataKey.Invalid;
                return false;
            }

            IChest chest = this.ChestFromLocation(protection.TileLocation);
            if (chest == null)
            {
                protection.BankChestKey = BankChestDataKey.Invalid;
                return false;
            }

            UserAccount owner = TShock.UserAccounts.GetUserAccountByID(protection.Owner);
            if (owner == null)
            {
                this.DestroyChest(chest);

                protection.BankChestKey = BankChestDataKey.Invalid;
                return false;
            }

            Group ownerGroup = TShock.Groups.GetGroupByName(owner.Group);
            if (ownerGroup != null)
            {
                if (protection.SharedUsers != null && !ownerGroup.HasPermission(ProtectorPlugin.BankChestShare_Permission))
                    protection.SharedUsers.Clear();
                if (protection.SharedGroups != null && !ownerGroup.HasPermission(ProtectorPlugin.BankChestShare_Permission))
                    protection.SharedGroups.Clear();
                if (protection.IsSharedWithEveryone && !ownerGroup.HasPermission(ProtectorPlugin.BankChestShare_Permission))
                    protection.IsSharedWithEveryone = false;
            }

            if (resetContent)
            {
                for (int i = 0; i < Chest.maxItems; i++)
                    chest.Items[i] = bankChest.Items[i];
            }

            return true;
        }

        public void HandleGameSecondUpdate()
        {
            this.RefillTimers.HandleGameUpdate();
            this.UpdateUnregionedProtections();
        }
        private void UpdateUnregionedProtections()
        {
            lock (this.WorldMetadata.Protections)
            {
                foreach (KeyValuePair<DPoint, ProtectionEntry> kvp in this.WorldMetadata.Protections)
                {
                    UserAccount user = TShock.UserAccounts.GetUserAccountByID(kvp.Value.Owner);
                    TSPlayer player = TSPlayer.FindByNameOrID(user.Name).FirstOrDefault();
                    if (player == null)
                        return;

                    if (player.HasPermission(ProtectorPlugin.RestrictProtections_Permission))
                    {
                        List<Region> regions = TShock.Regions.InAreaRegion(kvp.Value.TileLocation.X, kvp.Value.TileLocation.Y).ToList();

                        if (regions.Count == 0)
                        {
                            if (kvp.Value.BankChestKey != BankChestDataKey.Invalid)
                            {
                                IChest chest = this.ChestFromLocation(kvp.Value.TileLocation);
                                if (chest != null)
                                    for (int i = 0; i < Chest.maxItems; i++)
                                        chest.Items[i] = ItemData.None;
                            }

                            lock (this.WorldMetadata.Protections)
                            {
                                this.WorldMetadata.Protections.Remove(kvp.Value.TileLocation);
                                return;
                            }
                        }
                        bool canAccessProtection = player.HasBuildPermission(kvp.Value.TileLocation.X, kvp.Value.TileLocation.Y, false);
                        if (!canAccessProtection)
                        {
                            if (kvp.Value.BankChestKey != BankChestDataKey.Invalid)
                            {
                                IChest chest = this.ChestFromLocation(kvp.Value.TileLocation);
                                if (chest != null)
                                    for (int i = 0; i < Chest.maxItems; i++)
                                        chest.Items[i] = ItemData.None;
                            }

                            lock (this.WorldMetadata.Protections)
                            {
                                this.WorldMetadata.Protections.Remove(kvp.Value.TileLocation);
                                return;
                            }
                        }

                    }
                }
            }
        }
        private bool RefillChestTimer_Callback(TimerBase timer)
        {
            RefillChestMetadata refillChest = (RefillChestMetadata)timer.Data;
            lock (this.WorldMetadata.Protections)
            {
                ProtectionEntry protection = this.WorldMetadata.Protections.Values.SingleOrDefault(p => p.RefillChestData == refillChest);
                if (protection == null)
                    return false;

                DPoint chestLocation = protection.TileLocation;
                try
                {
                    this.TryRefillChest(chestLocation, refillChest);
                }
                catch (InvalidOperationException)
                {
                    this.PluginTrace.WriteLineWarning($"位置为 {chestLocation} 的宝箱似乎已不存在。无法为其补充物品。");
                    return false;
                }

                // 返回 true 意味着计时器将会重复。
                return false;
            }
        }
    }
}
