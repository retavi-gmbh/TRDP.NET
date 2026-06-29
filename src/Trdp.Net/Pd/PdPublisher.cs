// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/trdp_pdcom.c (trdp_pdPut, trdp_pdUpdate)
// und tlp_if.c (tlp_publish/tlp_put). Sende-Element der PD-Kommunikation.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using Trdp.Net.Core;

namespace Trdp.Net.Pd
{
    /// <summary>
    /// DE: Ein PD-Publisher (zyklisch zu sendendes Process-Data-Telegramm fuer eine ComId).
    /// Entspricht einem PD_ELE_T der Sendewarteschlange. Erzeugen ueber
    /// <see cref="TrdpPdSession.Publish"/>.
    /// </summary>
    public sealed class PdPublisher
    {
        /// <summary>DE: Eindeutige ComId des Telegramms.</summary>
        public uint ComId { get; }

        /// <summary>DE: Ziel-IP (Unicast oder Multicast-Gruppe).</summary>
        public IPAddress DestIp { get; }

        /// <summary>DE: Ziel-Port (Standard 17224).</summary>
        public int DestPort { get; }

        /// <summary>DE: Sendeintervall in ms; 0 = nur manuell/on-demand.</summary>
        public int CycleTimeMs { get; }

        /// <summary>DE: ETB-Topozaehler (0 = consist-local).</summary>
        public uint EtbTopoCnt { get; set; }

        /// <summary>DE: Operational-Train-Topozaehler (0 = ignoriert).</summary>
        public uint OpTrnTopoCnt { get; set; }

        /// <summary>DE: Zuletzt gesendeter Sequenzzaehler.</summary>
        public uint SequenceCounter { get; private set; }

        /// <summary>DE: True, sobald gueltige Daten gesetzt wurden (TRDP_INVALID_DATA geloescht).</summary>
        public bool DataValid { get; private set; }

        // DE: Aktuelles Dataset (net data). Laenge 0 erlaubt (Header-only Telegramm).
        private byte[] _data;

        // DE: Naechster faelliger Sendezeitpunkt (ms, Session-Uhr).
        internal long NextSendMs;

        internal PdPublisher(uint comId, IPAddress destIp, int destPort, int cycleTimeMs, byte[] initialData)
        {
            ComId = comId;
            DestIp = destIp;
            DestPort = destPort;
            CycleTimeMs = cycleTimeMs;
            _data = initialData ?? Array.Empty<byte>();
            DataValid = _data.Length == 0 ? false : true;
        }

        /// <summary>DE: Aktuelle Nettodatenlaenge.</summary>
        public int DataSize => _data.Length;

        /// <summary>DE: Bruttogroesse des Frames (Header + Daten).</summary>
        public int GrossSize => TrdpConstants.PacketSizePd(_data.Length);

        /// <summary>
        /// DE: Setzt das zu sendende Dataset (tlp_put). Markiert die Daten als gueltig,
        /// sodass sie beim naechsten Zyklus gesendet werden.
        /// </summary>
        public void SetData(ReadOnlySpan<byte> data)
        {
            if (data.Length > TrdpConstants.MaxPdDataSize)
                throw new ArgumentException($"PD-Dataset zu gross ({data.Length} > {TrdpConstants.MaxPdDataSize}).", nameof(data));

            if (_data.Length != data.Length)
            {
                _data = data.ToArray();
            }
            else
            {
                data.CopyTo(_data);
            }
            DataValid = true;
        }

        /// <summary>
        /// DE: Baut den vollstaendigen PD-Frame in <paramref name="dest"/> (mind. <see cref="GrossSize"/>
        /// Bytes), inkrementiert den Sequenzzaehler und berechnet die FCS (trdp_pdUpdate).
        /// Liefert die Frame-Laenge.
        /// </summary>
        internal int BuildFrame(Span<byte> dest)
        {
            int gross = GrossSize;
            if (dest.Length < gross)
                throw new ArgumentException($"Puffer zu klein: {dest.Length} < {gross}.", nameof(dest));

            // DE: Sequenzzaehler hochzaehlen (erstes Telegramm => 1), wie trdp_pdUpdate.
            SequenceCounter++;

            var header = new PdHeader
            {
                SequenceCounter = SequenceCounter,
                ProtocolVersion = TrdpConstants.ProtocolVersion,
                MsgType = TrdpConstants.MsgTypePd,
                ComId = ComId,
                EtbTopoCnt = EtbTopoCnt,
                OpTrnTopoCnt = OpTrnTopoCnt,
                DatasetLength = (uint)_data.Length,
                Reserved = 0,
                ReplyComId = 0,
                ReplyIpAddress = 0,
                FrameCheckSum = 0,
            };

            header.Write(dest);
            if (_data.Length > 0)
            {
                _data.AsSpan().CopyTo(dest.Slice(PdHeader.Size, _data.Length));
            }
            // FCS ueber die Header-Bytes (0..35), little-endian gespeichert.
            header.UpdateFrameCheckSum(dest.Slice(0, PdHeader.Size));
            return gross;
        }
    }
}
