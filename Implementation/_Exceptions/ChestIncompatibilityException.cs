using System;
using System.Runtime.Serialization;

namespace Terraria.Plugins.CoderCow.Protector {
  [Serializable]
  public class ChestIncompatibilityException: Exception {
    public ChestIncompatibilityException(string message, Exception inner = null): base(message, inner) {}

        public ChestIncompatibilityException() : base("给定的宝箱要么是一个补充宝箱要么是一个银行宝箱，这是无效的。") { }

        protected ChestIncompatibilityException(SerializationInfo info, StreamingContext context): base(info, context) {}
  }
}