### 关于
这个插件为 TShock 驱动的泰拉瑞亚服务器上的玩家提供了拥有某些物体或方块的所有权的能力，这样其他玩家就不能更改或使用它们。
受保护的钱箱内容不能被其他玩家查看或更改，受保护的开关不能被其他玩家击中，告示牌不能被编辑，床不能被使用，门不能被打开，甚至在受保护的黏土罐中的植物也不能在没有黏土罐所有权的情况下被收获。
如果一个玩家想要访问朋友宝箱中的物品，那么宝箱的拥有者应该与他们共享。Protector 提供了将保护直接共享给特定用户、TShock 用户组或与所有人共享的功能——这是保护的拥有者做的，不需要管理员操作。
保持控制，您可以定义哪些方块或物体可以受到保护，哪些在放置时会自动受到保护，可以解除保护，玩家总体上可以创建多少保护，以及可以共享什么或不可以共享。
此外，您可以利用 Protector 的特殊宝箱相关功能，例如强大的补充宝箱，允许定时补充其内容，限制玩家掠夺的次数超过一次或允许总共掠夺 X 次。另外，如果您的服务器经常更换世界，或者您只是想为您玩家提供可以使用包括其内容的易于运输的宝箱，那么您可以允许使用所谓的银行宝箱，它们存储的内容与世界无关——想象它们是服务器端的存钱罐。
通常，泰拉瑞亚世界最多只能有 1000 个宝箱。通过使用 Protector，您可以绕过这个限制，在配置文件中查看注释了解如何实现。
此插件的版本使用 [语义版本控制](http://semver.org/)。
### 如何安装
注意：此插件需要 [TerrariaAPI-Server](https://github.com/NyxStudios/TerrariaAPI-Server) 和 [TShock](https://github.com/NyxStudios/TShock) 才能工作。您不能在没有 TShock 的泰拉瑞亚服务器上使用它。
获取最新版本，并将 `.dll` 文件放入您服务器的 `ServerPlugins` 目录中。还将 `tshock/` 文件夹的内容放入您服务器的 `tshock/` 文件夹中。您可以通过编辑 `tshock/Protector/Config.xml` 文件来根据需要更改配置选项。
### 命令
* `/protect [-p]`
* `/deprotect [-p]`
* `/protectioninfo [-p]`
* `/share <玩家名称> [-p]`
* `/unshare <玩家名称> [-p]`
* `/sharegroup <组名称> [-p]`
* `/unsharegroup <组名称> [-p]`
* `/sharepublic [-p]`
* `/unsharepublic [-p]`
* `/bankchest <编号>`
* `/dumpbankchest`
* `/refillchest [时间] [+ot|-ot] [+ll 数量|-ll] [+al|-al] [-p]`
* `/refillchestmany <选择器> [时间] [+ot|-ot] [+ll 数量|-ll] [+al|-al] [-p]`
* `/lockchest [-p]`
* `/swapchest [-p]`
* `/tradechest <出售数量> <出售物品> <支付数量> <支付物品或组> [限制]`
* `/protector`
* `/protector commands`
* `/protector removeemptychests`
* `/protector summary`
* `/protector cleanup [-d]`
* `/protector removeall <区域 <区域>|用户 <用户>> [-d]`
* `/protector importinfinitechests`
* `/protector importinfinitesigns`
* `/protector reloadconfig|reloadcfg`
要获取有关命令的更多信息，请在上游戏中输入 `/<命令> help`。
### 权限
* **prot.manualprotect**
  可以手动创建保护（自动保护不需要）。
* **prot.manualdeprotect**
  可以手动移除拥有的保护（自动解除保护不需要）。
* **prot.chestshare**
  可以共享宝箱。
### 权限
* **prot.switchshare**
  可以共享开关/拉杆/压力板/计时器/音乐盒。
* **prot.othershare**
  可以共享告示牌、墓碑和床。
* **prot.sharewithgroups**
  可以与 TShock 组共享保护。
* **prot.setbankchest**
  可以设置银行宝箱。
* **prot.bankchestshare**
  可以共享银行宝箱。
* **prot.nobankchestlimits**
  可以设置无限制的银行宝箱。
* **prot.dumpbankchest**
  可以倾倒银行宝箱内容（警告：会复制银行宝箱的物品）。
* **prot.nolimits**
  可以创建无限数量的保护。
* **prot.viewall**
  可以查看所有保护的详细信息。通常只有所有者或共享玩家才能查看保护的扩展信息。
* **prot.useeverything**
  可以使用和操作任何宝箱、告示牌、开关、拉杆、压力板等（不包括移除它们）。
* **prot.protectionmaster**
  可以修改或移除任何保护，也可以更改任何补充宝箱（如果也提供了 "prot_setrefillchest"）。
* **prot.setrefillchest**
  可以设置一个自动补充其内容的宝箱。
* **prot.settradechest**
  可以设置一个与其他玩家交易的交易宝箱。
* **prot.freetradechests**
  设置交易宝箱时不需要支付 SEconomy 货币。
* **prot.utility**
  可以显示有关世界中所有宝箱、告示牌和保护的摘要，可以锁定宝箱，可以将所有地牢宝箱、天空岛屿宝箱、海洋宝箱、地狱阴影宝箱转换为补充宝箱（也需要 "prot_setrefillchest"），可以移除世界中所有空置的非保护宝箱，可以交换宝箱数据存储。
* **prot.cfg**
  可以导入 Infinite Chests 的数据或 Infinite Signs 的数据库文件，可以重新加载 Protector 的配置文件。
* **prot.regionsonlyprotections**
  只能在具有建筑权限的区域创建保护。
### 数据导入和兼容性
此插件可以导入 Infinite Chests 和 Infinite Signs 插件的钱箱和告示牌数据。在使用此功能之前，确保创建世界备份，因为这些更改否则无法撤销。
注意，导入功能并没有很好地测试，特别是与插件的较新版本。
它也仅限于常规补充宝箱。银行宝箱不会被导入。另外，区域宝箱保护和宝箱密码也不被 Protector 支持。
如果您的无限宝箱数据库中有超过 999 个宝箱，所有超过的宝箱**应该**成为 Protector 宝箱。
不要尝试在 Protector 中同时使用任何宝箱功能和 Infinite Chests，因为这会导致世界文件和宝箱数据库中的物品数据混合。
### 致谢
图标由 [freepik](http://www.freepik.com/) 制作。

