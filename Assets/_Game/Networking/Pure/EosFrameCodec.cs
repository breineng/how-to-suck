using System;
using System.Collections.Generic;

namespace HowToSuck.Networking
{
    // EOS packets are at most 1170 bytes. NGO's reliable scene snapshots can be much larger.
    public enum EosFrameResult { Incomplete, Complete, Invalid }
    public sealed class EosFrameCodec
    {
        public const int PacketBytes = 1170, HeaderBytes = 16, ChunkBytes = PacketBytes - HeaderBytes;
        public const int MaximumFrameBytes = 512 * 1024;
        private const int MaximumPendingBytes = 2 * 1024 * 1024, MaximumPendingFrames = 16;
        private sealed class Assembly
        {
            public byte[] Data;
            public bool[] Parts;
            public int Received;
            public double Started;
            public byte Delivery;
        }
        private readonly Dictionary<ulong, Assembly> pending = new Dictionary<ulong, Assembly>();
        private readonly List<ulong> expired = new List<ulong>();
        private int pendingBytes;
        private uint lastSequenced, lastReliable;
        public int PendingBytes => pendingBytes;
        public static int FragmentCount(int bytes) => Math.Max(1, (bytes + ChunkBytes - 1) / ChunkBytes);
        public static int WriteFragment(ArraySegment<byte> data, byte delivery, uint message, int part, byte[] output)
        {
            if (data.Array == null || data.Count > MaximumFrameBytes || delivery > 2 || message == 0 ||
                part < 0 || part >= FragmentCount(data.Count) || output == null || output.Length < PacketBytes)
                throw new ArgumentException("Invalid EOS frame");
            output[0] = (byte)'H'; output[1] = (byte)'E'; output[2] = 1; output[3] = delivery;
            Write32(output, 4, message); Write32(output, 8, (uint)data.Count);
            Write16(output, 12, part); Write16(output, 14, FragmentCount(data.Count));
            int length = Math.Min(ChunkBytes, data.Count - part * ChunkBytes);
            Buffer.BlockCopy(data.Array, data.Offset + part * ChunkBytes, output, HeaderBytes, length);
            return HeaderBytes + length;
        }
        public EosFrameResult Receive(byte[] packet, int length, byte channel, double now, out byte[] frame)
        {
            frame = null;
            if (packet == null || length < HeaderBytes || length > PacketBytes || length > packet.Length ||
                packet[0] != 'H' || packet[1] != 'E' || packet[2] != 1 || packet[3] > 2 || channel != packet[3] + 1)
                return EosFrameResult.Invalid;
            byte delivery = packet[3]; uint message = Read32(packet, 4), total = Read32(packet, 8);
            int part = Read16(packet, 12), count = Read16(packet, 14);
            if (message == 0 || total > MaximumFrameBytes || count != FragmentCount((int)total) || part >= count ||
                length - HeaderBytes != Math.Min(ChunkBytes, (int)total - part * ChunkBytes)) return EosFrameResult.Invalid;
            if (delivery == 1 && lastSequenced != 0 && unchecked((int)(message - lastSequenced)) <= 0) return EosFrameResult.Incomplete;
            if (delivery == 2 && lastReliable != 0 && unchecked((int)(message - lastReliable)) <= 0) return EosFrameResult.Incomplete;
            ulong key = ((ulong)delivery << 32) | message;
            if (!pending.TryGetValue(key, out var assembly))
            {
                if (pending.Count >= MaximumPendingFrames || pendingBytes + total > MaximumPendingBytes) return EosFrameResult.Invalid;
                assembly = new Assembly { Data = new byte[total], Parts = new bool[count], Started = now, Delivery = delivery };
                pending.Add(key, assembly); pendingBytes += (int)total;
            }
            if (assembly.Data.Length != total || assembly.Parts.Length != count) return EosFrameResult.Invalid;
            if (assembly.Parts[part]) return EosFrameResult.Incomplete;
            Buffer.BlockCopy(packet, HeaderBytes, assembly.Data, part * ChunkBytes, length - HeaderBytes);
            assembly.Parts[part] = true; assembly.Received++;
            if (assembly.Received != count) return EosFrameResult.Incomplete;
            pending.Remove(key); pendingBytes -= assembly.Data.Length;
            if (delivery == 1) lastSequenced = message;
            if (delivery == 2)
            {
                uint expected = lastReliable == uint.MaxValue ? 1 : lastReliable + 1;
                if (message != expected) return EosFrameResult.Invalid; // Never silently skip reliable data.
                lastReliable = message;
            }
            frame = assembly.Data; return EosFrameResult.Complete;
        }
        public bool Expire(double now)
        {
            bool reliableExpired = false; expired.Clear();
            foreach (var pair in pending)
                if (now - pair.Value.Started >= (pair.Value.Delivery == 2 ? 15 : 2))
                { expired.Add(pair.Key); reliableExpired |= pair.Value.Delivery == 2; }
            foreach (var key in expired) { pendingBytes -= pending[key].Data.Length; pending.Remove(key); }
            return reliableExpired;
        }
        private static uint Read32(byte[] bytes, int offset) => (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        private static int Read16(byte[] bytes, int offset) => bytes[offset] | bytes[offset + 1] << 8;
        private static void Write32(byte[] bytes, int offset, uint value)
        { for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (8 * i)); }
        private static void Write16(byte[] bytes, int offset, int value)
        { bytes[offset] = (byte)value; bytes[offset + 1] = (byte)(value >> 8); }
    }
}
