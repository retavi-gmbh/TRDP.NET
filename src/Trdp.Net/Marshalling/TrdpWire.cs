// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tau_marshall.c (Byte-Operationen
// der Marshalling-Schleifen). Die Wire-Seite ist GEPACKT und BIG-ENDIAN.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Buffers.Binary;

namespace Trdp.Net.Marshalling
{
    /// <summary>
    /// DE: Schreibender Cursor fuer das gepackte Big-Endian-Wire-Format von TRDP-Datasets.
    /// </summary>
    public ref struct TrdpWireWriter
    {
        private readonly Span<byte> _buf;

        public int Position { get; private set; }

        public TrdpWireWriter(Span<byte> buffer)
        {
            _buf = buffer;
            Position = 0;
        }

        public void PutUInt8(byte v) => _buf[Position++] = v;
        public void PutInt8(sbyte v) => PutUInt8((byte)v);
        public void PutBool8(bool v) => PutUInt8(v ? (byte)1 : (byte)0);

        public void PutUInt16(ushort v) { BinaryPrimitives.WriteUInt16BigEndian(_buf.Slice(Position, 2), v); Position += 2; }
        public void PutInt16(short v)   { BinaryPrimitives.WriteInt16BigEndian(_buf.Slice(Position, 2), v); Position += 2; }
        public void PutChar16(char v)   => PutUInt16(v);

        public void PutUInt32(uint v)   { BinaryPrimitives.WriteUInt32BigEndian(_buf.Slice(Position, 4), v); Position += 4; }
        public void PutInt32(int v)     { BinaryPrimitives.WriteInt32BigEndian(_buf.Slice(Position, 4), v); Position += 4; }
        public void PutReal32(float v)  { BinaryPrimitives.WriteSingleBigEndian(_buf.Slice(Position, 4), v); Position += 4; }

        public void PutUInt64(ulong v)  { BinaryPrimitives.WriteUInt64BigEndian(_buf.Slice(Position, 8), v); Position += 8; }
        public void PutInt64(long v)    { BinaryPrimitives.WriteInt64BigEndian(_buf.Slice(Position, 8), v); Position += 8; }
        public void PutReal64(double v) { BinaryPrimitives.WriteDoubleBigEndian(_buf.Slice(Position, 8), v); Position += 8; }

        public void PutTimeDate32(uint seconds) => PutUInt32(seconds);

        public void PutTimeDate48(TrdpTimeDate48 v) { PutUInt32(v.Seconds); PutUInt16(v.Ticks); }

        public void PutTimeDate64(TrdpTimeDate64 v) { PutUInt32(v.Seconds); PutUInt32(v.Microseconds); }
    }

    /// <summary>
    /// DE: Lesender Cursor fuer das gepackte Big-Endian-Wire-Format von TRDP-Datasets.
    /// </summary>
    public ref struct TrdpWireReader
    {
        private readonly ReadOnlySpan<byte> _buf;

        public int Position { get; private set; }

        public TrdpWireReader(ReadOnlySpan<byte> buffer)
        {
            _buf = buffer;
            Position = 0;
        }

        public int Remaining => _buf.Length - Position;

        public byte GetUInt8() => _buf[Position++];
        public sbyte GetInt8() => (sbyte)_buf[Position++];
        public bool GetBool8() => _buf[Position++] != 0;

        public ushort GetUInt16() { var v = BinaryPrimitives.ReadUInt16BigEndian(_buf.Slice(Position, 2)); Position += 2; return v; }
        public short GetInt16()   { var v = BinaryPrimitives.ReadInt16BigEndian(_buf.Slice(Position, 2)); Position += 2; return v; }
        public char GetChar16()   => (char)GetUInt16();

        public uint GetUInt32()   { var v = BinaryPrimitives.ReadUInt32BigEndian(_buf.Slice(Position, 4)); Position += 4; return v; }
        public int GetInt32()     { var v = BinaryPrimitives.ReadInt32BigEndian(_buf.Slice(Position, 4)); Position += 4; return v; }
        public float GetReal32()  { var v = BinaryPrimitives.ReadSingleBigEndian(_buf.Slice(Position, 4)); Position += 4; return v; }

        public ulong GetUInt64()  { var v = BinaryPrimitives.ReadUInt64BigEndian(_buf.Slice(Position, 8)); Position += 8; return v; }
        public long GetInt64()    { var v = BinaryPrimitives.ReadInt64BigEndian(_buf.Slice(Position, 8)); Position += 8; return v; }
        public double GetReal64() { var v = BinaryPrimitives.ReadDoubleBigEndian(_buf.Slice(Position, 8)); Position += 8; return v; }

        public uint GetTimeDate32() => GetUInt32();

        public TrdpTimeDate48 GetTimeDate48() { uint s = GetUInt32(); ushort t = GetUInt16(); return new TrdpTimeDate48(s, t); }

        public TrdpTimeDate64 GetTimeDate64() { uint s = GetUInt32(); uint u = GetUInt32(); return new TrdpTimeDate64(s, u); }
    }
}
