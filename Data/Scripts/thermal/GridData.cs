using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;

namespace ThermalOverhaul
{
    [ProtoContract]
    public class GridData
    {
        [ProtoMember(1)]
        public long[] position;

        [ProtoMember(2)]
        public float[] temperature;
    }
}
