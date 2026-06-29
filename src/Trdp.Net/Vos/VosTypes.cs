// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/trdp_types.h (TRDP_ERR_T).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013-2021.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

namespace Trdp.Net.Vos
{
    /// <summary>
    /// DE: Fehlercodes — 1:1-Port von <c>TRDP_ERR_T</c> (trdp_types.h). Numerische Werte
    /// bewusst identisch zum Original (negative Codes), damit Wire-/Logvergleiche passen.
    /// </summary>
    public enum TrdpError
    {
        NoErr            = 0,    // TRDP_NO_ERR
        ParamErr         = -1,   // TRDP_PARAM_ERR
        InitErr          = -2,   // TRDP_INIT_ERR
        NoInitErr        = -3,   // TRDP_NOINIT_ERR
        TimeoutErr       = -4,   // TRDP_TIMEOUT_ERR
        NoDataErr        = -5,   // TRDP_NODATA_ERR
        SockErr          = -6,   // TRDP_SOCK_ERR
        IoErr            = -7,   // TRDP_IO_ERR
        MemErr           = -8,   // TRDP_MEM_ERR
        SemaErr          = -9,   // TRDP_SEMA_ERR
        QueueErr         = -10,  // TRDP_QUEUE_ERR
        QueueFullErr     = -11,  // TRDP_QUEUE_FULL_ERR
        MutexErr         = -12,  // TRDP_MUTEX_ERR
        ThreadErr        = -13,  // TRDP_THREAD_ERR
        BlockErr         = -14,  // TRDP_BLOCK_ERR
        IntegrationErr   = -15,  // TRDP_INTEGRATION_ERR
        NoConnErr        = -16,  // TRDP_NOCONN_ERR
        NoSessionErr     = -30,  // TRDP_NOSESSION_ERR
        SessionAbortErr  = -31,  // TRDP_SESSION_ABORT_ERR
        NoSubErr         = -32,  // TRDP_NOSUB_ERR
        NoPubErr         = -33,  // TRDP_NOPUB_ERR
        NoListErr        = -34,  // TRDP_NOLIST_ERR
        CrcErr           = -35,  // TRDP_CRC_ERR
        WireErr          = -36,  // TRDP_WIRE_ERR
        TopoErr          = -37,  // TRDP_TOPO_ERR
        ComIdErr         = -38,  // TRDP_COMID_ERR
        StateErr         = -39,  // TRDP_STATE_ERR
        AppTimeoutErr    = -40,  // TRDP_APP_TIMEOUT_ERR
        AppReplyToErr    = -41,  // TRDP_APP_REPLYTO_ERR
        AppConfirmToErr  = -42,  // TRDP_APP_CONFIRMTO_ERR
        ReplyToErr       = -43,  // TRDP_REPLYTO_ERR
        ConfirmToErr     = -44,  // TRDP_CONFIRMTO_ERR
        ReqConfirmToErr  = -45,  // TRDP_REQCONFIRMTO_ERR
        PacketErr        = -46,  // TRDP_PACKET_ERR
        UnresolvedErr    = -47,  // TRDP_UNRESOLVED_ERR
        XmlParserErr     = -48,  // TRDP_XML_PARSER_ERR
        InUseErr         = -49,  // TRDP_INUSE_ERR
        MarshallingErr   = -50,  // TRDP_MARSHALLING_ERR
        UnknownErr       = -99   // TRDP_UNKNOWN_ERR
    }
}
