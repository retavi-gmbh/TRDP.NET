// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/trdp_private.h (PD_HEADER_T)
// und trdp/src/common/trdp_pdcom.c (FCS-Berechnung/-Pruefung).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013-2021.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Buffers.Binary;
using Trdp.Net.Vos;

namespace Trdp.Net.Core
{
    /// <summary>
    /// DE: PD-PDU-Header — 1:1-Port von <c>PD_HEADER_T</c> (40 Bytes, network byte order).
    /// Reihenfolge/Groessen exakt wie im Original (GNU_PACKED).
    /// </summary>
    public struct PdHeader
    {
        /// <summary>DE: Headergroesse in Bytes (sizeof(PD_HEADER_T)).</summary>
        public const int Size = 40;

        public uint SequenceCounter;   // [ 0]
        public ushort ProtocolVersion; // [ 4]
        public ushort MsgType;         // [ 6]
        public uint ComId;             // [ 8]
        public uint EtbTopoCnt;        // [12]
        public uint OpTrnTopoCnt;      // [16]
        public uint DatasetLength;     // [20]
        public uint Reserved;          // [24] (ServiceID/InstanceID)
        public uint ReplyComId;        // [28]
        public uint ReplyIpAddress;    // [32]
        public uint FrameCheckSum;     // [36]

        /// <summary>DE: Liest einen PD-Header aus einem Puffer (big-endian).</summary>
        public static PdHeader Parse(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException($"PD-Header benoetigt {Size} Bytes, erhalten {buffer.Length}.", nameof(buffer));

            return new PdHeader
            {
                SequenceCounter = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(0, 4)),
                ProtocolVersion = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(4, 2)),
                MsgType         = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(6, 2)),
                ComId           = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(8, 4)),
                EtbTopoCnt      = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(12, 4)),
                OpTrnTopoCnt    = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(16, 4)),
                DatasetLength   = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(20, 4)),
                Reserved        = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(24, 4)),
                ReplyComId      = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(28, 4)),
                ReplyIpAddress  = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(32, 4)),
                // DE: FCS ist als EINZIGES Feld little-endian (MAKE_LE im Original).
                FrameCheckSum   = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(36, 4)),
            };
        }

        /// <summary>DE: Schreibt den Header (big-endian) in <paramref name="buffer"/>.</summary>
        public readonly void Write(Span<byte> buffer)
        {
            if (buffer.Length < Size)
                throw new ArgumentException($"PD-Header benoetigt {Size} Bytes, erhalten {buffer.Length}.", nameof(buffer));

            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(0, 4), SequenceCounter);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(4, 2), ProtocolVersion);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(6, 2), MsgType);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(8, 4), ComId);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(12, 4), EtbTopoCnt);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(16, 4), OpTrnTopoCnt);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(20, 4), DatasetLength);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(24, 4), Reserved);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(28, 4), ReplyComId);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(32, 4), ReplyIpAddress);
            // DE: FCS little-endian (MAKE_LE).
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(36, 4), FrameCheckSum);
        }

        /// <summary>
        /// DE: Berechnet die FCS ueber die ersten 36 Header-Bytes (sizeof - SIZE_OF_FCS),
        /// setzt <see cref="FrameCheckSum"/> und schreibt sie in den Puffer.
        /// Entspricht trdp_pdcom.c:1392 (vos_crc32(INITFCS, &frameHead, sizeof(PD_HEADER_T)-SIZE_OF_FCS)).
        /// </summary>
        public void UpdateFrameCheckSum(Span<byte> buffer)
        {
            FrameCheckSum = VosCrc32.Compute(buffer.Slice(0, Size - TrdpConstants.SizeOfFcs));
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(36, 4), FrameCheckSum);
        }

        /// <summary>DE: Prueft die FCS eines empfangenen Headers (true = gueltig).</summary>
        public static bool VerifyFrameCheckSum(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < Size) return false;
            uint expected = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(36, 4));
            uint actual = VosCrc32.Compute(buffer.Slice(0, Size - TrdpConstants.SizeOfFcs));
            return expected == actual;
        }
    }
}
