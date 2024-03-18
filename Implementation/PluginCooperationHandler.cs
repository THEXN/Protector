using System;
using System.Data;
using System.IO;
using DPoint = System.Drawing.Point;

using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Terraria;
using Terraria.ID;
using Terraria.Plugins.Common;

using TShockAPI;
using TShockAPI.DB;

#if SEconomy
using Wolfje.Plugins.SEconomy;
using Wolfje.Plugins.SEconomy.Journal;
#endif // SEconomy

namespace Terraria.Plugins.CoderCow.Protector {
  public class PluginCooperationHandler {
    [Flags]
    private enum InfiniteChestsChestFlags {
      PUBLIC = 1,
      REGION = 2,
      REFILL = 4,
      BANK = 8
    }

    private const string SeconomySomeTypeQualifiedName = "Wolfje.Plugins.SEconomy.SEconomy, Wolfje.Plugins.SEconomy";

    public PluginTrace PluginTrace { get; }
    public Configuration Config { get; private set; }
    public ChestManager ChestManager { get; set; }
    public bool IsSeconomyAvailable { get; private set; }


    public PluginCooperationHandler(PluginTrace pluginTrace, Configuration config, ChestManager chestManager) {
      if (pluginTrace == null) throw new ArgumentNullException();
      if (config == null) throw new ArgumentNullException();

      this.PluginTrace = pluginTrace;
      this.Config = config;
      this.ChestManager = chestManager;
      
      this.IsSeconomyAvailable = (Type.GetType(SeconomySomeTypeQualifiedName, false) != null);
    }

    #region Infinite Chests
    public void InfiniteChests_ChestDataImport(
      ChestManager chestManager, ProtectionManager protectionManager, out int importedChests, out int protectFailures
    ) {
      //Contract.Assert(this.ChestManager != null);

      importedChests = 0;
      protectFailures = 0;

      IDbConnection dbConnection = null;
      try {
        switch (TShock.Config.Settings.StorageType.ToLower()) {
          case "mysql":
            string[] host = TShock.Config.Settings.MySqlHost.Split(':');
            dbConnection = new MySqlConnection(string.Format(
              "Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
              host[0],
              host.Length == 1 ? "3306" : host[1],
              TShock.Config.Settings.MySqlDbName,
              TShock.Config.Settings.MySqlUsername,
              TShock.Config.Settings.MySqlPassword
            ));

            break;
          case "sqlite":
            string sqliteDatabaseFilePath = Path.Combine(TShock.SavePath, "chests.sqlite");
            if (!File.Exists(sqliteDatabaseFilePath))
              throw new FileNotFoundException("未找到 Sqlite 数据库文件。", sqliteDatabaseFilePath);

            dbConnection = new SqliteConnection(string.Format("数据源={0}", sqliteDatabaseFilePath));

            break;
          default:
            throw new NotImplementedException("不支持的数据库。");
        }

        using (QueryResult reader = dbConnection.QueryReader(
          "SELECT X, Y, Account, Flags, Items, RefillTime FROM Chests WHERE WorldID = @0", Main.worldID)
        ) {
          while (reader.Read()) {
            int rawX = reader.Get<int>("X");
            int rawY = reader.Get<int>("Y");
            string rawAccount = reader.Get<string>("Account");
            InfiniteChestsChestFlags rawFlags = (InfiniteChestsChestFlags)reader.Get<int>("Flags");
            string rawItems = reader.Get<string>("Items");
            int refillTime = reader.Get<int>("RefillTime");

            if (!TerrariaUtils.Tiles.IsValidCoord(rawX, rawY))
              continue;

            DPoint chestLocation = new DPoint(rawX, rawY);
            ITile chestTile = TerrariaUtils.Tiles[chestLocation];
            if (!chestTile.active() || (chestTile.type != TileID.Containers && chestTile.type != TileID.Containers2 && chestTile.type != TileID.Dressers)) {
              this.PluginTrace.WriteLineWarning($"因为世界上不存在对应的宝箱图格，所以无法导入位于 {chestLocation} 的宝箱数据。");
              continue;
            }

            // TSPlayer.All = 宝箱将不被保护
            TSPlayer owner = TSPlayer.All;
            if (!string.IsNullOrEmpty(rawAccount)) {
              UserAccount tUser = TShock.UserAccounts.GetUserAccountByName(rawAccount);
              if (tUser != null) {
                owner = new TSPlayer(0);
                owner.Account.ID = tUser.ID;
                owner.Account.Name = tUser.Name;
                owner.Group = TShock.Groups.GetGroupByName(tUser.Group);
              } else {
                // 宝箱的原始所有者已不存在，因此我们只为服务器玩家保护它。
                owner = TSPlayer.Server;
              }
            }

            IChest importedChest;
            try {
              importedChest = this.ChestManager.ChestFromLocation(chestLocation);
              if (importedChest == null)
                importedChest = this.ChestManager.CreateChestData(chestLocation);
            } catch (LimitEnforcementException) {
              this.PluginTrace.WriteLineWarning($"已达到宝箱限制，上限为 {Main.chest.Length + this.Config.MaxProtectorChests - 1}！");
              break;
            }
            
            string[] itemData = rawItems.Split(',');
            int[] itemArgs = new int[itemData.Length];
            for (int i = 0; i < itemData.Length; i++)
              itemArgs[i] = int.Parse(itemData[i]);

            for (int i = 0; i < 40; i++) {
              int type = itemArgs[i * 3];
              int stack = itemArgs[i * 3 + 1];
              int prefix = (byte)itemArgs[i * 3 + 2];

              importedChest.Items[i] = new ItemData(prefix, type, stack);
            }
            importedChests++;

            if (owner != TSPlayer.All) {
              try {
                ProtectionEntry protection = protectionManager.CreateProtection(owner, chestLocation, false, false, false);
                protection.IsSharedWithEveryone = (rawFlags & InfiniteChestsChestFlags.PUBLIC) != 0;

                if ((rawFlags & InfiniteChestsChestFlags.REFILL) != 0)
                  chestManager.SetUpRefillChest(owner, chestLocation, TimeSpan.FromSeconds(refillTime));
              } catch (Exception ex) {
                this.PluginTrace.WriteLineWarning($"在 {chestLocation} 处创建保护或定义填充宝箱失败：\n{ex}");
                protectFailures++;
              }
            }
          }
        }
      } finally {
        dbConnection?.Close();
      }
    }

    public void InfiniteSigns_SignDataImport(
      ProtectionManager protectionManager, 
      out int importedSigns, out int protectFailures
    ) {
      string sqliteDatabaseFilePath = Path.Combine(TShock.SavePath, "signs.sqlite");
      if (!File.Exists(sqliteDatabaseFilePath))
        throw new FileNotFoundException("SQLite数据库文件未找到。", sqliteDatabaseFilePath);

      IDbConnection dbConnection = null;
      try {
        switch (TShock.Config.Settings.StorageType.ToLower()) {
          case "mysql":
            string[] host = TShock.Config.Settings.MySqlHost.Split(':');
            dbConnection = new MySqlConnection(string.Format(
              "Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
              host[0],
              host.Length == 1 ? "3306" : host[1],
              TShock.Config.Settings.MySqlDbName,
              TShock.Config.Settings.MySqlUsername,
              TShock.Config.Settings.MySqlPassword
            ));

            break;
          case "sqlite":
            dbConnection = new SqliteConnection(
              string.Format("uri=file://{0},Version=3", sqliteDatabaseFilePath)
            );

            break;
          default:
            throw new NotImplementedException("不支持的数据库。");
        }

        importedSigns = 0;
        protectFailures = 0;
        using (QueryResult reader = dbConnection.QueryReader(
          "SELECT X, Y, Account, Text FROM Signs WHERE WorldID = @0", Main.worldID)
        ) {
          while (reader.Read()) {
            int rawX = reader.Get<int>("X");
            int rawY = reader.Get<int>("Y");
            string rawAccount = reader.Get<string>("Account");
            string rawText = reader.Get<string>("Text");

            if (!TerrariaUtils.Tiles.IsValidCoord(rawX, rawY))
              continue;

            // TSPlayer.All 意味着该标识不得被保护。
            TSPlayer owner = TSPlayer.All;
            if (!string.IsNullOrEmpty(rawAccount)) {
              UserAccount tUser = TShock.UserAccounts.GetUserAccountByName(rawAccount);
              if (tUser != null) {
                owner = new TSPlayer(0);
                owner.Account.ID = tUser.ID;
                owner.Account.Name = tUser.Name;
                owner.Group = TShock.Groups.GetGroupByName(tUser.Group);
              } else {
                // 该标识的原始所有者已不存在，因此我们只为服务器玩家保护它。
                owner = TSPlayer.Server;
              }
            }

            DPoint signLocation = new DPoint(rawX, rawY);
            int signIndex = -1;
            for (int i = 0; i < Main.sign.Length; i++) {
              Sign sign = Main.sign[i];
              if (sign == null || sign.x != signLocation.X || sign.y != signLocation.Y)
                continue;

              signIndex = i;
              break;
            }

            if (signIndex == -1) {
              ITile signTile = TerrariaUtils.Tiles[signLocation];
              if (!signTile.active() || (signTile.type != TileID.Signs && signTile.type != TileID.Tombstones)) {
                this.PluginTrace.WriteLineWarning(
                  $"因为世界上不存在对应的标识，所以无法导入位于 {signLocation} 的标识数据。");
                continue;
              }

              for (int i = 0; i < Main.sign.Length; i++) {
                Sign sign = Main.sign[i];
                if (sign == null)
                  continue;

                Main.sign[i] = new Sign() {
                  x = rawX, 
                  y = rawY,
                  text = rawText
                };

                signIndex = i;
                break;
              }
            } else {
              Sign.TextSign(signIndex, rawText);
              importedSigns++;
            }

            if (owner != TSPlayer.All) {
              try {
                protectionManager.CreateProtection(owner, signLocation, true, false, false);
              } catch (Exception ex) {
                this.PluginTrace.WriteLineWarning("在 {0} 处创建保护失败：\n{1}", signLocation, ex);
                protectFailures++;
              }
            }
          }
        }
      } finally {
        if (dbConnection != null)
          dbConnection.Close();
      }
    }
    #endregion

    #region Seconomy
    #if SEconomy
    public Money Seconomy_GetBalance(string playerName) => this.Seconomy_Instance().GetPlayerBankAccount(playerName).Balance;

    public void Seconomy_TransferToWorld(string playerName, Money amount, string journalMsg, string transactionMsg) {
      SEconomy seconomy = this.Seconomy_Instance();

      IBankAccount account = seconomy.GetPlayerBankAccount(playerName);
      account.TransferTo(seconomy.WorldAccount, amount, BankAccountTransferOptions.AnnounceToSender, journalMsg, transactionMsg);
    }

    public string Seconomy_MoneyName() => this.Seconomy_Instance().Configuration.MoneyConfiguration.MoneyNamePlural;

    private SEconomy Seconomy_Instance() {
      SEconomy seconomy = SEconomyPlugin.Instance;
      if (seconomy == null) {
        var errMsg = "The SEconomy instance was null.";
        this.PluginTrace.WriteLineWarning(errMsg);
        throw new InvalidOperationException(errMsg);
      }

      return seconomy;
    }
    #endif // SEconomy
    #endregion
  }
}
