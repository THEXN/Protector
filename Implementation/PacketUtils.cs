using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.Plugins.Common;
using TShockAPI;

namespace Terraria.Plugins.CoderCow.Protector {
  public static class PacketUtils {
    public static void SendChestItem(TSPlayer player, int chestIndex, IList<ItemData> items) {
      const int TerrariaPacketHeaderSize = 3;
      const int ChestItemPacketSizeNoHeader = 8;
      const short PacketSize = TerrariaPacketHeaderSize + ChestItemPacketSizeNoHeader;

      using (MemoryStream packetData = new MemoryStream(new byte[PacketSize])) {
        BinaryWriter writer = new BinaryWriter(packetData);

        // 标题
        writer.Write(PacketSize); // 数据包大小
        writer.Write((byte)PacketTypes.ChestItem);

        writer.Write((short)chestIndex);

        // 为每个项目重新写入项目数据并发送数据包
        for (int i = 0; i < items.Count; i++) {
          ItemData item = items[i];

          writer.Write((byte)i);
          writer.Write((short)item.StackSize);
          writer.Write((byte)item.Prefix);
          writer.Write((short)item.Type);

          player.SendRawData(packetData.ToArray());

          // 倒带以写入另一个项目的项目数据
          packetData.Position -= ChestItemPacketSizeNoHeader;
        }
      }
    }

    public static void SendChestName(TSPlayer player, int chestIndex, string name) {
      
    }
  }
}
