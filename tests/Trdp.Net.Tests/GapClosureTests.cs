// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using Trdp.Net.Marshalling;
using Trdp.Net.Tau.XMarshall;
using Trdp.Net.Tau.XSession;
using Xunit;

namespace Trdp.Net.Tests
{
    public class GapClosureTests
    {
        [Fact]
        public void Marshaller_ComputeSize_HandlesVariableArrays()
        {
            // UINT16 count + variables CHAR8-Array
            var ds = new TrdpDataset(4100,
                new TrdpDatasetElement(TrdpDataType.UInt16, 1),
                new TrdpDatasetElement(TrdpDataType.Char8, 0));
            var m = new TrdpMarshaller();
            object[] values = { (ushort)5, (byte)1, (byte)2, (byte)3, (byte)4, (byte)5 };

            int size = m.ComputeSize(ds, values);
            byte[] wire = m.Marshal(ds, values);
            Assert.Equal(wire.Length, size);          // wertbasiert == tatsaechliche Wire-Laenge
            Assert.Equal(2 + 5, size);
        }

        [Fact]
        public void XMarshall_CalcDatasetSize_ValueBased_ForVariable()
        {
            const string xml = """
                <device>
                  <bus-interface-list><bus-interface>
                    <telegram com-id="4200" data-set-id="4200"/>
                  </bus-interface></bus-interface-list>
                  <data-set-list>
                    <data-set id="4200">
                      <element type="UINT16"/>
                      <element type="CHAR8" array-size="0"/>
                    </data-set>
                  </data-set-list>
                </device>
                """;
            var x = TauXMarshall.InitMarshall(Trdp.Net.Xml.TrdpXmlConfig.Parse(xml));
            object[] values = { (ushort)3, (byte)9, (byte)8, (byte)7 };
            Assert.Equal(2 + 3, x.CalcDatasetSizeByComId(4200, values));
        }

        [Fact]
        public void XSession_UriResolver_ResolvesNonNumericDestination()
        {
            const string xml = """
                <device>
                  <bus-interface-list><bus-interface>
                    <telegram com-id="4300" data-set-id="4300">
                      <pd-parameter cycle="100000" marshall="on"/>
                      <destination id="1" uri="myhost"/>
                    </telegram>
                  </bus-interface></bus-interface-list>
                  <data-set-list>
                    <data-set id="4300"><element type="UINT32"/></data-set>
                  </data-set-list>
                </device>
                """;
            // Resolver bildet "myhost" auf eine IP ab (analog dnr.IpFromUri).
            using var x = TauXSession.LoadXml(xml, IPAddress.Loopback,
                uri => uri == "myhost" ? IPAddress.Parse("127.0.0.9") : null);

            var pubs = x.GetPublishers(4300);
            Assert.Single(pubs);
            Assert.Equal(IPAddress.Parse("127.0.0.9"), pubs[0].DestIp);
        }

        [Fact]
        public void XSession_SourceUri_BecomesSubscriberFilter()
        {
            const string xml = """
                <device>
                  <bus-interface-list><bus-interface>
                    <telegram com-id="4400" data-set-id="4400">
                      <pd-parameter cycle="100000"/>
                      <source id="1" uri="127.0.0.5"/>
                    </telegram>
                  </bus-interface></bus-interface-list>
                  <data-set-list>
                    <data-set id="4400"><element type="UINT32"/></data-set>
                  </data-set-list>
                </device>
                """;
            using var x = TauXSession.LoadXml(xml, IPAddress.Loopback);
            var sub = x.GetSubscriber(4400);
            Assert.NotNull(sub);
            Assert.Equal(IPAddress.Parse("127.0.0.5"), sub!.SourceFilter);
        }

        [Fact]
        public void XSession_ProcessUntil_ReturnsFalseOnTimeout()
        {
            const string xml = """
                <device>
                  <data-set-list><data-set id="4500"><element type="UINT32"/></data-set></data-set-list>
                </device>
                """;
            using var x = TauXSession.LoadXml(xml, IPAddress.Loopback);
            bool result = x.ProcessUntil(() => false, timeoutMs: 30, stepMs: 5);
            Assert.False(result);
        }
    }
}
