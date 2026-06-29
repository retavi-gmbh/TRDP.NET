// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.
//
// Regressionstests zu den Review-Befunden gegen die C-Referenz (PD + MD).

using System;
using System.Net;
using System.Threading;
using Trdp.Net.Core;
using Trdp.Net.Md;
using Trdp.Net.Pd;
using Xunit;

namespace Trdp.Net.Tests
{
    public class ReviewFixesTests
    {
        // ── PD #1: Padding auf 4 Byte (trdp_packetSizePD) ──
        [Fact]
        public void Pd_DataIsPaddedTo4Bytes()
        {
            Assert.Equal(PdHeader.Size + 8, TrdpConstants.PacketSizePd(6)); // 6 -> 8
            Assert.Equal(PdHeader.Size + 4, TrdpConstants.PacketSizePd(4));
            Assert.Equal(PdHeader.Size, TrdpConstants.PacketSizePd(0));

            using var s = new TrdpPdSession(IPAddress.Loopback, 0);
            var pub = s.Publish(1000, IPAddress.Loopback, 0);
            pub.SetData(new byte[] { 1, 2, 3, 4, 5, 6 }); // 6 -> gross 48
            Assert.Equal(PdHeader.Size + 8, pub.GrossSize);

            var frame = new byte[pub.GrossSize];
            int len = pub.BuildFrame(frame);
            Assert.Equal(48, len);
            Assert.Equal(6u, PdHeader.Parse(frame).DatasetLength); // datasetLength ungepaddet
            Assert.Equal(0, frame[46]);                            // Padding genullt
            Assert.Equal(0, frame[47]);
        }

        // ── PD #5: alte/umsortierte Sequenz verwerfen, Restart (seq==0) akzeptieren ──
        [Fact]
        public void Pd_RejectsOldSequence_AcceptsRestart()
        {
            using var s = new TrdpPdSession(IPAddress.Loopback, 0);
            var sub = s.Subscribe(2000);

            s.HandleDatagram(PdFrame(2000, seq: 5, data: new byte[] { 0xA }), IPAddress.Loopback, 0);
            Assert.Equal(5u, sub.SequenceCounter);

            // aelteres (umsortiertes) Telegramm seq=3 -> verwerfen, Zaehler/Daten bleiben
            s.HandleDatagram(PdFrame(2000, seq: 3, data: new byte[] { 0xB }), IPAddress.Loopback, 0);
            Assert.Equal(5u, sub.SequenceCounter);
            Assert.Equal((byte)0xA, sub.LastData![0]);

            // Publisher-Restart seq=0 -> akzeptieren
            s.HandleDatagram(PdFrame(2000, seq: 0, data: new byte[] { 0xC }), IPAddress.Loopback, 0);
            Assert.Equal(0u, sub.SequenceCounter);
            Assert.Equal((byte)0xC, sub.LastData![0]);
        }

        // ── PD #3: Topo-Validierung ──
        [Fact]
        public void Pd_TopoCounterValidation()
        {
            using var s = new TrdpPdSession(IPAddress.Loopback, 0);
            var sub = s.Subscribe(3000, etbTopoCnt: 42);

            s.HandleDatagram(PdFrame(3000, seq: 1, data: new byte[] { 1 }, etbTopo: 42), IPAddress.Loopback, 0);
            Assert.True(sub.IsValid);

            // falscher Topozaehler -> verworfen (Daten unveraendert)
            s.HandleDatagram(PdFrame(3000, seq: 2, data: new byte[] { 9 }, etbTopo: 99), IPAddress.Loopback, 0);
            Assert.Equal((byte)1, sub.LastData![0]);
        }

        // ── PD #7: Protokollversion nur High-Byte (0x01xx) ──
        [Fact]
        public void Pd_AcceptsFutureMinorProtocolVersion()
        {
            using var s = new TrdpPdSession(IPAddress.Loopback, 0);
            var sub = s.Subscribe(4000);
            s.HandleDatagram(PdFrame(4000, seq: 1, data: new byte[] { 7 }, protoVer: 0x0102), IPAddress.Loopback, 0);
            Assert.True(sub.IsValid);
        }

        // ── MD: Infinite-Reply-Timeout wird als 0 auf den Draht kodiert ──
        [Fact]
        public void Md_InfiniteTimeout_EncodedAsZero()
        {
            const int port = 27400;
            using var server = new MdSession(IPAddress.Loopback, port);
            using var client = new MdSession(IPAddress.Loopback, 0);
            uint? seenTimeout = null;
            var l = server.AddListener(5000);
            l.Received += c => seenTimeout = c.Message.ReplyTimeoutUs;

            client.Request(5000, IPAddress.Loopback, Array.Empty<byte>(), replyTimeoutMs: 0, destPort: port);
            for (int i = 0; i < 50 && seenTimeout == null; i++) { server.Process(); Thread.Sleep(2); }
            Assert.Equal(0u, seenTimeout); // unendlich -> 0 (nicht 0xFFFFFFFF)
        }

        [Fact]
        public void Md_FiniteTimeout_EncodedAsMicroseconds()
        {
            const int port = 27401;
            using var server = new MdSession(IPAddress.Loopback, port);
            using var client = new MdSession(IPAddress.Loopback, 0);
            uint? seenTimeout = null;
            var l = server.AddListener(5001);
            l.Received += c => seenTimeout = c.Message.ReplyTimeoutUs;

            client.Request(5001, IPAddress.Loopback, Array.Empty<byte>(), replyTimeoutMs: 50, destPort: port);
            for (int i = 0; i < 50 && seenTimeout == null; i++) { server.Process(); Thread.Sleep(2); }
            Assert.Equal(50_000u, seenTimeout); // 50 ms -> 50000 us
        }

        // ── Helfer: PD-Frame mit gewaehlten Header-Feldern ──
        private static byte[] PdFrame(uint comId, uint seq, byte[] data,
                                      uint etbTopo = 0, uint opTrnTopo = 0, ushort protoVer = 0x0100)
        {
            int gross = TrdpConstants.PacketSizePd(data.Length);
            var buf = new byte[gross];
            var h = new PdHeader
            {
                SequenceCounter = seq,
                ProtocolVersion = protoVer,
                MsgType = TrdpConstants.MsgTypePd,
                ComId = comId,
                EtbTopoCnt = etbTopo,
                OpTrnTopoCnt = opTrnTopo,
                DatasetLength = (uint)data.Length,
            };
            h.Write(buf);
            data.AsSpan().CopyTo(buf.AsSpan(PdHeader.Size, data.Length));
            h.UpdateFrameCheckSum(buf.AsSpan(0, PdHeader.Size));
            return buf;
        }
    }
}
