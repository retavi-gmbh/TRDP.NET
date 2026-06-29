// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": Tests fuer tau_dnr.c (Mapping/Parsing-Funktionen).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Trdp.Net.Core;
using Trdp.Net.Tau.Dnr;
using Trdp.Net.Vos;
using Xunit;

namespace Trdp.Net.Tests
{
    public class TauDnrTests
    {
        // ── Standard-DNS Namenskodierung (changetoDnsNameFormat / readName) ───────────────────

        [Fact]
        public void EncodeDnsName_ProducesLengthPrefixedLabels()
        {
            byte[] enc = TauDnrWire.EncodeDnsName("www.newtec.de");
            byte[] expected =
            {
                3, (byte)'w', (byte)'w', (byte)'w',
                6, (byte)'n', (byte)'e', (byte)'w', (byte)'t', (byte)'e', (byte)'c',
                2, (byte)'d', (byte)'e',
                0,
            };
            Assert.Equal(expected, enc);
        }

        [Fact]
        public void DecodeDnsName_RoundTripsEncodedName()
        {
            byte[] enc = TauDnrWire.EncodeDnsName("www.newtec.de");
            // Hinter einem 12-Byte-DNS-Header platzieren.
            var packet = new byte[12 + enc.Length];
            Array.Copy(enc, 0, packet, 12, enc.Length);

            string name = TauDnrWire.DecodeDnsName(packet, 12, out int count);

            Assert.Equal("www.newtec.de", name);
            Assert.Equal(enc.Length, count);   // gesamtes kodiertes Feld inkl. 0-Byte
        }

        [Fact]
        public void DecodeDnsName_FollowsCompressionPointer()
        {
            byte[] enc = TauDnrWire.EncodeDnsName("host.local");
            var packet = new byte[12 + enc.Length + 2];
            Array.Copy(enc, 0, packet, 12, enc.Length);
            // Kompressionszeiger auf Offset 12.
            int ptr = 12 + enc.Length;
            packet[ptr] = 0xC0;
            packet[ptr + 1] = 0x0C;   // 12

            string name = TauDnrWire.DecodeDnsName(packet, ptr, out int count);

            Assert.Equal("host.local", name);
            Assert.Equal(2, count);   // Zeiger = 2 Schritte
        }

        // ── Standard-DNS Query/Response (createSendQuery / parseResponse) ─────────────────────

        [Fact]
        public void BuildDnsQuery_HasHeaderAndQuestion()
        {
            byte[] q = TauDnrWire.BuildDnsQuery(0x1234, "a.b", out int querySize);

            // id big-endian
            Assert.Equal(0x12, q[0]);
            Assert.Equal(0x34, q[1]);
            Assert.Equal(0x01, q[2]);          // Recursion desired
            Assert.Equal(1, (q[4] << 8) | q[5]); // q_count = 1
            // QTYPE = A, QCLASS = IN am Ende
            Assert.Equal(new byte[] { 0, 1, 0, 1 }, q[^4..]);
            // querySize = kodierter Name (inkl. 0) + 4
            Assert.Equal(TauDnrWire.EncodeDnsName("a.b").Length + 4, querySize);
        }

        [Fact]
        public void ParseDnsResponse_ExtractsIPv4Address()
        {
            byte[] enc = TauDnrWire.EncodeDnsName("host.local");
            int querySize = enc.Length + 4;

            var p = new List<byte>();
            // Header: id, param1/2, q_count=1, ans_count=1, auth=0, add=0
            p.AddRange(new byte[] { 0x00, 0x01, 0x81, 0x80, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 });
            // Question: Name + QTYPE + QCLASS
            p.AddRange(enc);
            p.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 });
            // Answer: Pointer auf Offset 12, type=A, class=IN, ttl, data_len=4, IP 192.168.1.50
            p.AddRange(new byte[] { 0xC0, 0x0C });
            p.AddRange(new byte[] { 0x00, 0x01 });             // type A
            p.AddRange(new byte[] { 0x00, 0x01 });             // class IN
            p.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x3C }); // ttl
            p.AddRange(new byte[] { 0x00, 0x04 });             // data_len
            p.AddRange(new byte[] { 192, 168, 1, 50 });

            bool ok = TauDnrWire.ParseDnsResponse(p.ToArray(), querySize, out uint ip);

            Assert.True(ok);
            Assert.Equal(DnrIp.FromHostUint(ip), IPAddress.Parse("192.168.1.50"));
        }

        // ── TCN-DNS Telegramme (buildRequest / parseUpdateTCNResponse) ───────────────────────

        [Fact]
        public void BuildRequestPayload_HasExpectedGeometry()
        {
            var uris = new List<string> { "grpAll.lCst.lTrn", "device1" };
            byte[] payload = TauDnrWire.BuildRequestPayload("myHost", etbTopoCnt: 0x11223344,
                                                            opTrnTopoCnt: 0x55667788, uris);

            int expectedSize = TauDnrWire.DnsTelegramHeaderSize + uris.Count * TauDnrWire.TcnUriSize
                               + TauDnrWire.SafetyTrailSize;
            Assert.Equal(expectedSize, payload.Length);

            Assert.Equal(1, payload[0]);   // version.ver
            Assert.Equal(0, payload[1]);   // version.rel
            // etbId = 255 @ Offset 28, tcnUriCnt @ Offset 31
            Assert.Equal(255, payload[28]);
            Assert.Equal(uris.Count, payload[31]);
            // deviceName @ Offset 4 (ASCII "myHost")
            Assert.Equal("myHost", Encoding.ASCII.GetString(payload, 4, 6));
            // erste URI @ Offset 32
            Assert.Equal("grpAll.lCst.lTrn", Encoding.ASCII.GetString(payload, 32, "grpAll.lCst.lTrn".Length));
        }

        [Fact]
        public void ParseReply_ReadsResolvedAddresses()
        {
            // Reply-Telegramm zusammenbauen (Header + 2 TCN_URI_T + SafetyTrail).
            var reply = BuildTcnReply(
                etbTopoCnt: 0xAABBCCDD, opTrnTopoCnt: 0x01020304,
                records: new[]
                {
                    ("device1", (short)0, IPAddress.Parse("10.0.8.35")),
                    ("device2", (short)-1, IPAddress.Any),       // nicht aufgeloest
                });

            TauDnrWire.ParseReply(reply, out uint etb, out uint opTrn, out List<TcnUriRecord> recs);

            Assert.Equal(0xAABBCCDDu, etb);
            Assert.Equal(0x01020304u, opTrn);
            Assert.Equal(2, recs.Count);
            Assert.Equal("device1", recs[0].Uri);
            Assert.Equal(0, recs[0].ResolvState);
            Assert.Equal(IPAddress.Parse("10.0.8.35"), DnrIp.FromHostUint(recs[0].IpAddr));
            Assert.Equal(-1, recs[1].ResolvState);
        }

        // ── IP-Adress-Helfer (vos_dottedIP) ──────────────────────────────────────────────────

        [Theory]
        [InlineData("10.0.8.35", true)]
        [InlineData("255.255.255.255", true)]
        [InlineData("0.0.0.0", false)]        // == INADDR_ANY -> ungueltig
        [InlineData("www.newtec.de", false)]
        [InlineData("10.0.8", false)]
        [InlineData("256.1.1.1", false)]
        public void DottedIp_ParsesOnlyValidDottedQuads(string input, bool valid)
        {
            uint result = DnrIp.DottedIp(input);
            Assert.Equal(valid, result != 0u);
            if (valid)
            {
                Assert.Equal(IPAddress.Parse(input), DnrIp.FromHostUint(result));
            }
        }

        [Fact]
        public void HostUint_RoundTripsThroughIpAddress()
        {
            var ip = IPAddress.Parse("172.16.250.9");
            Assert.Equal(ip, DnrIp.FromHostUint(DnrIp.ToHostUint(ip)));
        }

        // ── Instanz-Verhalten (tau_uri2Addr / tau_addr2Uri / tau_DNRstatus) ──────────────────

        [Fact]
        public void Uri2Addr_ReturnsDottedIpDirectly_WithoutDns()
        {
            using var session = new TrdpSession(IPAddress.Loopback, enableMd: false);
            using var dnr = new TauDnr(session, dnsIpAddr: null, dnsPort: 0, hostsFileName: null,
                                       dnsOptions: TrdpDnrOptions.StandardDns, waitForDnr: false);

            TrdpError err = dnr.Uri2Addr(out IPAddress addr, "192.168.5.7");

            Assert.Equal(TrdpError.NoErr, err);
            Assert.Equal(IPAddress.Parse("192.168.5.7"), addr);
        }

        [Fact]
        public void Uri2Addr_NullUri_ReturnsOwnAddress()
        {
            using var session = new TrdpSession(IPAddress.Loopback, enableMd: false);
            using var dnr = new TauDnr(session, dnsIpAddr: null, dnsPort: 0, hostsFileName: null,
                                       dnsOptions: TrdpDnrOptions.StandardDns, waitForDnr: false);

            TrdpError err = dnr.Uri2Addr(out IPAddress addr, null);

            Assert.Equal(TrdpError.NoErr, err);
            Assert.Equal(IPAddress.Loopback, addr);
        }

        [Fact]
        public void HostsFile_PopulatesCache_AndEnablesForwardAndReverseLookup()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "# Kommentarzeile",
                    "10.0.8.35   device1.lCst.lTrn",
                    "10.0.8.36   device2.lCst.lTrn",
                });

                using var session = new TrdpSession(IPAddress.Loopback, enableMd: false);
                using var dnr = new TauDnr(session, dnsIpAddr: null, dnsPort: 0, hostsFileName: path,
                                           dnsOptions: TrdpDnrOptions.StandardDns, waitForDnr: true);

                Assert.Equal(2, dnr.CachedEntryCount);
                Assert.Equal(TrdpDnrState.HostsFile, dnr.DnrStatus());

                // Vorwaerts: URI -> IP (fester Eintrag, keine DNS-Abfrage).
                Assert.Equal(TrdpError.NoErr, dnr.Uri2Addr(out IPAddress addr, "device1.lCst.lTrn"));
                Assert.Equal(IPAddress.Parse("10.0.8.35"), addr);

                // Rueckwaerts: IP -> URI.
                Assert.Equal(TrdpError.NoErr, dnr.Addr2Uri(out string uri, IPAddress.Parse("10.0.8.36")));
                Assert.Equal("device2.lCst.lTrn", uri);

                // Unbekannte Adresse -> UnresolvedErr.
                Assert.Equal(TrdpError.UnresolvedErr, dnr.Addr2Uri(out _, IPAddress.Parse("10.0.8.99")));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Uri2Addr_TcnDnsWithoutMdSession_ReturnsUnresolved()
        {
            // Ohne MD-Session kann TCN-DNS nicht aufloesen -> schnell UnresolvedErr (kein Hang).
            using var session = new TrdpSession(IPAddress.Loopback, enableMd: false);
            using var dnr = new TauDnr(session, dnsIpAddr: null, dnsPort: 0, hostsFileName: null,
                                       dnsOptions: TrdpDnrOptions.CommonThread, waitForDnr: false);

            TrdpError err = dnr.Uri2Addr(out IPAddress addr, "unknown.host.tcn");

            Assert.Equal(TrdpError.UnresolvedErr, err);
            Assert.Equal(IPAddress.Any, addr);
        }

        // ── Helfer ───────────────────────────────────────────────────────────────────────────

        // DE: Baut ein TCN-DNS-Reply-Telegramm (big-endian, gepackt) fuer die Tests.
        private static byte[] BuildTcnReply(uint etbTopoCnt, uint opTrnTopoCnt,
                                            (string uri, short resolvState, IPAddress ip)[] records)
        {
            int size = TauDnrWire.DnsTelegramHeaderSize + records.Length * TauDnrWire.TcnUriSize
                       + TauDnrWire.SafetyTrailSize;
            var buf = new byte[size];

            buf[0] = 1;   // version.ver
            buf[1] = 0;   // version.rel
            // reserved01 @2..3, deviceName @4..19 (0)
            WriteBe32(buf, 20, etbTopoCnt);
            WriteBe32(buf, 24, opTrnTopoCnt);
            buf[28] = 255;                 // etbId
            buf[29] = 0;                   // dnsStatus
            buf[30] = 0;                   // reserved02
            buf[31] = (byte)records.Length; // tcnUriCnt

            int off = TauDnrWire.DnsTelegramHeaderSize;
            foreach ((string uri, short resolvState, IPAddress ip) in records)
            {
                byte[] name = Encoding.ASCII.GetBytes(uri);
                Array.Copy(name, 0, buf, off, Math.Min(name.Length, TauDnrConstants.MaxUriHostLen));
                // reserved01 @ off+80, resolvState @ off+82
                WriteBe16(buf, off + 82, (ushort)resolvState);
                WriteBe32(buf, off + 84, DnrIp.ToHostUint(ip));
                WriteBe32(buf, off + 88, 0u);
                off += TauDnrWire.TcnUriSize;
            }
            return buf;
        }

        private static void WriteBe16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)(v >> 8);
            b[o + 1] = (byte)v;
        }

        private static void WriteBe32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }
    }
}
