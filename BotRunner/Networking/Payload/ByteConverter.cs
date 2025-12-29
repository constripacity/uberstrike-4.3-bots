using System;
using System.Collections.Generic;
using System.Text;

namespace BotRunner.Networking.Payload
{
    /// <summary>
    /// Little-endian serialization helpers that mimic the layout used by Photon RPC payloads
    /// in the original Unity client.
    /// </summary>
    public static class ByteConverter
    {
        public static IEnumerable<byte> GetBytes(short value) => BitConverter.GetBytes(value);

        public static IEnumerable<byte> GetBytes(int value) => BitConverter.GetBytes(value);

        public static IEnumerable<byte> GetBytes(string value)
        {
            var strBytes = Encoding.UTF8.GetBytes(value);
            var lengthBytes = BitConverter.GetBytes((short)strBytes.Length);
            var buffer = new byte[lengthBytes.Length + strBytes.Length];
            Buffer.BlockCopy(lengthBytes, 0, buffer, 0, lengthBytes.Length);
            Buffer.BlockCopy(strBytes, 0, buffer, lengthBytes.Length, strBytes.Length);
            return buffer;
        }

        public static IEnumerable<byte> GetBytes(ShortVector3 vector)
        {
            var buffer = new byte[sizeof(short) * 3];
            Buffer.BlockCopy(BitConverter.GetBytes(vector.X), 0, buffer, 0, sizeof(short));
            Buffer.BlockCopy(BitConverter.GetBytes(vector.Y), 0, buffer, sizeof(short), sizeof(short));
            Buffer.BlockCopy(BitConverter.GetBytes(vector.Z), 0, buffer, sizeof(short) * 2, sizeof(short));
            return buffer;
        }
    }
}
