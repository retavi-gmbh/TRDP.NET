// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/iec61375-2-3.h (TTDB-ComIDs, URIs,
// Multicast-Adressen und Timeouts) sowie trdp/src/api/tau_tti_types.h (Groessenkonstanten).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2014.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System.Net;

namespace Trdp.Net.Tau.Tti
{
    /// <summary>
    /// DE: Konstanten der Zugtopologie-Telegramme (TTDB/ECSP) nach IEC 61375-2-3.
    /// ComIDs, Ziel-URIs, Multicast-Adressen und Timeouts 1:1 aus iec61375-2-3.h.
    /// </summary>
    public static class TauTtiConstants
    {
        // DE: Groessenkonstanten (tau_tti_types.h / iec61375-2-3.h).
        public const int MaxLabelLen = 16;          // TRDP_MAX_LABEL_LEN (inkl. Terminator)
        public const int NetLabelLen = 16;          // TRDP_NET_LABEL_T  (ohne Terminator, im Wireformat)
        public const int UuidLen = 16;              // TRDP_UUID_T / VOS_UUID_T
        public const int MaxCstCnt = 63;            // TRDP_MAX_CST_CNT
        public const int MaxVehCnt = 63;            // TRDP_MAX_VEH_CNT
        public const int MaxPropLen = 32768;        // TRDP_MAX_PROP_LEN (#378)

        // ── PD 100: Operational Train Directory Status Info ──
        public const uint TtdbStatusComId = 100u;           // TTDB_STATUS_COMID / TRDP_TTDB_OP_TRN_DIR_STAT_INF_COMID
        public const int TtdbStatusCycleUs = 1000000;       // TTDB_STATUS_CYCLE
        public const int TtdbStatusTimeoutUs = 5000000;     // TTDB_STATUS_TO_US (5s)
        public static readonly IPAddress TtdbStatusDestIp = IPAddress.Parse("239.255.0.0");       // TTDB_STATUS_DEST_IP
        public static readonly IPAddress TtdbStatusDestIpEtb0 = IPAddress.Parse("239.194.0.0");   // TTDB_STATUS_DEST_IP_ETB0

        // ── MD 101: Push der OP_TRAIN_DIRECTORY (Notification) ──
        public const uint TtdbOpDirInfoComId = 101u;        // TTDB_OP_DIR_INFO_COMID
        public static readonly IPAddress TtdbOpDirInfoIp = IPAddress.Parse("239.255.0.0");        // TTDB_OP_DIR_INFO_IP
        public static readonly IPAddress TtdbOpDirInfoIpEtb0 = IPAddress.Parse("239.194.0.0");    // TTDB_OP_DIR_INFO_IP_ETB0

        // ── MD 102/103: TRAIN_DIRECTORY ──
        public const uint TtdbTrnDirReqComId = 102u;        // TTDB_TRN_DIR_REQ_COMID
        public const string TtdbTrnDirReqUri = "devECSP.anyVeh.lCst.lClTrn.lTrn"; // TTDB_TRN_DIR_REQ_URI
        public const int TtdbTrnDirReqTimeoutUs = 3000000;  // TTDB_TRN_DIR_REQ_TO_US
        public const uint TtdbTrnDirRepComId = 103u;        // TTDB_TRN_DIR_REP_COMID

        // ── MD 104/105: STATIC_CONSIST_INFO ──
        public const uint TtdbStatCstReqComId = 104u;       // TTDB_STAT_CST_REQ_COMID
        public const string TtdbStatCstReqUri = "devECSP.anyVeh.lCst.lClTrn.lTrn"; // TTDB_STAT_CST_REQ_URI
        public const int TtdbStatCstReqTimeoutUs = 3000000; // TTDB_STAT_CST_REQ_TO_US
        public const uint TtdbStatCstRepComId = 105u;       // TTDB_STAT_CST_REP_COMID

        // ── MD 106/107: TRAIN_NETWORK_DIRECTORY ──
        public const uint TtdbNetDirReqComId = 106u;        // TTDB_NET_DIR_REQ_COMID
        public const string TtdbNetDirReqUri = "devECSP.anyVeh.lCst"; // TTDB_NET_DIR_REQ_URI
        public const int TtdbNetDirReqTimeoutUs = 3000000;  // TTDB_NET_DIR_REQ_TO_US
        public const uint TtdbNetDirRepComId = 107u;        // TTDB_NET_DIR_REP_COMID

        // ── MD 108/109: OP_TRAIN_DIRECTORY (Request/Reply) ──
        public const uint TtdbOpDirInfoReqComId = 108u;     // TTDB_OP_DIR_INFO_REQ_COMID
        public const string TtdbOpDirInfoReqUri = "devECSP.anyVeh.lCst"; // TTDB_OP_DIR_INFO_REQ_URI
        public const int TtdbOpDirInfoReqTimeoutUs = 3000000; // TTDB_OP_DIR_INFO_REQ_TO_US
        public const uint TtdbOpDirInfoRepComId = 109u;     // TTDB_OP_DIR_INFO_REP_COMID

        // ── MD 110/111: READ_COMPLETE (komplette TTDB) ──
        public const uint TtdbReadCmpltReqComId = 110u;     // TTDB_READ_CMPLT_REQ_COMID
        public const string TtdbReadCmpltReqUri = "devECSP.anyVeh.lCst"; // TTDB_READ_CMPLT_REQ_URI
        public const int TtdbReadCmpltReqTimeoutUs = 3000000; // TTDB_READ_CMPLT_REQ_TO_US
        public const uint TtdbReadCmpltRepComId = 111u;     // TTDB_READ_CMPLT_REP_COMID
    }
}
