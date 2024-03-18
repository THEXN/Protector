using System;
using System.Runtime.Serialization;

namespace Terraria.Plugins.CoderCow.Protector {
  [Serializable]
  public class ChestTypeAlreadyDefinedException: Exception {
    public ChestTypeAlreadyDefinedException(string message, Exception inner = null) : base(message, inner) {}

        public ChestTypeAlreadyDefinedException() : base("这个宝箱已经定义为此类型的宝箱。") { }

        protected ChestTypeAlreadyDefinedException(SerializationInfo info, StreamingContext context) : base(info, context) {}
  }
}