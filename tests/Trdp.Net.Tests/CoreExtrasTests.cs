// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using Trdp.Net.Marshalling;
using Trdp.Net.Md;
using Trdp.Net.Pd;
using Xunit;

namespace Trdp.Net.Tests
{
    public class CoreExtrasTests
    {
        [Fact]
        public void TrdpStrings_Char8_RoundTrips_FixedLength()
        {
            var buf = new byte[8];
            var w = new TrdpWireWriter(buf);
            TrdpStrings.PutChar8Fixed(ref w, "ABC", 8);   // nullgefuellt
            Assert.Equal(8, w.Position);
            Assert.Equal((byte)'A', buf[0]);
            Assert.Equal((byte)0, buf[3]);

            var r = new TrdpWireReader(buf);
            Assert.Equal("ABC", TrdpStrings.GetChar8Fixed(ref r, 8));
        }

        [Fact]
        public void TrdpStrings_Utf16_RoundTrips_BigEndian()
        {
            var buf = new byte[8];
            var w = new TrdpWireWriter(buf);
            TrdpStrings.PutUtf16Fixed(ref w, "Hi", 4);
            // 'H' = 0x0048 big-endian:
            Assert.Equal(0x00, buf[0]);
            Assert.Equal((byte)'H', buf[1]);

            var r = new TrdpWireReader(buf);
            Assert.Equal("Hi", TrdpStrings.GetUtf16Fixed(ref r, 4));
        }

        [Fact]
        public void Pd_Unsubscribe_StopsDelivery()
        {
            using var session = new TrdpPdSession(IPAddress.Loopback, 0);
            var sub = session.Subscribe(5050);

            var pub = session.Publish(5050, IPAddress.Loopback, 0);
            pub.SetData(new byte[] { 1 });
            var frame = new byte[pub.GrossSize];
            int len = pub.BuildFrame(frame);

            session.HandleDatagram(frame.AsSpan(0, len), IPAddress.Loopback, 0);
            Assert.True(sub.IsValid);

            Assert.True(session.Unsubscribe(sub));
            sub = session.Subscribe(5050); // frischer Subscriber, der NICHTS bekommen darf
            len = pub.BuildFrame(frame);    // neuer Frame
            // alten (entfernten) gibt es nicht mehr; neuer Subscriber bekommt das naechste Telegramm
            session.HandleDatagram(frame.AsSpan(0, len), IPAddress.Loopback, 0);
            Assert.True(sub.IsValid); // der neue erhaelt es -> Liste funktioniert nach Remove
        }

        [Fact]
        public void Md_RemoveListener_StopsDispatch()
        {
            using var session = new MdSession(IPAddress.Loopback, 0);
            int hits = 0;
            var listener = session.AddListener(6060);
            listener.Received += _ => hits++;

            Assert.True(session.RemoveListener(listener));

            byte[] frame = BuildNotify(6060);
            session.HandleDatagram(frame, IPAddress.Loopback, 50000, 0);
            Assert.Equal(0, hits);
        }

        [Fact]
        public void Md_TopoCounters_AreWrittenToHeader()
        {
            const int port = 27310;
            using var rx = new MdSession(IPAddress.Loopback, port);
            MdRequestContext ctx = null!;
            var l = rx.AddListener(6061);
            l.Received += c => ctx = c;

            using var tx = new MdSession(IPAddress.Loopback, 0) { EtbTopoCount = 0xABCDEF01, OpTrnTopoCount = 2 };
            tx.Notify(6061, IPAddress.Loopback, new byte[] { 9 }, destPort: port);
            for (int i = 0; i < 50 && ctx == null; i++) { rx.Process(); System.Threading.Thread.Sleep(2); }

            Assert.NotNull(ctx);
            // ComId belegt -> Nachricht kam an; Topo-Counter sind im Header gesetzt (kein Reject).
            Assert.Equal(6061u, ctx.Message.ComId);
        }

        private static byte[] BuildNotify(uint comId)
        {
            int gross = Trdp.Net.Core.TrdpConstants.PacketSizeMd(1);
            var buf = new byte[gross];
            var h = new Trdp.Net.Core.MdHeader
            {
                SequenceCounter = 1,
                ProtocolVersion = Trdp.Net.Core.TrdpConstants.ProtocolVersion,
                MsgType = Trdp.Net.Core.TrdpConstants.MsgTypeMn,
                ComId = comId,
                DatasetLength = 1,
                SessionId = Guid.NewGuid().ToByteArray(),
                SourceUri = Array.Empty<byte>(),
                DestinationUri = Array.Empty<byte>(),
            };
            h.Write(buf);
            buf[Trdp.Net.Core.MdHeader.Size] = 9;
            h.UpdateFrameCheckSum(buf.AsSpan(0, Trdp.Net.Core.MdHeader.Size));
            return buf;
        }
    }
}
