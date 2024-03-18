using System;
using System.Runtime.Serialization;

namespace Terraria.Plugins.CoderCow.Protector {
  [Serializable]
  public class ProtectionNotSharedException: Exception {
    public ProtectionNotSharedException(string message, Exception inner = null): base(message, inner) {}

        public ProtectionNotSharedException() : base("保护没有与给定的用户或组共享。") { }

        protected ProtectionNotSharedException(SerializationInfo info, StreamingContext context): base(info, context) {}
  }
}