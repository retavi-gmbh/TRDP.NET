// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tlc_if.c (Session/Lifecycle),
// trdp_pdcom.c (trdp_pdSendQueued/trdp_pdReceive/trdp_pdCheck), tlp_if.c (PD-API).
// Loop-getriebenes Modell (vgl. tlc_process + vos_select): Process() aus dem App-Zyklus aufrufen.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using Trdp.Net.Core;
using Trdp.Net.Vos;

namespace Trdp.Net.Pd
{
    /// <summary>
    /// DE: PD-Session — verwaltet Socket, Publisher und Subscriber und treibt Senden,
    /// Empfangen und Timeout-Ueberwachung. Nicht thread-safe; aus EINEM Zyklus aufrufen.
    /// </summary>
    public sealed class TrdpPdSession : IDisposable
    {
        private readonly VosSock _sock;
        private readonly List<PdPublisher> _publishers = new();
        private readonly List<PdSubscriber> _subscribers = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly byte[] _rxBuffer = new byte[TrdpConstants.MaxPdPacketSize];
        private readonly byte[] _txBuffer = new byte[TrdpConstants.MaxPdPacketSize];

        /// <summary>DE: Tatsaechlich gebundener lokaler PD-Port (bei Bind auf 0 der ephemere Port).</summary>
        public int Port => _sock.LocalPort;

        /// <summary>DE: Anzahl gesendeter PD-Telegramme (Statistik).</summary>
        public long PacketsSent { get; private set; }

        /// <summary>DE: Anzahl empfangener gueltiger PD-Telegramme (Statistik).</summary>
        public long PacketsReceived { get; private set; }

        /// <summary>
        /// DE: Oeffnet eine PD-Session und bindet den UDP-Socket.
        /// </summary>
        /// <param name="bindAddress">Lokale IP (null = INADDR_ANY).</param>
        /// <param name="port">Lokaler PD-Port (Standard 17224).</param>
        public TrdpPdSession(IPAddress? bindAddress = null, int port = TrdpConstants.PdUdpPort)
        {
            _sock = VosSock.OpenUdp(bindAddress, port);
        }

        /// <summary>DE: Tritt einer PD-Multicast-Gruppe bei (fuer Multicast-Subscriber).</summary>
        public void JoinMulticast(IPAddress group, IPAddress? iface = null) => _sock.JoinMulticast(group, iface);

        /// <summary>DE: Aktuelle Session-Zeit in ms (monotone Uhr).</summary>
        public long NowMs => _clock.ElapsedMilliseconds;

        /// <summary>
        /// DE: Legt einen zyklischen Publisher an (tlp_publish).
        /// </summary>
        public PdPublisher Publish(uint comId, IPAddress destIp, int cycleTimeMs,
                                   ReadOnlySpan<byte> initialData = default, int destPort = TrdpConstants.PdUdpPort)
        {
            var pub = new PdPublisher(comId, destIp, destPort, cycleTimeMs, initialData.ToArray());
            pub.NextSendMs = NowMs; // sofort faellig
            _publishers.Add(pub);
            return pub;
        }

        /// <summary>
        /// DE: Abonniert ein PD-Telegramm (tlp_subscribe).
        /// </summary>
        public PdSubscriber Subscribe(uint comId, int timeoutMs = 0, IPAddress? sourceFilter = null)
        {
            var sub = new PdSubscriber(comId, timeoutMs, sourceFilter);
            _subscribers.Add(sub);
            return sub;
        }

        /// <summary>DE: Entfernt einen Publisher (tlp_unpublish). True, wenn er aktiv war.</summary>
        public bool Unpublish(PdPublisher pub) => _publishers.Remove(pub);

        /// <summary>DE: Entfernt einen Subscriber (tlp_unsubscribe). True, wenn er aktiv war.</summary>
        public bool Unsubscribe(PdSubscriber sub) => _subscribers.Remove(sub);

        /// <summary>DE: Sendet einen Publisher sofort (Push/Pull-Antwort), unabhaengig vom Zyklus.</summary>
        public void SendNow(PdPublisher pub)
        {
            if (!pub.DataValid) return;
            int len = pub.BuildFrame(_txBuffer);
            _sock.SendTo(_txBuffer.AsSpan(0, len), pub.DestIp, pub.DestPort);
            PacketsSent++;
        }

        /// <summary>
        /// DE: Verarbeitungszyklus (tlc_process): faellige Telegramme senden, eingehende
        /// empfangen und verteilen, Timeouts pruefen. Aus dem App-/ExecutionLoop aufrufen.
        /// </summary>
        public void Process() => Process(NowMs);

        // DE: testbarer Kern mit explizitem Zeitstempel.
        internal void Process(long nowMs)
        {
            // 1) Senden: faellige Publisher.
            foreach (PdPublisher pub in _publishers)
            {
                if (pub.CycleTimeMs <= 0 || !pub.DataValid) continue;
                if (nowMs >= pub.NextSendMs)
                {
                    int len = pub.BuildFrame(_txBuffer);
                    _sock.SendTo(_txBuffer.AsSpan(0, len), pub.DestIp, pub.DestPort);
                    PacketsSent++;
                    // Naechsten Termin setzen (Drift vermeiden: relativ zum Soll).
                    pub.NextSendMs += pub.CycleTimeMs;
                    if (pub.NextSendMs <= nowMs)
                    {
                        pub.NextSendMs = nowMs + pub.CycleTimeMs;
                    }
                }
            }

            // 2) Empfangen: alle anliegenden Datagramme abholen.
            while (_sock.TryReceive(_rxBuffer, out int length, out IPAddress src, out _))
            {
                HandleDatagram(_rxBuffer.AsSpan(0, length), src, nowMs);
            }

            // 3) Timeouts.
            foreach (PdSubscriber sub in _subscribers)
            {
                sub.CheckTimeout(nowMs);
            }
        }

        /// <summary>
        /// DE: Validiert ein empfangenes PD-Datagramm (trdp_pdCheck) und verteilt es an passende
        /// Subscriber. Oeffentlich-intern fuer Tests ohne realen Socket.
        /// </summary>
        internal void HandleDatagram(ReadOnlySpan<byte> frame, IPAddress src, long nowMs)
        {
            // Groesse pruefen.
            if (frame.Length < TrdpConstants.MinPdPacketSize || frame.Length > TrdpConstants.MaxPdPacketSize)
            {
                return; // TRDP_WIRE_ERR
            }

            // FCS pruefen (Header-CRC).
            if (!PdHeader.VerifyFrameCheckSum(frame))
            {
                return; // TRDP_CRC_ERR
            }

            PdHeader header = PdHeader.Parse(frame);

            // Protokollversion (0x0100 oder 0x0101).
            if (header.ProtocolVersion != TrdpConstants.ProtocolVersion &&
                header.ProtocolVersion != TrdpConstants.ProtocolVersionServiceId)
            {
                return;
            }

            // Nur Process-Data (Pd). PD-Request (Pr/Pull) wird hier (noch) nicht behandelt.
            if (header.MsgType != TrdpConstants.MsgTypePd)
            {
                return;
            }

            int dataLen = (int)header.DatasetLength;
            if (dataLen > TrdpConstants.MaxPdDataSize || frame.Length < TrdpConstants.PacketSizePd(dataLen))
            {
                return; // TRDP_WIRE_ERR
            }

            ReadOnlySpan<byte> data = frame.Slice(PdHeader.Size, dataLen);
            PacketsReceived++;

            foreach (PdSubscriber sub in _subscribers)
            {
                if (sub.Matches(header, src))
                {
                    sub.OnReceive(header, data, src, nowMs);
                }
            }
        }

        public void Dispose() => _sock.Dispose();
    }
}
