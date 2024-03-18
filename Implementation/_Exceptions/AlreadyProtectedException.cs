using System;
using System.Runtime.Serialization;

namespace Terraria.Plugins.CoderCow.Protector {
  [Serializable]
  public class AlreadyProtectedException: Exception {
    public AlreadyProtectedException(string message, Exception inner = null): base(message, inner) {}

        public AlreadyProtectedException() : base("该方块或对象已经受到保护。") { }

        protected AlreadyProtectedException(SerializationInfo info, StreamingContext context): base(info, context) {}
  }
}