// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/trdp_serviceRegistry.h
// (ComIDs, URIs, Timeouts, Flags und SOA-Makros der Service-Registry).
// Original-C: Copyright 2019 Bombardier Transportation & NewTec GmbH.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

namespace Trdp.Net.Tau.SoIf
{
    /// <summary>
    /// DE: 2-Byte-Versionsfeld fuer Kommunikationspuffer (TRDP_SHORT_VERSION_T):
    /// <c>ver</c> (inkompatible Aenderungen) + <c>rel</c> (kompatible Aenderungen).
    /// </summary>
    public readonly struct SrmShortVersion
    {
        public readonly byte Ver;
        public readonly byte Rel;

        public SrmShortVersion(byte ver, byte rel)
        {
            Ver = ver;
            Rel = rel;
        }
    }

    /// <summary>
    /// DE: Auswahl der Service-Registry-Operation (entspricht dem internen
    /// <c>SRM_REQ_SELECTOR_T</c> aus tau_so_if.c).
    /// </summary>
    public enum SrmReqSelector
    {
        /// <summary>DE: Service hinzufuegen/aktualisieren (SRM_ADD).</summary>
        Add,
        /// <summary>DE: Service entfernen (SRM_DEL).</summary>
        Del,
    }

    /// <summary>
    /// DE: Konstanten der konsist-lokalen Service-Registry (SRM) gemaess IEC 61375-2-3
    /// (vorlaeufige Definitionen). 1:1 aus trdp_serviceRegistry.h.
    /// </summary>
    public static class SrmServiceRegistry
    {
        // ── Flags fuer SRM_SERVICE_INFO_T.SrvFlags (Diagnose/Logging) ──

        public const byte FlagSdt2    = 0x01;
        public const byte FlagSdt4    = 0x02;
        public const byte FlagEvent   = 0x04;
        public const byte FlagMethods = 0x08;
        public const byte FlagFields  = 0x10;

        // ── ComId / Dataset-Id der Service-Info ──

        public const uint ServiceComId = 113u;     // SRM_SERVICE_COMID
        public const uint ServiceDsId  = ServiceComId;

        // ── Read Services (MD ueber TCP bevorzugt) ──

        public const uint   ReadReqComId = 112u;                       // SRM_SERVICE_READ_REQ_COMID
        public const string ReadReqUri   = "devECSP.anyVeh.lCst";      // SRM_SERVICE_READ_REQ_URI
        public const uint   ReadReqTimeoutUs = 3000000u;               // [us] 3s
        public const uint   ReadRepComId = 113u;                       // SRM_SERVICE_READ_REP_COMID

        // ── Add Service ──

        public const uint   AddReqComId = 114u;                        // SRM_SERVICE_ADD_REQ_COMID
        public const string AddReqUri   = "devECSP.anyVeh.lCst";       // SRM_SERVICE_ADD_REQ_URI
        public const uint   AddReqTimeoutUs = 3000000u;                // [us] 3s
        public const uint   AddRepComId = 115u;                        // SRM_SERVICE_ADD_REP_COMID (liefert instanceId)

        // ── Update Service (Notification) ──

        public const uint   UpdNotifyComId = 116u;                     // SRM_SERVICE_UPD_NOTIFY_COMID
        public const string UpdNotifyUri   = "devECSP.anyVeh.lCst";    // SRM_SERVICE_UPD_NOTIFY_URI
        public const uint   UpdNotifyTtlUs = 3000000u;                 // [us] default TTL

        // ── Delete Service ──

        public const uint   DelReqComId = 117u;                        // SRM_SERVICE_DEL_REQ_COMID
        public const string DelReqUri   = "devECSP.anyVeh.lCst";       // SRM_SERVICE_DEL_REQ_URI
        public const uint   DelReqTimeoutUs = 3000000u;                // [us] 3s
        public const uint   DelRepComId = 118u;                        // SRM_SERVICE_DEL_REP_COMID

        // ── Statisch vordefinierte Services ──

        public const byte   DefaultInstId = 1;                         // SRM_DEFAULT_INST_ID

        // ── SOA-Makros: serviceId = (instanceId << 24) | (typeId & 0xFFFFFF) ──

        /// <summary>DE: serviceId aus 8-Bit-Instanz-Id und 24-Bit-Typ-Id (SOA_SERVICEID).</summary>
        public static uint ServiceId(byte instId, uint typeId) => ((uint)instId << 24) | (typeId & 0xFFFFFFu);

        /// <summary>DE: 24-Bit-Service-Typ aus einer serviceId (SOA_TYPE).</summary>
        public static uint ServiceType(uint serviceId) => serviceId & 0xFFFFFFu;

        /// <summary>DE: 8-Bit-Instanz-Id aus einer serviceId (SOA_INST).</summary>
        public static byte ServiceInstance(uint serviceId) => (byte)((serviceId >> 24) & 0xFFu);

        /// <summary>DE: true, wenn a == 0 oder a == b (SOA_SAME_SERVICEID_OR0).</summary>
        public static bool SameServiceIdOr0(uint a, uint b) => a == 0u || a == b;

        /// <summary>DE: true, wenn die Service-Typen uebereinstimmen (SOA_SAME_SERVICE_TYPE).</summary>
        public static bool SameServiceType(uint a, uint b) => ServiceType(a) == ServiceType(b);
    }
}
