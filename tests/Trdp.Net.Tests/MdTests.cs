// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using System.Threading;
using Trdp.Net.Core;
using Trdp.Net.Md;
using Xunit;

namespace Trdp.Net.Tests
{
    public class MdTests
    {
        [Fact]
        public void Notify_IsDispatchedToListener_NoReplyExpected()
        {
            using var session = new MdSession(IPAddress.Loopback, 0);
            var listener = session.AddListener(comId: 6001);

            MdRequestContext? ctx = null;
            listener.Received += c => ctx = c;

            byte[] frame = BuildMd(MdMessageType.Notify, 6001, NewSid(), 0, new byte[] { 0xAA, 0xBB });
            session.HandleDatagram(frame, IPAddress.Loopback, srcPort: 50000, nowMs: 0);

            Assert.NotNull(ctx);
            Assert.False(ctx!.ReplyExpected);
            Assert.Equal(MdMessageType.Notify, ctx.Message.MessageType);
            Assert.Equal(new byte[] { 0xAA, 0xBB }, ctx.Message.Data);
        }

        [Fact]
        public void Reply_IsMatchedToCallerBySessionId()
        {
            using var session = new MdSession(IPAddress.Loopback, 0);

            // Caller anlegen, indem ein Request gesendet wird (Ziel egal, kein Server).
            var caller = session.Request(7001, IPAddress.Loopback, new byte[] { 1 }, replyTimeoutMs: 5000, destPort: 1);

            MdMessage? reply = null;
            caller.ReplyReceived += (_, m) => reply = m;

            // Passendes Reply mit gleicher Session-ID injizieren.
            byte[] frame = BuildMd(MdMessageType.Reply, 7001, caller.SessionId, 0, new byte[] { 0x42 });
            session.HandleDatagram(frame, IPAddress.Loopback, srcPort: 50000, nowMs: 0);

            Assert.NotNull(reply);
            Assert.Equal(new byte[] { 0x42 }, reply!.Data);
            Assert.Equal(1u, caller.ReceivedReplies);
            Assert.False(caller.IsPending); // numReplies=1 erreicht
        }

        [Fact]
        public void Request_TimesOut_WhenNoReply()
        {
            using var session = new MdSession(IPAddress.Loopback, 0);
            var caller = session.Request(8001, IPAddress.Loopback, ReadOnlySpan<byte>.Empty, replyTimeoutMs: 100, destPort: 1);

            bool timedOut = false;
            caller.TimedOut += _ => timedOut = true;

            // Zeit weit ueber das Deadline schieben.
            session.Process(nowMs: long.MaxValue / 2);

            Assert.True(timedOut);
            Assert.False(caller.IsPending);
        }

        [Fact]
        public void RealLoopback_RequestReply_OverUdp()
        {
            const int serverPort = 27225;
            using var server = new MdSession(IPAddress.Loopback, serverPort);
            using var client = new MdSession(IPAddress.Loopback, 0);

            var listener = server.AddListener(9001);
            listener.Received += c =>
            {
                Assert.True(c.ReplyExpected);
                c.Reply(new byte[] { 0xC0, 0xDE });
            };

            var caller = client.Request(9001, IPAddress.Loopback, new byte[] { 0x01 },
                                        replyTimeoutMs: 2000, destPort: serverPort);
            byte[]? replyData = null;
            caller.ReplyReceived += (_, m) => replyData = m.Data;

            for (int i = 0; i < 100 && replyData == null; i++)
            {
                server.Process();
                client.Process();
                Thread.Sleep(2);
            }

            Assert.NotNull(replyData);
            Assert.Equal(new byte[] { 0xC0, 0xDE }, replyData);
        }

        [Fact]
        public void RealLoopback_RequestReply_OverTcp()
        {
            const int serverPort = 27226;
            using var server = new MdSession(IPAddress.Loopback, udpPort: 0, enableTcpServer: true, tcpPort: serverPort);
            using var client = new MdSession(IPAddress.Loopback, udpPort: 0);

            var listener = server.AddListener(9101);
            bool sawTcp = false;
            listener.Received += c =>
            {
                sawTcp = c.Message.IsTcp;
                c.Reply(new byte[] { 0xBE, 0xEF });
            };

            var caller = client.Request(9101, IPAddress.Loopback, new byte[] { 0x01 },
                                        replyTimeoutMs: 2000, destPort: serverPort, useTcp: true);
            byte[]? replyData = null;
            caller.ReplyReceived += (_, m) => replyData = m.Data;

            for (int i = 0; i < 200 && replyData == null; i++)
            {
                server.Process();
                client.Process();
                Thread.Sleep(2);
            }

            Assert.True(sawTcp);
            Assert.NotNull(replyData);
            Assert.Equal(new byte[] { 0xBE, 0xEF }, replyData);
        }

        [Fact]
        public void MdFrame_DataIsPaddedTo4Bytes()
        {
            // 3 Byte Daten -> Padding auf 4 -> Gross = 116 + 4.
            Assert.Equal(MdHeader.Size + 4, TrdpConstants.PacketSizeMd(3));
            Assert.Equal(MdHeader.Size + 4, TrdpConstants.PacketSizeMd(4));
            Assert.Equal(MdHeader.Size, TrdpConstants.PacketSizeMd(0));
        }

        // ── Helfer ──

        private static byte[] NewSid() => Guid.NewGuid().ToByteArray();

        private static byte[] BuildMd(MdMessageType type, uint comId, byte[] sessionId, int replyStatus, byte[] data)
        {
            int gross = TrdpConstants.PacketSizeMd(data.Length);
            var buf = new byte[gross];
            var header = new MdHeader
            {
                SequenceCounter = 1,
                ProtocolVersion = TrdpConstants.ProtocolVersion,
                MsgType = (ushort)type,
                ComId = comId,
                DatasetLength = (uint)data.Length,
                ReplyStatus = replyStatus,
                SessionId = sessionId,
                SourceUri = Array.Empty<byte>(),
                DestinationUri = Array.Empty<byte>(),
            };
            header.Write(buf);
            data.AsSpan().CopyTo(buf.AsSpan(MdHeader.Size, data.Length));
            header.UpdateFrameCheckSum(buf.AsSpan(0, MdHeader.Size));
            return buf;
        }
    }
}
