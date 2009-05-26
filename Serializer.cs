using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Claymore.SharpMediaWiki
{
    public class Serializer
    {
        private readonly List<byte> _body;
        
        public Serializer()
        {
            _body = new List<byte>();
        }

        private void Put(ValueType type)
        {
            _body.Add((byte)type);
        }

        public void Put(int val)
        {
            Put(ValueType.Int32);
            _body.Add((byte)((val >> 24) & 0xFF));
            _body.Add((byte)((val >> 16) & 0xFF));
            _body.Add((byte)((val >> 8) & 0xFF));
            _body.Add((byte)(val & 0xFF));
        }

        public void Put(byte[] val)
        {
            Put(ValueType.ByteArray);
            Put(val.Length);
            _body.AddRange(val);
        }

        public void Put(string val)
        {
            Put(ValueType.String);
            for (int i = 0; i < val.Length; ++i)
            {
                _body.Add((byte)(val[i] & 0xFF));
                _body.Add((byte)((val[i] >> 8) & 0xFF));
            }
            _body.Add(0);
            _body.Add(0);
        }

        public byte[] ToArray()
        {
            return _body.ToArray();
        }
    }

    public enum ValueType
    {
        Int32 = 1,
        String = 2,
        ByteArray = 3
    };
}
