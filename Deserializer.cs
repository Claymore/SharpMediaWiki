using System;
using System.Collections.Generic;
using System.Text;

namespace Claymore.SharpMediaWiki
{
    public class Deserializer
    {
        private readonly Queue<byte> _body;

        public Deserializer(IEnumerable<byte> body)
        {
            _body = new Queue<byte>();
            foreach (byte b in body)
            {
                _body.Enqueue(b);
            }
        }

        private ValueType GetValueType()
        {
            try
            {
                return (ValueType)_body.Dequeue();
            }
            catch (InvalidOperationException e)
            {
                throw new SerializationException(e.Message, e);
            }
        }

        public int GetInt()
        {
            if (GetValueType() != ValueType.Int32)
            {
                throw new SerializationException("Invalid format: not an integer value");
            }
            uint result = 0;
            result |= (uint)_body.Dequeue() << 24;
            result |= (uint)_body.Dequeue() << 16;
            result |= (uint)_body.Dequeue() << 8;
            result |= (uint)_body.Dequeue();
            return (int)result;
        }

        public byte[] GetVector()
        {
            if (GetValueType() != ValueType.ByteArray)
            {
                throw new SerializationException("Invalid format: not a vector");
            }
            int length = GetInt();
            try
            {
                byte[] buffer = new byte[length];
                for (int i = 0; i < length; ++i)
                {
                    buffer[i] = _body.Dequeue();
                }
                return buffer;
            }
            catch (InvalidOperationException e)
            {
                throw new SerializationException(e.Message, e);
            }
        }

        public string GetString()
        {
            if (GetValueType() != ValueType.String)
            {
                throw new SerializationException("Invalid format: not a string");
            }
            StringBuilder result = new StringBuilder();
            char ch;
            while ((ch = FastGetChar()) != (char)0)
            {
                result.Append(ch);
            }
            return result.ToString();
        }

        private char FastGetChar()
        {
            try
            {
                char result = (char)0;
                result |= (char)_body.Dequeue();
                result |= (char)(_body.Dequeue() << 8);
                return result;
            }
            catch (InvalidOperationException e)
            {
                throw new SerializationException(e.Message, e);
            }
        }
    }
}
