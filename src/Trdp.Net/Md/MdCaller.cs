// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tlm_if.c (tlm_request/tlm_confirm)
// und trdp_mdcom.c (Session-Matching ueber sessionID, Reply-Timeout).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;

namespace Trdp.Net.Md
{
    /// <summary>
    /// DE: Laufende MD-Anfrage (Client-Seite). Entspricht einem MD_ELE_T im Caller-Zustand,
    /// gematcht ueber die 16-Byte-Session-ID. Erzeugen ueber <see cref="MdSession.Request"/>.
    /// </summary>
    public sealed class MdCaller
    {
        /// <summary>DE: ComId der Anfrage.</summary>
        public uint ComId { get; }

        /// <summary>DE: Eindeutige Session-ID (UUID, 16 Byte).</summary>
        public byte[] SessionId { get; }

        /// <summary>DE: Erwartete Anzahl Replies (numReplies); 0 = unbegrenzt.</summary>
        public uint ExpectedReplies { get; }

        /// <summary>DE: Anzahl bereits empfangener Replies.</summary>
        public uint ReceivedReplies { get; private set; }

        /// <summary>DE: True, solange die Anfrage auf Replies wartet.</summary>
        public bool IsPending { get; internal set; } = true;

        // DE: Sessionzeit (ms), zu der die Anfrage als Timeout gilt; long.MaxValue = unendlich.
        internal long DeadlineMs;

        // DE: Quelle des letzten Reply (fuer Confirm-Ziel).
        internal IPAddress LastReplyIp = IPAddress.Any;
        internal int LastReplyPort;

        // DE: Transport fuer ausgehende Frames (TCP-Verbindung des Requests; null = UDP).
        internal IMdReplyTarget? Transport;

        private readonly MdSession _session;

        /// <summary>DE: Wird bei jedem passenden Reply (Mp/Mq/Me) ausgeloest.</summary>
        public event Action<MdCaller, MdMessage>? ReplyReceived;

        /// <summary>DE: Wird ausgeloest, wenn die Reply-Ueberwachungszeit ablaeuft.</summary>
        public event Action<MdCaller>? TimedOut;

        internal MdCaller(MdSession session, uint comId, byte[] sessionId, uint expectedReplies, long deadlineMs)
        {
            _session = session;
            ComId = comId;
            SessionId = sessionId;
            ExpectedReplies = expectedReplies;
            DeadlineMs = deadlineMs;
        }

        internal void OnReply(MdMessage msg)
        {
            ReceivedReplies++;
            LastReplyIp = msg.SourceIp;
            LastReplyPort = msg.SourcePort;
            ReplyReceived?.Invoke(this, msg);

            // Genug Replies erhalten -> nicht mehr pending (numReplies != 0).
            if (ExpectedReplies != 0u && ReceivedReplies >= ExpectedReplies)
            {
                IsPending = false;
            }
        }

        internal void OnTimeout()
        {
            IsPending = false;
            TimedOut?.Invoke(this);
        }

        /// <summary>
        /// DE: Bestaetigt einen mit Confirmation angeforderten Reply (Mq) per Mc (tlm_confirm).
        /// Sendet an die Quelle des letzten Reply.
        /// </summary>
        public void Confirm(int replyStatus = 0)
        {
            _session.SendConfirm(this, replyStatus);
        }
    }
}
