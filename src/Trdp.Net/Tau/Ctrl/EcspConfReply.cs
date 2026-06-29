// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/tau_ctrl_types.h
// (TRDP_ECSP_CONF_REPLY_T) sowie das manuelle Unmarshalling aus tau_ctrl.c
// (ecspConfRepMDCallback, #356).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using Trdp.Net.Marshalling;

namespace Trdp.Net.Tau.Ctrl
{
    /// <summary>
    /// DE: ECSP-Bestaetigungs-/Korrektur-Antwort (TRDP_ECSP_CONF_REPLY_T), via MD-Reply empfangen.
    /// Wertbasierte C#-Sicht; gepacktes 40-Byte-Big-Endian-Wire-Format ueber
    /// <see cref="TrdpWireReader"/>/<see cref="TrdpWireWriter"/>.
    /// </summary>
    public sealed class EcspConfReply
    {
        /// <summary>DE: Gepackte Wire-Groesse (sizeof(TRDP_ECSP_CONF_REPLY_T)).</summary>
        public const int WireSize = 40;

        /// <summary>DE: Telegrammversion (1.0).</summary>
        public EcspShortVersion Version { get; set; } = EcspShortVersion.Default;

        /// <summary>DE: status — Speicherstatus der Korrekturinfo (0 = gespeichert, 1 = nicht).</summary>
        public byte Status { get; set; }

        /// <summary>DE: reserved01 (=0).</summary>
        public byte Reserved01 { get; set; }

        /// <summary>DE: deviceName — Funktionsgeraet des ECSC.</summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>DE: reqSafetyCode — SC-32-Wert der zugehoerigen Anfrage.</summary>
        public uint ReqSafetyCode { get; set; }

        /// <summary>DE: safetyTrail — ETBCTRL-VDP-Trailer.</summary>
        public EtbCtrlVdp SafetyTrail { get; set; } = new EtbCtrlVdp();

        /// <summary>DE: Schreibt das Telegramm gepackt/big-endian in den Writer.</summary>
        public void Encode(ref TrdpWireWriter w)
        {
            w.PutUInt8(Version.Ver);
            w.PutUInt8(Version.Rel);
            w.PutUInt8(Status);
            w.PutUInt8(Reserved01);
            CtrlWire.PutLabel(ref w, DeviceName);
            w.PutUInt32(ReqSafetyCode);
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
        public static EcspConfReply Decode(ref TrdpWireReader r)
        {
            byte ver = r.GetUInt8();
            byte rel = r.GetUInt8();
            var reply = new EcspConfReply
            {
                Version = new EcspShortVersion(ver, rel),
                Status = r.GetUInt8(),
                Reserved01 = r.GetUInt8(),
                DeviceName = CtrlWire.GetLabel(ref r),
                ReqSafetyCode = r.GetUInt32(),
            };
            reply.SafetyTrail = EtbCtrlVdp.Decode(ref r);
            return reply;
        }

        /// <summary>DE: Deserialisiert aus einem Puffer (mind. <see cref="WireSize"/> Byte).</summary>
        public static EcspConfReply Decode(ReadOnlySpan<byte> data)
        {
            var r = new TrdpWireReader(data);
            return Decode(ref r);
        }
    }
}
