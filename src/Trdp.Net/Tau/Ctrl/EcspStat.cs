// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/tau_ctrl_types.h (TRDP_ECSP_STAT_T)
// sowie die manuelle Unmarshalling-Schleife aus tau_ctrl.c (tau_getEcspStat, #356).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using Trdp.Net.Marshalling;

namespace Trdp.Net.Tau.Ctrl
{
    /// <summary>
    /// DE: ECSP-Status-Telegramm (TRDP_ECSP_STAT_T), zyklisch vom ECSP empfangen (PD-Subscribe).
    /// Wertbasierte C#-Sicht; das gepackte 40-Byte-Big-Endian-Wire-Format wird ueber
    /// <see cref="TrdpWireReader"/>/<see cref="TrdpWireWriter"/> 1:1 abgebildet.
    /// </summary>
    public sealed class EcspStat
    {
        /// <summary>DE: Gepackte Wire-Groesse (sizeof(TRDP_ECSP_STAT_T)).</summary>
        public const int WireSize = 40;

        /// <summary>DE: Telegrammversion (1.0).</summary>
        public EcspShortVersion Version { get; set; } = EcspShortVersion.Default;

        /// <summary>DE: reserved01 (=0).</summary>
        public ushort Reserved01 { get; set; }

        /// <summary>DE: lifesign — Wrap-around-Zaehler, je Datagramm inkrementiert.</summary>
        public ushort Lifesign { get; set; }

        /// <summary>DE: ecspState — ECSP-Zustand (0 = nicht betriebsbereit, 1 = in Betrieb).</summary>
        public byte EcspState { get; set; }

        /// <summary>DE: etbInhibit — Inaugurations-Inhibit-Anzeige (0..4).</summary>
        public byte EtbInhibit { get; set; }

        /// <summary>DE: etbLength — Zugverlaengerung erkannt (0/1).</summary>
        public byte EtbLength { get; set; }

        /// <summary>DE: etbShort — Zugverkuerzung erkannt (0/1).</summary>
        public byte EtbShort { get; set; }

        /// <summary>DE: reserved02 (=0).</summary>
        public ushort Reserved02 { get; set; }

        /// <summary>DE: etbLeadState — lokale Konsist-Fuehrung (5/6/9/10).</summary>
        public byte EtbLeadState { get; set; }

        /// <summary>DE: etbLeadDir — Richtung des fuehrenden Endwagens (0/1/2).</summary>
        public byte EtbLeadDir { get; set; }

        /// <summary>DE: ttdbSrvState — TTDB-Server-Zustand (0..3).</summary>
        public byte TtdbSrvState { get; set; }

        /// <summary>DE: dnsSrvState — DNS-Server-Zustand (0..3).</summary>
        public byte DnsSrvState { get; set; }

        /// <summary>DE: trnDirState — Train-Directory-Zustand (1/2).</summary>
        public byte TrnDirState { get; set; }

        /// <summary>DE: opTrnDirState — operationeller Train-Directory-Zustand (1/2/4).</summary>
        public byte OpTrnDirState { get; set; }

        /// <summary>DE: sleepCtrlState — Sleep-Control-Zustand (Option, 0..3).</summary>
        public byte SleepCtrlState { get; set; }

        /// <summary>DE: sleepReqCnt — Anzahl Sleep-Anforderungen (Option, 0..63).</summary>
        public byte SleepReqCnt { get; set; }

        /// <summary>DE: opTrnTopoCnt — operationeller Train-Topozaehler.</summary>
        public uint OpTrnTopoCnt { get; set; }

        /// <summary>DE: safetyTrail — ETBCTRL-VDP-Trailer.</summary>
        public EtbCtrlVdp SafetyTrail { get; set; } = new EtbCtrlVdp();

        /// <summary>DE: Schreibt das Telegramm gepackt/big-endian in den Writer.</summary>
        public void Encode(ref TrdpWireWriter w)
        {
            w.PutUInt8(Version.Ver);
            w.PutUInt8(Version.Rel);
            w.PutUInt16(Reserved01);
            w.PutUInt16(Lifesign);
            w.PutUInt8(EcspState);
            w.PutUInt8(EtbInhibit);
            w.PutUInt8(EtbLength);
            w.PutUInt8(EtbShort);
            w.PutUInt16(Reserved02);
            w.PutUInt8(EtbLeadState);
            w.PutUInt8(EtbLeadDir);
            w.PutUInt8(TtdbSrvState);
            w.PutUInt8(DnsSrvState);
            w.PutUInt8(TrnDirState);
            w.PutUInt8(OpTrnDirState);
            w.PutUInt8(SleepCtrlState);
            w.PutUInt8(SleepReqCnt);
            w.PutUInt32(OpTrnTopoCnt);
            SafetyTrail.Encode(ref w);
        }

        /// <summary>DE: Serialisiert das Telegramm in einen frischen Puffer (Wire-Groesse).</summary>
        public byte[] Encode()
        {
            var buf = new byte[WireSize];
            var w = new TrdpWireWriter(buf);
            Encode(ref w);
            return buf;
        }

        /// <summary>DE: Liest das Telegramm gepackt/big-endian aus dem Reader.</summary>
        public static EcspStat Decode(ref TrdpWireReader r)
        {
            byte ver = r.GetUInt8();
            byte rel = r.GetUInt8();
            var s = new EcspStat
            {
                Version = new EcspShortVersion(ver, rel),
                Reserved01 = r.GetUInt16(),
                Lifesign = r.GetUInt16(),
                EcspState = r.GetUInt8(),
                EtbInhibit = r.GetUInt8(),
                EtbLength = r.GetUInt8(),
                EtbShort = r.GetUInt8(),
                Reserved02 = r.GetUInt16(),
                EtbLeadState = r.GetUInt8(),
                EtbLeadDir = r.GetUInt8(),
                TtdbSrvState = r.GetUInt8(),
                DnsSrvState = r.GetUInt8(),
                TrnDirState = r.GetUInt8(),
                OpTrnDirState = r.GetUInt8(),
                SleepCtrlState = r.GetUInt8(),
                SleepReqCnt = r.GetUInt8(),
                OpTrnTopoCnt = r.GetUInt32(),
            };
            s.SafetyTrail = EtbCtrlVdp.Decode(ref r);
            return s;
        }

        /// <summary>DE: Deserialisiert aus einem Puffer (mind. <see cref="WireSize"/> Byte).</summary>
        public static EcspStat Decode(ReadOnlySpan<byte> data)
        {
            var r = new TrdpWireReader(data);
            return Decode(ref r);
        }
    }
}
