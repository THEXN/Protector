using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using DPoint = System.Drawing.Point;

using Terraria.Plugins.Common;
using Terraria.Plugins.Common.Collections;
using TShockAPI;
using TShockAPI.DB;
using IL.Terraria.DataStructures;
using IL.Terraria.UI;
using Terraria.UI;

namespace Terraria.Plugins.CoderCow.Protector
{
    public class UserInteractionHandler : UserInteractionHandlerBase, IDisposable
    {
        protected PluginInfo PluginInfo { get; }
        protected Configuration Config { get; private set; }
        protected ServerMetadataHandler ServerMetadataHandler { get; }
        protected WorldMetadata WorldMetadata { get; }
        protected ChestManager ChestManager { get; }
        protected ProtectionManager ProtectionManager { get; }
        public PluginCooperationHandler PluginCooperationHandler { get; }
        protected Func<Configuration> ReloadConfigurationCallback { get; private set; }
        // 哪个玩家当前打开了哪个宝箱，并且反向查找以加快检索速度。
        protected Dictionary<int, DPoint> PlayerIndexChestDictionary { get; }
        protected Dictionary<DPoint, int> ChestPlayerIndexDictionary { get; }

        public UserInteractionHandler(
          PluginTrace trace, PluginInfo pluginInfo, Configuration config, ServerMetadataHandler serverMetadataHandler,
          WorldMetadata worldMetadata, ProtectionManager protectionManager, ChestManager chestManager,
          PluginCooperationHandler pluginCooperationHandler, Func<Configuration> reloadConfigurationCallback
    ) : base(trace)
        {
            if (trace == null) throw new ArgumentNullException();
            if (!(!pluginInfo.Equals(PluginInfo.Empty))) throw new ArgumentException();
            if (config == null) throw new ArgumentNullException();
            if (serverMetadataHandler == null) throw new ArgumentNullException();
            if (worldMetadata == null) throw new ArgumentNullException();
            if (protectionManager == null) throw new ArgumentNullException();
            if (pluginCooperationHandler == null) throw new ArgumentNullException();
            if (reloadConfigurationCallback == null) throw new ArgumentNullException();

            this.PluginInfo = pluginInfo;
            this.Config = config;
            this.ServerMetadataHandler = serverMetadataHandler;
            this.WorldMetadata = worldMetadata;
            this.ChestManager = chestManager;
            this.ProtectionManager = protectionManager;
            this.PluginCooperationHandler = pluginCooperationHandler;
            this.ReloadConfigurationCallback = reloadConfigurationCallback;

            this.PlayerIndexChestDictionary = new Dictionary<int, DPoint>(20);
            this.ChestPlayerIndexDictionary = new Dictionary<DPoint, int>(20);

            #region Command Setup
            base.RegisterCommand(
              new[] { "protector" }, this.RootCommand_Exec, this.RootCommand_HelpCallback
            );
            base.RegisterCommand(
              new[] { "protect", "pt" },
              this.ProtectCommand_Exec, this.ProtectCommand_HelpCallback, ProtectorPlugin.ManualProtect_Permission,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "deprotect", "dp" },
              this.DeprotectCommand_Exec, this.DeprotectCommand_HelpCallback, ProtectorPlugin.ManualDeprotect_Permission,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "protectioninfo", "ptinfo", "pi" }, this.ProtectionInfoCommand_Exec, this.ProtectionInfoCommand_HelpCallback,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "share" }, this.ShareCommand_Exec, this.ShareCommandHelpCallback,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "unshare" }, this.UnshareCommand_Exec, this.UnshareCommand_HelpCallback,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "sharepublic" }, this.SharePublicCommand_Exec, this.SharePublicCommandHelpCallback,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "unsharepublic" }, this.UnsharePublicCommand_Exec, this.UnsharePublicCommand_HelpCallback,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "sharegroup" }, this.ShareGroupCommand_Exec, this.ShareGroupCommand_HelpCallback,
              ProtectorPlugin.ShareWithGroups_Permission,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "unsharegroup" }, this.UnshareGroupCommand_Exec, this.UnshareGroup_HelpCallback,
              ProtectorPlugin.ShareWithGroups_Permission,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "lockchest", "lchest" },
              this.LockChestCommand_Exec, this.LockChestCommand_HelpCallback, ProtectorPlugin.Utility_Permission,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "swapchest", "schest" },
              this.SwapChestCommand_Exec, this.SwapChestCommand_HelpCallback, ProtectorPlugin.Utility_Permission,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "refillchest", "rchest" },
              this.RefillChestCommand_Exec, this.RefillChestCommand_HelpCallback, ProtectorPlugin.SetRefillChests_Permission,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "refillchestmany", "rchestmany" },
              this.RefillChestManyCommand_Exec, this.RefillChestManyCommand_HelpCallback, ProtectorPlugin.Utility_Permission
            );
            base.RegisterCommand(
              new[] { "bankchest", "bchest" },
              this.BankChestCommand_Exec, this.BankChestCommand_HelpCallback, ProtectorPlugin.SetBankChests_Permission,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "dumpbankchest", "dbchest" },
              this.DumpBankChestCommand_Exec, this.DumpBankChestCommand_HelpCallback, ProtectorPlugin.DumpBankChests_Permission,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "tradechest", "tchest" },
              this.TradeChestCommand_Exec, this.TradeChestCommand_HelpCallback, ProtectorPlugin.SetTradeChests_Permission,
              allowServer: false
            );
            base.RegisterCommand(
              new[] { "scanchests" },
              this.ScanChestsCommand_Exec, this.ScanChestsCommand_HelpCallback, ProtectorPlugin.ScanChests_Permission
            );
            base.RegisterCommand(
              new[] { "tpchest" },
              this.TpChestCommand_Exec, this.TpChestCommand_HelpCallback, ProtectorPlugin.ScanChests_Permission,
              allowServer: false
            );
            #endregion

#if DEBUG
      base.RegisterCommand(new[] { "fc" }, args => {
        for (int i= 0; i < Main.chest.Length; i++) {
          if (i != ChestManager.DummyChestIndex)
            Main.chest[i] = Main.chest[i] ?? new Chest();
        }
      }, requiredPermission: Permissions.maintenance);
      base.RegisterCommand(new[] { "fcnames" }, args => {
        for (int i= 0; i < Main.chest.Length; i++) {
          if (i != ChestManager.DummyChestIndex) {
            Main.chest[i] = Main.chest[i] ?? new Chest();
            Main.chest[i].name = "Chest!";
          }
        }
      }, requiredPermission: Permissions.maintenance);
#endif
        }

        #region [Command Handling /protector]
        private void RootCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            base.StopInteraction(args.Player);

            if (args.Parameters.Count >= 1)
            {
                string subCommand = args.Parameters[0].ToLowerInvariant();

                if (this.TryExecuteSubCommand(subCommand, args))
                    return;
            }

            args.Player.SendMessage(this.PluginInfo.ToString(), Color.White);
            args.Player.SendMessage(this.PluginInfo.Description, Color.White);
            args.Player.SendMessage(string.Empty, Color.Yellow);

            int playerProtectionCount = 0;
            lock (this.WorldMetadata.Protections)
            {
                foreach (KeyValuePair<DPoint, ProtectionEntry> protection in this.WorldMetadata.Protections)
                {
                    if (protection.Value.Owner == args.Player.Account.ID)
                        playerProtectionCount++;
                }
            }

            string statsMessage = string.Format(
        "到目前为止，您已经创建了{0}种可能的保护中的{1}种。", playerProtectionCount,
              this.Config.MaxProtectionsPerPlayerPerWorld
            );
            args.Player.SendMessage(statsMessage, Color.Yellow);
            args.Player.SendMessage("输入“/protector commands”以获取可用命令列表。", Color.Yellow);
            args.Player.SendMessage("要获取有关此插件的更多一般信息，请输入“/protector help”。", Color.Yellow);
        }

        private bool TryExecuteSubCommand(string commandNameLC, CommandArgs args)
        {
            switch (commandNameLC)
            {
                case "commands":
                case "cmds":
                    {
                        int pageNumber = 1;
                        if (args.Parameters.Count > 1 && (!int.TryParse(args.Parameters[1], out pageNumber) || pageNumber < 1))
                        {
                            args.Player.SendErrorMessage($"\"{args.Parameters[1]}\"不是一个有效的页码。");
                            return true;
                        }

                        List<string> terms = new List<string>();
                        if (args.Player.Group.HasPermission(ProtectorPlugin.ManualProtect_Permission))
                            terms.Add("/protect");
                        if (args.Player.Group.HasPermission(ProtectorPlugin.ManualDeprotect_Permission))
                            terms.Add("/deprotect");

                        terms.Add("/protectioninfo");
                        if (
                          args.Player.Group.HasPermission(ProtectorPlugin.ChestSharing_Permission) ||
                          args.Player.Group.HasPermission(ProtectorPlugin.SwitchSharing_Permission) ||
                          args.Player.Group.HasPermission(ProtectorPlugin.OtherSharing_Permission)
          )
                        {
                            terms.Add("/share");
                            terms.Add("/unshare");
                            terms.Add("/sharepublic");
                            terms.Add("/unsharepublic");

                            if (args.Player.Group.HasPermission(ProtectorPlugin.ShareWithGroups_Permission))
                            {
                                terms.Add("/sharegroup");
                                terms.Add("/unsharegroup");
                            }
                        }
                        if (args.Player.Group.HasPermission(ProtectorPlugin.SetRefillChests_Permission))
                        {
                            terms.Add("/refillchest");
                            if (args.Player.Group.HasPermission(ProtectorPlugin.Utility_Permission))
                                terms.Add("/refillchestmany");
                        }
                        if (args.Player.Group.HasPermission(ProtectorPlugin.SetBankChests_Permission))
                            terms.Add("/bankchest");
                        if (args.Player.Group.HasPermission(ProtectorPlugin.DumpBankChests_Permission))
                            terms.Add("/dumpbankchest");
                        if (args.Player.Group.HasPermission(ProtectorPlugin.SetTradeChests_Permission))
                            terms.Add("/tradechest");
                        if (args.Player.Group.HasPermission(ProtectorPlugin.Utility_Permission))
                        {
                            terms.Add("/lockchest");
                            terms.Add("/swapchest");
                            terms.Add("/protector invalidate");
                            if (args.Player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission))
                            {
                                terms.Add("/protector cleanup");
                                terms.Add("/protector removeall");
                            }

                            terms.Add("/protector removeemptychests");
                            terms.Add("/protector summary");
                        }
                        if (args.Player.Group.HasPermission(ProtectorPlugin.Cfg_Permission))
                        {
                            terms.Add("/protector importinfinitechests");
                            terms.Add("/protector importinfinitesigns");
                            terms.Add("/protector reloadconfig");
                        }

                        List<string> lines = PaginationTools.BuildLinesFromTerms(terms);
                        PaginationTools.SendPage(args.Player, pageNumber, lines, new PaginationTools.Settings
                        {
                            HeaderFormat = "保护器命令（第 {0} 页，共 {1} 页）",
                            LineTextColor = Color.LightGray,
                        });

                        return true;
                    }
                case "cleanup":
                    {
                        if (
                          !args.Player.Group.HasPermission(ProtectorPlugin.Utility_Permission) ||
                          !args.Player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission)
          )
                        {
                            args.Player.SendErrorMessage("你没有执行此操作所需的权限。");
                            return true;
                        }

                        if (args.Parameters.Count == 2 && args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                        {
                            args.Player.SendMessage("/protector cleanup 命令参考（第 1 页，共 1 页）", Color.Lime);
                            args.Player.SendMessage("/protector cleanup", Color.White);
                            args.Player.SendMessage("删除所有属于不存在于TShock中的用户ID的保护", Color.LightGray);
                            args.Player.SendMessage("的数据库条目", Color.LightGray);
                            args.Player.SendMessage(string.Empty, Color.LightGray);
                            args.Player.SendMessage("-d = 不破坏设置保护时所在的图格。", Color.LightGray);
                            return true;
                        }

                        bool destroyRelatedTiles = true;
                        if (args.Parameters.Count > 1)
                        {
                            if (args.Parameters[1].Equals("-d", StringComparison.InvariantCultureIgnoreCase))
                            {
                                destroyRelatedTiles = false;
                            }
                            else
                            {
                                args.Player.SendErrorMessage("正确的语法：/protector cleanup [-d]");
                                args.Player.SendErrorMessage("输入/protector cleanup help以获取此命令的更多帮助。");
                                return true;
                            }
                        }

                        List<DPoint> protectionsToRemove = new List<DPoint>();
                        lock (this.WorldMetadata.Protections)
                        {
                            foreach (KeyValuePair<DPoint, ProtectionEntry> protectionPair in this.WorldMetadata.Protections)
                            {
                                DPoint location = protectionPair.Key;
                                ProtectionEntry protection = protectionPair.Value;

                                TShockAPI.DB.UserAccount tsUser = TShock.UserAccounts.GetUserAccountByID(protection.Owner);
                                if (tsUser == null)
                                    protectionsToRemove.Add(location);
                            }

                            foreach (DPoint protectionLocation in protectionsToRemove)
                            {
                                this.WorldMetadata.Protections.Remove(protectionLocation);
                                if (destroyRelatedTiles)
                                    this.DestroyBlockOrObject(protectionLocation);
                            }
                        }
                        if (args.Player != TSPlayer.Server)
                            args.Player.SendSuccessMessage("移除了 {0} 个保护。", protectionsToRemove.Count);
                        this.PluginTrace.WriteLineInfo("移除了 {0} 个保护。", protectionsToRemove.Count);

                        return true;
                    }
                case "removeall":
                    {
                        if (
                          !args.Player.Group.HasPermission(ProtectorPlugin.Utility_Permission) ||
                          !args.Player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission)
          )
                        {
                            args.Player.SendErrorMessage("你没有执行此操作所需的权限。");
                            return true;
                        }

                        if (args.Parameters.Count == 2 && args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                        {
                            args.Player.SendMessage("/protector removeall 命令参考（第 1 页，共 1 页）", Color.Lime);
                            args.Player.SendMessage("/protector removeall <区域 <区域名>|用户 <用户名>> [-d]", Color.White);
                            args.Player.SendMessage("从给定的区域或指定的用户拥有的保护中移除所有保护。", Color.LightGray);
                            args.Player.SendMessage(string.Empty, Color.LightGray);
                            args.Player.SendMessage("region <区域> = 移除<区域>内的所有保护。", Color.LightGray);
                            args.Player.SendMessage("user <用户> = 移除此世界中<用户>拥有的所有保护。", Color.LightGray);
                            args.Player.SendMessage("-d = 不破坏设置保护时所在的图格（或方块）。", Color.LightGray);
                            return true;
                        }

                        bool destroyRelatedTiles = true;
                        bool regionMode = true;
                        string target = null;
                        bool invalidSyntax = (args.Parameters.Count < 3 || args.Parameters.Count > 4);
                        if (!invalidSyntax)
                        {
                            if (args.Parameters[1].Equals("region", StringComparison.InvariantCultureIgnoreCase))
                                regionMode = true;
                            else if (args.Parameters[1].Equals("user", StringComparison.InvariantCultureIgnoreCase))
                                regionMode = false;
                            else
                                invalidSyntax = true;
                        }
                        if (!invalidSyntax)
                        {
                            target = args.Parameters[2];

                            if (args.Parameters.Count == 4)
                            {
                                if (args.Parameters[3].Equals("-d", StringComparison.InvariantCultureIgnoreCase))
                                    destroyRelatedTiles = false;
                                else
                                    invalidSyntax = true;
                            }
                        }
                        if (invalidSyntax)
                        {
                            args.Player.SendErrorMessage("正确的语法：/protector removeall <region <区域名>|user <用户名>> [-d]");
                            args.Player.SendErrorMessage("输入 /protector removeall help 以获取此命令的更多帮助。");
                            return true;
                        }

                        List<DPoint> protectionsToRemove;
                        lock (this.WorldMetadata.Protections)
                        {
                            if (regionMode)
                            {
                                TShockAPI.DB.Region tsRegion = TShock.Regions.GetRegionByName(target);
                                if (tsRegion == null)
                                {
                                    args.Player.SendErrorMessage("区域“{0}”不存在。", target);
                                    return true;
                                }

                                protectionsToRemove = new List<DPoint>(
                                  from loc in this.WorldMetadata.Protections.Keys
                                  where tsRegion.InArea(loc.X, loc.Y)
                                  select loc
                                );
                            }
                            else
                            {
                                int userId;
                                if (!TShockEx.MatchUserIdByPlayerName(target, out userId, args.Player))
                                    return true;

                                protectionsToRemove = new List<DPoint>(
                                  from pt in this.WorldMetadata.Protections.Values
                                  where pt.Owner == userId
                                  select pt.TileLocation
                                );
                            }

                            foreach (DPoint protectionLocation in protectionsToRemove)
                            {
                                this.WorldMetadata.Protections.Remove(protectionLocation);
                                if (destroyRelatedTiles)
                                    this.DestroyBlockOrObject(protectionLocation);
                            }
                        }

                        if (args.Player != TSPlayer.Server)
                            args.Player.SendSuccessMessage("{0}个保护已被移除。", protectionsToRemove.Count);
                        this.PluginTrace.WriteLineInfo("{{0}个保护已被移除。", protectionsToRemove.Count);

                        return true;
                    }
                case "removeemptychests":
                case "cleanupchests":
                    {
                        if (!args.Player.Group.HasPermission(ProtectorPlugin.Utility_Permission))
                        {
                            args.Player.SendErrorMessage("你没有执行此操作所需的权限。");
                            return true;
                        }

                        if (args.Parameters.Count == 2 && args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                        {
                            args.Player.SendMessage("/protector removeemptychests 命令参考（第 1 页，共 1 页）", Color.Lime);
                            args.Player.SendMessage("/protector removeemptychests 或 cleanupchests", Color.White);
                            args.Player.SendMessage("从世界中移除所有空的且未受保护的箱子。", Color.LightGray);
                            return true;
                        }

                        int cleanedUpChestsCount = 0;
                        int cleanedUpInvalidChestDataCount = 0;
                        for (int i = 0; i < Main.chest.Length; i++)
                        {
                            if (i == ChestManager.DummyChestIndex)
                                continue;

                            Chest tChest = Main.chest[i];
                            if (tChest == null)
                                continue;

                            bool isEmpty = true;
                            for (int j = 0; j < tChest.item.Length; j++)
                            {
                                if (tChest.item[j].stack > 0)
                                {
                                    isEmpty = false;
                                    break;
                                }
                            }

                            if (!isEmpty)
                                continue;

                            bool isInvalidEntry = false;
                            DPoint chestLocation = new DPoint(tChest.x, tChest.y);
                            ITile chestTile = TerrariaUtils.Tiles[chestLocation];
                            if (chestTile.active() && (chestTile.type == TileID.Containers || chestTile.type == TileID.Containers2 || chestTile.type == TileID.Dressers))
                            {
                                chestLocation = TerrariaUtils.Tiles.MeasureObject(chestLocation).OriginTileLocation;
                                lock (this.WorldMetadata.Protections)
                                {
                                    if (this.WorldMetadata.Protections.ContainsKey(chestLocation))
                                        continue;
                                }
                            }
                            else
                            {
                                Main.chest[i] = null;
                                isInvalidEntry = true;
                            }

                            if (!isInvalidEntry)
                            {
                                WorldGen.KillTile(chestLocation.X, chestLocation.Y, false, false, true);
                                TSPlayer.All.SendTileSquareCentered(chestLocation, 4);
                                cleanedUpChestsCount++;
                            }
                            else
                            {
                                cleanedUpInvalidChestDataCount++;
                            }
                        }

                        if (args.Player != TSPlayer.Server)
                        {
                            args.Player.SendSuccessMessage(string.Format(
              "移除了{0}个空的且未受保护的箱子。移除了{1}个无效的箱子数量。",
                              cleanedUpChestsCount, cleanedUpInvalidChestDataCount
                            ));
                        }
                        this.PluginTrace.WriteLineInfo(
                                            "已移除 {0} 个空置且未受保护的钱箱。已移除 {1} 个无效的钱箱数据条目。",
                          cleanedUpChestsCount, cleanedUpInvalidChestDataCount
                        );


                        return true;
                    }
                case "invalidate":
                case "ensure":
                    {
                        if (!args.Player.Group.HasPermission(ProtectorPlugin.Utility_Permission))
                        {
                            args.Player.SendErrorMessage("你没有执行此操作所需的权限。");
                            return true;
                        }

                        if (args.Parameters.Count > 1 && args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                        {
                            args.Player.SendMessage("/protector invalidate 命令参考（第 1 页，共 1 页）", Color.Lime);
                            args.Player.SendMessage("/protector invalidate 或 ensure", Color.White);
                            args.Player.SendMessage("移除或修复当前世界中所有无效的保护。", Color.LightGray);
                            return true;
                        }

                        this.EnsureProtectionData(args.Player, false);
                        return true;
                    }
                case "summary":
                case "stats":
                    {
                        if (!args.Player.Group.HasPermission(ProtectorPlugin.Utility_Permission))
                        {
                            args.Player.SendErrorMessage("你没有执行此操作所需的权限。");
                            return true;
                        }

                        if (args.Parameters.Count == 2 && args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                        {
                            args.Player.SendMessage("/protector summary 命令参考（第 1 页，共 1 页）", Color.Lime);
                            args.Player.SendMessage("/protector summary 或 stats", Color.White);
                            args.Player.SendMessage("测量关于箱子、标志、保护和保险箱的全局世界信息。", Color.LightGray);
                            return true;
                        }

                        int protectorChestCount = this.WorldMetadata.ProtectorChests.Count;
                        int chestCount = Main.chest.Count(chest => chest != null) + protectorChestCount - 1;
                        int signCount = Main.sign.Count(sign => sign != null);
                        int protectionsCount = this.WorldMetadata.Protections.Count;
                        int sharedProtectionsCount = this.WorldMetadata.Protections.Values.Count(p => p.IsShared);
                        int refillChestsCount = this.WorldMetadata.Protections.Values.Count(p => p.RefillChestData != null);

                        Dictionary<int, int> userProtectionCounts = new Dictionary<int, int>(100);
                        lock (this.WorldMetadata.Protections)
                        {
                            foreach (ProtectionEntry protection in this.WorldMetadata.Protections.Values)
                            {
                                if (!userProtectionCounts.ContainsKey(protection.Owner))
                                    userProtectionCounts.Add(protection.Owner, 1);
                                else
                                    userProtectionCounts[protection.Owner]++;
                            }
                        }
                        int usersWhoReachedProtectionLimitCount = userProtectionCounts.Values.Count(
                          protectionCount => protectionsCount == this.Config.MaxProtectionsPerPlayerPerWorld
                        );

                        int bankChestCount = this.ServerMetadataHandler.EnqueueGetBankChestCount().Result;
                        int bankChestInstancesCount;
                        lock (this.WorldMetadata.Protections)
                        {
                            bankChestInstancesCount = this.WorldMetadata.Protections.Values.Count(
                              p => p.BankChestKey != BankChestDataKey.Invalid
                            );
                        }

                        if (args.Player != TSPlayer.Server)
                        {
                            args.Player.SendInfoMessage(string.Format(
              "这个世界中有 {0}/{1} 个箱子（{2} 个保护箱）和 {3}/{4} 个标志。",
              chestCount, Main.chest.Length + this.Config.MaxProtectorChests - 1, protectorChestCount, signCount, Sign.maxSigns
                            ));
                            args.Player.SendInfoMessage(string.Format(
                                              "共有 {0} 个保护完好无损，其中 {1} 个与其他玩家共享，",
                              protectionsCount, sharedProtectionsCount
                            ));
                            args.Player.SendInfoMessage(string.Format(
                                              "已设置 {0} 个补充钱箱，有 {1} 个用户已达到他们的保护限制。",
                              refillChestsCount, usersWhoReachedProtectionLimitCount
                            ));
                            args.Player.SendInfoMessage(string.Format(
                                              "数据库中记录有 {0} 个银行钱箱，其中 {1} 个在此世界中实例化。",
                              bankChestCount, bankChestInstancesCount
                            ));
                        }
                        this.PluginTrace.WriteLineInfo(string.Format(
        "这个世界中有 {0}/{1} 个箱子和 {2}/{3} 个标志。",
        chestCount, Main.chest.Length, signCount, Sign.maxSigns
                        ));
                        this.PluginTrace.WriteLineInfo(string.Format(
        "{0}个保护是完整的，其中{1}个与其他玩家共享，",
                          protectionsCount, sharedProtectionsCount
                        ));
                        this.PluginTrace.WriteLineInfo(string.Format(
        "已经设置了{0}个自动填充箱子，并且有{1}个用户达到了他们的保护限制。",
                          refillChestsCount, usersWhoReachedProtectionLimitCount
                        ));
                        this.PluginTrace.WriteLineInfo(string.Format(
        "数据库包含{0}个保险箱，其中{1}个已在本世界中实例化。",
                          bankChestCount, bankChestInstancesCount
                        ));

                        return true;
                    }
                case "importinfinitechests":
                    {
                        if (!args.Player.Group.HasPermission(ProtectorPlugin.Cfg_Permission))
                        {
                            args.Player.SendErrorMessage("你没有执行此操作所需的权限。");
                            return true;
                        }

                        if (args.Parameters.Count == 2 && args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                        {
                            args.Player.SendMessage("/protector importinfinitechests 命令参考（第 1 页，共 1 页）", Color.Lime);
                            args.Player.SendMessage("/protector importinfinitechests", Color.White);
                            args.Player.SendMessage("尝试从InfiniteChests的数据库中导入所有箱子数据.", Color.LightGray);
                            args.Player.SendMessage("这个操作要求未安装InfiniteChests插件。", Color.LightGray);
                            args.Player.SendMessage("现有的箱子数据将被覆盖，导入的自动填充箱子将会", Color.LightGray);
                            args.Player.SendMessage("失去它们的计时器.", Color.LightGray);
                            return true;
                        }

                        args.Player.SendInfoMessage("正在导入InfiniteChests数据...");
                        this.PluginTrace.WriteLineInfo("正在导入InfiniteChests数据...");

                        int importedChests;
                        int protectFailures;
                        try
                        {
                            this.PluginCooperationHandler.InfiniteChests_ChestDataImport(
                              this.ChestManager, this.ProtectionManager, out importedChests, out protectFailures
                            );
                        }
                        catch (FileNotFoundException ex)
                        {
                            args.Player.SendErrorMessage($"“{ex.FileName}”数据库文件未找到。");
                            return true;
                        }

                        args.Player.SendSuccessMessage($"导入了{importedChests}个箱子。未能保护{protectFailures}个箱子。");

                        return true;
                    }
                case "importinfinitesigns":
                    {
                        if (!args.Player.Group.HasPermission(ProtectorPlugin.Cfg_Permission))
                        {
                            args.Player.SendErrorMessage("你没有执行此操作所需的权限。");
                            return true;
                        }

                        if (args.Parameters.Count == 2 && args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                        {
                            args.Player.SendMessage("/protector importinfinitesigns 命令参考（第 1 页，共 1 页）", Color.Lime);
                            args.Player.SendMessage("/protector importinfinitesigns", Color.White);
                            args.Player.SendMessage("尝试从InfiniteSigns的数据库中导入所有标志数据。", Color.LightGray);
                            args.Player.SendMessage("这个操作要求未安装InfiniteSigns插件。", Color.LightGray);
                            args.Player.SendMessage("现有的标志数据将被覆盖。", Color.LightGray);
                            return true;
                        }

                        args.Player.SendInfoMessage("正在导入InfiniteSigns数据...");
                        this.PluginTrace.WriteLineInfo("正在导入InfiniteSigns数据...");

                        int importedSigns;
                        int protectFailures;
                        try
                        {
                            this.PluginCooperationHandler.InfiniteSigns_SignDataImport(
                              this.ProtectionManager, out importedSigns, out protectFailures
                            );
                        }
                        catch (FileNotFoundException ex)
                        {
                            args.Player.SendErrorMessage(string.Format("“{0}”数据库文件未找到.", ex.FileName));
                            return true;
                        }

                        args.Player.SendSuccessMessage(string.Format(
                          "导入了{0}个标志。未能保护{1}个标志。", importedSigns, protectFailures
                                      ));

                        return true;
                    }
                case "reloadconfiguration":
                case "reloadconfig":
                case "reloadcfg":
                    {
                        if (!args.Player.Group.HasPermission(ProtectorPlugin.Cfg_Permission))
                        {
                            args.Player.SendErrorMessage("你没有执行此操作所需的权限。");
                            return true;
                        }

                        if (args.Parameters.Count == 2 && args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                        {
                            args.Player.SendMessage("/protector reloadconfiguration 命令参考（第 1 页，共 1 页）", Color.Lime);
                            args.Player.SendMessage("/protector reloadconfiguration 或 reloadconfig 或 reloadcfg", Color.White);
                            args.Player.SendMessage("重新加载Protector的配置文件并应用所有新设置。", Color.LightGray);
                            args.Player.SendMessage("如果保险箱子的数量限制被减少了，那么现有的保险箱子将会...", Color.LightGray);
                            args.Player.SendMessage("超过这个限制的现有保险箱子仍然可以访问，直到服务器重新启动。", Color.LightGray);
                            return true;
                        }

                        this.PluginTrace.WriteLineInfo("重新加载配置文件。");
                        try
                        {
                            this.Config = this.ReloadConfigurationCallback();
                            this.PluginTrace.WriteLineInfo("配置文件已成功重新加载。");

                            if (args.Player != TSPlayer.Server)
                                args.Player.SendSuccessMessage("配置文件已成功重新加载。");
                        }
                        catch (Exception ex)
                        {
                            this.PluginTrace.WriteLineError(
                              "重新加载配置文件失败。保留旧配置。异常详情：\n{0}", ex
                            );
                        }

                        return true;
                    }
            }

            return false;
        }

        private bool RootCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Protector 概述（第 1 页，共 2 页）", Color.Lime);
                    args.Player.SendMessage("这个插件为运行在TShock的Terraria服务器上的玩家提供了可能性", Color.LightGray);
                    args.Player.SendMessage("来拥有某些物体或方块的所有权，以便其他玩家不能 更改或使用它们。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("受保护的箱子中的内容不能被其他玩家更改", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("受保护的开关不会被其他玩家触发，告示牌不能被编辑，床不能被被使用，\n未使用的门甚至受保护的陶罐中的植物都不能被挖", Color.LightGray);
                    args.Player.SendMessage("除非拥有这个陶罐，否则无法收获其中的植物。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("如需更多信息和支持，请访问TShock论坛上的Protector版块。", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /protect]
        private void ProtectCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            bool persistentMode = false;
            if (args.Parameters.Count > 0)
            {
                if (args.ContainsParameter("-p", StringComparison.InvariantCultureIgnoreCase))
                {
                    persistentMode = true;
                }
                else
                {
                    args.Player.SendErrorMessage("正确的语法：/protect [-p]");
                    args.Player.SendInfoMessage("输入/protect help以获取此命令的更多帮助。");
                    return;
                }
            }

            CommandInteraction interaction = this.StartOrResetCommandInteraction(args.Player);
            interaction.DoesNeverComplete = persistentMode;
            interaction.TileEditCallback += (playerLocal, editType, tileId, location, objectStyle) =>
            {
                if (
                  editType != TileEditType.PlaceTile ||
                  editType != TileEditType.PlaceWall ||
                  editType != TileEditType.DestroyWall ||
                  editType != TileEditType.PlaceActuator
        )
                {
                    this.TryCreateProtection(playerLocal, location);

                    playerLocal.SendTileSquareCentered(location);
                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }
                else if (editType == TileEditType.DestroyWall)
                {
                    playerLocal.SendErrorMessage("墙壁不能被保护。");

                    playerLocal.SendTileSquareCentered(location);
                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }

                return new CommandInteractionResult { IsHandled = false, IsInteractionCompleted = false };
            };
            Func<TSPlayer, DPoint, CommandInteractionResult> usageCallbackFunc = (playerLocal, location) =>
            {
                this.TryCreateProtection(playerLocal, location);
                playerLocal.SendTileSquareCentered(location, 3);

                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.SignReadCallback += usageCallbackFunc;
            interaction.ChestOpenCallback += usageCallbackFunc;
            interaction.HitSwitchCallback += usageCallbackFunc;
            interaction.SignEditCallback += (playerLocal, signIndex, location, newText) =>
            {
                this.TryCreateProtection(playerLocal, location);
                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.TimeExpiredCallback += (playerLocal) =>
            {
                playerLocal.SendErrorMessage("等待时间过长。下一个被击中的物体或方块将不会被保护。");
            };

            args.Player.SendInfoMessage("击中或使用一个物体或方块以对其进行保护。");
        }

        private bool ProtectCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("/protect 命令参考（第 1 页，共 1 页）", Color.Lime);
                    args.Player.SendMessage("/protect 或 pt [-p]", Color.White);
                    args.Player.SendMessage("保护选中的物体或方块。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("-p = 激活持久模式。该命令将保持持久状态，直到其超时", Color.LightGray);
                    args.Player.SendMessage("或者输入了任何其他Protector命令。", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /deprotect]
        private void DeprotectCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            bool persistentMode = false;
            if (args.Parameters.Count > 0)
            {
                if (args.ContainsParameter("-p", StringComparison.InvariantCultureIgnoreCase))
                {
                    persistentMode = true;
                }
                else
                {
                    args.Player.SendErrorMessage("正确的语法：/deprotect [-p]");
                    args.Player.SendInfoMessage("正确的语法是：/deprotect [-p]");
                    return;
                }
            }

            CommandInteraction interaction = this.StartOrResetCommandInteraction(args.Player);
            interaction.DoesNeverComplete = persistentMode;
            interaction.TileEditCallback += (playerLocal, editType, tileId, location, objectStyle) =>
            {
                if (
                  editType != TileEditType.PlaceTile ||
                  editType != TileEditType.PlaceWall ||
                  editType != TileEditType.DestroyWall ||
                  editType != TileEditType.PlaceActuator
        )
                {
                    this.TryRemoveProtection(playerLocal, location);

                    playerLocal.SendTileSquareCentered(location);
                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }

                playerLocal.SendTileSquareCentered(location);
                return new CommandInteractionResult { IsHandled = false, IsInteractionCompleted = false };
            };
            Func<TSPlayer, DPoint, CommandInteractionResult> usageCallbackFunc = (playerLocal, location) =>
            {
                this.TryRemoveProtection(playerLocal, location);
                playerLocal.SendTileSquareCentered(location, 3);

                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.SignReadCallback += usageCallbackFunc;
            interaction.ChestOpenCallback += usageCallbackFunc;
            interaction.HitSwitchCallback += usageCallbackFunc;
            interaction.SignEditCallback += (playerLocal, signIndex, location, newText) =>
            {
                this.TryGetProtectionInfo(playerLocal, location);
                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.TimeExpiredCallback += (playerLocal) =>
            {
                playerLocal.SendMessage("等待时间过长。下一个击中的物体或方块将不会被取消保护了。", Color.Red);
            };

            args.Player.SendInfoMessage("击中或使用受保护的物体或方块以取消其保护状态。");
        }

        private bool DeprotectCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("/deprotect 命令参考（第 1 页，共 2 页）", Color.Lime);
                    args.Player.SendMessage("/deprotect 或 dp [-p]", Color.White);
                    args.Player.SendMessage("取消选中物体或方块的保护状态。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("-p = 激活持久模式。该命令将保持持久状态，直到其超时", Color.LightGray);
                    args.Player.SendMessage("或者输入了任何其他Protector命令", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("只有拥有者或管理员才能移除保护。如果选中的物体", Color.LightGray);
                    args.Player.SendMessage("是一个保险箱子，那么这个保险箱子的实例将会从世界中移除，以便它可能会被重新实例化。", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /protectioninfo]
        private void ProtectionInfoCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            bool persistentMode = false;
            if (args.Parameters.Count > 0)
            {
                if (args.ContainsParameter("-p", StringComparison.InvariantCultureIgnoreCase))
                {
                    persistentMode = true;
                }
                else
                {
                    args.Player.SendErrorMessage("正确的语法：/protectioninfo [-p]");
                    args.Player.SendInfoMessage("输入/protectioninfo help以获取此命令的更多帮助。");
                    return;
                }
            }

            CommandInteraction interaction = this.StartOrResetCommandInteraction(args.Player);
            interaction.DoesNeverComplete = persistentMode;
            interaction.TileEditCallback += (playerLocal, editType, tileId, location, objectStyle) =>
            {
                if (
                  editType != TileEditType.PlaceTile ||
                  editType != TileEditType.PlaceWall ||
                  editType != TileEditType.DestroyWall ||
                  editType != TileEditType.PlaceActuator
        )
                {
                    this.TryGetProtectionInfo(playerLocal, location);

                    playerLocal.SendTileSquareCentered(location);
                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }

                playerLocal.SendTileSquareCentered(location);
                return new CommandInteractionResult { IsHandled = false, IsInteractionCompleted = false };
            };
            Func<TSPlayer, DPoint, CommandInteractionResult> usageCallbackFunc = (playerLocal, location) =>
            {
                this.TryGetProtectionInfo(playerLocal, location);
                playerLocal.SendTileSquareCentered(location, 3);

                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.SignReadCallback += usageCallbackFunc;
            interaction.ChestOpenCallback += usageCallbackFunc;
            interaction.HitSwitchCallback += usageCallbackFunc;
            interaction.SignEditCallback += (playerLocal, signIndex, location, newText) =>
            {
                this.TryGetProtectionInfo(playerLocal, location);
                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.TimeExpiredCallback += (playerLocal) =>
            {
                playerLocal.SendMessage("等待时间过长。下一个被击中的物体或方块将不会显示保护信息。", Color.Red);
            };

            args.Player.SendInfoMessage("击中或使用受保护的物体或方块以获取其相关信息。");
        }

        private bool ProtectionInfoCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("/protectioninfo 命令参考（第 1 页，共 1 页）", Color.Lime);
                    args.Player.SendMessage("/protectioninfo 或 ptinfo 或 pi [-p]", Color.White);
                    args.Player.SendMessage("显示有关选定保护的一些信息。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("-p = 激活持续有效模式。该命令将持续有效，直到其超时", Color.LightGray);
                    args.Player.SendMessage("或者输入了任何其他Protector命令", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /share]
        private void ShareCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("正确语法: /share 玩家名字 [-p]");
                args.Player.SendInfoMessage("输入/share help以获取此命令的更多帮助。");
                return;
            }

            bool persistentMode;
            string playerName;
            if (args.Parameters[args.Parameters.Count - 1].Equals("-p", StringComparison.InvariantCultureIgnoreCase))
            {
                persistentMode = true;
                playerName = args.ParamsToSingleString(0, 1);
            }
            else
            {
                persistentMode = false;
                playerName = args.ParamsToSingleString();
            }

            TShockAPI.DB.UserAccount tsUser;
            if (!TShockEx.MatchUserByPlayerName(playerName, out tsUser, args.Player))
                return;

            this.StartShareCommandInteraction(args.Player, persistentMode, true, false, false, tsUser.ID, tsUser.Name);
        }

        private bool ShareCommandHelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("/share 命令参考（第 1 页，共 2 页）", Color.Lime);
                    args.Player.SendMessage("/share 玩家名字 [-p]", Color.White);
                    args.Player.SendMessage("为选定的保护添加一个玩家共享。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("玩家名称 = 要添加的玩家的名称。可以是精确的用户名或用户名的一部分。", Color.LightGray);
                    args.Player.SendMessage("当前在线的玩家的名称或其名称的一部分.", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("-p = 激活持续有效模式。该命令将持续有效，直到其超时", Color.LightGray);
                    args.Player.SendMessage("或者输入了任何其他Protector命令", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /unshare]
        private void UnshareCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("正确语法: /unshare 玩家名字");
                args.Player.SendErrorMessage("输入/unshare help 来获取这个命令的更多帮助。");
                return;
            }

            bool persistentMode;
            string playerName;
            if (args.Parameters[args.Parameters.Count - 1].Equals("-p", StringComparison.InvariantCultureIgnoreCase))
            {
                persistentMode = true;
                playerName = args.ParamsToSingleString(0, 1);
            }
            else
            {
                persistentMode = false;
                playerName = args.ParamsToSingleString();
            }

            TShockAPI.DB.UserAccount tsUser;
            if (!TShockEx.MatchUserByPlayerName(playerName, out tsUser, args.Player))
                return;

            this.StartShareCommandInteraction(args.Player, persistentMode, false, false, false, tsUser.ID, tsUser.Name);
        }

        private bool UnshareCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("/unshare 命令参考（第1页，共2页）", Color.Lime);
                    args.Player.SendMessage("/unshare 玩家名字", Color.White);
                    args.Player.SendMessage("从选定的保护中移除玩家的共享权限。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("-p = 激活持久模式。该命令将保持持久性，直到它过期", Color.LightGray);
                    args.Player.SendMessage("或输入其他保护者命令。", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("玩家名称 = 要添加的玩家的名称。可以是确切的用户名称，", Color.LightGray);
                    args.Player.SendMessage("或者是当前在线的玩家的名称的一部分。", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /sharepublic]
        private void SharePublicCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            bool persistentMode = false;
            if (args.Parameters.Count > 0)
            {
                if (args.ContainsParameter("-p", StringComparison.InvariantCultureIgnoreCase))
                {
                    persistentMode = true;
                }
                else
                {
                    args.Player.SendErrorMessage("正确语法: /sharepublic [-p]");
                    args.Player.SendInfoMessage("输入/sharepublic help 来获取这个命令的更多帮助。");
                    return;
                }
            }

            this.StartShareCommandInteraction(args.Player, persistentMode, true, false, true);
        }

        private bool SharePublicCommandHelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("/sharepublic 命令参考（第1页，共1页）", Color.Lime);
                    args.Player.SendMessage("/sharepublic [-p]", Color.White);
                    args.Player.SendMessage("允许所有人使用选定的对象。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("-p = 激活持久模式。该命令将保持持久性，直到它过期。", Color.LightGray);
                    args.Player.SendMessage("或输入其他保护者命令。", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /unsharepublic]
        private void UnsharePublicCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            bool persistentMode = false;
            if (args.Parameters.Count > 0)
            {
                if (args.ContainsParameter("-p", StringComparison.InvariantCultureIgnoreCase))
                {
                    persistentMode = true;
                }
                else
                {
                    args.Player.SendErrorMessage("正确语法: /unsharepublic [-p]");
                    args.Player.SendInfoMessage("输入/unsharepublic help 来获取这个命令的更多帮助。");
                    return;
                }
            }

            this.StartShareCommandInteraction(args.Player, persistentMode, false, false, true);
        }

        private bool UnsharePublicCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("/unsharepublic 命令参考（第1页，共1页）", Color.Lime);
                    args.Player.SendMessage("/unsharepublic [-p]", Color.White);
                    args.Player.SendMessage("撤销了所有人使用选定对象的权限。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("-p = 激活持久模式。该命令将保持持久性，直到它过期。", Color.LightGray);
                    args.Player.SendMessage("或输入其他保护者命令。", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /sharegroup]
        private void ShareGroupCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("正确语法: /sharegroup 用户组名");
                args.Player.SendErrorMessage("输入/sharegroup help 来获取这个命令的更多帮助。");
                return;
            }

            bool persistentMode;
            string groupName;
            if (args.Parameters[args.Parameters.Count - 1].Equals("-p", StringComparison.InvariantCultureIgnoreCase))
            {
                persistentMode = true;
                groupName = args.ParamsToSingleString(0, 1);
            }
            else
            {
                persistentMode = false;
                groupName = args.ParamsToSingleString();
            }

            if (TShock.Groups.GetGroupByName(groupName) == null)
            {
                args.Player.SendErrorMessage($"用户组“{groupName}”不存在。");

                return;
            }

            this.StartShareCommandInteraction(args.Player, persistentMode, true, true, false, groupName, groupName);
        }

        private bool ShareGroupCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("/sharegroup 命令参考（第1页，共2页）", Color.Lime);
                    args.Player.SendMessage("/sharegroup 用户组名 [-p]", Color.White);
                    args.Player.SendMessage("为选定的保护添加用户组共享权限。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("用户组组名称 = 要添加的TShock组的名称。", Color.LightGray);
                    args.Player.SendMessage("-p = 激活持久模式。该命令将保持持久性，直到它过期。", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("或输入其他保护者命令。.", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /unsharegroup]
        private void UnshareGroupCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("正确语法: /unsharegroup 用户组名");
                args.Player.SendErrorMessage("输入/unsharegroup help 来获取这个命令的更多帮助。");
                return;
            }

            bool persistentMode;
            string groupName;
            if (args.Parameters[args.Parameters.Count - 1].Equals("-p", StringComparison.InvariantCultureIgnoreCase))
            {
                persistentMode = true;
                groupName = args.ParamsToSingleString(0, 1);
            }
            else
            {
                persistentMode = false;
                groupName = args.ParamsToSingleString();
            }

            this.StartShareCommandInteraction(args.Player, persistentMode, false, true, false, groupName, groupName);
        }

        private bool UnshareGroup_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("/unsharegroup 命令参考（第1页，共2页）", Color.Lime);
                    args.Player.SendMessage("/unsharegroup 用户组名 [-p]", Color.White);
                    args.Player.SendMessage("从选定的保护中移除用户组共享权限。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("用户组组名称 = 要添加的TShock组的名称。", Color.LightGray);
                    args.Player.SendMessage("-p = 激活持久模式。该命令将保持持久性，直到它过期。", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("或输入其他保护者命令。.", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Method: StartShareCommandInteraction]
        private void StartShareCommandInteraction(
          TSPlayer player, bool isPersistent, bool isShareOrUnshare, bool isGroup, bool isShareAll,
          object shareTarget = null, string shareTargetName = null
    )
        {
            CommandInteraction interaction = this.StartOrResetCommandInteraction(player);
            interaction.DoesNeverComplete = isPersistent;
            interaction.TileEditCallback += (playerLocal, editType, tileId, location, objectStyle) =>
            {
                if (
                  editType != TileEditType.PlaceTile ||
                  editType != TileEditType.PlaceWall ||
                  editType != TileEditType.DestroyWall ||
                  editType != TileEditType.PlaceActuator
        )
                {
                    this.TryAlterProtectionShare(playerLocal, location, isShareOrUnshare, isGroup, isShareAll, shareTarget, shareTargetName);

                    playerLocal.SendTileSquareCentered(location);
                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }

                playerLocal.SendTileSquareCentered(location);
                return new CommandInteractionResult { IsHandled = false, IsInteractionCompleted = false };
            };
            Func<TSPlayer, DPoint, CommandInteractionResult> usageCallbackFunc = (playerLocal, location) =>
            {
                this.TryAlterProtectionShare(playerLocal, location, isShareOrUnshare, isGroup, isShareAll, shareTarget, shareTargetName);
                playerLocal.SendTileSquareCentered(location, 3);

                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.SignReadCallback += usageCallbackFunc;
            interaction.ChestOpenCallback += usageCallbackFunc;
            interaction.HitSwitchCallback += usageCallbackFunc;
            interaction.SignEditCallback += (playerLocal, signIndex, location, newText) =>
            {
                this.TryAlterProtectionShare(playerLocal, location, isShareOrUnshare, isGroup, isShareAll, shareTarget, shareTargetName);
                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };

            interaction.TimeExpiredCallback += (playerLocal) =>
            {
                if (isShareOrUnshare)
                    playerLocal.SendMessage("等待时间过长。没有保护将被共享。", Color.Red);
                else
                    playerLocal.SendMessage("等待时间过长。没有保护将被共享。", Color.Red);
            };

            if (isShareOrUnshare)
                player.SendInfoMessage("点击或使用你想要共享的受保护对象或方块。");
            else
                player.SendInfoMessage("点击或使用你想要共享的受保护对象或方块。");
        }
        #endregion

        #region [Command Handling /lockchest]
        private void LockChestCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            bool persistentMode = false;
            if (args.Parameters.Count > 0)
            {
                if (args.ContainsParameter("-p", StringComparison.InvariantCultureIgnoreCase))
                {
                    persistentMode = true;
                }
                else
                {
                    args.Player.SendErrorMessage("正确语法: /lockchest [-p]");
                    args.Player.SendInfoMessage("输入/lockchest help 来获取这个命令的更多帮助。");
                    return;
                }
            }

            CommandInteraction interaction = this.StartOrResetCommandInteraction(args.Player);
            interaction.DoesNeverComplete = persistentMode;
            interaction.TileEditCallback += (playerLocal, editType, tileId, location, objectStyle) =>
            {
                if (
                  editType != TileEditType.PlaceTile ||
                  editType != TileEditType.PlaceWall ||
                  editType != TileEditType.DestroyWall ||
                  editType != TileEditType.PlaceActuator
        )
                {
                    this.TryLockChest(playerLocal, location);

                    playerLocal.SendTileSquareCentered(location);
                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }

                playerLocal.SendTileSquareCentered(location);
                return new CommandInteractionResult { IsHandled = false, IsInteractionCompleted = false };
            };
            interaction.ChestOpenCallback += (playerLocal, location) =>
            {
                this.TryLockChest(playerLocal, location);
                playerLocal.SendTileSquareCentered(location, 3);

                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.TimeExpiredCallback += (playerLocal) =>
            {
                playerLocal.SendErrorMessage("等待时间过长。下一次点击或打开的箱子将不会被锁定。");
            };

            args.Player.SendInfoMessage("点击或打开一个箱子来锁定它。");
        }

        private bool LockChestCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("/lockchest 命令参考（第1页，共2页）", Color.Lime);
                    args.Player.SendMessage("/lockchest或 /lchest [-p]", Color.White);
                    args.Player.SendMessage("锁定选定的箱子，需要一把钥匙才能打开它.", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("-p = 激活持久模式。该命令将保持持久性，直到它过期。", Color.LightGray);
                    args.Player.SendMessage("或输入其他保护者命令。", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("请注意，并非所有类型的箱子都可以被锁定。", Color.LightGray);
                    break;
            }

            return false;
        }
        #endregion

        #region [Command Handling /swapchest]
        private void SwapChestCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            bool persistentMode = false;
            if (args.Parameters.Count > 0)
            {
                if (args.ContainsParameter("-p", StringComparison.InvariantCultureIgnoreCase))
                {
                    persistentMode = true;
                }
                else
                {
                    args.Player.SendErrorMessage("正确语法: /swapchest [-p]");
                    args.Player.SendInfoMessage("输入/swapchest help 来获取这个命令的更多帮助。");
                    return;
                }
            }

            CommandInteraction interaction = this.StartOrResetCommandInteraction(args.Player);
            interaction.DoesNeverComplete = persistentMode;
            interaction.TileEditCallback += (playerLocal, editType, tileId, location, objectStyle) =>
            {
                if (
                  editType != TileEditType.PlaceTile ||
                  editType != TileEditType.PlaceWall ||
                  editType != TileEditType.DestroyWall ||
                  editType != TileEditType.PlaceActuator
        )
                {
                    IChest newChest;
                    this.TrySwapChestData(playerLocal, location, out newChest);

                    playerLocal.SendTileSquareCentered(location);
                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }

                playerLocal.SendTileSquareCentered(location);
                return new CommandInteractionResult { IsHandled = false, IsInteractionCompleted = false };
            };
            interaction.ChestOpenCallback += (playerLocal, location) =>
            {
                IChest newChest;
                this.TrySwapChestData(playerLocal, location, out newChest);
                playerLocal.SendTileSquareCentered(location, 3);

                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.TimeExpiredCallback += (playerLocal) =>
            {
                playerLocal.SendErrorMessage("等待时间过长。下一次点击或打开的箱子将不会被交换。");
            };

            args.Player.SendInfoMessage("点击或打开一个箱子来交换其数据存储。");
        }

        private bool SwapChestCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("输入/swapchest help 来获取这个命令的更多帮助。", Color.Lime);
                    args.Player.SendMessage("/swapchest 或 /schest [-p]", Color.White);
                    args.Player.SendMessage("将选定箱子的数据交换为世界数据或保护者数据。", Color.LightGray);
                    args.Player.SendMessage("这不会改变箱子的内容或其保护状态，但会移除其名称。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("-p = 激活持久模式。该命令将保持持久性，直到它过期。", Color.LightGray);
                    args.Player.SendMessage("或输入其他保护者命令。", Color.LightGray);
                    break;
            }

            return false;
        }
        #endregion

        #region [Command Handling /refillchest]
        private void RefillChestCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            bool persistentMode = false;
            bool? oneLootPerPlayer = null;
            int? lootLimit = null;
            bool? autoLock = null;
            TimeSpan? refillTime = null;
            bool invalidSyntax = false;
            int timeParameters = 0;
            bool? autoEmpty = null;
            for (int i = 0; i < args.Parameters.Count; i++)
            {
                string param = args.Parameters[i];
                if (param.Equals("-p", StringComparison.InvariantCultureIgnoreCase))
                    persistentMode = true;
                else if (param.Equals("+ot", StringComparison.InvariantCultureIgnoreCase))
                    oneLootPerPlayer = true;
                else if (param.Equals("-ot", StringComparison.InvariantCultureIgnoreCase))
                    oneLootPerPlayer = false;
                else if (param.Equals("-ll", StringComparison.InvariantCultureIgnoreCase))
                    lootLimit = -1;
                else if (param.Equals("+ll", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (args.Parameters.Count - 1 == i)
                    {
                        invalidSyntax = true;
                        break;
                    }

                    int lootTimeAmount;
                    if (!int.TryParse(args.Parameters[i + 1], out lootTimeAmount) || lootTimeAmount < 0)
                    {
                        invalidSyntax = true;
                        break;
                    }

                    lootLimit = lootTimeAmount;
                    i++;
                }
                else if (param.Equals("+al", StringComparison.InvariantCultureIgnoreCase))
                    autoLock = true;
                else if (param.Equals("-al", StringComparison.InvariantCultureIgnoreCase))
                    autoLock = false;
                else if (param.Equals("+ae", StringComparison.InvariantCultureIgnoreCase))
                    autoEmpty = true;
                else if (param.Equals("-ae", StringComparison.InvariantCultureIgnoreCase))
                    autoEmpty = false;
                else
                    timeParameters++;
            }

            if (!invalidSyntax && timeParameters > 0)
            {
                if (!TimeSpanEx.TryParseShort(
                  args.ParamsToSingleString(0, args.Parameters.Count - timeParameters), out refillTime
        ))
                {
                    invalidSyntax = true;
                }
            }

            if (invalidSyntax)
            {
                args.Player.SendErrorMessage("正确语法: /refillchest 时间 [±ot] [±ll 数量] [±al] [±ae] [-p]");
                args.Player.SendErrorMessage("输入/refillchest help 来获取这个命令的更多帮助。");
                return;
            }

            CommandInteraction interaction = this.StartOrResetCommandInteraction(args.Player);
            interaction.DoesNeverComplete = persistentMode;
            interaction.TileEditCallback += (playerLocal, editType, tileId, location, objectStyle) =>
            {
                if (
                  editType != TileEditType.PlaceTile ||
                  editType != TileEditType.PlaceWall ||
                  editType != TileEditType.DestroyWall ||
                  editType != TileEditType.PlaceActuator
        )
                {
                    this.TrySetUpRefillChest(playerLocal, location, refillTime, oneLootPerPlayer, lootLimit, autoLock, autoEmpty);

                    playerLocal.SendTileSquareCentered(location);
                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }

                playerLocal.SendTileSquareCentered(location);
                return new CommandInteractionResult { IsHandled = false, IsInteractionCompleted = false };
            };
            interaction.ChestOpenCallback += (playerLocal, location) =>
            {
                this.TrySetUpRefillChest(playerLocal, location, refillTime, oneLootPerPlayer, lootLimit, autoLock, autoEmpty);
                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.TimeExpiredCallback += (playerLocal) =>
            {
                playerLocal.SendMessage("等待时间过长。没有重新填充的箱子将被创建。", Color.Red);
            };

            args.Player.SendInfoMessage("打开一个箱子将其转换为重新填充的箱子。");
        }

        private bool RefillChestCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("/refillchest 命令参考（第1页，共5页）", Color.Lime);
                    args.Player.SendMessage("/rchest 时间 [±ot] [±ll 数量] [±al] [±ae] [-p]", Color.White);
                    args.Player.SendMessage("将一个箱子转换为特殊箱子，该箱子可以自动重新填充其内容。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("时间 = 示例：2h（2小时）、2h30m（2小时30分钟）、2h30m10s（2小时30分钟10秒）、1d6h（1天6小时）等。.", Color.LightGray);
                    args.Player.SendMessage("+ot = 每个玩家只能掠夺这个箱子一次。", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("+ll amount = The chest can only be looted the given amount of times in total.", Color.LightGray);
                    args.Player.SendMessage("+al = After being looted, the chest is automatically locked.", Color.LightGray);
                    args.Player.SendMessage("+ae = After being looted, the chest is automatically emptied, regardless of content.", Color.LightGray);
                    args.Player.SendMessage("-p = Activates persistent mode. The command will stay persistent until it times", Color.LightGray);
                    args.Player.SendMessage("     out or any other protector command is entered.", Color.LightGray);
                    args.Player.SendMessage("If +ot or +ll is applied, a player must be logged in in order to loot it.", Color.LightGray);
                    break;
                case 3:
                    args.Player.SendMessage("To remove a feature from an existing refill chest, put a '-' before it:", Color.LightGray);
                    args.Player.SendMessage("  /refillchest -ot", Color.White);
                    args.Player.SendMessage("Removes the 'ot' feature from the selected chest.", Color.LightGray);
                    args.Player.SendMessage("To remove the timer, simply leave the time parameter away.", Color.LightGray);
                    args.Player.SendMessage("Example #1: Make a chest refill its contents after one hour and 30 minutes:", Color.LightGray);
                    break;
                case 4:
                    args.Player.SendMessage("  /refillchest 1h30m", Color.White);
                    args.Player.SendMessage("Example #2: Make a chest one time lootable per player without a refill timer:", Color.LightGray);
                    args.Player.SendMessage("  /refillchest +ot", Color.White);
                    args.Player.SendMessage("Example #3: Make a chest one time lootable per player with a 30 minutes refill timer:", Color.LightGray);
                    args.Player.SendMessage("  /refillchest 30m +ot", Color.White);
                    break;
                case 5:
                    args.Player.SendMessage("Example #4: Make a chest one time lootable per player and 10 times lootable in total:", Color.LightGray);
                    args.Player.SendMessage("  /refillchest +ot +ll 10", Color.White);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /refillchestmany]
        private void RefillChestManyCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (!args.Player.Group.HasPermission(ProtectorPlugin.SetRefillChests_Permission))
            {
                args.Player.SendErrorMessage("You do not have the permission to set up refill chests.");
                return;
            }

            bool? oneLootPerPlayer = null;
            int? lootLimit = null;
            bool? autoLock = null;
            TimeSpan? refillTime = null;
            bool? autoEmpty = null;
            string selector = null;
            bool fairLoot = false;
            bool invalidSyntax = (args.Parameters.Count == 0);
            if (!invalidSyntax)
            {
                selector = args.Parameters[0].ToLowerInvariant();

                int timeParameters = 0;
                for (int i = 1; i < args.Parameters.Count; i++)
                {
                    string param = args.Parameters[i];
                    if (param.Equals("+ot", StringComparison.InvariantCultureIgnoreCase))
                        oneLootPerPlayer = true;
                    else if (param.Equals("-ot", StringComparison.InvariantCultureIgnoreCase))
                        oneLootPerPlayer = false;
                    else if (param.Equals("-ll", StringComparison.InvariantCultureIgnoreCase))
                        lootLimit = -1;
                    else if (param.Equals("+ll", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (args.Parameters.Count - 1 == i)
                        {
                            invalidSyntax = true;
                            break;
                        }

                        int lootTimeAmount;
                        if (!int.TryParse(args.Parameters[i + 1], out lootTimeAmount) || lootTimeAmount < 0)
                        {
                            invalidSyntax = true;
                            break;
                        }

                        lootLimit = lootTimeAmount;
                        i++;
                    }
                    else if (param.Equals("+al", StringComparison.InvariantCultureIgnoreCase))
                        autoLock = true;
                    else if (param.Equals("+fl", StringComparison.InvariantCultureIgnoreCase))
                        fairLoot = true;
                    else if (param.Equals("-al", StringComparison.InvariantCultureIgnoreCase))
                        autoLock = false;
                    else if (param.Equals("+ae", StringComparison.InvariantCultureIgnoreCase))
                        autoEmpty = true;
                    else if (param.Equals("-ae", StringComparison.InvariantCultureIgnoreCase))
                        autoEmpty = false;
                    else
                        timeParameters++;
                }

                if (!invalidSyntax && timeParameters > 0)
                {
                    if (!TimeSpanEx.TryParseShort(
                      args.ParamsToSingleString(1, args.Parameters.Count - timeParameters - 1), out refillTime
          ))
                    {
                        invalidSyntax = true;
                    }
                }
            }

            ChestKind chestKindToSelect = ChestKind.Unknown;
            switch (selector)
            {
                case "dungeon":
                    chestKindToSelect = ChestKind.DungeonChest;
                    break;
                case "sky":
                    chestKindToSelect = ChestKind.SkyIslandChest;
                    break;
                case "ocean":
                    chestKindToSelect = ChestKind.OceanChest;
                    break;
                case "shadow":
                    chestKindToSelect = ChestKind.HellShadowChest;
                    break;
                case "hardmodedungeon":
                    chestKindToSelect = ChestKind.HardmodeDungeonChest;
                    break;
                case "pyramid":
                    chestKindToSelect = ChestKind.PyramidChest;
                    break;
                default:
                    invalidSyntax = true;
                    break;
            }

            if (invalidSyntax)
            {
                args.Player.SendErrorMessage("Proper syntax: /refillchestmany <selector> [time] [+ot|-ot] [+ll amount|-ll] [+al|-al] [+ae|-ae] [+fl]");
                args.Player.SendErrorMessage("Type /refillchestmany help to get more help to this command.");
                return;
            }

            if (chestKindToSelect != ChestKind.Unknown)
            {
                int createdChestsCounter = 0;
                for (int i = 0; i < Main.chest.Length; i++)
                {
                    Chest chest = Main.chest[i];
                    if (chest == null)
                        continue;

                    DPoint chestLocation = new DPoint(chest.x, chest.y);
                    ITile chestTile = TerrariaUtils.Tiles[chestLocation];
                    if (!chestTile.active() || (chestTile.type != TileID.Containers && chestTile.type != TileID.Containers2))
                        continue;

                    if (TerrariaUtils.Tiles.GuessChestKind(chestLocation) != chestKindToSelect)
                        continue;

                    try
                    {
                        ProtectionEntry protection = this.ProtectionManager.CreateProtection(args.Player, chestLocation, false);
                        protection.IsSharedWithEveryone = this.Config.AutoShareRefillChests;
                    }
                    catch (AlreadyProtectedException)
                    {
                        if (!this.ProtectionManager.CheckBlockAccess(args.Player, chestLocation, true) && !args.Player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission))
                        {
                            args.Player.SendWarningMessage($"You did not have access to convert chest {TShock.Utils.ColorTag(chestLocation.ToString(), Color.Red)} into a refill chest.");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        this.PluginTrace.WriteLineWarning($"Failed to create protection at {TShock.Utils.ColorTag(chestLocation.ToString(), Color.Red)}: \n{ex}");
                    }

                    try
                    {
                        this.ChestManager.SetUpRefillChest(
                          args.Player, chestLocation, refillTime, oneLootPerPlayer, lootLimit, autoLock, autoEmpty, fairLoot
                        );
                        createdChestsCounter++;
                    }
                    catch (Exception ex)
                    {
                        this.PluginTrace.WriteLineWarning($"Failed to create / update refill chest at {TShock.Utils.ColorTag(chestLocation.ToString(), Color.Red)}: \n{ex}");
                    }
                }

                args.Player.SendSuccessMessage($"{TShock.Utils.ColorTag(createdChestsCounter.ToString(), Color.Red)} refill chests were created / updated.");
            }
        }

        private bool RefillChestManyCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Command reference for /refillchestmany (Page 1 of 3)", Color.Lime);
                    args.Player.SendMessage("/refillchestmany|/rchestmany <selector> [time] [+ot|-ot] [+ll amount|-ll] [+al|-al] [+ae|-ae] [+fl]", Color.White);
                    args.Player.SendMessage("Converts all selected chests to refill chests or alters them.", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("selector = dungeon, sky, ocean, shadow, hardmodedungeon or pyramid", Color.LightGray);
                    args.Player.SendMessage("time = Examples: 2h, 2h30m, 2h30m10s, 1d6h etc.", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("+ot = The chest can only be looted once per player.", Color.LightGray);
                    args.Player.SendMessage("+ll = The chest can only be looted the given amount of times in total.", Color.LightGray);
                    args.Player.SendMessage("+al = After being looted, the chest is automatically locked.", Color.LightGray);
                    args.Player.SendMessage("+ae = After being looted, the chest is automatically emptied, regardless of contents.", Color.LightGray);
                    args.Player.SendMessage("+fl = An item of the chest's own type will be placed inside the chest yielding in a fair loot.", Color.LightGray);
                    args.Player.SendMessage("This command is expected to be used on a fresh world, the specified selector might", Color.LightGray);
                    args.Player.SendMessage("also select player chests. This is how chest kinds are distinguished:", Color.LightGray);
                    break;
                case 3:
                    args.Player.SendMessage("Dungeon = Locked gold chest with natural dungeon walls behind.", Color.LightGray);
                    args.Player.SendMessage("Sky = Locked gold chest above surface level.", Color.LightGray);
                    args.Player.SendMessage("Ocean = Unlocked submerged gold chest in the ocean biome.", Color.LightGray);
                    args.Player.SendMessage("Shadow = Locked shadow chest in the world's last seventh.", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("For more information about refill chests and their parameters type /help rchest.", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /bankchest]
        private void BankChestCommand_Exec(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Proper syntax: /bankchest <number>");
                args.Player.SendErrorMessage("Type /bankchest help to get more help to this command.");
                return;
            }

            int chestIndex;
            if (!int.TryParse(args.Parameters[0], out chestIndex))
            {
                args.Player.SendErrorMessage("The given prameter is not a valid number.");
                return;
            }

            bool hasNoBankChestLimits = args.Player.Group.HasPermission(ProtectorPlugin.NoBankChestLimits_Permission);
            if (
              chestIndex < 1 || (chestIndex > this.Config.MaxBankChestsPerPlayer && !hasNoBankChestLimits)
      )
            {
                string messageFormat;
                if (!hasNoBankChestLimits)
                    messageFormat = "The bank chest number must be between 1 to {0}.";
                else
                    messageFormat = "The bank chest number must be greater than 1.";

                args.Player.SendErrorMessage(string.Format(messageFormat, this.Config.MaxBankChestsPerPlayer));
                return;
            }

            CommandInteraction interaction = this.StartOrResetCommandInteraction(args.Player);
            interaction.TileEditCallback += (playerLocal, editType, tileId, location, objectStyle) =>
            {
                if (
                  editType != TileEditType.PlaceTile ||
                  editType != TileEditType.PlaceWall ||
                  editType != TileEditType.DestroyWall ||
                  editType != TileEditType.PlaceActuator
        )
                {
                    this.TrySetUpBankChest(playerLocal, location, chestIndex);

                    playerLocal.SendTileSquareCentered(location);
                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }
                else if (editType == TileEditType.DestroyWall)
                {
                    playerLocal.SendTileSquareCentered(location);
                    return new CommandInteractionResult { IsHandled = false, IsInteractionCompleted = false };
                }

                return new CommandInteractionResult { IsHandled = false, IsInteractionCompleted = false };
            };
            interaction.ChestOpenCallback += (playerLocal, location) =>
            {
                this.TrySetUpBankChest(playerLocal, location, chestIndex);
                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.TimeExpiredCallback += (playerLocal) =>
            {
                playerLocal.SendMessage("Waited too long. No bank chest will be created.", Color.Red);
            };

            args.Player.SendInfoMessage("Open a chest to convert it into a bank chest.");
        }

        private bool BankChestCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Command reference for /bankchest (Page 1 of 5)", Color.Lime);
                    args.Player.SendMessage("/bankchest|/bchest <number>", Color.White);
                    args.Player.SendMessage("Converts a protected chest into a bank chest instance. Bank chests store their content in a separate", Color.LightGray);
                    args.Player.SendMessage("non world related database - their content remains the same, no matter what world they are instanced in.", Color.LightGray);
                    args.Player.SendMessage("They are basically like piggy banks, but server sided.", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("number = A 1-based number to uniquely identify the bank chest.", Color.LightGray);
                    args.Player.SendMessage("Usually, the number '1' is assigned to the first created bank chest, '2' for the next etc.", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("In order to be converted to a bank chest, a chest must be protected and the player has to own it.", Color.LightGray);
                    args.Player.SendMessage("Also, if this is the first instance of a bank chest ever created, the content of the chest will", Color.LightGray);
                    break;
                case 3:
                    args.Player.SendMessage("be considered as the new bank chest content. If the bank chest with that number was already instanced", Color.LightGray);
                    args.Player.SendMessage("before though, then the chest has to be empty so that it can safely be overwritten by the bank chest's", Color.LightGray);
                    args.Player.SendMessage("actual content.", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("To remove a bank chest instance, simply /deprotect it.", Color.LightGray);
                    break;
                case 4:
                    args.Player.SendMessage("The amount of bank chests a player can own is usually limited by configuration, also an additional permission", Color.LightGray);
                    args.Player.SendMessage("is required to share a bank chest with other players.", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("Only one bank chest instance with the same number shall be present in one and the same world.", Color.White);
                    break;
                case 5:
                    args.Player.SendMessage("Example #1: Create a bank chest with the number 1:", Color.LightGray);
                    args.Player.SendMessage("  /bankchest 1", Color.White);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /dumpbankchest]
        private void DumpBankChestCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            bool persistentMode = false;
            if (args.Parameters.Count > 0)
            {
                if (args.ContainsParameter("-p", StringComparison.InvariantCultureIgnoreCase))
                {
                    persistentMode = true;
                }
                else
                {
                    args.Player.SendErrorMessage("Proper syntax: /dumpbankchest [-p]");
                    args.Player.SendInfoMessage("Type /dumpbankchest help to get more help to this command.");
                    return;
                }
            }

            Action<TSPlayer, DPoint> dumpBankChest = (playerLocal, chestLocation) =>
            {
                foreach (ProtectionEntry protection in this.ProtectionManager.EnumerateProtectionEntries(chestLocation))
                {
                    if (protection.BankChestKey == BankChestDataKey.Invalid)
                    {
                        args.Player.SendErrorMessage("This is not a bank chest.");
                        return;
                    }

                    protection.BankChestKey = BankChestDataKey.Invalid;
                    args.Player.SendSuccessMessage("The bank chest content was sucessfully dumped and the bank chest instance was removed.");
                    return;
                }

                args.Player.SendErrorMessage("This chest is not protected by Protector at all.");
            };

            CommandInteraction interaction = base.StartOrResetCommandInteraction(args.Player);
            interaction.DoesNeverComplete = persistentMode;
            interaction.TileEditCallback += (playerLocal, editType, tileId, location, objectStyle) =>
            {
                if (
                  editType != TileEditType.PlaceTile ||
                  editType != TileEditType.PlaceWall ||
                  editType != TileEditType.DestroyWall ||
                  editType != TileEditType.PlaceActuator
        )
                {
                    dumpBankChest(playerLocal, location);
                    playerLocal.SendTileSquareCentered(location);

                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }

                playerLocal.SendTileSquareCentered(location);
                return new CommandInteractionResult { IsHandled = false, IsInteractionCompleted = false };
            };
            interaction.ChestOpenCallback += (playerLocal, chestLocation) =>
            {
                dumpBankChest(playerLocal, chestLocation);
                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.TimeExpiredCallback += (player) =>
            {
                player.SendErrorMessage("Waited too long, no bank chest will be dumped.");
            };
            args.Player.SendInfoMessage("Open a bank chest to dump its content.");
        }

        private bool DumpBankChestCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Command reference for /dumpbankchest (Page 1 of 2)", Color.Lime);
                    args.Player.SendMessage("/dumpbankchest|dbchest [-p]", Color.White);
                    args.Player.SendMessage("Removes a bank chest instance but keeps its content in place actually duplicating all items.", Color.LightGray);
                    args.Player.SendMessage("This allows you to use bank chests like chest-templates.", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("-p = Activates persistent mode. The command will stay persistent until it times", Color.LightGray);
                    args.Player.SendMessage("     out or any other protector command is entered.", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /tradechest]
        private void TradeChestCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count < 4)
            {
                args.Player.SendErrorMessage("Proper syntax: /tradechest <sell amount> <sell item> <pay amount> <pay item or group> [limit]");
                args.Player.SendErrorMessage("Example to sell 200 wood for 5 gold coins: /tradechest 200 Wood 5 \"Gold Coin\"");
                args.Player.SendErrorMessage("Type /tradechest help to get more help to this command.");
                return;
            }

            string sellAmountRaw = args.Parameters[0];
            string sellItemRaw = args.Parameters[1];
            string payAmountRaw = args.Parameters[2];
            string payItemRaw = args.Parameters[3];

            int sellAmount;
            Item sellItem;
            int payAmount;
            object payItemIdOrGroup;
            int lootLimit = 0;

            if (!int.TryParse(sellAmountRaw, out sellAmount) || sellAmount <= 0)
            {
                args.Player.SendErrorMessage($"Expected <sell amount> to be a postive number, but \"{sellAmountRaw}\" was given.");
                return;
            }
            if (!int.TryParse(payAmountRaw, out payAmount) || payAmount <= 0)
            {
                args.Player.SendErrorMessage($"Expected <sell amount> to be a postive number, but \"{payAmountRaw}\" was given.");
                return;
            }
            if (args.Parameters.Count > 4 && (!int.TryParse(args.Parameters[4], out lootLimit) || lootLimit <= 0))
            {
                args.Player.SendErrorMessage($"Expected [limit] to be a postive number, but \"{args.Parameters[4]}\" was given.");
                return;
            }

            List<Item> itemsToLookup = TShock.Utils.GetItemByIdOrName(sellItemRaw);
            if (itemsToLookup.Count == 0)
            {
                args.Player.SendErrorMessage($"Unable to guess a valid item type from \"{sellItemRaw}\".");
                return;
            }
            if (itemsToLookup.Count > 1)
            {
                args.Player.SendErrorMessage("Found multiple matches for the given <sell item>: " + string.Join(", ", itemsToLookup));
                return;
            }
            sellItem = itemsToLookup[0];

            bool isItemGroup = this.Config.TradeChestItemGroups.ContainsKey(payItemRaw);
            if (!isItemGroup)
            {
                itemsToLookup = TShock.Utils.GetItemByIdOrName(payItemRaw);
                if (itemsToLookup.Count == 0)
                {
                    args.Player.SendErrorMessage($"无法从 \"{payItemRaw}\" 猜测一个有效的物品类型。");
                    return;
                }
                if (itemsToLookup.Count > 1)
                {
                    args.Player.SendErrorMessage("找到多个与给定的 <支付物品> 匹配的物品：" + string.Join(", ", itemsToLookup));
                    return;
                }
                payItemIdOrGroup = itemsToLookup[0].netID;

                if (sellItem.netID == (int)payItemIdOrGroup || (TerrariaUtils.Items.IsCoinType(sellItem.netID) && TerrariaUtils.Items.IsCoinType((int)payItemIdOrGroup)))
                {
                    args.Player.SendErrorMessage("要出售的物品应该与支付物品不同。");
                    return;
                }
            }
            else
            {
                payItemIdOrGroup = payItemRaw;
            }

            CommandInteraction interaction = this.StartOrResetCommandInteraction(args.Player);
            interaction.TileEditCallback += (playerLocal, editType, tileId, location, objectStyle) =>
            {
                if (
                  editType != TileEditType.PlaceTile ||
                  editType != TileEditType.PlaceWall ||
                  editType != TileEditType.DestroyWall ||
                  editType != TileEditType.PlaceActuator
        )
                {
                    this.TrySetUpTradeChest(playerLocal, location, sellAmount, sellItem.netID, payAmount, payItemIdOrGroup, lootLimit);

                    playerLocal.SendTileSquareCentered(location);
                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }

                playerLocal.SendTileSquareCentered(location);
                return new CommandInteractionResult { IsHandled = false, IsInteractionCompleted = false };
            };
            interaction.ChestOpenCallback += (playerLocal, location) =>
            {
                this.TrySetUpTradeChest(playerLocal, location, sellAmount, sellItem.netID, payAmount, payItemIdOrGroup, lootLimit);
                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
            };
            interaction.TimeExpiredCallback += (playerLocal) =>
            {
                playerLocal.SendMessage("等待时间过长。不会创建交易宝箱。", Color.Red);
            };

            string priceInfo = "";
#if SEconomy
      if (this.PluginCooperationHandler.IsSeconomyAvailable && this.Config.TradeChestPayment > 0 && !args.Player.Group.HasPermission(ProtectorPlugin.FreeTradeChests_Permission))
        priceInfo = $" This will cost you {this.Config.TradeChestPayment} {this.PluginCooperationHandler.Seconomy_MoneyName()}";
#endif

            args.Player.SendInfoMessage("打开一个宝箱将其转换为交易宝箱。" + priceInfo);
        }

        private bool TradeChestCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("命令 /tradechest 的参考信息（第 1 页，共 3 页）", Color.Lime);
                    args.Player.SendMessage("/tradechest|/tchest <出售数量> <出售物品> <支付数量> <支付物品或组> [限制]", Color.White);
                    args.Player.SendMessage("出售数量 = 每次点击宝箱时向玩家出售的物品数量。", Color.LightGray);
                    args.Player.SendMessage("出售物品 = 要出售的物品类型。", Color.LightGray);
                    args.Player.SendMessage("支付数量 = 当玩家购买时，要从玩家背包中取出的 <支付物品> 的数量。", Color.LightGray);
                    args.Player.SendMessage("支付物品或组 = 当玩家购买时，要从玩家那里取出的物品类型。这也可能是一个物品组名称。", Color.LightGray);
                    args.Player.SendMessage("限制 = 可选。单个玩家允许从这个宝箱购买的次数。", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("将一个宝箱转换为一个特殊的宝箱，可以向其他玩家出售其内容。", Color.LightGray);
                    args.Player.SendMessage("您也可以使用这个命令来更改一个现有的交易宝箱。", Color.LightGray);
                    args.Player.SendMessage("其他玩家通过简单点击交易宝箱购买，只有所有者、共享用户或管理员可以查看交易宝箱的内容。", Color.LightGray);
                    args.Player.SendMessage("购买者的支付也存储在宝箱中，所以确保始终有足够的空间可用。还要确保", Color.LightGray);
                    args.Player.SendMessage("宝箱始终装满足够的商品，否则玩家将无法从您那里购买。", Color.LightGray);
                    break;
                case 3:
                    args.Player.SendMessage("请注意，前缀不会被考虑用于支付或要出售的物品。", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        private readonly ConditionalWeakTable<TSPlayer, DPoint[]> scanChestsResults = new ConditionalWeakTable<TSPlayer, DPoint[]>();
        #region [Command Handling /scanchests]
        private void ScanChestsCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count == 0)
            {
                args.Player.SendErrorMessage("正确的语法: /scanchests <物品名称> [<页码>]");
                args.Player.SendInfoMessage("输入 /scanchests help 以获取更多关于这个命令的信息。");
                return;
            }

            string itemNamePart;
            int pageNumber = 1;
            if (args.Parameters.Count == 1)
            {
                itemNamePart = args.Parameters[0];
            }
            else
            {
                string lastParam = args.Parameters[args.Parameters.Count - 1];
                if (lastParam.Length <= 2 && int.TryParse(lastParam, out pageNumber))
                    itemNamePart = args.ParamsToSingleString(0, 1);
                else
                    itemNamePart = args.ParamsToSingleString();

                if (pageNumber < 1)
                {
                    args.Player.SendErrorMessage($"\"{lastParam}\" 不是有效的页码。");
                    return;
                }
            }

            List<Item> itemsToLookup = TShock.Utils.GetItemByIdOrName(itemNamePart);
            if (itemsToLookup.Count == 0)
            {
                args.Player.SendErrorMessage($"无法从 \"{itemNamePart}\" 猜测一个有效的物品类型。");
                return;
            }

            // DPoint is the chest location.
            List<Tuple<ItemData[], DPoint>> results = new List<Tuple<ItemData[], DPoint>>();
            foreach (IChest chest in this.ChestManager.EnumerateAllChests())
            {
                List<ItemData> matchingItems = new List<ItemData>(
                  from item in chest.Items
                  where itemsToLookup.Any(li => li.netID == item.Type)
                  select item);

                if (matchingItems.Count > 0)
                    results.Add(new Tuple<ItemData[], DPoint>(matchingItems.ToArray(), chest.Location));
            }

            DPoint[] resultsChestLocations = results.Select(r => r.Item2).ToArray();
            this.scanChestsResults.Remove(args.Player);
            this.scanChestsResults.Add(args.Player, resultsChestLocations);

            PaginationTools.SendPage(args.Player, pageNumber, results, new PaginationTools.Settings
            {
                HeaderFormat = $"以下宝箱包含 \"{itemNamePart}\" (第 {{0}} 页，共 {{1}} 页)",
                NothingToDisplayString = $"没有宝箱包含与 \"{itemNamePart}\" 匹配的物品",
                LineTextColor = Color.LightGray,
                MaxLinesPerPage = 10,
                LineFormatter = (lineData, dataIndex, pageNumberLocal) =>
                {
                    var result = (lineData as Tuple<ItemData[], DPoint>);
                    if (result == null)
                        return null;

                    ItemData[] foundItems = result.Item1;
                    DPoint chestLocation = result.Item2;

                    string foundItemsString = string.Join(" ", foundItems.Select(i => TShock.Utils.ItemTag(i.ToItem())));

                    string chestOwner = "{未受保护}";
                    ProtectionEntry protection = this.ProtectionManager.GetProtectionAt(chestLocation);
                    if (protection != null)
                    {
                        UserAccount tsUser = TShock.UserAccounts.GetUserAccountByID(protection.Owner);
                        chestOwner = tsUser?.Name ?? $"{{用户 ID: {protection.Owner}}}";
                    }

                    return new Tuple<string, Color>($"{dataIndex}. 由 {TShock.Utils.ColorTag(chestOwner, Color.Red)} 拥有的宝箱包含 {foundItemsString}", Color.LightGray);
                }
            });

            if (results.Count > 0)
                args.Player.SendSuccessMessage("输入 /tpchest <结果索引> 以传送到相应的宝箱。");
        }

        private bool ScanChestsCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return false;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("命令 /scanchests 的参考信息（第 1 页，共 1 页）", Color.Lime);
                    args.Player.SendMessage("/scanchests <物品名称> [页码]", Color.White);
                    args.Player.SendMessage("在当前世界中搜索所有宝箱，寻找与给定名称匹配的物品。用户可以使用 /tpchest 命令传送到通过这个命令找到的宝箱。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("物品名称 = 要检查的物品（们）名称的一部分。", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /tpchest]
        private void TpChestCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count != 1)
            {
                args.Player.SendErrorMessage("正确的语法: /tpchest <结果索引>");
                args.Player.SendInfoMessage("输入 /tpchest help 以获取更多关于这个命令的信息。");
                return;
            }

            DPoint[] chestLocations;
            if (!this.scanChestsResults.TryGetValue(args.Player, out chestLocations))
            {
                args.Player.SendErrorMessage("在使用这个命令之前，您必须先使用 /scanchests。");
                return;
            }

            int chestIndex;
            if (!int.TryParse(args.Parameters[0], out chestIndex) || chestIndex < 1 || chestIndex > chestLocations.Length)
            {
                args.Player.SendErrorMessage($"\"{args.Parameters[0]}\" 不是一个有效的结果索引。");
                return;
            }

            DPoint chestLocation = chestLocations[chestIndex - 1];
            args.Player.Teleport(chestLocation.X * 16, chestLocation.Y * 16);
        }

        private bool TpChestCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return false;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("命令 /tpchest 的参考信息（第 1 页，共 1 页）", Color.Lime);
                    args.Player.SendMessage("/tpchest <结果索引>", Color.White);
                    args.Player.SendMessage("将您传送到由 /scanchests 命令找到的宝箱。", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("结果索引 = 搜索结果的索引。", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Hook Handlers]
        public override bool HandleTileEdit(TSPlayer player, TileEditType editType, int blockType, DPoint location, int objectStyle)
        {
            return this.HandleTileEdit(player, editType, blockType, location, objectStyle, false);
        }

        /// <param name="isLate">
        ///   if <c>true</c>, then this tile edit handler was invoked after all other plugins.
        /// </param>
        public bool HandleTileEdit(TSPlayer player, TileEditType editType, int blockType, DPoint location, int objectStyle, bool isLate)
        {
            if (this.IsDisposed)
                return false;
            if (base.HandleTileEdit(player, editType, blockType, location, objectStyle))
                return true;

            switch (editType)
            {
                case TileEditType.PlaceTile:
                    {
                        if (!isLate)
                            break;

                        WorldGen.PlaceTile(location.X, location.Y, blockType, false, true, -1, objectStyle);
                        NetMessage.SendData((int)PacketTypes.Tile, -1, player.Index, NetworkText.Empty, 1, location.X, location.Y, blockType, objectStyle);

                        if (this.Config.AutoProtectedTiles[blockType])
                            this.TryCreateAutoProtection(player, location);

                        return true;
                    }
                case TileEditType.TileKill:
                case TileEditType.TileKillNoItem:
                    {
                        // Is the tile really going to be destroyed or just being hit?
                        //if (blockType != 0)
                        //  break;

                        ITile tile = TerrariaUtils.Tiles[location];
                        bool isChest = (tile.type == TileID.Containers || tile.type == TileID.Containers2 || tile.type == TileID.Dressers);
                        foreach (ProtectionEntry protection in this.ProtectionManager.EnumerateProtectionEntries(location))
                        {
                            // If the protection is invalid, just remove it.
                            if (!TerrariaUtils.Tiles.IsValidCoord(protection.TileLocation))
                            {
                                this.ProtectionManager.RemoveProtection(TSPlayer.Server, protection.TileLocation, false);
                                continue;
                            }

                            ITile protectedTile = TerrariaUtils.Tiles[protection.TileLocation];
                            // If the protection is invalid, just remove it.
                            if (!protectedTile.active() || protectedTile.type != protection.BlockType)
                            {
                                this.ProtectionManager.RemoveProtection(TSPlayer.Server, protection.TileLocation, false);
                                continue;
                            }

                            if (
                              protection.Owner == player.Account.ID || (
                                this.Config.AutoDeprotectEverythingOnDestruction &&
                                player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission)
                              )
            )
                            {
                                if (isChest)
                                {
                                    bool isBankChest = (protection.BankChestKey != BankChestDataKey.Invalid);
                                    ObjectMeasureData measureData = TerrariaUtils.Tiles.MeasureObject(protection.TileLocation);
                                    DPoint chestLocation = measureData.OriginTileLocation;
                                    IChest chest = this.ChestManager.ChestFromLocation(chestLocation);

                                    if (chest == null)
                                        return true;

                                    if (isBankChest)
                                    {
                                        this.DestroyBlockOrObject(chestLocation);
                                    }
                                    else
                                    {
                                        for (int i = 0; i < Chest.maxItems; i++)
                                        {
                                            if (chest.Items[i].StackSize > 0)
                                                return true;
                                        }
                                    }
                                }
                                this.ProtectionManager.RemoveProtection(player, protection.TileLocation, false);

                                if (this.Config.NotifyAutoDeprotections)
                                    player.SendWarningMessage("这个物体不再受到保护。");
                            }
                            else
                            {
                                player.SendErrorMessage("这个物体受到保护。");
                                if (protection.TradeChestData != null)
                                    player.SendWarningMessage("如果您想与这个宝箱进行交易，请先右键点击它。");
                                player.SendTileSquareCentered(location);
                                return true;
                            }
                        }

                        // note: if the chest was a bank chest, then it was already removed
                        if (isChest && TerrariaUtils.Tiles[location].active())
                        {
                            ObjectMeasureData measureData = TerrariaUtils.Tiles.MeasureObject(location);
                            DPoint chestLocation = measureData.OriginTileLocation;
                            IChest chest = this.ChestManager.ChestFromLocation(chestLocation);
                            if (chest != null)
                            {
                                // Don't allow removing of non empty chests.
                                for (int i = 0; i < Chest.maxItems; i++)
                                {
                                    if (chest.Items[i].StackSize > 0)
                                        return true;
                                }

                                this.DestroyBlockOrObject(chestLocation);
                                return true;
                            }
                        }

                        break;
                    }
                case TileEditType.PlaceWire:
                case TileEditType.PlaceWireBlue:
                case TileEditType.PlaceWireGreen:
                case TileEditType.PlaceWireYellow:
                case TileEditType.PlaceActuator:
                case TileEditType.DestroyWire:
                case TileEditType.DestroyWireBlue:
                case TileEditType.DestroyWireGreen:
                case TileEditType.DestroyWireYellow:
                case TileEditType.DestroyActuator:
                    if (this.Config.AllowWiringProtectedBlocks)
                        break;

                    if (this.CheckProtected(player, location, false))
                    {
                        player.SendTileSquareCentered(location);
                        return true;
                    }

                    break;
                case TileEditType.PokeLogicGate:
                case TileEditType.Actuate:
                    if (this.CheckProtected(player, location, false))
                    {
                        player.SendTileSquareCentered(location);
                        return true;
                    }

                    break;
            }

            return false;
        }

        public virtual bool HandleObjectPlacement(TSPlayer player, DPoint location, int blockType, int objectStyle, int alternative, int random, bool direction)
        {
            if (this.IsDisposed)
                return false;

            int directionInt = direction ? 1 : -1;
            WorldGen.PlaceObject(location.X, location.Y, blockType, false, objectStyle, alternative, random, directionInt);
            NetMessage.SendObjectPlacement(player.Index, location.X, location.Y, blockType, objectStyle, alternative, random, directionInt);

            if (this.Config.AutoProtectedTiles[blockType])
                this.TryCreateAutoProtection(player, location);

            return true;
        }

        public virtual bool HandleChestPlace(TSPlayer player, DPoint location, int storageType, int storageStyle)
        {
            if (this.IsDisposed)
                return false;

            ushort tileToPlace = TileID.Containers;
            if (storageType == 2)
                tileToPlace = TileID.Dressers;
            else if (storageType == 4)
                tileToPlace = TileID.Containers2;

            try
            {
                this.ChestManager.PlaceChest(tileToPlace, storageStyle, location);
            }
            catch (LimitEnforcementException)
            {
                player.SendTileSquareCentered(location.X, location.Y, 2);
                player.SendErrorMessage("已达到可能的最大宝箱数量上限。请向服务器管理员报告此问题。");
                this.PluginTrace.WriteLineWarning($"已达到 {Main.chest.Length + this.Config.MaxProtectorChests - 1} 个宝箱的上限！");
            }

            if (this.Config.AutoProtectedTiles[tileToPlace])
                this.TryCreateAutoProtection(player, location);

            return true;
        }

        private bool TryCreateAutoProtection(TSPlayer forPlayer, DPoint location)
        {
            try
            {
                if (forPlayer.HasPermission(ProtectorPlugin.RestrictProtections_Permission)
                    && (!forPlayer.HasBuildPermission(location.X, location.Y, false)
                    || TShock.Regions.InAreaRegion(location.X, location.Y).Count() == 0))
                    return false;

                this.ProtectionManager.CreateProtection(forPlayer, location, false);

                if (this.Config.NotifyAutoProtections)
                    forPlayer.SendSuccessMessage("这个物体现在受到保护。");

                return true;
            }
            catch (PlayerNotLoggedInException)
            {
                forPlayer.SendWarningMessage("这个物体无法被保护，因为您没有登录。");
            }
            catch (LimitEnforcementException)
            {
                forPlayer.SendWarningMessage("这个物体无法被保护，因为您已达到保护容量上限。");
            }
            catch (TileProtectedException)
            {
                this.PluginTrace.WriteLineError("错误：尝试在一个不应允许放置方块的位置自动保护方块。");
            }
            catch (AlreadyProtectedException)
            {
                this.PluginTrace.WriteLineError("错误：尝试在现有保护的同一位置自动保护方块。");
            }
            catch (Exception ex)
            {
                this.PluginTrace.WriteLineError("在自动保护过程中出现了意外的异常：\n" + ex);
            }

            return false;
        }

        public virtual bool HandleChestRename(TSPlayer player, int chestIndex, string newName)
        {
            if (this.IsDisposed)
                return false;

            IChest chest = this.LastOpenedChest(player);
            if (chest == null)
                return true;

            bool isAllowed = true;
            if (this.CheckProtected(player, chest.Location, true))
            {
                player.SendErrorMessage("您必须是宝箱的所有者才能重命名它。");
                isAllowed = false;
            }

            if (this.Config.LoginRequiredForChestUsage && !player.IsLoggedIn)
            {
                player.SendErrorMessage("您必须登录才能重命名宝箱。");
                isAllowed = false;
            }

            if (!isAllowed)
            {
                string originalName = string.Empty;
                if (chest.IsWorldChest)
                    originalName = chest.Name;

                // 名称更改对于玩家来说已经发生，所以必须将原始名称发送回他们。
                player.SendData(PacketTypes.ChestName, originalName, chest.Index, chest.Location.X, chest.Location.Y);
                return true;
            }
            else
            {
                // 只有世界宝箱可以有名称，所以尝试将其转换为一个。
                if (!chest.IsWorldChest && !this.TrySwapChestData(null, chest.Location, out chest))
                {
                    player.SendErrorMessage("这个世界命名的宝箱数量已达到上限。");
                    return true;
                }

                chest.Name = newName;
                player.SendData(PacketTypes.ChestName, chest.Name, chest.Index, chest.Location.X, chest.Location.Y);

                return true;
            }
        }

        // Note: chestLocation is always {0, 0}. chestIndex == -1 chest, piggy, safe closed. chestIndex == -2 piggy bank opened, chestIndex == -3 safe opened.
        public virtual bool HandleChestOpen(TSPlayer player, int chestIndex, DPoint chestLocation)
        {
            if (this.IsDisposed)
                return false;
            bool isChestClosed = (chestIndex == -1);
            if (!isChestClosed)
                return false;

            IChest chest = this.LastOpenedChest(player);
            if (chest == null)
                return false;

            ITile chestTile = TerrariaUtils.Tiles[chest.Location];
            bool isLocked;
            ChestStyle chestStyle = TerrariaUtils.Tiles.GetChestStyle(chestTile, out isLocked);
            if (isLocked)
                return false;

            ProtectionEntry protection = null;
            foreach (ProtectionEntry enumProtection in this.ProtectionManager.EnumerateProtectionEntries(chest.Location))
            {
                protection = enumProtection;
                break;
            }

            // Convert this chest to a world chest if it contains a key of night/light only, so that Terraria can do its
            // thing with it.
            if (!chest.IsWorldChest)
            {
                int containedLightNightKeys = 0;
                bool isOtherwiseEmpty = true;
                for (int i = 0; i < Chest.maxItems; i++)
                {
                    ItemData chestItem = chest.Items[i];
                    if (chestItem.StackSize == 1 && (chestItem.Type == ItemID.NightKey || chestItem.Type == ItemID.LightKey))
                    {
                        containedLightNightKeys++;
                    }
                    else if (chestItem.StackSize > 0)
                    {
                        isOtherwiseEmpty = false;
                        break;
                    }
                }

                if (containedLightNightKeys == 1 && isOtherwiseEmpty)
                {
                    this.TrySwapChestData(null, chest.Location, out chest);
                    player.TPlayer.lastChest = chest.Index;
                }
            }

            if (protection == null)
                return false;

            if (protection.RefillChestData != null)
            {
                if (protection.RefillChestData.AutoEmpty && !player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission))
                {
                    for (int i = 0; i < Chest.maxItems; i++)
                        chest.Items[i] = ItemData.None;
                }

                if (
                  protection.RefillChestData.AutoLock &&
                  TerrariaUtils.Tiles.IsChestStyleLockable(chestStyle) &&
                  protection.RefillChestData.RefillTime == TimeSpan.Zero
                )
                    TerrariaUtils.Tiles.LockChest(chest.Location);
            }

            return false;
        }

        public override bool HandleChestGetContents(TSPlayer player, DPoint location)
        {
            if (this.IsDisposed)
                return false;

            return this.HandleChestGetContents(player, location, skipInteractions: false);
        }

        public bool HandleChestGetContents(TSPlayer player, DPoint location, bool skipInteractions)
        {
            if (this.IsDisposed)
                return false;
            if (!skipInteractions && base.HandleChestGetContents(player, location))
                return true;
            bool isDummyChest = (location.X == 0);
            if (isDummyChest)
                return true;
            if (!TerrariaUtils.Tiles[location].active())
                return true;
            if (this.Config.LoginRequiredForChestUsage && !player.IsLoggedIn)
            {
                player.SendErrorMessage("您必须登录才能使用宝箱。");
                return true;
            }

            if (this.Config.DungeonChestProtection && !NPC.downedBoss3 && !player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission))
            {
                ChestKind kind = TerrariaUtils.Tiles.GuessChestKind(location);
                if (kind == ChestKind.DungeonChest || kind == ChestKind.HardmodeDungeonChest)
                {
                    player.SendErrorMessage("骷髅王尚未被击败。");
                    return true;
                }
            }

            ProtectionEntry protection = null;
            // Only need the first enumerated entry as we don't need the protections of adjacent blocks.
            foreach (ProtectionEntry enumProtection in this.ProtectionManager.EnumerateProtectionEntries(location))
            {
                protection = enumProtection;
                break;
            }

            DPoint chestLocation = TerrariaUtils.Tiles.MeasureObject(location).OriginTileLocation;

            IChest chest = this.ChestManager.ChestFromLocation(chestLocation, player);
            if (chest == null)
                return true;

            if (this.IsChestInUse(player, chest))
            {
                player.SendErrorMessage("Another player is already viewing the content of this chest.");
                return true;
            }

            if (protection != null)
            {
                bool isTradeChest = (protection.TradeChestData != null);
                if (!this.ProtectionManager.CheckProtectionAccess(protection, player))
                {
                    if (isTradeChest)
                        this.InitTrade(player, chest, protection);
                    else
                        player.SendErrorMessage("这个宝箱受到保护。");

                    return true;
                }

                if (isTradeChest)
                {
                    Item sellItem = new Item();
                    sellItem.netDefaults(protection.TradeChestData.ItemToSellId);
                    sellItem.stack = protection.TradeChestData.ItemToSellAmount;

                    string paymentDescription = this.PaymentItemDescription(protection.TradeChestData);
                    player.SendMessage($"这是一个交易宝箱，正在出售 {TShock.Utils.ItemTag(sellItem)} 以换取 {paymentDescription}", Color.OrangeRed);
                    player.SendMessage("您有访问权限，所以您可以随时修改它。", Color.LightGray);
                }

                if (protection.RefillChestData != null)
                {
                    RefillChestMetadata refillChest = protection.RefillChestData;
                    if (this.CheckRefillChestLootability(refillChest, player))
                    {
                        if (refillChest.OneLootPerPlayer)
                            player.SendMessage("您只能掠夺这个宝箱一次。", Color.OrangeRed);
                    }
                    else
                    {
                        return true;
                    }

                    if (refillChest.RefillTime != TimeSpan.Zero)
                    {
                        lock (this.ChestManager.RefillTimers)
                        {
                            if (this.ChestManager.RefillTimers.IsTimerRunning(refillChest.RefillTimer))
                            {
                                TimeSpan timeLeft = (refillChest.RefillStartTime + refillChest.RefillTime) - DateTime.Now;
                                player.SendMessage($"这个宝箱将在 {timeLeft.ToLongString()} 内补充。", Color.OrangeRed);
                            }
                            else
                            {
                                player.SendMessage("这个宝箱将补充其内容。", Color.OrangeRed);
                            }
                        }
                    }
                    else
                    {
                        player.SendMessage("这个宝箱将补充其内容。", Color.OrangeRed);
                    }
                }
            }

            lock (ChestManager.DummyChest)
            {
                if (chest.IsWorldChest)
                {
                    ChestManager.DummyChest.name = chest.Name;
                    player.TPlayer.chest = chest.Index;
                }
                else
                {
                    Main.chest[ChestManager.DummyChestIndex] = ChestManager.DummyChest;
                    player.TPlayer.chest = -1;
                }

                for (int i = 0; i < Chest.maxItems; i++)
                {
                    ChestManager.DummyChest.item[i] = chest.Items[i].ToItem();
                    player.SendData(PacketTypes.ChestItem, string.Empty, player.TPlayer.chest, i);
                }

                ChestManager.DummyChest.x = chestLocation.X;
                ChestManager.DummyChest.y = chestLocation.Y;
                player.SendData(PacketTypes.ChestOpen, string.Empty, player.TPlayer.chest);
                player.SendData(PacketTypes.SyncPlayerChestIndex, string.Empty, player.Index, player.TPlayer.chest);

                ChestManager.DummyChest.x = 0;
            }

            DPoint oldChestLocation;
            if (this.PlayerIndexChestDictionary.TryGetValue(player.Index, out oldChestLocation))
            {
                this.PlayerIndexChestDictionary.Remove(player.Index);
                this.ChestPlayerIndexDictionary.Remove(oldChestLocation);
            }

            if (!chest.IsWorldChest)
            {
                this.PlayerIndexChestDictionary[player.Index] = chestLocation;
                this.ChestPlayerIndexDictionary[chestLocation] = player.Index;
            }

            return false;
        }

        private string PaymentItemDescription(TradeChestMetadata tradeChestData)
        {
            bool isPayGroup = tradeChestData.ItemToPayGroup != null;
            if (!isPayGroup)
            {
                Item payItem = new Item();
                payItem.netDefaults(tradeChestData.ItemToPayId);
                payItem.stack = tradeChestData.ItemToPayAmount;

                return TShock.Utils.ItemTag(payItem);
            }
            else
            {
                string groupName = tradeChestData.ItemToPayGroup;
                HashSet<int> groupItemIds;
                if (this.Config.TradeChestItemGroups.TryGetValue(groupName.ToLowerInvariant(), out groupItemIds))
                {
                    StringBuilder builder = new StringBuilder();
                    builder.Append(tradeChestData.ItemToPayGroup).Append(' ').Append('(');

                    bool isFirst = true;
                    foreach (int itemId in groupItemIds)
                    {
                        if (!isFirst)
                            builder.Append(' ');

                        Item item = new Item();
                        item.netDefaults(itemId);
                        item.stack = tradeChestData.ItemToPayAmount;

                        builder.Append(TShock.Utils.ItemTag(item));
                        isFirst = false;
                    }

                    builder.Append(')');
                    return builder.ToString();
                }
                else
                {
                    return $"{{non existing group: {groupName}}}";
                }
            }
        }

        private bool IsChestInUse(TSPlayer player, IChest chest)
        {
            int usingPlayerIndex = -1;
            if (chest.IsWorldChest)
                usingPlayerIndex = Chest.UsingChest(chest.Index);

            return
              (usingPlayerIndex != -1 && usingPlayerIndex != player.Index) ||
              (this.ChestPlayerIndexDictionary.TryGetValue(chest.Location, out usingPlayerIndex) && usingPlayerIndex != player.Index);
        }

        private void InitTrade(TSPlayer player, IChest chest, ProtectionEntry protection)
        {
            TradeChestMetadata tradeChestData = protection.TradeChestData;
            Item sellItem = new Item();
            sellItem.netDefaults(tradeChestData.ItemToSellId);
            sellItem.stack = tradeChestData.ItemToSellAmount;

            string paymentDescription = this.PaymentItemDescription(tradeChestData);

            player.SendMessage($"这是一个由 {TShock.Utils.ColorTag(GetUserName(protection.Owner), Color.Red)} 拥有的交易宝箱。", Color.LightGray);

            Inventory chestInventory = new Inventory(chest.Items, specificPrefixes: false);
            int stock = chestInventory.Amount(sellItem.netID);
            if (stock < sellItem.stack)
            {
                player.SendMessage($"它原本正在出售 {TShock.Utils.ItemTag(sellItem)} 以换取 {paymentDescription}，但现在已缺货。", Color.LightGray);
                return;
            }

            player.SendMessage($"再次点击购买 {TShock.Utils.ItemTag(sellItem)} 以换取 {paymentDescription}", Color.LightGray);

            CommandInteraction interaction = this.StartOrResetCommandInteraction(player);
            interaction.ChestOpenCallback += (playerLocal, chestLocation) =>
            {
                bool complete = false;

                bool wasThisChestHit = (chestLocation == chest.Location);
                if (wasThisChestHit)
                {
                    Item payItem = new Item();
                    // 这非常重要，否则玩家可以使用交易宝箱轻松复制物品
                    if (!this.IsChestInUse(playerLocal, chest))
                    {
                        if (tradeChestData.ItemToPayGroup == null)
                        {

                            payItem.netDefaults(tradeChestData.ItemToPayId);
                            payItem.stack = tradeChestData.ItemToPayAmount;

                            this.PerformTrade(player, protection, chestInventory, sellItem, payItem);
                        }
                        else
                        {
                            Inventory playerInventory = new Inventory(new PlayerItemsAdapter(player.Index, player.TPlayer.inventory, 0, 53), specificPrefixes: false);
                            bool performedTrade = false;
                            foreach (int payItemId in this.Config.TradeChestItemGroups[tradeChestData.ItemToPayGroup])
                            {
                                int amountInInventory = playerInventory.Amount(payItemId);
                                if (amountInInventory >= tradeChestData.ItemToPayAmount)
                                {
                                    payItem.netDefaults(payItemId);
                                    payItem.stack = tradeChestData.ItemToPayAmount;

                                    this.PerformTrade(player, protection, chestInventory, sellItem, payItem);
                                    performedTrade = true;
                                    break;
                                }
                            }

                            if (!performedTrade)
                                playerLocal.SendErrorMessage($"您没有足够的任何一种 {paymentDescription}");
                        }
                    }
                    else
                    {
                        player.SendErrorMessage("另一个玩家目前正在查看这个宝箱的内容。");
                    }
                }
                else
                {
                    this.HandleChestGetContents(playerLocal, chestLocation, skipInteractions: true);
                    complete = true;
                }

                playerLocal.SendTileSquareCentered(chest.Location);
                return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = complete };
            };
        }

        private void PerformTrade(TSPlayer player, ProtectionEntry protection, Inventory chestInventory, Item sellItem, Item payItem)
        {
            Inventory playerInventory = new Inventory(new PlayerItemsAdapter(player.Index, player.TPlayer.inventory, 0, 53), specificPrefixes: false);

            ItemData sellItemData = ItemData.FromItem(sellItem);
            ItemData payItemData = ItemData.FromItem(payItem);
            ItemData?[] playerInvUpdates;
            try
            {
                playerInvUpdates = playerInventory.Remove(payItemData);
                playerInventory.Add(playerInvUpdates, sellItemData);
            }
            catch (InvalidOperationException)
            {
                player.SendErrorMessage($"您要么没有足够的 {TShock.Utils.ItemTag(payItem)} 来交易 {TShock.Utils.ItemTag(sellItem)}，要么您的背包已满。");
                return;
            }

            bool isRefillChest = (protection.RefillChestData != null);
            ItemData?[] chestInvUpdates = null;
            try
            {
                if (!isRefillChest)
                {
                    chestInvUpdates = chestInventory.Remove(sellItemData);
                    chestInventory.Add(chestInvUpdates, payItemData);
                }
            }
            catch (InvalidOperationException)
            {
                player.SendErrorMessage("交易宝箱中的物品已经售罄，或者没有空间放入您的付款物品。");
                return;
            }

            try
            {
                protection.TradeChestData.AddOrUpdateLooter(player.Account.ID);
            }
            catch (InvalidOperationException)
            {
                player.SendErrorMessage($"商人不允许每个玩家进行超过 {protection.TradeChestData.LootLimitPerPlayer} 笔交易。");
                return;
            }

            playerInventory.ApplyUpdates(playerInvUpdates);
            if (!isRefillChest)
                chestInventory.ApplyUpdates(chestInvUpdates);

            protection.TradeChestData.AddJournalEntry(player.Name, sellItem, payItem);
            player.SendSuccessMessage($"您刚刚用 {TShock.Utils.ItemTag(sellItem)} 换取了 {TShock.Utils.ItemTag(payItem)}，交易对象是 {TShock.Utils.ColorTag(GetUserName(protection.Owner), Color.Red)}。");
        }

        public virtual bool HandleChestModifySlot(TSPlayer player, int chestIndex, int slotIndex, ItemData newItem)
        {
            if (this.IsDisposed)
                return false;

            // Get the chest location of the chest the player has last opened.
            IChest chest = this.LastOpenedChest(player);
            if (chest == null)
                return true;

            ProtectionEntry protection = null;
            // Only need the first enumerated entry as we don't need the protections of adjacent blocks.
            foreach (ProtectionEntry enumProtection in this.ProtectionManager.EnumerateProtectionEntries(chest.Location))
            {
                protection = enumProtection;
                break;
            }

            bool playerHasAccess = true;
            if (protection != null)
                playerHasAccess = this.ProtectionManager.CheckProtectionAccess(protection, player, false);

            if (!playerHasAccess)
                return true;

            if (protection != null && protection.RefillChestData != null)
            {
                RefillChestMetadata refillChest = protection.RefillChestData;
                // The player who set up the refill chest or masters shall modify its contents.
                if (
                  this.Config.AllowRefillChestContentChanges &&
                  (refillChest.Owner == player.Account.ID || player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission))
        )
                {
                    refillChest.RefillItems[slotIndex] = newItem;

                    this.ChestManager.TryRefillChest(chest, refillChest);

                    if (refillChest.RefillTime == TimeSpan.Zero)
                    {
                        player.SendSuccessMessage("这个补充宝箱的内容已更新。");
                    }
                    else
                    {
                        lock (this.ChestManager.RefillTimers)
                        {
                            if (this.ChestManager.RefillTimers.IsTimerRunning(refillChest.RefillTimer))
                                this.ChestManager.RefillTimers.RemoveTimer(refillChest.RefillTimer);
                        }
                        player.SendSuccessMessage("这个补充宝箱的内容已更新，定时器已重置。");
                    }

                    return false;
                }

                if (refillChest.OneLootPerPlayer || refillChest.RemainingLoots > 0)
                {
                    //Contract.Assert(refillChest.Looters != null);
                    if (!refillChest.Looters.Contains(player.Account.ID))
                    {
                        refillChest.Looters.Add(player.Account.ID);

                        if (refillChest.RemainingLoots > 0)
                            refillChest.RemainingLoots--;
                    }
                }

                // As the first item is taken out, we start the refill timer.
                ItemData oldItem = chest.Items[slotIndex];
                if (newItem.Type == 0 || (newItem.Type == oldItem.Type && newItem.StackSize <= oldItem.StackSize))
                {
                    // TODO: Bad code, refill timers shouldn't be public at all.
                    lock (this.ChestManager.RefillTimers)
                        this.ChestManager.RefillTimers.StartTimer(refillChest.RefillTimer);
                }
                else
                {
                    player.SendErrorMessage("您不能将物品放入这个宝箱。");
                    return true;
                }
            }
            else if (protection != null && protection.BankChestKey != BankChestDataKey.Invalid)
            {
                BankChestDataKey bankChestKey = protection.BankChestKey;
                this.ServerMetadataHandler.EnqueueUpdateBankChestItem(bankChestKey, slotIndex, newItem);
            }

            chest.Items[slotIndex] = newItem;
            return true;
        }

        public virtual bool HandleChestUnlock(TSPlayer player, DPoint chestLocation)
        {
            if (this.IsDisposed)
                return false;

            ProtectionEntry protection = null;
            // Only need the first enumerated entry as we don't need the protections of adjacent blocks.
            foreach (ProtectionEntry enumProtection in this.ProtectionManager.EnumerateProtectionEntries(chestLocation))
            {
                protection = enumProtection;
                break;
            }
            if (protection == null)
                return false;

            bool undoUnlock = false;
            if (!this.ProtectionManager.CheckProtectionAccess(protection, player, false))
            {
                player.SendErrorMessage("这个宝箱受到保护，您不能解锁它。");
                undoUnlock = true;
            }
            if (protection.RefillChestData != null && !this.CheckRefillChestLootability(protection.RefillChestData, player))
                undoUnlock = true;

            if (undoUnlock)
            {
                bool dummy;
                ChestStyle style = TerrariaUtils.Tiles.GetChestStyle(TerrariaUtils.Tiles[chestLocation], out dummy);
                if (style != ChestStyle.ShadowChest)
                {
                    int keyType = TerrariaUtils.Tiles.KeyItemTypeFromChestStyle(style);
                    if (keyType != 0)
                    {
                        int itemIndex = Item.NewItem(null, chestLocation.X * TerrariaUtils.TileSize, chestLocation.Y * TerrariaUtils.TileSize, 0, 0, keyType);
                        player.SendData(PacketTypes.ItemDrop, string.Empty, itemIndex);
                    }
                }

                player.SendTileSquareCentered(chestLocation, 3);
                return true;
            }

            return false;
        }

        public override bool HandleSignEdit(TSPlayer player, int signIndex, DPoint location, string newText)
        {
            if (this.IsDisposed)
                return false;
            if (base.HandleSignEdit(player, signIndex, location, newText))
                return true;

            return this.CheckProtected(player, location, false);
        }

        public override bool HandleHitSwitch(TSPlayer player, DPoint location)
        {
            if (this.IsDisposed)
                return false;
            if (base.HandleHitSwitch(player, location))
                return true;

            if (this.CheckProtected(player, location, false))
            {
                player.SendTileSquareCentered(location, 3);
                return true;
            }

            return false;
        }

        public virtual bool HandleDoorUse(TSPlayer player, DPoint location, bool isOpening, Direction direction)
        {
            if (this.IsDisposed)
                return false;
            if (this.CheckProtected(player, location, false))
            {
                player.SendTileSquareCentered(location, 5);
                return true;
            }

            return false;
        }

        public virtual bool HandlePlayerSpawn(TSPlayer player, DPoint spawnTileLocation)
        {
            if (this.IsDisposed)
                return false;

            bool isBedSpawn = (spawnTileLocation.X != -1 || spawnTileLocation.Y != -1);
            RemoteClient client = Netplay.Clients[player.Index];
            if (!isBedSpawn || client.State <= 3)
                return false;

            DPoint bedTileLocation = new DPoint(spawnTileLocation.X, spawnTileLocation.Y - 1);
            ITile spawnTile = TerrariaUtils.Tiles[bedTileLocation];
            bool isInvalidBedSpawn = (!spawnTile.active() || spawnTile.type != TileID.Beds);

            bool allowNewSpawnSet = true;
            if (isInvalidBedSpawn)
            {
                player.Teleport(Main.spawnTileX * TerrariaUtils.TileSize, (Main.spawnTileY - 3) * TerrariaUtils.TileSize);
                this.PluginTrace.WriteLineWarning($"玩家 \"{player.Name}\" 试图在无效位置生成。");

                allowNewSpawnSet = false;
            }
            else if (this.Config.EnableBedSpawnProtection)
            {
                if (this.CheckProtected(player, bedTileLocation, false))
                {
                    player.SendErrorMessage("您设置的出生点床铺受到保护，您不能在那里出生。");
                    player.SendErrorMessage("您被传送到了您的最后一个有效出生位置。");

                    if (player.TPlayer.SpawnX == -1 && player.TPlayer.SpawnY == -1)
                        player.Teleport(Main.spawnTileX * TerrariaUtils.TileSize, (Main.spawnTileY - 3) * TerrariaUtils.TileSize);
                    else
                        player.Teleport(player.TPlayer.SpawnX * TerrariaUtils.TileSize, (player.TPlayer.SpawnY - 3) * TerrariaUtils.TileSize);

                    allowNewSpawnSet = false;
                }
            }

            if (allowNewSpawnSet)
            {
                player.TPlayer.SpawnX = spawnTileLocation.X;
                player.TPlayer.SpawnY = spawnTileLocation.Y;
                player.sX = spawnTileLocation.X;
                player.sY = spawnTileLocation.X;
            }

            player.TPlayer.Spawn(PlayerSpawnContext.ReviveFromDeath);
            NetMessage.SendData(12, -1, player.Index, NetworkText.Empty, player.Index);
            player.Dead = false;

            return true;
        }

        public virtual bool HandleQuickStackNearby(TSPlayer player, int playerSlotIndex)
        {
            if (this.IsDisposed)
                return false;

            Item item = player.TPlayer.inventory[playerSlotIndex];
            this.PutItemInNearbyChest(player, item, player.TPlayer.Center);


            player.SendData(PacketTypes.PlayerSlot, string.Empty, player.Index, playerSlotIndex, item.prefix);
            return true;
        }

        // Modded version of Terraria's original method.
        private Item PutItemInNearbyChest(TSPlayer player, Item itemToStore, Vector2 position)
        {
            bool isStored = false;

            for (int i = 0; i < Main.chest.Length; i++)
            {
                if (i == ChestManager.DummyChestIndex)
                    continue;
                Chest tChest = Main.chest[i];
                if (tChest == null || !Main.tile[tChest.x, tChest.y].active())
                    continue;

                bool isPlayerInChest = Main.player.Any((p) => p.chest == i);
                if (!isPlayerInChest)
                {
                    IChest chest = new ChestAdapter(i, tChest);
                    isStored = this.TryToStoreItemInNearbyChest(player, position, itemToStore, chest);
                    if (isStored)
                        break;
                }
            }

            if (!isStored)
            {
                lock (this.WorldMetadata.ProtectorChests)
                {
                    foreach (DPoint chestLocation in this.WorldMetadata.ProtectorChests.Keys)
                    {
                        if (!TerrariaUtils.Tiles[chestLocation].active())
                            continue;

                        bool isPlayerInChest = this.ChestPlayerIndexDictionary.ContainsKey(chestLocation);
                        if (!isPlayerInChest)
                        {
                            IChest chest = this.WorldMetadata.ProtectorChests[chestLocation];
                            isStored = this.TryToStoreItemInNearbyChest(player, position, itemToStore, chest);
                            if (isStored)
                                break;
                        }
                    }
                }
            }

            return itemToStore;
        }

        // Modded version of Terraria's Original
        private bool TryToStoreItemInNearbyChest(TSPlayer player, Vector2 playerPosition, Item itemToStore, IChest chest)
        {
            float quickStackRange = this.Config.QuickStackNearbyRange * 16;

            if (Chest.IsLocked(chest.Location.X, chest.Location.Y))
                return false;

            Vector2 vector2 = new Vector2((chest.Location.X * 16 + 16), (chest.Location.Y * 16 + 16));
            if ((vector2 - playerPosition).Length() > quickStackRange)
                return false;

            ProtectionEntry protection;
            if (this.ProtectionManager.CheckBlockAccess(player, chest.Location, false, out protection))
            {
                bool isRefillChest = (protection != null && protection.RefillChestData != null);
                bool isTradeChest = (protection != null && protection.TradeChestData != null);

                if (!isRefillChest && !isTradeChest)
                {
                    bool isBankChest = (protection != null && protection.BankChestKey != BankChestDataKey.Invalid);
                    bool hasEmptySlot = false;
                    bool containsSameItem = false;

                    for (int i = 0; i < Chest.maxItems; i++)
                    {
                        ItemData chestItem = chest.Items[i];

                        if (chestItem.Type <= 0 || chestItem.StackSize <= 0)
                            hasEmptySlot = true;
                        else if (itemToStore.netID == chestItem.Type)
                        {
                            int remainingStack = itemToStore.maxStack - chestItem.StackSize;

                            if (remainingStack > 0)
                            {
                                if (remainingStack > itemToStore.stack)
                                    remainingStack = itemToStore.stack;

                                itemToStore.stack = itemToStore.stack - remainingStack;
                                Main.chest[chest.Index].item[i].stack = Main.chest[chest.Index].item[i].stack + remainingStack;
                                chestItem = ItemData.FromItem(Main.chest[chest.Index].item[i]);

                                if (isBankChest)
                                    this.ServerMetadataHandler.EnqueueUpdateBankChestItem(protection.BankChestKey, i, chestItem);

                                if (itemToStore.stack <= 0)
                                {
                                    itemToStore.SetDefaults();
                                    return true;
                                }
                            }

                            containsSameItem = true;
                        }
                    }
                    if (containsSameItem && hasEmptySlot && itemToStore.stack > 0)
                    {
                        for (int i = 0; i < Chest.maxItems; i++)
                        {
                            ItemData chestItem = chest.Items[i];

                            if (chestItem.Type == 0 || chestItem.StackSize == 0)
                            {
                                ItemData itemDataToStore = ItemData.FromItem(itemToStore);
                                chest.Items[i] = itemDataToStore;

                                if (isBankChest)
                                    this.ServerMetadataHandler.EnqueueUpdateBankChestItem(protection.BankChestKey, i, itemDataToStore);

                                itemToStore.SetDefaults();
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private IChest LastOpenedChest(TSPlayer player)
        {
            DPoint chestLocation;
            int chestIndex = player.TPlayer.chest;

            bool isWorldDataChest = (chestIndex != -1 && chestIndex != ChestManager.DummyChestIndex);
            if (isWorldDataChest)
            {
                bool isPiggyOrSafeOrForge = (chestIndex == -2 || chestIndex == -3 || chestIndex == -4);
                if (isPiggyOrSafeOrForge)
                    return null;

                Chest chest = Main.chest[chestIndex];

                if (chest != null)
                    return new ChestAdapter(chestIndex, chest);
                else
                    return null;
            }
            else if (this.PlayerIndexChestDictionary.TryGetValue(player.Index, out chestLocation))
            {
                lock (this.WorldMetadata.ProtectorChests)
                    return this.WorldMetadata.ProtectorChests[chestLocation];
            }
            else
            {
                return null;
            }
        }
        #endregion

        private bool TryCreateProtection(TSPlayer player, DPoint tileLocation, bool sendFailureMessages = true)
        {
            if (!player.IsLoggedIn)
            {
                if (sendFailureMessages)
                    player.SendErrorMessage("您没有登录。");

                return false;
            }
            if (player.HasPermission(ProtectorPlugin.RestrictProtections_Permission)
                && (!player.HasBuildPermission(tileLocation.X, tileLocation.Y, false)
                || TShock.Regions.InAreaRegion(tileLocation.X, tileLocation.Y).Count() == 0))
            {
                player.SendErrorMessage("您不能在这里放置保护。");
                return false;
            }
            try
            {
                this.ProtectionManager.CreateProtection(player, tileLocation);
                player.SendSuccessMessage("这个物体现在受到保护。");

                return true;
            }
            catch (ArgumentException ex)
            {
                if (ex.ParamName == "tileLocation" && sendFailureMessages)
                    player.SendErrorMessage("这里没有可保护的物体。");

                throw;
            }
            catch (InvalidBlockTypeException ex)
            {
                if (sendFailureMessages)
                {
                    string message;
                    if (TerrariaUtils.Tiles.IsSolidBlockType(ex.BlockType, true))
                        message = "这类型的方块不能受到保护。";
                    else
                        message = "这类型的物体不能受到保护。";

                    player.SendErrorMessage(message);
                }
            }
            catch (LimitEnforcementException)
            {
                if (sendFailureMessages)
                {
                    player.SendErrorMessage(
                        $"保护容量已达到：{this.Config.MaxProtectionsPerPlayerPerWorld}。");
                }
            }
            catch (AlreadyProtectedException)
            {
                if (sendFailureMessages)
                    player.SendErrorMessage("这个物体已经受到保护。");
            }
            catch (TileProtectedException)
            {
                if (sendFailureMessages)
                    player.SendErrorMessage("这个物体被其他人保护了，或者位于一个受保护的区域中。");
            }
            catch (Exception ex)
            {
                player.SendErrorMessage("发生了意外的内部错误。");
                this.PluginTrace.WriteLineError("创建保护时出错：", ex.ToString());
            }

            return false;
        }

        private bool TryAlterProtectionShare(
          TSPlayer player, DPoint tileLocation, bool isShareOrUnshare, bool isGroup, bool isShareAll,
          object shareTarget, string shareTargetName, bool sendFailureMessages = true
    )
        {
            if (!player.IsLoggedIn)
            {
                if (sendFailureMessages)
                    player.SendErrorMessage("您没有登录。");

                return false;
            }

            try
            {
                if (isShareAll)
                {
                    this.ProtectionManager.ProtectionShareAll(player, tileLocation, isShareOrUnshare, true);

                    if (isShareOrUnshare)
                    {
                        player.SendSuccessMessage($"这个物体现在与所有人共享。");
                    }
                    else
                    {
                        player.SendSuccessMessage($"这个物体不再与所有人共享。");
                    }
                }
                else if (!isGroup)
                {
                    this.ProtectionManager.ProtectionShareUser(player, tileLocation, (int)shareTarget, isShareOrUnshare, true);

                    if (isShareOrUnshare)
                    {
                        player.SendSuccessMessage($"这个物体现在与玩家 \"{shareTargetName}\" 共享。");
                    }
                    else
                    {
                        player.SendSuccessMessage($"这个物体不再与玩家 \"{shareTargetName}\" 共享。");
                    }
                }
                else
                {
                    this.ProtectionManager.ProtectionShareGroup(player, tileLocation, (string)shareTarget, isShareOrUnshare, true);

                    if (isShareOrUnshare)
                    {
                        player.SendSuccessMessage($"这个物体现在与组 \"{shareTargetName}\" 共享。");
                    }
                    else
                    {
                        player.SendSuccessMessage($"这个物体不再与组 \"{shareTargetName}\" 共享。");
                    }
                }

                return true;
            }
            catch (ProtectionAlreadySharedException)
            {
                if (isShareAll)
                {
                    player.SendErrorMessage($"这个物体已经与所有人共享。");
                }
                else if (!isGroup)
                {
                    player.SendErrorMessage($"这个物体已经与玩家 \"{shareTargetName}\" 共享。");
                }
                else
                {
                    player.SendErrorMessage($"这个物体已经与组 \"{shareTargetName}\" 共享。");
                }

                return false;
            }
            catch (ProtectionNotSharedException)
            {
                if (isShareAll)
                {
                    player.SendErrorMessage($"这个物体不与所有人共享。");
                }
                else if (!isGroup)
                {
                    player.SendErrorMessage($"这个物体不与玩家 \"{shareTargetName}\" 共享。");
                }
                else
                {
                    player.SendErrorMessage($"这个物体不与组 \"{shareTargetName}\" 共享。");
                }

                return false;
            }
            catch (InvalidBlockTypeException)
            {
                if (sendFailureMessages)
                    player.SendErrorMessage("这类型的物体不能与其他人共享。");

                return false;
            }
            catch (MissingPermissionException ex)
            {
                if (sendFailureMessages)
                {
                    if (ex.Permission == ProtectorPlugin.BankChestShare_Permission)
                    {
                        player.SendErrorMessage("您不允许共享银行宝箱。");
                    }
                    else
                    {
                        player.SendErrorMessage("您不允许共享这类型的物体。");
                    }
                }

                return false;
            }
            catch (NoProtectionException)
            {
                if (sendFailureMessages)
                    player.SendErrorMessage("这个物体没有受到保护，因此不能与其他人共享。");

                return false;
            }
            catch (TileProtectedException)
            {
                if (sendFailureMessages)
                    player.SendErrorMessage("您必须是这个物体的所有者才能共享它。");

                return false;
            }
        }


        private bool TryRemoveProtection(TSPlayer player, DPoint tileLocation, bool sendFailureMessages = true)
        {
            if (!player.IsLoggedIn)
            {
                if (sendFailureMessages)
                    player.SendErrorMessage("您没有登录。");

                return false;
            }

            try
            {
                this.ProtectionManager.RemoveProtection(player, tileLocation);
                player.SendSuccessMessage("物体不再受到保护。");

                return true;
            }
            catch (InvalidBlockTypeException ex)
            {
                if (sendFailureMessages)
                {
                    string message;
                    if (TerrariaUtils.Tiles.IsSolidBlockType(ex.BlockType, true))
                        message = "不允许解除这类方块的保护。";
                    else
                        message = "不允许解除这类物体的保护。";

                    player.SendErrorMessage(message);
                }

                return false;
            }
            catch (NoProtectionException)
            {
                if (sendFailureMessages)
                    player.SendErrorMessage("物体没有受到保护。");

                return false;
            }
            catch (TileProtectedException)
            {
                player.SendErrorMessage("您不是这个物体的所有者。");

                return false;
            }
        }

        private bool TryGetProtectionInfo(TSPlayer player, DPoint tileLocation, bool sendFailureMessages = true)
        {
            ITile tile = TerrariaUtils.Tiles[tileLocation];
            if (!tile.active())
                return false;

            ProtectionEntry protection = null;
            // Only need the first enumerated entry as we don't need the protections of adjacent blocks.
            foreach (ProtectionEntry enumProtection in this.ProtectionManager.EnumerateProtectionEntries(tileLocation))
            {
                protection = enumProtection;
                break;
            }

            if (protection == null)
            {
                if (sendFailureMessages)
                    player.SendErrorMessage($"这个物体没有受到保护。");

                return false;
            }

            bool canViewExtendedInfo = (
              player.Group.HasPermission(ProtectorPlugin.ViewAllProtections_Permission) ||
              protection.Owner == player.Account.ID ||
              protection.IsSharedWithPlayer(player)
            );

            if (!canViewExtendedInfo)
            {
                player.SendMessage($"这个物体受到保护，并且没有与你共享。", Color.LightGray);

                player.SendWarningMessage("你不被允许获取更多关于这个保护的信息。");
                return true;
            }

            string ownerName;
            if (protection.Owner == -1)
                ownerName = "{服务器}";
            else
                ownerName = GetUserName(protection.Owner);

            player.SendMessage($"这个物体受到保护。所有者是 {TShock.Utils.ColorTag(ownerName, Color.Red)}。", Color.LightGray);

            string creationTimeFormat = "未知";
            if (protection.TimeOfCreation != DateTime.MinValue)
                creationTimeFormat = "{0:MM/dd/yy, h:mm tt} UTC ({1} 前)";

            player.SendMessage(
              string.Format(
                CultureInfo.InvariantCulture, "保护创建时间： " + creationTimeFormat, protection.TimeOfCreation,
                (DateTime.UtcNow - protection.TimeOfCreation).ToLongString()
              ),
              Color.LightGray
            );

            int blockType = TerrariaUtils.Tiles[tileLocation].type;
            if (blockType == TileID.Containers || blockType == TileID.Containers2 || blockType == TileID.Dressers)
            {
                if (protection.RefillChestData != null)
                {
                    RefillChestMetadata refillChest = protection.RefillChestData;
                    if (refillChest.RefillTime != TimeSpan.Zero)
                        player.SendMessage($"这是一个带有定时器的补充宝箱，定时器设置为 {TShock.Utils.ColorTag(refillChest.RefillTime.ToLongString(), Color.Red)}。", Color.LightGray);
                    else
                        player.SendMessage("这是一个没有定时器的补充宝箱。", Color.LightGray);

                    StringBuilder messageBuilder = new StringBuilder();
                    if (refillChest.OneLootPerPlayer || refillChest.RemainingLoots != -1)
                    {
                        messageBuilder.Append("它只能被掠夺 ");
                        if (refillChest.OneLootPerPlayer)
                            messageBuilder.Append("每个玩家只能掠夺一次");
                        if (refillChest.RemainingLoots != -1)
                        {
                            if (messageBuilder.Length > 0)
                                messageBuilder.Append("和");

                            messageBuilder.Append(TShock.Utils.ColorTag(refillChest.RemainingLoots.ToString(), Color.Red));
                            messageBuilder.Append("总共可以掠夺更多次");
                        }
                        messageBuilder.Append('。');
                    }

                    if (refillChest.Looters != null)
                    {
                        messageBuilder.Append("它已经被掠夺了");
                        messageBuilder.Append(TShock.Utils.ColorTag(refillChest.Looters.Count.ToString(), Color.Red));
                        messageBuilder.Append("次。");
                    }

                    if (messageBuilder.Length > 0)
                        player.SendMessage(messageBuilder.ToString(), Color.LightGray);
                }
                else if (protection.BankChestKey != BankChestDataKey.Invalid)
                {
                    BankChestDataKey bankChestKey = protection.BankChestKey;
                    player.SendMessage($"这是一个银行宝箱实例，编号为 {bankChestKey.BankChestIndex}。", Color.LightGray);
                }
                else if (protection.TradeChestData != null)
                {
                    Item sellItem = new Item();
                    sellItem.netDefaults(protection.TradeChestData.ItemToSellId);
                    sellItem.stack = protection.TradeChestData.ItemToSellAmount;
                    Item payItem = new Item();
                    payItem.netDefaults(protection.TradeChestData.ItemToPayId);
                    payItem.stack = protection.TradeChestData.ItemToPayAmount;

                    player.SendMessage($"这是一个交易宝箱。它正在出售 {TShock.Utils.ItemTag(sellItem)} 以换取 {TShock.Utils.ItemTag(payItem)}", Color.LightGray);
                }

                IChest chest = this.ChestManager.ChestFromLocation(protection.TileLocation);
                if (chest.IsWorldChest)
                    player.SendMessage($"它作为世界数据的一部分被存储（id: {TShock.Utils.ColorTag(chest.Index.ToString(), Color.Red)}）。", Color.LightGray);
                else
                    player.SendMessage($"它{TShock.Utils.ColorTag("不", Color.Red)}作为世界数据的一部分被存储。", Color.LightGray);
            }

            if (ProtectionManager.IsShareableBlockType(blockType))
            {
                if (protection.IsSharedWithEveryone)
                {
                    player.SendMessage("保护已与所有人共享。", Color.LightGray);
                }
                else
                {
                    StringBuilder sharedListBuilder = new StringBuilder();
                    if (protection.SharedUsers != null)
                    {
                        for (int i = 0; i < protection.SharedUsers.Count; i++)
                        {
                            if (i > 0)
                                sharedListBuilder.Append(", ");

                            TShockAPI.DB.UserAccount tsUser = TShock.UserAccounts.GetUserAccountByID(protection.SharedUsers[i]);
                            if (tsUser != null)
                                sharedListBuilder.Append(tsUser.Name);
                        }
                    }

                    if (sharedListBuilder.Length == 0 && protection.SharedGroups == null)
                    {
                        player.SendMessage($"保护{TShock.Utils.ColorTag("不", Color.Red)}与用户或组共享。", Color.LightGray);
                    }
                    else
                    {
                        if (sharedListBuilder.Length > 0)
                            player.SendMessage($"与用户共享：{TShock.Utils.ColorTag(sharedListBuilder.ToString(), Color.Red)}", Color.LightGray);
                        else
                            player.SendMessage($"保护{TShock.Utils.ColorTag("不", Color.Red)}与用户共享。", Color.LightGray);

                        if (protection.SharedGroups != null)
                            player.SendMessage($"与组共享：{TShock.Utils.ColorTag(protection.SharedGroups.ToString(), Color.Red)}", Color.LightGray);
                        else
                            player.SendMessage($"保护{TShock.Utils.ColorTag("不", Color.Red)}与组共享。", Color.LightGray);
                    }
                }
            }

            if (protection.TradeChestData != null && protection.TradeChestData.TransactionJournal.Count > 0)
            {
                player.SendMessage($"交易宝箱日志（最后 {protection.TradeChestData.TransactionJournal.Count} 笔交易）", Color.LightYellow);
                protection.TradeChestData.TransactionJournal.ForEach(entry =>
                {
                    string entryText = entry.Item1;
                    DateTime entryTime = entry.Item2;
                    TimeSpan timeSpan = DateTime.UtcNow - entryTime;

                    player.SendMessage($"{entryText} {timeSpan.ToLongString()} 前。", Color.LightGray);
                });
            }

            return true;
        }

        private static string GetUserName(int userId)
        {
            TShockAPI.DB.UserAccount tsUser = TShock.UserAccounts.GetUserAccountByID(userId);
            if (tsUser != null)
                return tsUser.Name;
            else
                return string.Concat("{已删除的用户ID: ", userId, "}");
        }

        private bool CheckProtected(TSPlayer player, DPoint tileLocation, bool fullAccessRequired)
        {
            if (!TerrariaUtils.Tiles[tileLocation].active())
                return false;

            ProtectionEntry protection;
            if (this.ProtectionManager.CheckBlockAccess(player, tileLocation, fullAccessRequired, out protection))
                return false;

            player.SendErrorMessage("这个物体受到保护。");
            return true;
        }

        public bool TryLockChest(TSPlayer player, DPoint anyChestTileLocation, bool sendMessages = true)
        {
            try
            {
                TerrariaUtils.Tiles.LockChest(anyChestTileLocation);
                return true;
            }
            catch (ArgumentException)
            {
                player.SendErrorMessage("这里没有宝箱。");
                return false;
            }
            catch (InvalidChestStyleException)
            {
                player.SendErrorMessage("宝箱必须是未锁定的可锁定宝箱。");
                return false;
            }
        }

        public bool TrySwapChestData(TSPlayer player, DPoint anyChestTileLocation, out IChest newChest)
        {
            newChest = null;

            int tileID = TerrariaUtils.Tiles[anyChestTileLocation].type;
            if (tileID != TileID.Containers && tileID != TileID.Containers2 && tileID != TileID.Dressers)
            {
                player?.SendErrorMessage("选定的方块不是宝箱或衣柜。");
                return false;
            }

            DPoint chestLocation = TerrariaUtils.Tiles.MeasureObject(anyChestTileLocation).OriginTileLocation;
            IChest chest = this.ChestManager.ChestFromLocation(chestLocation, player);
            if (chest == null)
                return false;

            ItemData[] content = new ItemData[Chest.maxItems];
            for (int i = 0; i < Chest.maxItems; i++)
                content[i] = chest.Items[i];

            if (chest.IsWorldChest)
            {
                lock (this.WorldMetadata.ProtectorChests)
                {
                    bool isChestAvailable = this.WorldMetadata.ProtectorChests.Count < this.Config.MaxProtectorChests;
                    if (!isChestAvailable)
                    {
                        player?.SendErrorMessage("已达到可能的保护者宝箱的最大数量。");
                        return false;
                    }

                    int playerUsingChestIndex = Chest.UsingChest(chest.Index);
                    if (playerUsingChestIndex != -1)
                        Main.player[playerUsingChestIndex].chest = -1;

                    Main.chest[chest.Index] = null;
                    newChest = new ProtectorChestData(chestLocation, content);

                    this.WorldMetadata.ProtectorChests.Add(chestLocation, (ProtectorChestData)newChest);

                    //TSPlayer.All.SendData(PacketTypes.ChestName, string.Empty, chest.Index, chestLocation.X, chestLocation.Y);

                    // Tell the client to remove the chest with the given index from its own chest array.
                    TSPlayer.All.SendData(PacketTypes.PlaceChest, string.Empty, 1, chestLocation.X, chestLocation.Y, 0, chest.Index);
                    TSPlayer.All.SendTileSquareCentered(chestLocation.X, chestLocation.Y, 2);
                    player?.SendWarningMessage("这个宝箱现在是一个保护者宝箱。");
                }
            }
            else
            {
                int availableUnnamedChestIndex = -1;
                int availableEmptyChestIndex = -1;
                for (int i = 0; i < Main.chest.Length; i++)
                {
                    if (i == ChestManager.DummyChestIndex)
                        continue;

                    Chest tChest = Main.chest[i];
                    if (tChest == null)
                    {
                        availableEmptyChestIndex = i;
                        break;
                    }
                    else if (availableUnnamedChestIndex == -1 && string.IsNullOrWhiteSpace(tChest.name))
                    {
                        availableUnnamedChestIndex = i;
                    }
                }

                // 优先考虑未设置的宝箱而非未命名的宝箱。
                int availableChestIndex = availableEmptyChestIndex;
                if (availableChestIndex == -1)
                    availableChestIndex = availableUnnamedChestIndex;

                bool isChestAvailable = (availableChestIndex != -1);
                if (!isChestAvailable)
                {
                    player?.SendErrorMessage("已达到世界宝箱的可能最大数量。");
                    return false;
                }

                lock (this.WorldMetadata.ProtectorChests)
                    this.WorldMetadata.ProtectorChests.Remove(chestLocation);

                Chest availableChest = Main.chest[availableChestIndex];
                bool isExistingButUnnamedChest = (availableChest != null);
                if (isExistingButUnnamedChest)
                {
                    if (!this.TrySwapChestData(null, new DPoint(availableChest.x, availableChest.y), out newChest))
                        return false;
                }

                availableChest = Main.chest[availableChestIndex] = new Chest();
                availableChest.x = chestLocation.X;
                availableChest.y = chestLocation.Y;
                availableChest.item = content.Select(i => i.ToItem()).ToArray();

                newChest = new ChestAdapter(availableChestIndex, availableChest);
                player?.SendWarningMessage("这个宝箱现在是一个世界宝箱。");
            }
            return true;
        }

        public void RemoveChestData(IChest chest)
        {
            ItemData[] content = new ItemData[Chest.maxItems];
            for (int i = 0; i < Chest.maxItems; i++)
                content[i] = chest.Items[i];

            if (chest.IsWorldChest)
            {
                int playerUsingChestIndex = Chest.UsingChest(chest.Index);
                if (playerUsingChestIndex != -1)
                    Main.player[playerUsingChestIndex].chest = -1;

                Main.chest[chest.Index] = null;
            }
            else
            {
                lock (this.WorldMetadata.ProtectorChests)
                    this.WorldMetadata.ProtectorChests.Remove(chest.Location);
            }
        }

        /// <exception cref="FormatException">The format item in <paramref name="format" /> is invalid.-or- The index of a format item is not zero. </exception>
        public bool TrySetUpRefillChest(
          TSPlayer player, DPoint tileLocation, TimeSpan? refillTime, bool? oneLootPerPlayer, int? lootLimit, bool? autoLock,
          bool? autoEmpty, bool sendMessages = true
)
        {
            if (!player.IsLoggedIn)
            {
                if (sendMessages)
                    player.SendErrorMessage("您必须登录才能设置补充宝箱。");

                return false;
            }

            if (!this.ProtectionManager.CheckBlockAccess(player, tileLocation, true) && !player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission))
            {
                player.SendErrorMessage("您不拥有这个宝箱的保护权。");
                return false;
            }

            try
            {
                if (this.ChestManager.SetUpRefillChest(
                  player, tileLocation, refillTime, oneLootPerPlayer, lootLimit, autoLock, autoEmpty, false, true
                ))
                {
                    if (sendMessages)
                    {
                        player.SendSuccessMessage("补充宝箱已成功设置。");

                        if (this.Config.AllowRefillChestContentChanges)
                            player.SendSuccessMessage("作为所有者，您仍然可以自由修改其内容。");
                    }
                }
                else
                {
                    if (sendMessages)
                    {
                        if (refillTime != null)
                        {
                            if (refillTime != TimeSpan.Zero)
                                player.SendSuccessMessage($"已将这个宝箱的补充定时器设置为 {refillTime.Value.ToLongString()}。");
                            else
                                player.SendSuccessMessage("这个宝箱现在可以立即补充。");
                        }
                        if (oneLootPerPlayer != null)
                        {
                            if (oneLootPerPlayer.Value)
                                player.SendSuccessMessage("这个宝箱现在每个玩家只能掠夺一次。");
                            else
                                player.SendSuccessMessage("这个宝箱现在可以自由掠夺。");
                        }
                        if (lootLimit != null)
                        {
                            if (lootLimit.Value != -1)
                                player.SendSuccessMessage($"这个宝箱现在还可以被掠夺 {lootLimit} 次。");
                            else
                                player.SendSuccessMessage("这个宝箱现在可以无限次掠夺。");
                        }
                        if (autoLock != null)
                        {
                            if (autoLock.Value)
                                player.SendSuccessMessage("这个宝箱在被掠夺时会自动锁定。");
                            else
                                player.SendSuccessMessage("这个宝箱将不再在被掠夺时自动锁定。");
                        }
                        if (autoEmpty != null)
                        {
                            if (autoEmpty.Value)
                                player.SendSuccessMessage("这个宝箱在被掠夺时会自动清空。");
                            else
                                player.SendSuccessMessage("这个宝箱将不再在被掠夺时自动清空。");
                        }
                    }
                }

                if (this.Config.AutoShareRefillChests)
                {
                    foreach (ProtectionEntry protection in this.ProtectionManager.EnumerateProtectionEntries(tileLocation))
                    {
                        protection.IsSharedWithEveryone = true;
                        break;
                    }
                }

                return true;
            }
            catch (ArgumentException ex)
            {
                if (ex.ParamName == "tileLocation")
                {
                    if (sendMessages)
                        player.SendErrorMessage("这里没有宝箱。");

                    return false;
                }

                throw;
            }
            catch (MissingPermissionException)
            {
                if (sendMessages)
                    player.SendErrorMessage("您不允许定义补充宝箱。");

                return false;
            }
            catch (NoProtectionException)
            {
                if (sendMessages)
                    player.SendErrorMessage("宝箱必须先受保护。");

                return false;
            }
            catch (ChestIncompatibilityException)
            {
                if (sendMessages)
                    player.SendErrorMessage("一个宝箱不能同时是补充宝箱和银行宝箱。");

                return false;
            }
            catch (NoChestDataException)
            {
                if (sendMessages)
                {
                    player.SendErrorMessage("错误：没有这个宝箱的宝箱数据可用。这个世界可能已损坏。");
                }

                return false;
            }
        }

        public bool TrySetUpBankChest(TSPlayer player, DPoint tileLocation, int bankChestIndex, bool sendMessages = true)
        {
            if (!player.IsLoggedIn)
            {
                if (sendMessages)
                    player.SendErrorMessage("您必须登录才能设置银行宝箱。");

                return false;
            }

            if (!this.ProtectionManager.CheckBlockAccess(player, tileLocation, true) && !player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission))
            {
                player.SendErrorMessage("您不拥有这个宝箱的保护权。");
                return false;
            }

            try
            {
                this.ChestManager.SetUpBankChest(player, tileLocation, bankChestIndex, true);

                player.SendSuccessMessage(string.Format(
                  $"这个宝箱现在是您的银行宝箱实例，编号为 {TShock.Utils.ColorTag(bankChestIndex.ToString(), Color.Red)}。"
                ));

                return true;
            }
            catch (ArgumentException ex)
            {
                if (ex.ParamName == "tileLocation")
                {
                    if (sendMessages)
                        player.SendErrorMessage("这里没有宝箱。");

                    return false;
                }
                else if (ex.ParamName == "bankChestIndex")
                {
                    ArgumentOutOfRangeException actualEx = (ArgumentOutOfRangeException)ex;
                    if (sendMessages)
                    {
                        string messageFormat;
                        if (!player.Group.HasPermission(ProtectorPlugin.NoBankChestLimits_Permission))
                            messageFormat = "银行宝箱编号必须在 1 和 {0} 之间。";
                        else
                            messageFormat = "银行宝箱编号必须大于 1。";

                        player.SendErrorMessage(string.Format(messageFormat, actualEx.ActualValue));
                    }

                    return false;
                }

                throw;
            }
            catch (MissingPermissionException)
            {
                if (sendMessages)
                    player.SendErrorMessage("您不允许定义银行宝箱。");

                return false;
            }
            catch (InvalidBlockTypeException)
            {
                if (sendMessages)
                    player.SendErrorMessage("只有宝箱可以转换为银行宝箱。");

                return false;
            }
            catch (NoProtectionException)
            {
                if (sendMessages)
                    player.SendErrorMessage("宝箱必须先受保护。");

                return false;
            }
            catch (ChestNotEmptyException)
            {
                if (sendMessages)
                    player.SendErrorMessage("宝箱必须为空才能在这里恢复银行宝箱。");

                return false;
            }
            catch (ChestTypeAlreadyDefinedException)
            {
                if (sendMessages)
                    player.SendErrorMessage("这个宝箱已经是银行宝箱了。");

                return false;
            }
            catch (ChestIncompatibilityException)
            {
                if (sendMessages)
                    player.SendErrorMessage("银行宝箱不能同时是补充宝箱或交易宝箱。");

                return false;
            }
            catch (NoChestDataException)
            {
                if (sendMessages)
                {
                    player.SendErrorMessage("错误：没有这个宝箱的宝箱数据可用。这个世界的数据可能已损坏。");
                    player.SendErrorMessage("");
                }

                return false;
            }
            catch (BankChestAlreadyInstancedException)
            {
                if (sendMessages)
                {
                    player.SendErrorMessage($"这个世界中已经有编号为 {bankChestIndex} 的您的银行宝箱实例。");
                    player.SendErrorMessage("");
                }

                return false;
            }
        }

        public bool TrySetUpTradeChest(TSPlayer player, DPoint tileLocation, int sellAmount, int sellItemId, int payAmount, object payItemIdOrGroup, int lootLimit = 0, bool sendMessages = true)
        {
            if (!player.IsLoggedIn)
            {
                if (sendMessages)
                    player.SendErrorMessage("您必须登录才能设置交易宝箱。");

                return false;
            }

            if (!this.ProtectionManager.CheckBlockAccess(player, tileLocation, true) && !player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission))
            {
                player.SendErrorMessage("您不拥有这个宝箱的保护权。");
                return false;
            }

            try
            {
                this.ChestManager.SetUpTradeChest(player, tileLocation, sellAmount, sellItemId, payAmount, payItemIdOrGroup, lootLimit, true);

                player.SendSuccessMessage("交易宝箱已成功创建/更新。");
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                if (sendMessages)
                    player.SendErrorMessage("提供的物品数量无效。");

                return false;
            }
            catch (ArgumentException ex)
            {
                if (ex.ParamName == "tileLocation")
                {
                    if (sendMessages)
                        player.SendErrorMessage("这里没有宝箱。");

                    return false;
                }

                throw;
            }
            catch (MissingPermissionException)
            {
                if (sendMessages)
                    player.SendErrorMessage("您不允许定义交易宝箱。");
#if SEconomy
    } catch (PaymentException ex) {
        if (sendMessages)
            player.SendErrorMessage("您没有足够的 {0} {1} 来设置一个交易宝箱！", ex.PaymentAmount, this.PluginCooperationHandler.Seconomy_MoneyName());
#endif
            }
            catch (InvalidBlockTypeException)
            {
                if (sendMessages)
                    player.SendErrorMessage("只能将宝箱转换为交易宝箱。");
            }
            catch (NoProtectionException)
            {
                if (sendMessages)
                    player.SendErrorMessage("宝箱必须先受保护。");
            }
            catch (ChestTypeAlreadyDefinedException)
            {
                if (sendMessages)
                    player.SendErrorMessage("这个宝箱已经是交易宝箱了。");
            }
            catch (ChestIncompatibilityException)
            {
                if (sendMessages)
                    player.SendErrorMessage("交易宝箱不能同时是银行宝箱。");
            }
            catch (NoChestDataException)
            {
                if (sendMessages)
                    player.SendErrorMessage("错误：没有这个宝箱的宝箱数据可用。这个世界可能已损坏。");
            }

            return false;
        }

        public void EnsureProtectionData(TSPlayer player, bool resetBankChestContent)
        {
            int invalidProtectionsCount;
            int invalidRefillChestCount;
            int invalidBankChestCount;

            this.ProtectionManager.EnsureProtectionData(
              resetBankChestContent, out invalidProtectionsCount, out invalidRefillChestCount, out invalidBankChestCount);

            if (player != TSPlayer.Server)
            {
                if (invalidProtectionsCount > 0)
                    player.SendWarningMessage("已移除 {0} 个无效保护。", invalidProtectionsCount);
                if (invalidRefillChestCount > 0)
                    player.SendWarningMessage("已移除 {0} 个无效补充钱箱。", invalidRefillChestCount);
                if (invalidBankChestCount > 0)
                    player.SendWarningMessage("已移除 {0} 个无效银行钱箱实例。", invalidBankChestCount);

                player.SendInfoMessage("已完成保护数据的确认。");
            }

            if (invalidProtectionsCount > 0)
                this.PluginTrace.WriteLineWarning("已移除 {0} 个无效保护。", invalidProtectionsCount);
            if (invalidRefillChestCount > 0)
                this.PluginTrace.WriteLineWarning("已移除 {0} 个无效补充钱箱。", invalidRefillChestCount);
            if (invalidBankChestCount > 0)
                this.PluginTrace.WriteLineWarning("已移除 {0} 个无效银行钱箱实例。", invalidBankChestCount);

            this.PluginTrace.WriteLineInfo("已完成保护数据的确认。");
        }

        private void DestroyBlockOrObject(DPoint tileLocation)
        {
            ITile tile = TerrariaUtils.Tiles[tileLocation];
            if (!tile.active())
                return;

            if (tile.type == TileID.Containers || tile.type == TileID.Containers2 || tile.type == TileID.Dressers)
            {
                this.ChestManager.DestroyChest(tileLocation);
            }
            else
            {
                WorldGen.KillTile(tileLocation.X, tileLocation.Y, false, false, true);
                TSPlayer.All.SendData(PacketTypes.PlaceChest, string.Empty, 0, tileLocation.X, tileLocation.Y, 0, -1);
            }
        }

        public bool CheckRefillChestLootability(RefillChestMetadata refillChest, TSPlayer player, bool sendReasonMessages = true)
        {
            if (!player.IsLoggedIn && (refillChest.OneLootPerPlayer || refillChest.RemainingLoots != -1))
            {
                if (sendReasonMessages)
                    player.SendErrorMessage("您必须登录才能使用这个宝箱。");

                return false;
            }

            if (
              !this.Config.AllowRefillChestContentChanges ||
              (player.Account.ID != refillChest.Owner && !player.Group.HasPermission(ProtectorPlugin.ProtectionMaster_Permission))
      )
            {
                if (refillChest.RemainingLoots == 0)
                {
                    if (sendReasonMessages)
                        player.SendErrorMessage("这个宝箱附有限制每人只能掠夺一次的限制，因此不能再被掠夺了。");

                    return false;
                }

                if (refillChest.OneLootPerPlayer)
                {
                    //Contract.Assert(refillChest.Looters != null);
                    if (refillChest.Looters == null)
                        refillChest.Looters = new Collection<int>();

                    if (refillChest.Looters.Contains(player.Account.ID))
                    {
                        if (sendReasonMessages)
                            player.SendErrorMessage("每个玩家只能掠夺这个宝箱一次。");
                        return false;
                    }
                }
            }

            return true;
        }

        #region [IDisposable Implementation]
        protected override void Dispose(bool isDisposing)
        {
            if (this.IsDisposed)
                return;

            if (isDisposing)
                this.ReloadConfigurationCallback = null;

            base.Dispose(isDisposing);
        }
        #endregion
    }
}
