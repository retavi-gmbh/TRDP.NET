// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Buffers.Binary;
using System.Net;
using System.Threading;
using Trdp.Net.Core;
using Trdp.Net.Pd;
using Trdp.Net.Vos;
using Xunit;

namespace Trdp.Net.Tests
{
    public class PdTests
    {
        [Fact]
        public void Fcs_IsStoredLittleEndian_OnTheWire()
        {
            // DE: Wire-Quirk: FCS little-endian, alle anderen Felder big-endian.
            var h = new PdHeader
            {
                SequenceCounter = 1,
                ProtocolVersion = TrdpConstants.ProtocolVersion,
                MsgType = TrdpConstants.MsgTypePd,
                ComId = 1000,
                DatasetLength = 0,
            };
            Span<byte> buf = stackalloc byte[PdHeader.Size];
            h.Write(buf);
            h.UpdateFrameCheckSum(buf);

            uint crc = VosCrc32.Compute(buf.Slice(0, PdHeader.Size - TrdpConstants.SizeOfFcs));
            uint storedLe = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(36, 4));

            // FCS ist little-endian gespeichert:
            Assert.Equal(crc, storedLe);
            // ComId-Feld dagegen big-endian:
            Assert.Equal(1000u, BinaryPrimitives.ReadUInt32BigEndian(buf.Slice(8, 4)));
            // Und die Pruefung akzeptiert den Frame:
            Assert.True(PdHeader.VerifyFrameCheckSum(buf));
        }

        [Fact]
        public void PublisherBuild_To_HandleDatagram_RoundTrips()
        {
            using var session = new TrdpPdSession(IPAddress.Loopback, 0);
            var sub = session.Subscribe(comId: 2001);

            byte[]? received = null;
            sub.DataReceived += s => received = (byte[]?)s.LastData?.Clone();

            // Publisher manuell einen Frame bauen lassen und einspeisen.
            var pub = session.Publish(2001, IPAddress.Loopback, cycleTimeMs: 100);
            byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02 };
            pub.SetData(payload);

            var frame = new byte[pub.GrossSize];
            int len = pub.BuildFrame(frame);

            session.HandleDatagram(frame.AsSpan(0, len), IPAddress.Loopback, nowMs: 0);

            Assert.NotNull(received);
            Assert.Equal(payload, received);
            Assert.True(sub.IsValid);
            Assert.Equal(1u, sub.SequenceCounter);
        }

        [Fact]
        public void Subscriber_DetectsDuplicatesAndMissed()
        {
            using var session = new TrdpPdSession(IPAddress.Loopback, 0);
            var sub = session.Subscribe(3001);

            byte[] frameA = BuildFrame(3001, seq: 5, payload: new byte[] { 1 });
            byte[] frameDup = BuildFrame(3001, seq: 5, payload: new byte[] { 9 });
            byte[] frameGap = BuildFrame(3001, seq: 8, payload: new byte[] { 2 });

            session.HandleDatagram(frameA, IPAddress.Loopback, 0);
            Assert.Equal(5u, sub.SequenceCounter);

            session.HandleDatagram(frameDup, IPAddress.Loopback, 0); // Duplikat -> ignoriert
            Assert.Equal((byte)1, sub.LastData![0]);

            session.HandleDatagram(frameGap, IPAddress.Loopback, 0); // Luecke 6,7 fehlen
            Assert.Equal(8u, sub.SequenceCounter);
            Assert.Equal(2u, sub.MissedCount);
        }

        [Fact]
        public void Subscriber_TimesOut()
        {
            using var session = new TrdpPdSession(IPAddress.Loopback, 0);
            var sub = session.Subscribe(4001, timeoutMs: 100);
            bool timedOut = false;
            sub.Timeout += _ => timedOut = true;

            session.HandleDatagram(BuildFrame(4001, 1, new byte[] { 7 }), IPAddress.Loopback, nowMs: 0);
            Assert.True(sub.IsValid);

            session.Process(nowMs: 50);   // noch frisch
            Assert.True(sub.IsValid);

            session.Process(nowMs: 150);  // Ueberwachungszeit ueberschritten
            Assert.False(sub.IsValid);
            Assert.True(timedOut);
        }

        [Fact]
        public void RealLoopback_PublishReceive_OverUdp()
        {
            const int port = 27224;
            using var rx = new TrdpPdSession(IPAddress.Loopback, port);
            using var tx = new TrdpPdSession(IPAddress.Loopback, 0); // ephemerer Quellport

            var sub = rx.Subscribe(5001, timeoutMs: 1000);
            var pub = tx.Publish(5001, IPAddress.Loopback, cycleTimeMs: 10, destPort: port);
            pub.SetData(new byte[] { 0x11, 0x22, 0x33 });

            bool ok = false;
            for (int i = 0; i < 100 && !ok; i++)
            {
                tx.Process();
                rx.Process();
                if (sub.TryGetData(out var d) && d.Length == 3 && d[0] == 0x11)
                {
                    ok = true;
                }
                Thread.Sleep(2);
            }

            Assert.True(ok, "Subscriber hat ueber echten UDP-Loopback keine Daten erhalten.");
        }

        // ── Helfer ──

        private static byte[] BuildFrame(uint comId, uint seq, byte[] payload)
        {
            // DE: Baut einen Frame mit explizitem Sequenzzaehler ueber wiederholtes BuildFrame.
            using var s = new TrdpPdSession(IPAddress.Loopback, 0);
            var pub = s.Publish(comId, IPAddress.Loopback, 0);
            pub.SetData(payload);
            var buf = new byte[pub.GrossSize];
            int len = 0;
            for (uint i = 0; i < seq; i++) // Sequenzzaehler bis 'seq' hochzaehlen
            {
                len = pub.BuildFrame(buf);
            }
            return buf.AsSpan(0, len).ToArray();
        }
    }
}
