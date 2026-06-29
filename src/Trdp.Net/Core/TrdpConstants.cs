// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/iec61375-2-3.h, trdp/src/common/trdp_private.h.
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013-2021.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

namespace Trdp.Net.Core
{
    /// <summary>
    /// DE: Protokollkonstanten gemaess IEC 61375-2-3, portiert aus den Originalheadern.
    /// </summary>
    public static class TrdpConstants
    {
        /// <summary>UDP-Port fuer Process Data (iec61375-2-3.h: TRDP_PD_UDP_PORT).</summary>
        public const int PdUdpPort = 17224;

        /// <summary>UDP-Port fuer Message Data (TRDP_MD_UDP_PORT).</summary>
        public const int MdUdpPort = 17225;

        /// <summary>TCP-Port fuer Message Data (TRDP_MD_TCP_PORT).</summary>
        public const int MdTcpPort = 17225;

        /// <summary>Standard-Protokollversion (trdp_private.h: TRDP_PROTO_VER = 0x0100).</summary>
        public const ushort ProtocolVersion = 0x0100;

        /// <summary>Protokollversion mit ServiceId-Unterstuetzung (reserved-Feld), 0x0101.</summary>
        public const ushort ProtocolVersionServiceId = 0x0101;

        // ─── msgType-Werte (ASCII, 16-Bit) ───
        /// <summary>'Pd' (0x5064) — Process Data.</summary>
        public const ushort MsgTypePd = 0x5064;
        /// <summary>'Pr' (0x5072) — Process Data Request (Pull).</summary>
        public const ushort MsgTypePr = 0x5072;
        /// <summary>'Mn' (0x4D6E) — MD Notification (kein Reply).</summary>
        public const ushort MsgTypeMn = 0x4D6E;
        /// <summary>'Mr' (0x4D72) — MD Request (mit Reply).</summary>
        public const ushort MsgTypeMr = 0x4D72;
        /// <summary>'Mp' (0x4D70) — MD Reply (ohne Confirmation).</summary>
        public const ushort MsgTypeMp = 0x4D70;
        /// <summary>'Mq' (0x4D71) — MD Reply (mit Confirmation).</summary>
        public const ushort MsgTypeMq = 0x4D71;
        /// <summary>'Mc' (0x4D63) — MD Confirmation.</summary>
        public const ushort MsgTypeMc = 0x4D63;
        /// <summary>'Me' (0x4D65) — MD Error.</summary>
        public const ushort MsgTypeMe = 0x4D65;

        /// <summary>Groesse der FCS in Bytes (trdp_private.h: SIZE_OF_FCS).</summary>
        public const int SizeOfFcs = 4;

        /// <summary>Maximale PD-Datasetlaenge in Bytes (TRDP_MAX_PD_DATA_SIZE).</summary>
        public const int MaxPdDataSize = 1432;

        /// <summary>Minimale PD-Frame-Groesse (nur Header), TRDP_MIN_PD_HEADER_SIZE.</summary>
        public const int MinPdPacketSize = PdHeader.Size;

        /// <summary>Maximale PD-Frame-Groesse (Header + max. Daten), TRDP_MAX_PD_PACKET_SIZE.</summary>
        public const int MaxPdPacketSize = PdHeader.Size + MaxPdDataSize;

        /// <summary>
        /// DE: Bruttogroesse eines PD-Frames = Header + Daten, Daten auf 4 Byte gepaddet
        /// (trdp_packetSizePD). Header-only, wenn dataSize == 0.
        /// </summary>
        public static int PacketSizePd(int dataSize)
        {
            if (dataSize == 0) return PdHeader.Size;
            int padded = dataSize;
            if ((dataSize & 0x3) > 0) padded += 4 - dataSize % 4;
            return PdHeader.Size + padded;
        }

        /// <summary>Maximale MD-Datenlaenge in Bytes (TRDP_MAX_MD_DATA_SIZE).</summary>
        public const int MaxMdDataSize = 65388;

        /// <summary>Groesse der Session-ID (UUID) im MD-Header (TRDP_SESS_ID_SIZE).</summary>
        public const int SessionIdSize = 16;

        /// <summary>Unendlicher Reply-Timeout (TRDP_INFINITE_TIMEOUT).</summary>
        public const uint InfiniteTimeout = 0xFFFFFFFFu;

        /// <summary>Minimale MD-Frame-Groesse (nur Header).</summary>
        public const int MinMdPacketSize = MdHeader.Size;

        /// <summary>Maximale MD-Frame-Groesse (Header + max. Daten).</summary>
        public const int MaxMdPacketSize = MdHeader.Size + MaxMdDataSize;

        /// <summary>
        /// DE: Bruttogroesse eines MD-Frames = Header + Daten, Daten auf 4 Byte gepaddet
        /// (trdp_packetSizeMD). Header-only, wenn dataSize == 0.
        /// </summary>
        public static int PacketSizeMd(int dataSize)
        {
            if (dataSize == 0) return MdHeader.Size;
            int padded = dataSize;
            if ((dataSize & 0x3) > 0) padded += 4 - dataSize % 4;
            return MdHeader.Size + padded;
        }
    }
}
