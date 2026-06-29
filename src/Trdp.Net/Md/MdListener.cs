// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tlm_if.c (tlm_addListener,
// tlm_reply/tlm_replyQuery). Server-Seite der MD-Kommunikation.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;

namespace Trdp.Net.Md
{
    /// <summary>
    /// DE: Listener fuer eingehende MD-Requests/Notifications einer ComId (Server-Seite).
    /// Erzeugen ueber <see cref="MdSession.AddListener"/>.
    /// </summary>
    public sealed class MdListener
    {
        /// <summary>DE: ComId, auf die gehoert wird.</summary>
        public uint ComId { get; }

        /// <summary>
        /// DE: Wird bei eingehendem Request (Mr) oder Notification (Mn) ausgeloest.
        /// Bei Mr kann ueber den <see cref="MdRequestContext"/> geantwortet werden.
        /// </summary>
        public event Action<MdRequestContext>? Received;

        internal MdListener(uint comId) => ComId = comId;

        internal void Raise(MdRequestContext context) => Received?.Invoke(context);
    }

    /// <summary>
    /// DE: Kontext eines eingegangenen Requests; erlaubt das Antworten an den Absender.
    /// </summary>
    public sealed class MdRequestContext
    {
        private readonly MdSession _session;

        /// <summary>DE: Die empfangene Nachricht (Mr oder Mn).</summary>
        public MdMessage Message { get; }

        internal MdRequestContext(MdSession session, MdMessage message)
        {
            _session = session;
            Message = message;
        }

        /// <summary>DE: True, wenn der Absender eine Antwort erwartet (Mr).</summary>
        public bool ReplyExpected => Message.MessageType == MdMessageType.Request;

        /// <summary>DE: Sendet ein Reply ohne Confirmation (Mp), an den Absender, gleiche Session-ID.</summary>
        public void Reply(ReadOnlySpan<byte> data, int replyStatus = 0) =>
            _session.SendReply(MdMessageType.Reply, Message, data, replyStatus);

        /// <summary>DE: Sendet ein Reply mit Confirmation-Anforderung (Mq).</summary>
        public void ReplyQuery(ReadOnlySpan<byte> data, int replyStatus = 0) =>
            _session.SendReply(MdMessageType.ReplyQuery, Message, data, replyStatus);
    }
}
