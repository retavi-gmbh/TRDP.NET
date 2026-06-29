// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Tests fuer den C#-Port von TCNOpen TRDP "Light": tau_so_if.c / trdp_serviceRegistry.h.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using System.Threading;
using Trdp.Net.Core;
using Trdp.Net.Marshalling;
using Trdp.Net.Md;
using Trdp.Net.Tau.SoIf;
using Trdp.Net.Vos;
using Xunit;

namespace Trdp.Net.Tests
{
    public class TauSoIfTests
    {
        // ── Wire-Layout-Konstanten ──

        [Fact]
        public void ServiceInfo_WireSize_Is64Bytes()
        {
            Assert.Equal(64, SrmServiceInfo.WireSize);
        }

        [Fact]
        public void ServiceEntries_HeaderSize_Is4Bytes()
        {
            Assert.Equal(4, SrmServiceEntries.HeaderSize);
        }

        // ── SOA-Makros ──

        [Fact]
        public void Soa_ServiceId_PacksInstanceAndType()
        {
            uint id = SrmServiceRegistry.ServiceId(0x12, 0xABCDEF);
            Assert.Equal(0x12ABCDEFu, id);
            Assert.Equal((byte)0x12, SrmServiceRegistry.ServiceInstance(id));
            Assert.Equal(0xABCDEFu, SrmServiceRegistry.ServiceType(id));
        }

        [Fact]
        public void Soa_ServiceId_MasksTypeTo24Bit()
        {
            // DE: Typ-Anteil wird auf 24 Bit maskiert; ueberlaufende Bits gehen verloren.
            uint id = SrmServiceRegistry.ServiceId(0x01, 0xFFABCDEF);
            Assert.Equal(0x01ABCDEFu, id);
        }

        [Fact]
        public void Soa_SameServiceIdOr0_AndSameType()
        {
            Assert.True(SrmServiceRegistry.SameServiceIdOr0(0u, 0x123u));
            Assert.True(SrmServiceRegistry.SameServiceIdOr0(0x123u, 0x123u));
            Assert.False(SrmServiceRegistry.SameServiceIdOr0(0x123u, 0x124u));

            uint a = SrmServiceRegistry.ServiceId(1, 0x42);
            uint b = SrmServiceRegistry.ServiceId(2, 0x42);
            Assert.True(SrmServiceRegistry.SameServiceType(a, b));
        }

        // ── Encode/Decode der Strukturen ──

        [Fact]
        public void ServiceInfo_RoundTrip_PreservesAllFields()
        {
            SrmServiceInfo info = SampleInfo();

            var buf = new byte[SrmServiceInfo.WireSize];
            var w = new TrdpWireWriter(buf);
            info.Encode(ref w);
            Assert.Equal(SrmServiceInfo.WireSize, w.Position);

            var r = new TrdpWireReader(buf);
            SrmServiceInfo back = SrmServiceInfo.Decode(ref r);

            Assert.Equal(info.SrvName, back.SrvName);
            Assert.Equal(info.ServiceId, back.ServiceId);
            Assert.Equal(info.SrvVers.Ver, back.SrvVers.Ver);
            Assert.Equal(info.SrvVers.Rel, back.SrvVers.Rel);
            Assert.Equal(info.SrvFlags, back.SrvFlags);
            Assert.Equal(info.SrvTtl.Seconds, back.SrvTtl.Seconds);
            Assert.Equal(info.SrvTtl.Microseconds, back.SrvTtl.Microseconds);
            Assert.Equal(info.FctDev, back.FctDev);
            Assert.Equal(info.CstVehNo, back.CstVehNo);
            Assert.Equal(info.CstNo, back.CstNo);
            Assert.Equal(info.AddInfo, back.AddInfo);
        }

        [Fact]
        public void ServiceInfo_Encode_IsBigEndian()
        {
            var info = new SrmServiceInfo { ServiceId = 0x11223344u };
            var buf = new byte[SrmServiceInfo.WireSize];
            var w = new TrdpWireWriter(buf);
            info.Encode(ref w);

            // serviceId steht direkt hinter dem 16-Byte-Label (srvName), big-endian.
            Assert.Equal(0x11, buf[16]);
            Assert.Equal(0x22, buf[17]);
            Assert.Equal(0x33, buf[18]);
            Assert.Equal(0x44, buf[19]);
        }

        [Fact]
        public void ServiceEntries_RoundTrip_TwoEntries()
        {
            var entries = new SrmServiceEntries { Version = new SrmShortVersion(1, 0) };
            entries.Entries.Add(SampleInfo());
            var second = SampleInfo();
            second.SrvName = "DNS";
            second.ServiceId = SrmServiceRegistry.ServiceId(2, 140);
            entries.Entries.Add(second);

            Assert.Equal(SrmServiceEntries.HeaderSize + 2 * SrmServiceInfo.WireSize, entries.WireSize);

            byte[] data = entries.Encode();
            SrmServiceEntries back = SrmServiceEntries.Decode(data);

            Assert.Equal(2, back.NoOfEntries);
            Assert.Equal("TTDB-OpTrnInf", back.Entries[0].SrvName);
            Assert.Equal("DNS", back.Entries[1].SrvName);
            Assert.Equal(SrmServiceRegistry.ServiceId(2, 140), back.Entries[1].ServiceId);
        }

        [Fact]
        public void ServiceEntries_Header_NoOfEntriesIsBigEndian()
        {
            var entries = new SrmServiceEntries();
            entries.Entries.Add(SampleInfo());
            byte[] data = entries.Encode();

            // version(2) dann noOfEntries(2, big-endian) = 0x0001.
            Assert.Equal(1, entries.Version.Ver);
            Assert.Equal(0x00, data[2]);
            Assert.Equal(0x01, data[3]);
        }

        [Fact]
        public void ServiceEntries_Decode_TruncatedBuffer_StopsGracefully()
        {
            var entries = new SrmServiceEntries();
            entries.Entries.Add(SampleInfo());
            entries.Entries.Add(SampleInfo());
            byte[] data = entries.Encode();

            // DE: Letzten Eintrag abschneiden -> noOfEntries-Feld sagt 2, Puffer reicht nur fuer 1.
            var truncated = new byte[SrmServiceEntries.HeaderSize + SrmServiceInfo.WireSize + 10];
            Array.Copy(data, truncated, truncated.Length);

            SrmServiceEntries back = SrmServiceEntries.Decode(truncated);
            Assert.Single(back.Entries);
        }

        [Fact]
        public void ServiceEntries_Decode_TooShort_Throws()
        {
            Assert.Throws<ArgumentException>(() => SrmServiceEntries.Decode(new byte[3]));
        }

        // ── API-Parameterpruefung ──

        [Fact]
        public void AddService_NullArgs_ReturnsParamErr()
        {
            using var session = new TrdpSession(IPAddress.Loopback, enableMd: true);
            Assert.Equal(TrdpError.ParamErr,
                TauSoIf.AddService(session, IPAddress.Loopback, null!, waitForCompletion: false));
        }

        [Fact]
        public void GetServicesList_AnyAddress_ReturnsUnresolved()
        {
            using var session = new TrdpSession(IPAddress.Loopback, enableMd: true);
            TrdpError err = TauSoIf.GetServicesList(session, IPAddress.Any,
                out SrmServiceEntries? list, out uint count);
            Assert.Equal(TrdpError.UnresolvedErr, err);
            Assert.Null(list);
            Assert.Equal(0u, count);
        }

        [Fact]
        public void FreeServicesList_SetsReferenceNull()
        {
            SrmServiceEntries? list = new SrmServiceEntries();
            TauSoIf.FreeServicesList(ref list);
            Assert.Null(list);
        }

        // ── Ende-zu-Ende ueber lokale MD-Sessions (Add mit Wait) ──

        [Fact]
        public void AddService_WithWait_ReceivesReplyAndUpdatesInstanceId()
        {
            const int srmPort = 27240;

            // DE: SRM-Server simulieren: lauscht auf ADD-Request, dekodiert, vergibt eine
            // instanceId und antwortet mit den aktualisierten Service-Daten.
            using var srm = new MdSession(IPAddress.Loopback, srmPort);
            MdListener listener = srm.AddListener(SrmServiceRegistry.AddReqComId);
            listener.Received += ctx =>
            {
                SrmServiceEntries req = SrmServiceEntries.Decode(ctx.Message.Data);
                SrmServiceInfo entry = req.Entries[0];
                entry.ServiceId = SrmServiceRegistry.ServiceId(0x07, entry.ServiceTypeId);
                var reply = new SrmServiceEntries();
                reply.Entries.Add(entry);
                ctx.Reply(reply.Encode());
            };

            using var client = new TrdpSession(IPAddress.Loopback, enableMd: true);

            var service = SampleInfo();
            service.ServiceId = SrmServiceRegistry.ServiceId(0, 100); // Instanz noch 0

            // DE: Da AddService blockierend Process() pumpt, muss der Server parallel laufen.
            using var stop = new ManualResetEventSlim(false);
            var srmThread = new Thread(() =>
            {
                while (!stop.IsSet)
                {
                    srm.Process();
                    Thread.Sleep(2);
                }
            }) { IsBackground = true };
            srmThread.Start();

            TrdpError err = TauSoIf.AddService(client, IPAddress.Loopback, service,
                waitForCompletion: true, srmPort: srmPort);
            stop.Set();
            srmThread.Join(1000);

            Assert.Equal(TrdpError.NoErr, err);
            Assert.Equal((byte)0x07, service.InstanceId);
        }

        [Fact]
        public void AddService_NoWait_SendsRequest_ServerReceivesIt()
        {
            const int srmPort = 27241;
            using var srm = new MdSession(IPAddress.Loopback, srmPort);
            SrmServiceEntries? received = null;
            MdListener listener = srm.AddListener(SrmServiceRegistry.AddReqComId);
            listener.Received += ctx => received = SrmServiceEntries.Decode(ctx.Message.Data);

            using var client = new TrdpSession(IPAddress.Loopback, enableMd: true);

            TrdpError err = TauSoIf.AddService(client, IPAddress.Loopback, SampleInfo(),
                waitForCompletion: false, srmPort: srmPort);
            Assert.Equal(TrdpError.NoErr, err);

            for (int i = 0; i < 100 && received == null; i++)
            {
                srm.Process();
                Thread.Sleep(2);
            }

            Assert.NotNull(received);
            Assert.Equal("TTDB-OpTrnInf", received!.Entries[0].SrvName);
        }

        // ── Helfer ──

        private static SrmServiceInfo SampleInfo()
        {
            var info = new SrmServiceInfo
            {
                SrvName = "TTDB-OpTrnInf",
                ServiceId = SrmServiceRegistry.ServiceId(1, 100),
                SrvVers = new SrmShortVersion(1, 0),
                SrvFlags = SrmServiceRegistry.FlagEvent | SrmServiceRegistry.FlagFields,
                SrvTtl = new TrdpTimeDate64(0x12345678u, 0x0000ABCDu),
                FctDev = "ecsp",
                CstVehNo = 3,
                CstNo = 2,
            };
            info.AddInfo[0] = 0xAABBCCDDu;
            info.AddInfo[1] = 0x01020304u;
            info.AddInfo[2] = 0xDEADBEEFu;
            return info;
        }
    }
}
