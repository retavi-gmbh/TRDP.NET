// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/trdp_pdcom.c (trdp_pdReceive,
// Sequenzpruefung, Timeout) und tlp_if.c (tlp_subscribe/tlp_get). Empfangs-Element der PD-Kommunikation.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using Trdp.Net.Core;

namespace Trdp.Net.Pd
{
    /// <summary>
    /// DE: Ein PD-Subscriber (abonniertes Process-Data-Telegramm fuer eine ComId).
    /// Entspricht einem PD_ELE_T der Empfangswarteschlange. Erzeugen ueber
    /// <see cref="TrdpPdSession.Subscribe"/>.
    /// </summary>
    public sealed class PdSubscriber
    {
        /// <summary>DE: Abonnierte ComId.</summary>
        public uint ComId { get; }

        /// <summary>DE: Optionaler Quell-IP-Filter (null = beliebige Quelle).</summary>
        public IPAddress? SourceFilter { get; }

        /// <summary>DE: Ueberwachungszeit in ms; 0 = keine Timeout-Ueberwachung.</summary>
        public int TimeoutMs { get; }

        /// <summary>DE: Zuletzt empfangene Nettodaten (Kopie) oder null.</summary>
        public byte[]? LastData { get; private set; }

        /// <summary>DE: Quelle des zuletzt empfangenen Telegramms.</summary>
        public IPAddress LastSrcIp { get; private set; } = IPAddress.Any;

        /// <summary>DE: Sessionzeit (ms) des letzten gueltigen Empfangs.</summary>
        public long LastReceivedMs { get; private set; }

        /// <summary>DE: Zuletzt empfangener Sequenzzaehler.</summary>
        public uint SequenceCounter { get; private set; }

        /// <summary>DE: True, solange gueltige, nicht abgelaufene Daten vorliegen.</summary>
        public bool IsValid { get; private set; }

        /// <summary>DE: Anzahl erkannter Sequenzluecken (Statistik, numMissed).</summary>
        public uint MissedCount { get; private set; }

        private bool _hasSeq;

        /// <summary>DE: Wird bei jedem neuen gueltigen Telegramm ausgeloest.</summary>
        public event Action<PdSubscriber>? DataReceived;

        /// <summary>DE: Wird ausgeloest, wenn die Ueberwachungszeit ohne Telegramm ablaeuft.</summary>
        public event Action<PdSubscriber>? Timeout;

        internal PdSubscriber(uint comId, int timeoutMs, IPAddress? sourceFilter)
        {
            ComId = comId;
            TimeoutMs = timeoutMs;
            SourceFilter = sourceFilter;
        }

        /// <summary>DE: Prueft, ob ein empfangener Header zu diesem Subscriber passt.</summary>
        internal bool Matches(in PdHeader header, IPAddress src)
        {
            if (header.ComId != ComId) return false;
            if (SourceFilter != null && !SourceFilter.Equals(src)) return false;
            return true;
        }

        /// <summary>
        /// DE: Uebernimmt ein gueltiges Telegramm. Sequenzpruefung wie trdp_pdReceive:
        /// identischer Zaehler = Duplikat (verworfen), Luecke = numMissed erhoeht.
        /// </summary>
        internal void OnReceive(in PdHeader header, ReadOnlySpan<byte> data, IPAddress src, long nowMs)
        {
            uint newSeq = header.SequenceCounter;

            if (_hasSeq)
            {
                if (newSeq == SequenceCounter)
                {
                    // Duplikat -> ignorieren.
                    return;
                }
                if (newSeq > SequenceCounter + 1u)
                {
                    MissedCount += newSeq - SequenceCounter - 1u;
                }
            }

            SequenceCounter = newSeq;
            _hasSeq = true;

            if (LastData == null || LastData.Length != data.Length)
            {
                LastData = data.ToArray();
            }
            else
            {
                data.CopyTo(LastData);
            }

            LastSrcIp = src;
            LastReceivedMs = nowMs;
            IsValid = true;

            DataReceived?.Invoke(this);
        }

        /// <summary>DE: Prueft die Ueberwachungszeit; markiert bei Ablauf IsValid=false (trdp_pdHandleTimeOuts).</summary>
        internal void CheckTimeout(long nowMs)
        {
            if (TimeoutMs <= 0 || !IsValid) return;

            if (nowMs - LastReceivedMs >= TimeoutMs)
            {
                IsValid = false;
                Timeout?.Invoke(this);
            }
        }

        /// <summary>
        /// DE: Liefert die zuletzt empfangenen Daten (tlp_get). <paramref name="data"/> ist nur
        /// gesetzt, wenn gueltige (nicht abgelaufene) Daten vorliegen.
        /// </summary>
        public bool TryGetData(out byte[] data)
        {
            if (IsValid && LastData != null)
            {
                data = LastData;
                return true;
            }
            data = Array.Empty<byte>();
            return false;
        }
    }
}
