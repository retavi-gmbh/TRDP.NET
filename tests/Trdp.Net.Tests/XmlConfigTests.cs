// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System.Linq;
using Trdp.Net.Marshalling;
using Trdp.Net.Xml;
using Xunit;

namespace Trdp.Net.Tests
{
    public class XmlConfigTests
    {
        // Angelehnt an TCNOpen trdp/test/xml/nestedDS.xml.
        private const string SampleXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <device host-name="examplehost" leader-name="leaderhost" type="dummy">
                <bus-interface-list>
                    <bus-interface network-id="1" name="en0">
                        <telegram name="tlg1001" com-id="1001" data-set-id="2004" com-parameter-id="1">
                            <pd-parameter cycle="100000" marshall="on" timeout="300000" validity-behavior="keep"/>
                            <destination id="1" uri="239.0.1.1"/>
                        </telegram>
                    </bus-interface>
                </bus-interface-list>
                <com-parameter-list>
                    <com-parameter id="1" qos="5" ttl="64" />
                    <com-parameter id="2" qos="3" ttl="64" />
                </com-parameter-list>
                <data-set-list>
                    <data-set name="DS2" id="2002">
                        <element name="c8-1" type="CHAR8"/>
                        <element name="i32-1" type="INT32"/>
                    </data-set>
                    <data-set name="DS4" id="2004">
                        <element name="u" type="UINT32"/>
                        <element name="samples" type="UINT16" array-size="3"/>
                        <element name="child" type="2002"/>
                    </data-set>
                </data-set-list>
            </device>
            """;

        [Fact]
        public void Parse_ReadsDeviceAttributes()
        {
            var cfg = TrdpXmlConfig.Parse(SampleXml);
            Assert.Equal("examplehost", cfg.HostName);
            Assert.Equal("dummy", cfg.DeviceType);
        }

        [Fact]
        public void Parse_ReadsDatasets_WithTypesAndArraySizeAndNesting()
        {
            var cfg = TrdpXmlConfig.Parse(SampleXml);

            Assert.True(cfg.Datasets.TryGet(2004, out var ds4));
            Assert.Equal(3, ds4.Elements.Count);
            Assert.Equal((uint)TrdpDataType.UInt32, ds4.Elements[0].Type);
            Assert.Equal((uint)TrdpDataType.UInt16, ds4.Elements[1].Type);
            Assert.Equal(3u, ds4.Elements[1].Count);          // array-size
            Assert.Equal(2002u, ds4.Elements[2].Type);        // nested dataset id
            Assert.True(ds4.Elements[2].IsNested);
        }

        [Fact]
        public void Parse_ReadsTelegram_AndComParameters()
        {
            var cfg = TrdpXmlConfig.Parse(SampleXml);

            var tlg = Assert.Single(cfg.Telegrams);
            Assert.Equal(1001u, tlg.ComId);
            Assert.Equal(2004u, tlg.DataSetId);
            Assert.Equal(100000u, tlg.CycleTimeUs);
            Assert.True(tlg.Marshall);
            Assert.Equal("239.0.1.1", Assert.Single(tlg.Destinations).Uri);

            Assert.Equal(2, cfg.ComParameters.Count);
            Assert.Equal(5u, cfg.ComParameters.First(c => c.Id == 1).Qos);
        }

        [Fact]
        public void Integration_XmlDataset_RoundTripsThroughMarshaller()
        {
            var cfg = TrdpXmlConfig.Parse(SampleXml);
            var marshaller = new TrdpMarshaller(cfg.Datasets);

            TrdpDataset ds = cfg.DatasetForComId(1001)!; // DS4
            Assert.NotNull(ds);

            // DS4 = UINT32 + UINT16[3] + nested DS2 (CHAR8 + INT32)
            object[] input =
            {
                0x01020304u,
                (ushort)10, (ushort)20, (ushort)30,
                (byte)0x41,            // DS2.CHAR8
                -12345,                // DS2.INT32
            };

            byte[] wire = marshaller.Marshal(ds, input);
            // 4 + 3*2 + (1 + 4) = 15 Bytes
            Assert.Equal(15, wire.Length);

            object[] output = marshaller.Unmarshal(ds, wire);
            Assert.Equal(0x01020304u, output[0]);
            Assert.Equal((ushort)20, output[2]);
            Assert.Equal((byte)0x41, output[4]);
            Assert.Equal(-12345, output[5]);
        }
    }
}
