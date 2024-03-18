using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;

using Terraria.Plugins.Common;

namespace Terraria.Plugins.CoderCow.Protector {
  public class Configuration {
    public const string CurrentVersion = "1.4";

    public bool[] ManuallyProtectableTiles { get; set; }
    public bool[] AutoProtectedTiles { get; set; }
    public bool[] NotDeprotectableTiles { get; set; }
    public int MaxProtectionsPerPlayerPerWorld { get; set; }
    public int MaxBankChestsPerPlayer { get; set; }
    public bool AllowRefillChestContentChanges { get; set; }
    public bool EnableBedSpawnProtection { get; set; }
    public bool LoginRequiredForChestUsage { get; set; }
    public bool AutoShareRefillChests { get; set; }
    public bool AutoDeprotectEverythingOnDestruction { get; set; }
    public bool AllowChainedSharing { get; set; }
    public bool AllowChainedShareAltering { get; set; }
    public bool AllowWiringProtectedBlocks { get; set; }
    public bool NotifyAutoProtections { get; set; }
    public bool NotifyAutoDeprotections { get; set; }
    public float QuickStackNearbyRange { get; set; }
    public bool DungeonChestProtection { get; set; }
    public Dictionary<string,int> MaxBankChests { get; set; }
    public int MaxProtectorChests { get; set; }
    public int TradeChestPayment { get; set; }
    public Dictionary<string,HashSet<int>> TradeChestItemGroups { get; set; }

    public static Configuration Read(string filePath) {
      XmlReaderSettings configReaderSettings = new XmlReaderSettings {
        ValidationType = ValidationType.Schema,
        ValidationFlags = XmlSchemaValidationFlags.ProcessIdentityConstraints | XmlSchemaValidationFlags.ReportValidationWarnings
      };

      string configSchemaPath = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath) + ".xsd");
      configReaderSettings.Schemas.Add(null, configSchemaPath);
      
      XmlDocument document = new XmlDocument();
      using (XmlReader configReader = XmlReader.Create(filePath, configReaderSettings))
        document.Load(configReader);

      // 在使用架构进行验证之前，首先检查配置文件的版本是否与支持的版本匹配。
      XmlElement rootElement = document.DocumentElement;
      string fileVersionRaw;
      if (rootElement.HasAttribute("Version"))
        fileVersionRaw = rootElement.GetAttribute("Version");
      else
        fileVersionRaw = "1.0";
      
      if (fileVersionRaw != Configuration.CurrentVersion) {
        throw new FormatException(string.Format(
          "配置文件已过时或太新。预期版本为：｛0｝。文件版本为：｛1｝", 
          Configuration.CurrentVersion, fileVersionRaw
        ));
      }
      
      Configuration resultingConfig = new Configuration();
      Configuration.UpdateTileIdArrayByString(resultingConfig.ManuallyProtectableTiles, rootElement["手动保护图格"].InnerXml);
      Configuration.UpdateTileIdArrayByString(resultingConfig.AutoProtectedTiles, rootElement["自动保护图格"].InnerXml);
      Configuration.UpdateTileIdArrayByString(resultingConfig.NotDeprotectableTiles, rootElement["不可取消保护的图格"].InnerXml);
      resultingConfig.MaxProtectionsPerPlayerPerWorld = int.Parse(rootElement["每个世界每个玩家的最大保护"].InnerText);
      resultingConfig.MaxBankChestsPerPlayer = int.Parse(rootElement["每个玩家的最大钱箱数"].InnerXml);

      XmlElement subElement = rootElement["允许重新填充宝箱内容变化"];
      if (subElement == null)
        resultingConfig.AllowRefillChestContentChanges = true;
      else
        resultingConfig.AllowRefillChestContentChanges = BoolEx.ParseEx(subElement.InnerXml);

      resultingConfig.EnableBedSpawnProtection = BoolEx.ParseEx(rootElement["开启床出生点保护"].InnerXml);
      resultingConfig.LoginRequiredForChestUsage = BoolEx.ParseEx(rootElement["使用宝箱需要登录"].InnerXml);
      resultingConfig.AutoShareRefillChests = BoolEx.ParseEx(rootElement["自动共享可重新填充的宝箱"].InnerXml);
      resultingConfig.AllowChainedSharing = BoolEx.ParseEx(rootElement["允许链式共享"].InnerXml);
      resultingConfig.AllowChainedShareAltering = BoolEx.ParseEx(rootElement["允许链式共享更改"].InnerXml);
      resultingConfig.AllowWiringProtectedBlocks = BoolEx.ParseEx(rootElement["允许对受保护的方块进行接线"].InnerXml);
      resultingConfig.AutoDeprotectEverythingOnDestruction = BoolEx.ParseEx(rootElement["在破坏时自动取消对所有物品的保护"].InnerXml);
      resultingConfig.NotifyAutoProtections = BoolEx.ParseEx(rootElement["通知自动保护"].InnerXml);
      resultingConfig.NotifyAutoDeprotections = BoolEx.ParseEx(rootElement["通知自动取消保护"].InnerXml);
      resultingConfig.DungeonChestProtection = BoolEx.ParseEx(rootElement["地牢宝箱保护"].InnerXml);
      resultingConfig.QuickStackNearbyRange = float.Parse(rootElement["快速堆叠附近范围"].InnerXml);
      resultingConfig.MaxProtectorChests = int.Parse(rootElement["最大保护宝箱数"].InnerXml);
      resultingConfig.TradeChestPayment = int.Parse(rootElement["交易宝箱支付"].InnerXml);

      XmlElement maxBankChestsElement = rootElement["最大银行宝箱数"];
      resultingConfig.MaxBankChests = new Dictionary<string,int>();
      foreach (XmlNode node in maxBankChestsElement) {
        XmlElement limitElement = node as XmlElement;
        if (limitElement != null)
          resultingConfig.MaxBankChests.Add(limitElement.GetAttribute("组"), int.Parse(limitElement.InnerXml));
      }

      XmlElement tradeChestItemGroupsElement = rootElement["交易宝箱物品组"];
      resultingConfig.TradeChestItemGroups = new Dictionary<string,HashSet<int>>();
      foreach (XmlNode node in tradeChestItemGroupsElement) {
        XmlElement itemGroupElement = node as XmlElement;
        if (itemGroupElement != null) {
          string groupName = itemGroupElement.GetAttribute("用户组名").ToLowerInvariant();
          var itemIds = new HashSet<int>(itemGroupElement.InnerText.Split(',').Select(idRaw => int.Parse(idRaw)));
          resultingConfig.TradeChestItemGroups.Add(groupName, itemIds);
        }
      }

      return resultingConfig;
    }

    private static void UpdateTileIdArrayByString(bool[] idArray, string tileIds) {
      if (string.IsNullOrWhiteSpace(tileIds))
        return;

      foreach (string tileId in tileIds.Split(','))
        idArray[int.Parse(tileId)] = true;
    }

    public Configuration() {
      
      this.ManuallyProtectableTiles = new bool[TerrariaUtils.BlockType_Max + 50];
      this.AutoProtectedTiles = new bool[TerrariaUtils.BlockType_Max + 50];
      this.NotDeprotectableTiles = new bool[TerrariaUtils.BlockType_Max + 50];
      this.MaxBankChests = new Dictionary<string,int>();
    }
  }
}
