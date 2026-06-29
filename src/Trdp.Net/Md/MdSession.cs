// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/trdp_mdcom.c (Frame-Aufbau,
// trdp_mdUpdatePacket, Empfang/Dispatch, Session-Matching, TCP-Reassembly) und tlm_if.c (MD-API).
// UDP und TCP; loop-getrieben ueber Process() (vgl. tlm_process).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using Trdp.Net.Core;
using Trdp.Net.Vos;

namespace Trdp.Net.Md
{
    /// <summary>
    /// DE: MD-Session (Message Data ueber UDP und optional TCP). Verwaltet Sockets, Listener
    /// (Server) und laufende Anfragen (Caller). Nicht thread-safe; aus EINEM Zyklus aufrufen.
    /// </summary>
    public sealed class MdSession : IDisposable
    {
        private readonly VosSock _udp;
        private readonly VosTcpListener? _tcpListener;
        private readonly List<VosTcpConnection> _tcpConnections = new();
        private readonly Dictionary<string, VosTcpConnection> _clientTcp = new();

        private readonly List<MdCaller> _callers = new();
        private readonly List<MdListener> _listeners = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly byte[] _rxBuffer = new byte[TrdpConstants.MaxMdPacketSize];
        private readonly byte[] _txBuffer = new byte[TrdpConstants.MaxMdPacketSize];
        private uint _seqCnt;

        /// <summary>DE: Tatsaechlich gebundener lokaler MD-UDP-Port (bei Bind auf 0 der ephemere).</summary>
        public int Port => _udp.LocalPort;
        public bool TcpEnabled => _tcpListener != null;
        public long NowMs => _clock.ElapsedMilliseconds;

        /// <summary>DE: Anzahl gesendeter MD-Nachrichten (Statistik).</summary>
        public long MessagesSent { get; private set; }

        /// <summary>DE: Anzahl empfangener gueltiger MD-Nachrichten (Statistik).</summary>
        public long MessagesReceived { get; private set; }

        public MdSession(IPAddress? bindAddress = null, int udpPort = TrdpConstants.MdUdpPort,
                         bool enableTcpServer = false, int tcpPort = TrdpConstants.MdTcpPort)
        {
            _udp = VosSock.OpenUdp(bindAddress, udpPort);
            if (enableTcpServer)
            {
                _tcpListener = VosTcpListener.Open(bindAddress, tcpPort);
            }
        }

        /// <summary>DE: Lokaler TCP-Listener-Port (z. B. bei Bind auf 0 in Tests).</summary>
        public int TcpPort => _tcpListener?.Port ?? 0;

        public MdListener AddListener(uint comId)
        {
            var listener = new MdListener(comId);
            _listeners.Add(listener);
            return listener;
        }

        /// <summary>DE: Entfernt einen Listener (tlm_delListener). True, wenn er registriert war.</summary>
        public bool RemoveListener(MdListener listener) => _listeners.Remove(listener);

        /// <summary>DE: ETB-Topozaehler fuer ausgehende MD-Header (0 = ignoriert).</summary>
        public uint EtbTopoCount { get; set; }

        /// <summary>DE: Operational-Train-Topozaehler fuer ausgehende MD-Header.</summary>
        public uint OpTrnTopoCount { get; set; }

        /// <summary>DE: Sendet eine Notification ohne Reply (tlm_notify, Mn).</summary>
        public void Notify(uint comId, IPAddress destIp, ReadOnlySpan<byte> data,
                           string? srcUri = null, string? destUri = null,
                           int destPort = TrdpConstants.MdUdpPort, bool useTcp = false)
        {
            int len = BuildFrame(MdMessageType.Notify, comId, NextSeq(), NewSessionId(), 0u, 0, data, srcUri, destUri);
            ResolveTarget(destIp, destPort, useTcp).Send(_txBuffer.AsSpan(0, len));
            MessagesSent++;
        }

        /// <summary>DE: Sendet einen Request (tlm_request, Mr) und liefert das Caller-Handle.</summary>
        public MdCaller Request(uint comId, IPAddress destIp, ReadOnlySpan<byte> data, int replyTimeoutMs,
                                uint numReplies = 1, string? srcUri = null, string? destUri = null,
                                int destPort = TrdpConstants.MdUdpPort, bool useTcp = false)
        {
            byte[] sessionId = NewSessionId();
            long deadline = replyTimeoutMs <= 0 ? long.MaxValue : NowMs + replyTimeoutMs;
            // DE: Wire-Kodierung fuer "unendlich" ist 0 (NICHT der API-Sentinel 0xFFFFFFFF) — ein Replier
            // interpretiert replyTimeout==0 && msgType==Mr als unendlich (tlm_if.c:340, trdp_mdcom.c:1781).
            uint replyTimeoutUs = replyTimeoutMs <= 0 ? 0u : (uint)replyTimeoutMs * 1000u;

            var caller = new MdCaller(this, comId, sessionId, numReplies, deadline);
            IMdReplyTarget target = ResolveTarget(destIp, destPort, useTcp);
            caller.Transport = useTcp ? target : null;   // TCP: Reply/Confirm ueber dieselbe Verbindung
            _callers.Add(caller);

            int len = BuildFrame(MdMessageType.Request, comId, NextSeq(), sessionId, replyTimeoutUs, 0, data, srcUri, destUri);
            target.Send(_txBuffer.AsSpan(0, len));
            MessagesSent++;
            return caller;
        }

        internal void SendReply(MdMessageType type, MdMessage request, ReadOnlySpan<byte> data, int replyStatus)
        {
            int len = BuildFrame(type, request.ComId, NextSeq(), request.SessionId, 0u, replyStatus, data, null, null);
            IMdReplyTarget target = request.ReplyTarget
                ?? new UdpReplyTarget(this, request.SourceIp, request.SourcePort);
            target.Send(_txBuffer.AsSpan(0, len));
            MessagesSent++;
        }

        internal void SendConfirm(MdCaller caller, int replyStatus)
        {
            int len = BuildFrame(MdMessageType.Confirm, caller.ComId, NextSeq(), caller.SessionId, 0u, replyStatus,
                                 ReadOnlySpan<byte>.Empty, null, null);
            // DE: Confirm (Mc) geht an den FESTEN MD-UDP-Port (17225) — der replyPort-Sonderfall gilt
            // nur fuer Mp/Mq (trdp_mdcom.c:2343-2356). Bei TCP ueber dieselbe Verbindung (caller.Transport).
            IMdReplyTarget target = caller.Transport
                ?? new UdpReplyTarget(this, caller.LastReplyIp, TrdpConstants.MdUdpPort);
            target.Send(_txBuffer.AsSpan(0, len));
            MessagesSent++;
        }

        public void Process() => Process(NowMs);

        internal void Process(long nowMs)
        {
            // UDP-Empfang.
            while (_udp.TryReceive(_rxBuffer, out int length, out IPAddress src, out int srcPort))
            {
                var target = new UdpReplyTarget(this, src, srcPort);
                ParseAndDispatch(_rxBuffer.AsSpan(0, length), target, isTcp: false, nowMs);
            }

            // TCP: neue Verbindungen annehmen.
            if (_tcpListener != null)
            {
                while (_tcpListener.TryAccept(out VosTcpConnection conn))
                {
                    _tcpConnections.Add(conn);
                }
            }

            // TCP: Frames aus allen Verbindungen lesen.
            foreach (VosTcpConnection conn in _tcpConnections)
            {
                while (conn.TryReadFrame(MdFrameLength, out ReadOnlySpan<byte> frame))
                {
                    var target = new TcpReplyTarget(conn);
                    ParseAndDispatch(frame, target, isTcp: true, nowMs);
                    conn.ConsumeFrame();
                }
            }

            // Reply-Timeouts.
            for (int i = _callers.Count - 1; i >= 0; i--)
            {
                MdCaller caller = _callers[i];
                if (caller.IsPending && nowMs >= caller.DeadlineMs)
                {
                    caller.OnTimeout();
                }
                if (!caller.IsPending)
                {
                    _callers.RemoveAt(i);
                }
            }
        }

        /// <summary>DE: Test-Hook: UDP-Datagramm direkt einspeisen (ohne Socket).</summary>
        internal void HandleDatagram(ReadOnlySpan<byte> frame, IPAddress src, int srcPort, long nowMs)
        {
            ParseAndDispatch(frame, new UdpReplyTarget(this, src, srcPort), isTcp: false, nowMs);
        }

        private void ParseAndDispatch(ReadOnlySpan<byte> frame, IMdReplyTarget target, bool isTcp, long nowMs)
        {
            if (frame.Length < TrdpConstants.MinMdPacketSize || frame.Length > TrdpConstants.MaxMdPacketSize)
            {
                return;
            }
            if (!MdHeader.VerifyFrameCheckSum(frame))
            {
                return;
            }

            MdHeader header = MdHeader.Parse(frame);

            // DE: Protokollversion nur im High-Byte pruefen (MASK 0xFF00) wie trdp_mdCheck.
            if ((header.ProtocolVersion & 0xFF00) != (TrdpConstants.ProtocolVersion & 0xFF00))
            {
                return;
            }
            if (!Enum.IsDefined(typeof(MdMessageType), header.MsgType))
            {
                return;
            }
            // DE: Topo-Validierung (trdp_validTopoCounters): erwartete Session-Topo 0 => beliebig,
            // sonst muss der Frame-Topozaehler passen.
            if ((EtbTopoCount != 0 && header.EtbTopoCnt != EtbTopoCount) ||
                (OpTrnTopoCount != 0 && header.OpTrnTopoCnt != OpTrnTopoCount))
            {
                return;
            }

            int dataLen = (int)header.DatasetLength;
            if (dataLen > TrdpConstants.MaxMdDataSize || frame.Length < MdHeader.Size + dataLen)
            {
                return;
            }

            var msg = new MdMessage
            {
                MessageType = (MdMessageType)header.MsgType,
                ComId = header.ComId,
                SessionId = header.SessionId,
                ReplyStatus = header.ReplyStatus,
                Data = frame.Slice(MdHeader.Size, dataLen).ToArray(),
                SourceIp = target.RemoteIp,
                SourcePort = target.RemotePort,
                SequenceCounter = header.SequenceCounter,
                ReplyTimeoutUs = header.ReplyTimeout,
                ReplyTarget = target,
                IsTcp = isTcp,
            };
            MessagesReceived++;

            switch (msg.MessageType)
            {
                case MdMessageType.Request:
                case MdMessageType.Notify:
                    // DE: Ersten passenden Listener nehmen und abbrechen (wie trdp_mdcom.c:1736-1772),
                    // damit auf dieselbe comId nicht mehrfach (ggf. mehrfach geantwortet) wird.
                    foreach (MdListener listener in _listeners)
                    {
                        if (listener.ComId == msg.ComId)
                        {
                            listener.Raise(new MdRequestContext(this, msg));
                            break;
                        }
                    }
                    break;

                case MdMessageType.Reply:
                case MdMessageType.ReplyQuery:
                case MdMessageType.Error:
                    foreach (MdCaller caller in _callers)
                    {
                        if (caller.IsPending && SessionIdEquals(caller.SessionId, msg.SessionId))
                        {
                            caller.OnReply(msg);
                            break;
                        }
                    }
                    break;

                case MdMessageType.Confirm:
                    // Server-seitige Confirm-Behandlung folgt (siehe PORTING.md).
                    break;
            }
        }

        // ── Transport ──

        private IMdReplyTarget ResolveTarget(IPAddress destIp, int destPort, bool useTcp)
        {
            if (!useTcp)
            {
                return new UdpReplyTarget(this, destIp, destPort);
            }
            string key = $"{destIp}:{destPort}";
            if (!_clientTcp.TryGetValue(key, out VosTcpConnection? conn) || !conn.Connected)
            {
                conn = VosTcpConnection.Connect(destIp, destPort);
                _clientTcp[key] = conn;
                _tcpConnections.Add(conn); // auch hier auf Replies lauschen
            }
            return new TcpReplyTarget(conn);
        }

        // DE: Laenge eines MD-Frames aus den Pufferdaten (Header-only -> Header, sonst gepadded).
        private static int MdFrameLength(ReadOnlySpan<byte> buf)
        {
            if (buf.Length < MdHeader.Size) return 0;
            uint dataLen = BinaryPrimitives.ReadUInt32BigEndian(buf.Slice(20, 4)); // datasetLength @ Offset 20
            if (dataLen > TrdpConstants.MaxMdDataSize) return MdHeader.Size; // unplausibel -> nur Header konsumieren
            return TrdpConstants.PacketSizeMd((int)dataLen);
        }

        // ── Frame-Aufbau ──

        private int BuildFrame(MdMessageType type, uint comId, uint seq, byte[] sessionId,
                               uint replyTimeoutUs, int replyStatus, ReadOnlySpan<byte> data,
                               string? srcUri, string? destUri)
        {
            if (data.Length > TrdpConstants.MaxMdDataSize)
                throw new ArgumentException($"MD-Daten zu gross ({data.Length} > {TrdpConstants.MaxMdDataSize}).", nameof(data));

            int gross = TrdpConstants.PacketSizeMd(data.Length);
            Span<byte> span = _txBuffer.AsSpan(0, gross);
            span.Clear();

            var header = new MdHeader
            {
                SequenceCounter = seq,
                ProtocolVersion = TrdpConstants.ProtocolVersion,
                MsgType = (ushort)type,
                ComId = comId,
                EtbTopoCnt = EtbTopoCount,
                OpTrnTopoCnt = OpTrnTopoCount,
                DatasetLength = (uint)data.Length,
                ReplyStatus = replyStatus,
                SessionId = sessionId,
                ReplyTimeout = replyTimeoutUs,
                SourceUri = srcUri != null ? Encoding.ASCII.GetBytes(srcUri) : Array.Empty<byte>(),
                DestinationUri = destUri != null ? Encoding.ASCII.GetBytes(destUri) : Array.Empty<byte>(),
            };

            header.Write(span);
            if (data.Length > 0)
            {
                data.CopyTo(span.Slice(MdHeader.Size, data.Length));
            }
            header.UpdateFrameCheckSum(span.Slice(0, MdHeader.Size));
            return gross;
        }

        private uint NextSeq() => ++_seqCnt;
        private static byte[] NewSessionId() => Guid.NewGuid().ToByteArray();

        private static bool SessionIdEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        public void Dispose()
        {
            _udp.Dispose();
            _tcpListener?.Dispose();
            foreach (VosTcpConnection c in _tcpConnections) c.Dispose();
        }

        // ── Transport-Implementierungen ──

        private sealed class UdpReplyTarget : IMdReplyTarget
        {
            private readonly MdSession _owner;
            public IPAddress RemoteIp { get; }
            public int RemotePort { get; }

            public UdpReplyTarget(MdSession owner, IPAddress ip, int port)
            {
                _owner = owner;
                RemoteIp = ip;
                RemotePort = port;
            }

            public void Send(ReadOnlySpan<byte> frame) => _owner._udp.SendTo(frame, RemoteIp, RemotePort);
        }

        private sealed class TcpReplyTarget : IMdReplyTarget
        {
            private readonly VosTcpConnection _conn;
            public IPAddress RemoteIp => _conn.RemoteIp;
            public int RemotePort => _conn.RemotePort;

            public TcpReplyTarget(VosTcpConnection conn) => _conn = conn;

            public void Send(ReadOnlySpan<byte> frame) => _conn.Send(frame);
        }
    }
}
