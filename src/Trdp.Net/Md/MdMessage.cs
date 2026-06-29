// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/iec61375-2-3.h (MD msgTypes)
// und trdp/src/common/trdp_mdcom.c / tlm_if.c (MD-Semantik).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using Trdp.Net.Core;

namespace Trdp.Net.Md
{
    /// <summary>DE: MD-Nachrichtentyp (msgType-Feld), Werte = ASCII gem. IEC 61375-2-3.</summary>
    public enum MdMessageType : ushort
    {
        /// <summary>'Mn' — Notification (Request ohne Reply).</summary>
        Notify = TrdpConstants.MsgTypeMn,
        /// <summary>'Mr' — Request (mit Reply).</summary>
        Request = TrdpConstants.MsgTypeMr,
        /// <summary>'Mp' — Reply ohne Confirmation.</summary>
        Reply = TrdpConstants.MsgTypeMp,
        /// <summary>'Mq' — Reply mit Confirmation.</summary>
        ReplyQuery = TrdpConstants.MsgTypeMq,
        /// <summary>'Mc' — Confirmation.</summary>
        Confirm = TrdpConstants.MsgTypeMc,
        /// <summary>'Me' — Error.</summary>
        Error = TrdpConstants.MsgTypeMe,
    }

    /// <summary>
    /// DE: Ziel zum Zuruecksenden einer Antwort (abstrahiert UDP vs. TCP).
    /// </summary>
    internal interface IMdReplyTarget
    {
        IPAddress RemoteIp { get; }
        int RemotePort { get; }
        void Send(ReadOnlySpan<byte> frame);
    }

    /// <summary>
    /// DE: Eine empfangene MD-Nachricht (geparst aus Header + Daten).
    /// </summary>
    public sealed class MdMessage
    {
        public MdMessageType MessageType { get; init; }
        public uint ComId { get; init; }
        public byte[] SessionId { get; init; } = Array.Empty<byte>();
        public int ReplyStatus { get; init; }
        public byte[] Data { get; init; } = Array.Empty<byte>();
        public IPAddress SourceIp { get; init; } = IPAddress.Any;
        public int SourcePort { get; init; }
        public uint SequenceCounter { get; init; }

        /// <summary>DE: Transport, ueber den geantwortet wird (UDP oder TCP).</summary>
        internal IMdReplyTarget? ReplyTarget { get; init; }

        /// <summary>DE: True, wenn die Nachricht ueber TCP empfangen wurde.</summary>
        public bool IsTcp { get; init; }
    }
}
