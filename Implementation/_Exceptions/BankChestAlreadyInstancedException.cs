using System;
using System.Runtime.Serialization;

namespace Terraria.Plugins.CoderCow.Protector {
  [Serializable]
  public class BankChestAlreadyInstancedException : Exception {
    public BankChestAlreadyInstancedException(string message, Exception inner = null) : base(message, inner) {}

        public BankChestAlreadyInstancedException() : base("这个世界中已经有一个银行宝箱实例。") { }

        protected BankChestAlreadyInstancedException(SerializationInfo info, StreamingContext context) : base(info, context) {}
  }
}