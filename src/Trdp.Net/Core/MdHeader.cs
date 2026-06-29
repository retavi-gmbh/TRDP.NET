// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/trdp_private.h (MD_HEADER_T)
// und trdp/src/common/trdp_mdcom.c (FCS-Berechnung/-Pruefung).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013-2021.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Buffers.Binary;
using Trdp.Net.Vos;

namespace Trdp.Net.Core
{
    /// <summary>
    /// DE: MD-PDU-Header — 1:1-Port von <c>MD_HEADER_T</c> (116 Bytes, network byte order).
    /// </summary>
    public struct MdHeader
    {
        /// <summary>DE: Laenge von User-URI-Feldern (TRDP_USR_URI_SIZE).</summary>
        public const int UserUriSize = 32;

        /// <summary>DE: Headergroesse in Bytes (sizeof(MD_HEADER_T)).</summary>
        public const int Size = 116;

        public uint SequenceCounter;      // [  0]
        public ushort ProtocolVersion;    // [  4]
        public ushort MsgType;            // [  6]
        public uint ComId;                // [  8]
        public uint EtbTopoCnt;           // [ 12]
        public uint OpTrnTopoCnt;         // [ 16]
        public uint DatasetLength;        // [ 20]
        public int ReplyStatus;           // [ 24] (0 = OK)
        public byte[] SessionId;          // [ 28] 16 Bytes UUID
        public uint ReplyTimeout;         // [ 44] in us
        public byte[] SourceUri;          // [ 48] 32 Bytes
        public byte[] DestinationUri;     // [ 80] 32 Bytes
        public uint FrameCheckSum;        // [112]

        /// <summary>DE: Liest einen MD-Header aus einem Puffer (big-endian).</summary>
        public static MdHeader Parse(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException($"MD-Header benoetigt {Size} Bytes, erhalten {buffer.Length}.", nameof(buffer));

            var h = new MdHeader
            {
                SequenceCounter = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(0, 4)),
                ProtocolVersion = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(4, 2)),
                MsgType         = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(6, 2)),
                ComId           = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(8, 4)),
                EtbTopoCnt      = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(12, 4)),
                OpTrnTopoCnt    = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(16, 4)),
                DatasetLength   = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(20, 4)),
                ReplyStatus     = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(24, 4)),
                SessionId       = buffer.Slice(28, 16).ToArray(),
                ReplyTimeout    = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(44, 4)),
                SourceUri       = buffer.Slice(48, UserUriSize).ToArray(),
                DestinationUri  = buffer.Slice(80, UserUriSize).ToArray(),
                // DE: FCS ist als EINZIGES Feld little-endian (MAKE_LE im Original).
                FrameCheckSum   = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(112, 4)),
            };
            return h;
        }

        /// <summary>DE: Schreibt den Header (big-endian) in <paramref name="buffer"/>.</summary>
        public readonly void Write(Span<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException($"MD-Header benoetigt {Size} Bytes, erhalten {buffer.Length}.", nameof(buffer));

            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(0, 4), SequenceCounter);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(4, 2), ProtocolVersion);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(6, 2), MsgType);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(8, 4), ComId);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(12, 4), EtbTopoCnt);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(16, 4), OpTrnTopoCnt);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(20, 4), DatasetLength);
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(24, 4), ReplyStatus);
            WriteFixed(buffer.Slice(28, 16), SessionId);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(44, 4), ReplyTimeout);
            WriteFixed(buffer.Slice(48, UserUriSize), SourceUri);
            WriteFixed(buffer.Slice(80, UserUriSize), DestinationUri);
            // DE: FCS little-endian (MAKE_LE).
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(112, 4), FrameCheckSum);
        }

        /// <summary>
        /// DE: FCS ueber die ersten 112 Bytes (sizeof - SIZE_OF_FCS), setzen + schreiben.
        /// Entspricht trdp_mdcom.c:976.
        /// </summary>
        public void UpdateFrameCheckSum(Span<byte> buffer)
        {
            FrameCheckSum = VosCrc32.Compute(buffer.Slice(0, Size - TrdpConstants.SizeOfFcs));
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(112, 4), FrameCheckSum);
        }

        /// <summary>DE: Prueft die FCS eines empfangenen Headers (true = gueltig).</summary>
        public static bool VerifyFrameCheckSum(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < Size) return false;
            uint expected = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(112, 4));
            uint actual = VosCrc32.Compute(buffer.Slice(0, Size - TrdpConstants.SizeOfFcs));
            return expected == actual;
        }

        // DE: Kopiert ein (evtl. null/kuerzeres) Byte-Array fixer Laenge, Rest wird 0.
        private static void WriteFixed(Span<byte> dst, byte[]? src)
        {
            dst.Clear();
            if (src != null && src.Length > 0)
            {
                src.AsSpan(0, Math.Min(src.Length, dst.Length)).CopyTo(dst);
            }
        }
    }
}
