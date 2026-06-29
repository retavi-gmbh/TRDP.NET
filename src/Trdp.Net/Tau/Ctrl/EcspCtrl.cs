// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/tau_ctrl_types.h (TRDP_ECSP_CTRL_T)
// sowie die manuelle Marshalling-Schleife aus tau_ctrl.c (tau_setEcspCtrl, #356).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using Trdp.Net.Marshalling;

namespace Trdp.Net.Tau.Ctrl
{
    /// <summary>
    /// DE: ECSP-Control-Telegramm (TRDP_ECSP_CTRL_T), zyklisch per PD an den ECSP gesendet.
    /// Wertbasierte C#-Sicht; das gepackte 40-Byte-Big-Endian-Wire-Format wird ueber
    /// <see cref="TrdpWireReader"/>/<see cref="TrdpWireWriter"/> 1:1 abgebildet (Host-Struct-
    /// Alignment GNU_PACKED wird NICHT uebernommen).
    /// </summary>
    public sealed class EcspCtrl
    {
        /// <summary>DE: Gepackte Wire-Groesse (sizeof(TRDP_ECSP_CTRL_T)).</summary>
        public const int WireSize = 40;

        /// <summary>DE: Telegrammversion (1.0).</summary>
        public EcspShortVersion Version { get; set; } = EcspShortVersion.Default;

        /// <summary>DE: reserved01 (=0).</summary>
        public byte Reserved01 { get; set; }

        /// <summary>DE: leadVehOfCst — Position des fuehrenden Fahrzeugs im Konsist (0..32).</summary>
        public byte LeadVehOfCst { get; set; }

        /// <summary>DE: deviceName — Funktionsgeraet des ECSC, das das Telegramm sendet.</summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>DE: inhibit — Inaugurations-Inhibit (0 = kein Request, 1 = Request).</summary>
        public byte Inhibit { get; set; }

        /// <summary>DE: leadingReq — Fuehrungsanforderung (0 = nein, 1 = ja).</summary>
        public byte LeadingReq { get; set; }

        /// <summary>DE: leadingDir — Fuehrungsrichtung (0/1/2).</summary>
        public byte LeadingDir { get; set; }

        /// <summary>DE: sleepReq — Sleep-Anforderung (0/1).</summary>
        public byte SleepReq { get; set; }

        /// <summary>DE: safetyTrail — ETBCTRL-VDP-Trailer (komplett 0 == SDTv2 nicht genutzt).</summary>
        public EtbCtrlVdp SafetyTrail { get; set; } = new EtbCtrlVdp();

        /// <summary>DE: Schreibt das Telegramm gepackt/big-endian in den Writer.</summary>
        public void Encode(ref TrdpWireWriter w)
        {
            w.PutUInt8(Version.Ver);
            w.PutUInt8(Version.Rel);
            w.PutUInt8(Reserved01);
            w.PutUInt8(LeadVehOfCst);
            CtrlWire.PutLabel(ref w, DeviceName);
            w.PutUInt8(Inhibit);
            w.PutUInt8(LeadingReq);
            w.PutUInt8(LeadingDir);
            w.PutUInt8(SleepReq);
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
        public static EcspCtrl Decode(ref TrdpWireReader r)
        {
            byte ver = r.GetUInt8();
            byte rel = r.GetUInt8();
            var c = new EcspCtrl
            {
                Version = new EcspShortVersion(ver, rel),
                Reserved01 = r.GetUInt8(),
                LeadVehOfCst = r.GetUInt8(),
                DeviceName = CtrlWire.GetLabel(ref r),
                Inhibit = r.GetUInt8(),
                LeadingReq = r.GetUInt8(),
                LeadingDir = r.GetUInt8(),
                SleepReq = r.GetUInt8(),
            };
            c.SafetyTrail = EtbCtrlVdp.Decode(ref r);
            return c;
        }

        /// <summary>DE: Deserialisiert aus einem Puffer (mind. <see cref="WireSize"/> Byte).</summary>
        public static EcspCtrl Decode(ReadOnlySpan<byte> data)
        {
            var r = new TrdpWireReader(data);
            return Decode(ref r);
        }
    }
}
