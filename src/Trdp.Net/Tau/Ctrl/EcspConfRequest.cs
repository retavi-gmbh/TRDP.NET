// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/tau_ctrl_types.h
// (TRDP_ECSP_CONF_REQUEST_T) sowie das manuelle Marshalling aus tau_ctrl.c
// (tau_requestEcspConfirm, #356: safetyTrail direkt hinter die confVehCnt Fahrzeuge).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;
using Trdp.Net.Marshalling;

namespace Trdp.Net.Tau.Ctrl
{
    /// <summary>
    /// DE: ECSP-Bestaetigungs-/Korrektur-Anfrage (TRDP_ECSP_CONF_REQUEST_T), via MD-Request
    /// gesendet. Wertbasierte C#-Sicht des gepackten Big-Endian-Wire-Formats.
    ///
    /// DE-Anmerkung (#356): Das C-Original sendet immer die volle Struct-Groesse
    /// (sizeof = <see cref="FixedWireSize"/> Byte), platziert den safetyTrail aber direkt
    /// hinter die tatsaechlich belegten <c>confVehCnt</c> Fahrzeuge (nicht am Array-Ende).
    /// <see cref="Encode()"/> bildet genau das ab; die Bytes hinter dem safetyTrail werden mit 0
    /// gefuellt. <see cref="EncodeCompact"/> liefert die kompakte Variante (ohne Padding).
    /// </summary>
    public sealed class EcspConfRequest
    {
        /// <summary>DE: Fester Kopf-Teil bis einschliesslich confVehCnt (Wire-Bytes).</summary>
        public const int HeaderSize = 28;

        /// <summary>DE: Volle gepackte Struct-Groesse (sizeof(TRDP_ECSP_CONF_REQUEST_T)).</summary>
        public const int FixedWireSize =
            HeaderSize + EcspCtrlConstants.MaxVehCnt * OpVehicle.WireSize + EtbCtrlVdp.WireSize;

        /// <summary>DE: command — 1 = Confirm/Correction-Request, 2 = Un-Confirmation-Request.</summary>
        public byte Command { get; set; }

        /// <summary>DE: Telegrammversion (1.0).</summary>
        public EcspShortVersion Version { get; set; } = EcspShortVersion.Default;

        /// <summary>DE: reserved01 (=0).</summary>
        public byte Reserved01 { get; set; }

        /// <summary>DE: deviceName — Funktionsgeraet des ECSC.</summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>DE: opTrnTopoCnt — operationeller Train-Topozaehler, auf dem die Korrektur basiert.</summary>
        public uint OpTrnTopoCnt { get; set; }

        /// <summary>DE: reserved02 (=0).</summary>
        public ushort Reserved02 { get; set; }

        /// <summary>DE: Liste der bestaetigten Fahrzeuge (geordnet, Zugkopf zuerst). Max. 63.</summary>
        public List<OpVehicle> ConfVehList { get; } = new List<OpVehicle>();

        /// <summary>DE: safetyTrail — ETBCTRL-VDP-Trailer.</summary>
        public EtbCtrlVdp SafetyTrail { get; set; } = new EtbCtrlVdp();

        /// <summary>DE: confVehCnt — Anzahl bestaetigter Fahrzeuge (= ConfVehList.Count).</summary>
        public ushort ConfVehCnt => (ushort)ConfVehList.Count;

        /// <summary>DE: Kompakte Wire-Groesse (Kopf + belegte Fahrzeuge + safetyTrail).</summary>
        public int CompactWireSize =>
            HeaderSize + ConfVehList.Count * OpVehicle.WireSize + EtbCtrlVdp.WireSize;

        // DE: Schreibt Kopf, alle belegten Fahrzeuge und den safetyTrail (direkt anschliessend).
        private void EncodeBody(ref TrdpWireWriter w)
        {
            w.PutUInt8(Version.Ver);
            w.PutUInt8(Version.Rel);
            w.PutUInt8(Command);
            w.PutUInt8(Reserved01);
            CtrlWire.PutLabel(ref w, DeviceName);
            w.PutUInt32(OpTrnTopoCnt);
            w.PutUInt16(Reserved02);
            w.PutUInt16(ConfVehCnt);
            foreach (OpVehicle veh in ConfVehList)
            {
                veh.Encode(ref w);
            }
            // #356: safetyTrail direkt hinter die belegten Fahrzeuge.
            SafetyTrail.Encode(ref w);
        }

        /// <summary>
        /// DE: Serialisiert in einen Puffer voller Struct-Groesse (<see cref="FixedWireSize"/>),
        /// wie das C-Original (sizeof-Send). safetyTrail liegt hinter confVehCnt Fahrzeugen,
        /// die restlichen Bytes sind 0.
        /// </summary>
        public byte[] Encode()
        {
            if (ConfVehList.Count > EcspCtrlConstants.MaxVehCnt)
            {
                throw new InvalidOperationException(
                    $"confVehCnt {ConfVehList.Count} > TRDP_MAX_VEH_CNT {EcspCtrlConstants.MaxVehCnt}.");
            }
            var buf = new byte[FixedWireSize];
            var w = new TrdpWireWriter(buf);
            EncodeBody(ref w);
            return buf;
        }

        /// <summary>DE: Serialisiert kompakt (ohne Padding hinter dem safetyTrail).</summary>
        public byte[] EncodeCompact()
        {
            if (ConfVehList.Count > EcspCtrlConstants.MaxVehCnt)
            {
                throw new InvalidOperationException(
                    $"confVehCnt {ConfVehList.Count} > TRDP_MAX_VEH_CNT {EcspCtrlConstants.MaxVehCnt}.");
            }
            var buf = new byte[CompactWireSize];
            var w = new TrdpWireWriter(buf);
            EncodeBody(ref w);
            return buf;
        }

        /// <summary>
        /// DE: Liest die Anfrage gepackt/big-endian. Es werden confVehCnt Fahrzeuge gelesen,
        /// danach folgt der safetyTrail (entsprechend dem #356-Layout).
        /// </summary>
        public static EcspConfRequest Decode(ref TrdpWireReader r)
        {
            byte ver = r.GetUInt8();
            byte rel = r.GetUInt8();
            var req = new EcspConfRequest
            {
                Version = new EcspShortVersion(ver, rel),
                Command = r.GetUInt8(),
                Reserved01 = r.GetUInt8(),
                DeviceName = CtrlWire.GetLabel(ref r),
                OpTrnTopoCnt = r.GetUInt32(),
                Reserved02 = r.GetUInt16(),
            };
            ushort cnt = r.GetUInt16();
            for (int i = 0; i < cnt; i++)
            {
                req.ConfVehList.Add(OpVehicle.Decode(ref r));
            }
            req.SafetyTrail = EtbCtrlVdp.Decode(ref r);
            return req;
        }

        /// <summary>DE: Deserialisiert aus einem Puffer.</summary>
        public static EcspConfRequest Decode(ReadOnlySpan<byte> data)
        {
            var r = new TrdpWireReader(data);
            return Decode(ref r);
        }
    }
}
