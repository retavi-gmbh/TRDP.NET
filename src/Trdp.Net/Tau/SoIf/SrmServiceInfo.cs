// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/trdp_serviceRegistry.h
// (SRM_SERVICE_INFO_T) und die Byte-Swap-Schleife aus tau_so_if.c (netcpy).
// Original-C: Copyright 2019 Bombardier Transportation & NewTec GmbH.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Text;
using Trdp.Net.Marshalling;

namespace Trdp.Net.Tau.SoIf
{
    /// <summary>
    /// DE: Eintrag der Service-Registry (SRM_SERVICE_INFO_T). Wertbasierte C#-Sicht; das
    /// Host-Struct-Alignment (GNU_PACKED) wird NICHT uebernommen, gelesen/geschrieben wird
    /// 1:1 das gepackte 64-Byte-Big-Endian-Wire-Format ueber <see cref="TrdpWireReader"/>/
    /// <see cref="TrdpWireWriter"/>.
    /// </summary>
    public sealed class SrmServiceInfo
    {
        /// <summary>DE: Gepackte Wire-Groesse eines Eintrags in Byte (sizeof(SRM_SERVICE_INFO_T)).</summary>
        public const int WireSize = 64;

        // DE: Laenge der Netz-Labels (TRDP_NET_LABEL_T = CHAR8[16], OHNE Terminator).
        private const int LabelLen = 16;

        /// <summary>DE: Service-Kurzname (srvName, max. 16 Zeichen, ASCII).</summary>
        public string SrvName { get; set; } = string.Empty;

        /// <summary>DE: serviceId = (instanceId &lt;&lt; 24) | (serviceTypeId &amp; 0xFFFFFF).</summary>
        public uint ServiceId { get; set; }

        /// <summary>DE: Service-Version (srvVers).</summary>
        public SrmShortVersion SrvVers { get; set; }

        /// <summary>DE: Flags (srvFlags). Bit0 safety, Bit1 local, Bit3 list-update, Bit4 delete.</summary>
        public byte SrvFlags { get; set; }

        /// <summary>DE: Reserviert (reserved01 = 0).</summary>
        public byte Reserved01 { get; set; }

        /// <summary>DE: Time-to-Live des Service-Eintrags (srvTTL, TIMEDATE64).</summary>
        public TrdpTimeDate64 SrvTtl { get; set; }

        /// <summary>DE: Host-Kennung des Funktionsgeraets (fctDev, max. 16 Zeichen, ASCII).</summary>
        public string FctDev { get; set; } = string.Empty;

        /// <summary>DE: Fahrzeug-Folgenummer im Konsist (cstVehNo, 1..32).</summary>
        public byte CstVehNo { get; set; }

        /// <summary>DE: Konsist-Folgenummer (cstNo, 1..63).</summary>
        public byte CstNo { get; set; }

        /// <summary>DE: Reserviert (reserved03 = 0).</summary>
        public ushort Reserved03 { get; set; }

        /// <summary>DE: Service-spezifische Zusatzinfo (addInfo[3]).</summary>
        public uint[] AddInfo { get; } = new uint[3];

        /// <summary>DE: 8-Bit-Instanz-Id der serviceId (Komfort, SOA_INST).</summary>
        public byte InstanceId => SrmServiceRegistry.ServiceInstance(ServiceId);

        /// <summary>DE: 24-Bit-Service-Typ der serviceId (Komfort, SOA_TYPE).</summary>
        public uint ServiceTypeId => SrmServiceRegistry.ServiceType(ServiceId);

        /// <summary>
        /// DE: Schreibt den Eintrag gepackt/big-endian in den Writer. Entspricht der
        /// netcpy-Konvertierung host-&gt;network fuer serviceId, srvTTL und addInfo[].
        /// </summary>
        public void Encode(ref TrdpWireWriter w)
        {
            PutLabel(ref w, SrvName);
            w.PutUInt32(ServiceId);
            w.PutUInt8(SrvVers.Ver);
            w.PutUInt8(SrvVers.Rel);
            w.PutUInt8(SrvFlags);
            w.PutUInt8(Reserved01);
            w.PutTimeDate64(SrvTtl);
            PutLabel(ref w, FctDev);
            w.PutUInt8(CstVehNo);
            w.PutUInt8(CstNo);
            w.PutUInt16(Reserved03);
            w.PutUInt32(AddInfo[0]);
            w.PutUInt32(AddInfo[1]);
            w.PutUInt32(AddInfo[2]);
        }

        /// <summary>DE: Liest einen Eintrag gepackt/big-endian aus dem Reader.</summary>
        public static SrmServiceInfo Decode(ref TrdpWireReader r)
        {
            var info = new SrmServiceInfo
            {
                SrvName = GetLabel(ref r),
                ServiceId = r.GetUInt32(),
            };
            byte ver = r.GetUInt8();
            byte rel = r.GetUInt8();
            info.SrvVers = new SrmShortVersion(ver, rel);
            info.SrvFlags = r.GetUInt8();
            info.Reserved01 = r.GetUInt8();
            info.SrvTtl = r.GetTimeDate64();
            info.FctDev = GetLabel(ref r);
            info.CstVehNo = r.GetUInt8();
            info.CstNo = r.GetUInt8();
            info.Reserved03 = r.GetUInt16();
            info.AddInfo[0] = r.GetUInt32();
            info.AddInfo[1] = r.GetUInt32();
            info.AddInfo[2] = r.GetUInt32();
            return info;
        }

        /// <summary>DE: Uebernimmt alle Felder aus <paramref name="other"/> (fuer Reply-Update in place).</summary>
        public void CopyFrom(SrmServiceInfo other)
        {
            SrvName = other.SrvName;
            ServiceId = other.ServiceId;
            SrvVers = other.SrvVers;
            SrvFlags = other.SrvFlags;
            Reserved01 = other.Reserved01;
            SrvTtl = other.SrvTtl;
            FctDev = other.FctDev;
            CstVehNo = other.CstVehNo;
            CstNo = other.CstNo;
            Reserved03 = other.Reserved03;
            Array.Copy(other.AddInfo, AddInfo, 3);
        }

        // ── Label-Hilfen (festbreitiges, nullgefuelltes ASCII-Feld) ──

        private static void PutLabel(ref TrdpWireWriter w, string? s)
        {
            byte[] bytes = string.IsNullOrEmpty(s) ? Array.Empty<byte>() : Encoding.ASCII.GetBytes(s);
            for (int i = 0; i < LabelLen; i++)
            {
                w.PutUInt8(i < bytes.Length ? bytes[i] : (byte)0);
            }
        }

        private static string GetLabel(ref TrdpWireReader r)
        {
            var buf = new byte[LabelLen];
            for (int i = 0; i < LabelLen; i++)
            {
                buf[i] = r.GetUInt8();
            }
            int n = Array.IndexOf(buf, (byte)0);
            if (n < 0)
            {
                n = LabelLen;
            }
            return Encoding.ASCII.GetString(buf, 0, n);
        }
    }
}
