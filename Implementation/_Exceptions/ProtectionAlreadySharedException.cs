using System;
using System.Runtime.Serialization;

namespace Terraria.Plugins.CoderCow.Protector {
  [Serializable]
  public class ProtectionAlreadySharedException: Exception {
    public ProtectionAlreadySharedException(string message, Exception inner = null): base(message, inner) {}

        public ProtectionAlreadySharedException() : base("保护已经与一个用户或组共享。") { }

        protected ProtectionAlreadySharedException(SerializationInfo info, StreamingContext context): base(info, context) {}
  }
}