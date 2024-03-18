using System;
using System.Runtime.Serialization;
using System.Security;
using DPoint = System.Drawing.Point;

namespace Terraria.Plugins.CoderCow.Protector {
  [Serializable]
    public class NoChestDataException : Exception
    {
        public DPoint ChestLocation { get; private set; }


        public NoChestDataException(string message, DPoint chestLocation = default(DPoint)) : base(message, null)
        {
            this.ChestLocation = chestLocation;
        }

        public NoChestDataException(DPoint chestLocation) : base("没有与此宝箱注册的宝箱数据。")
        {
            this.ChestLocation = chestLocation;
        }

        public NoChestDataException(string message, Exception inner = null) : base(message, inner) { }

        public NoChestDataException() : base("没有与此宝箱注册的宝箱数据。") { }

        #region [Serializable Implementation]
        protected NoChestDataException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            this.ChestLocation = (DPoint)info.GetValue("NoChestDataException_ChestLocation", typeof(DPoint));
        }

        [SecurityCritical]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);

            info.AddValue("NoChestDataException_ChestLocation", this.ChestLocation, typeof(DPoint));
        }
        #endregion
    }
}