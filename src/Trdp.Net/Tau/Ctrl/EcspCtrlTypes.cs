// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/tau_ctrl_types.h und
// trdp/src/api/iec61375-2-3.h (ECSP-Control-ComIDs, Zykluszeit, Timeouts) sowie die
// gemeinsamen Telegramm-Bausteine TRDP_SHORT_VERSION_T, TRDP_ETB_CTRL_VDP_T und
// TRDP_OP_VEHICLE_T (aus tau_tti_types.h).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Text;
using Trdp.Net.Marshalling;

namespace Trdp.Net.Tau.Ctrl
{
    /// <summary>
    /// DE: 2-Byte-Versionsfeld der ECSP-Telegramme (TRDP_SHORT_VERSION_T):
    /// <c>ver</c> (inkompatible Aenderungen) + <c>rel</c> (kompatible Aenderungen).
    /// Reihenfolge auf dem Draht: ver, dann rel. Default = 1.0.
    /// </summary>
    public readonly struct EcspShortVersion
    {
        /// <summary>DE: Version (main_version), bei ECSP-Telegrammen = 1.</summary>
        public readonly byte Ver;

        /// <summary>DE: Release (sub_version), bei ECSP-Telegrammen = 0.</summary>
        public readonly byte Rel;

        public EcspShortVersion(byte ver, byte rel)
        {
            Ver = ver;
            Rel = rel;
        }

        /// <summary>DE: Standardversion 1.0 (main_version = 1, sub_version = 0).</summary>
        public static EcspShortVersion Default => new EcspShortVersion(1, 0);
    }

    /// <summary>
    /// DE: Konstanten der ECSP-Control-Schnittstelle (IEC 61375-2-3). 1:1 aus
    /// iec61375-2-3.h und tau_ctrl.h. Zeiten im Original in Mikrosekunden.
    /// </summary>
    public static class EcspCtrlConstants
    {
        // ── ComIds (iec61375-2-3.h) ──

        /// <summary>DE: ECSP_CTRL_COMID — ETB-Control-Telegramm (zyklischer PD-Publish).</summary>
        public const uint EcspCtrlComId = 120u;

        /// <summary>DE: ECSP_STATUS_COMID — ETB-Status-Telegramm (PD-Subscribe).</summary>
        public const uint EcspStatComId = 121u;

        /// <summary>DE: ECSP_CONF_REQ_COMID — Bestaetigungs-/Korrektur-Anfrage (MD-Request).</summary>
        public const uint EcspConfReqComId = 122u;

        /// <summary>DE: ECSP_CONF_REP_COMID — Bestaetigungs-/Korrektur-Antwort (MD-Reply).</summary>
        public const uint EcspConfRepComId = 123u;

        // ── Zeiten (us, aus iec61375-2-3.h / tau_ctrl.h) ──

        /// <summary>DE: ECSP_CTRL_CYCLE — Sendezyklus des Control-Telegramms [us] (1 s).</summary>
        public const uint EcspCtrlCycleUs = 1000000u;

        /// <summary>DE: ECSP_CTRL_TIMEOUT [us] (5 s) nach 61375-2-3.</summary>
        public const uint EcspCtrlTimeoutUs = 5000000u;

        /// <summary>DE: ECSP_STAT_TIMEOUT — Ueberwachungszeit des Status-Telegramms [us] (5 s).</summary>
        public const uint EcspStatTimeoutUs = 5000000u;

        /// <summary>DE: ECSP_CONF_REPLY_TIMEOUT — Reply-Wartezeit der Confirm-Anfrage [us] (3 s).</summary>
        public const uint EcspConfReplyTimeoutUs = 3000000u;

        // ── Sonstiges ──

        /// <summary>DE: TRDP_MAX_VEH_CNT — max. Anzahl Fahrzeuge pro Zug (tau_tti_types.h).</summary>
        public const int MaxVehCnt = 63;

        /// <summary>DE: Laenge eines Netz-Labels (TRDP_NET_LABEL_T = CHAR8[16], OHNE Terminator).</summary>
        public const int LabelLen = 16;
    }

    /// <summary>
    /// DE: ETBCTRL-VDP-Trailer (TRDP_ETB_CTRL_VDP_T, "safetyTrail"). Wertbasierte C#-Sicht;
    /// das gepackte 16-Byte-Big-Endian-Wire-Format wird ueber <see cref="TrdpWireReader"/>/
    /// <see cref="TrdpWireWriter"/> 1:1 abgebildet. Komplett 0 == SDTv2 nicht genutzt.
    /// </summary>
    public sealed class EtbCtrlVdp
    {
        /// <summary>DE: Gepackte Wire-Groesse (sizeof(TRDP_ETB_CTRL_VDP_T)).</summary>
        public const int WireSize = 16;

        /// <summary>DE: reserved01 (=0).</summary>
        public uint Reserved01 { get; set; }

        /// <summary>DE: reserved02 (=0).</summary>
        public ushort Reserved02 { get; set; }

        /// <summary>DE: Version der vitalen ETBCTRL-Nutzdaten (userDataVersion, 1.0).</summary>
        public EcspShortVersion UserDataVersion { get; set; }

        /// <summary>DE: safeSeqCount — Safe Sequence Counter (B.9).</summary>
        public uint SafeSeqCount { get; set; }

        /// <summary>DE: safetyCode — Pruefsumme (B.9).</summary>
        public uint SafetyCode { get; set; }

        /// <summary>DE: Schreibt den Trailer gepackt/big-endian (vgl. vos_htonl/vos_htons in tau_ctrl.c).</summary>
        public void Encode(ref TrdpWireWriter w)
        {
            w.PutUInt32(Reserved01);
            w.PutUInt16(Reserved02);
            w.PutUInt8(UserDataVersion.Ver);
            w.PutUInt8(UserDataVersion.Rel);
            w.PutUInt32(SafeSeqCount);
            w.PutUInt32(SafetyCode);
        }

        /// <summary>DE: Liest den Trailer gepackt/big-endian.</summary>
        public static EtbCtrlVdp Decode(ref TrdpWireReader r)
        {
            var v = new EtbCtrlVdp
            {
                Reserved01 = r.GetUInt32(),
                Reserved02 = r.GetUInt16(),
            };
            byte ver = r.GetUInt8();
            byte rel = r.GetUInt8();
            v.UserDataVersion = new EcspShortVersion(ver, rel);
            v.SafeSeqCount = r.GetUInt32();
            v.SafetyCode = r.GetUInt32();
            return v;
        }
    }

    /// <summary>
    /// DE: Operationelles Fahrzeug (TRDP_OP_VEHICLE_T, tau_tti_types.h). Bestandteil der
    /// confVehList einer ECSP-Confirm-Anfrage. Gepackte Wire-Groesse 24 Byte; alle Felder
    /// sind Byte/Label, daher kein Byte-Swap noetig (vgl. memcpy in tau_requestEcspConfirm).
    /// </summary>
    public sealed class OpVehicle
    {
        /// <summary>DE: Gepackte Wire-Groesse (sizeof(TRDP_OP_VEHICLE_T)).</summary>
        public const int WireSize = 24;

        /// <summary>DE: vehId — eindeutige Fahrzeugkennung (anwendungsdefiniert, z. B. UIC).</summary>
        public string VehId { get; set; } = string.Empty;

        /// <summary>DE: opVehNo — operationelle Fahrzeug-Folgenummer (1..63).</summary>
        public byte OpVehNo { get; set; }

        /// <summary>DE: isLead — Fahrzeug ist fuehrend (ANTIVALENT8). Bei Confirm = 0 setzen.</summary>
        public byte IsLead { get; set; }

        /// <summary>DE: leadDir — Fuehrungsrichtung. Bei Confirm = 0 setzen.</summary>
        public byte LeadDir { get; set; }

        /// <summary>DE: trnVehNo — Fahrzeug-Folgenummer im Zug (1..63, 0 = per Korrektur eingefuegt).</summary>
        public byte TrnVehNo { get; set; }

        /// <summary>DE: vehOrient — Fahrzeugorientierung.</summary>
        public byte VehOrient { get; set; }

        /// <summary>DE: ownOpCstNo — operationelle Konsist-Nummer des Fahrzeugs.</summary>
        public byte OwnOpCstNo { get; set; }

        /// <summary>DE: reserved01 (=0).</summary>
        public byte Reserved01 { get; set; }

        /// <summary>DE: reserved02 (=0).</summary>
        public byte Reserved02 { get; set; }

        /// <summary>DE: Schreibt das Fahrzeug gepackt/big-endian.</summary>
        public void Encode(ref TrdpWireWriter w)
        {
            CtrlWire.PutLabel(ref w, VehId);
            w.PutUInt8(OpVehNo);
            w.PutUInt8(IsLead);
            w.PutUInt8(LeadDir);
            w.PutUInt8(TrnVehNo);
            w.PutUInt8(VehOrient);
            w.PutUInt8(OwnOpCstNo);
            w.PutUInt8(Reserved01);
            w.PutUInt8(Reserved02);
        }

        /// <summary>DE: Liest ein Fahrzeug gepackt/big-endian.</summary>
        public static OpVehicle Decode(ref TrdpWireReader r)
        {
            return new OpVehicle
            {
                VehId = CtrlWire.GetLabel(ref r),
                OpVehNo = r.GetUInt8(),
                IsLead = r.GetUInt8(),
                LeadDir = r.GetUInt8(),
                TrnVehNo = r.GetUInt8(),
                VehOrient = r.GetUInt8(),
                OwnOpCstNo = r.GetUInt8(),
                Reserved01 = r.GetUInt8(),
                Reserved02 = r.GetUInt8(),
            };
        }
    }

    /// <summary>
    /// DE: Interne Wire-Hilfen fuer ECSP-Telegramme: festbreitige, nullgefuellte
    /// ASCII-Netz-Labels (TRDP_NET_LABEL_T = CHAR8[16] OHNE Terminator).
    /// </summary>
    internal static class CtrlWire
    {
        internal static void PutLabel(ref TrdpWireWriter w, string? s)
        {
            byte[] bytes = string.IsNullOrEmpty(s) ? Array.Empty<byte>() : Encoding.ASCII.GetBytes(s);
            for (int i = 0; i < EcspCtrlConstants.LabelLen; i++)
            {
                w.PutUInt8(i < bytes.Length ? bytes[i] : (byte)0);
            }
        }

        internal static string GetLabel(ref TrdpWireReader r)
        {
            var buf = new byte[EcspCtrlConstants.LabelLen];
            for (int i = 0; i < EcspCtrlConstants.LabelLen; i++)
            {
                buf[i] = r.GetUInt8();
            }
            int n = Array.IndexOf(buf, (byte)0);
            if (n < 0)
            {
                n = EcspCtrlConstants.LabelLen;
            }
            return Encoding.ASCII.GetString(buf, 0, n);
        }
    }
}
