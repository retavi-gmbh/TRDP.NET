// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using Trdp.Net.Pd;
using Trdp.Net.Tau.XSession;
using Xunit;

namespace Trdp.Net.Tests
{
    public class TauXSessionTests
    {
        // DE: Kleine Config: ein PD-Telegramm (ComId 1000) mit Unicast-Ziel Loopback + passendes Dataset.
        private const string Xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <device host-name="host" type="dummy">
                <bus-interface-list>
                    <bus-interface network-id="1" name="en0">
                        <telegram name="t1" com-id="1000" data-set-id="1000" com-parameter-id="1">
                            <pd-parameter cycle="10000" marshall="on" timeout="1000000"/>
                            <destination id="1" uri="127.0.0.1"/>
                        </telegram>
                    </bus-interface>
                </bus-interface-list>
                <com-parameter-list>
                    <com-parameter id="1" qos="5" ttl="64"/>
                </com-parameter-list>
                <data-set-list>
                    <data-set name="DS1" id="1000">
                        <element name="counter" type="UINT32"/>
                        <element name="value" type="UINT16"/>
                    </data-set>
                </data-set-list>
            </device>
            """;

        [Fact]
        public void Load_SetsUpPublisherSubscriberAndDataset()
        {
            using var x = TauXSession.LoadXml(Xml, IPAddress.Loopback);

            // Publisher aus Destination + Subscriber je Telegramm.
            var pubs = x.GetPublishers(1000);
            Assert.Single(pubs);
            Assert.Equal(1000u, pubs[0].ComId);
            Assert.Equal(IPAddress.Loopback, pubs[0].DestIp);
            Assert.Equal(10, pubs[0].CycleTimeMs); // cycle 10000us / 1000

            Assert.NotNull(x.GetSubscriber(1000));

            // ComId -> DatasetId Lookup.
            Assert.True(x.TryGetDatasetId(1000, out uint dsId));
            Assert.Equal(1000u, dsId);
            Assert.False(x.TryGetDatasetId(9999, out _));

            // Dataset ueber die Config aufloesbar.
            Assert.NotNull(x.Config.DatasetForComId(1000));
        }

        [Fact]
        public void SetComId_Marshals_IntoPublisherFrame()
        {
            using var x = TauXSession.LoadXml(Xml, IPAddress.Loopback);

            x.SetComId(1000, new object[] { 0x11223344u, (ushort)4242 });

            PdPublisher pub = x.GetPublishers(1000)[0];
            Assert.True(pub.DataValid);
            Assert.Equal(6, pub.DataSize); // UINT32 + UINT16
        }

        [Fact]
        public void SetComId_OnUnknownComId_Throws()
        {
            using var x = TauXSession.LoadXml(Xml, IPAddress.Loopback);
            Assert.Throws<InvalidOperationException>(
                () => x.SetComId(4711, new object[] { 0u }));
        }

        [Fact]
        public void GetComId_BeforeAnyData_ReturnsNull()
        {
            using var x = TauXSession.LoadXml(Xml, IPAddress.Loopback);
            Assert.Null(x.GetComId(1000));
        }

        [Fact]
        public void SelfLoopback_SetComId_Then_GetComId_RoundTrips()
        {
            // DE: SetComId marshallt -> Publisher. Dessen Frame wird (wie es der Process-Zyklus an das
            //     Loopback-Ziel senden wuerde) in den eigenen Subscriber eingespeist; GetComId unmarshallt.
            //     Deterministischer Self-Loopback ohne Socket-Race auf dem gemeinsamen PD-Port.
            using var x = TauXSession.LoadXml(Xml, IPAddress.Loopback);

            x.SetComId(1000, new object[] { 0x11223344u, (ushort)4242 });

            PdPublisher pub = x.GetPublishers(1000)[0];
            var frame = new byte[pub.GrossSize];
            int len = pub.BuildFrame(frame);
            x.Session.Pd.HandleDatagram(frame.AsSpan(0, len), IPAddress.Loopback, nowMs: 0);

            object[]? got = x.GetComId(1000);

            Assert.NotNull(got);
            Assert.Equal(0x11223344u, got![0]);
            Assert.Equal((ushort)4242, got[1]);
        }

        [Fact]
        public void NonNumericDestinationUri_CreatesNoPublisher_ButSubscriber()
        {
            // DE: Nicht-numerische URI -> DNR noetig (TODO), daher kein Publisher; Subscriber existiert.
            const string xml = """
                <?xml version="1.0" encoding="UTF-8"?>
                <device host-name="host" type="dummy">
                    <bus-interface-list>
                        <bus-interface network-id="1" name="en0">
                            <telegram name="t1" com-id="2000" data-set-id="1000" com-parameter-id="1">
                                <pd-parameter cycle="10000"/>
                                <destination id="1" uri="some.host.name"/>
                            </telegram>
                        </bus-interface>
                    </bus-interface-list>
                    <data-set-list>
                        <data-set name="DS1" id="1000">
                            <element name="counter" type="UINT32"/>
                        </data-set>
                    </data-set-list>
                </device>
                """;

            using var x = TauXSession.LoadXml(xml, IPAddress.Loopback);
            Assert.Empty(x.GetPublishers(2000));
            Assert.NotNull(x.GetSubscriber(2000));
        }
    }
}
