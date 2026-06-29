// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Tests fuer den C#-Port von TCNOpen TRDP "Light": trdp/src/common/tau_xmarshall.c.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System.Collections.Generic;
using Trdp.Net.Marshalling;
using Trdp.Net.Tau.XMarshall;
using Trdp.Net.Vos;
using Trdp.Net.Xml;
using Xunit;

namespace Trdp.Net.Tests
{
    public class TauXMarshallTests
    {
        // Angelehnt an XmlConfigTests / TCNOpen trdp/test/xml/nestedDS.xml.
        private const string SampleXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <device host-name="examplehost" leader-name="leaderhost" type="dummy">
                <bus-interface-list>
                    <bus-interface network-id="1" name="en0">
                        <telegram name="tlg1001" com-id="1001" data-set-id="2004" com-parameter-id="1">
                            <pd-parameter cycle="100000" marshall="on" timeout="300000"/>
                            <destination id="1" uri="239.0.1.1"/>
                        </telegram>
                    </bus-interface>
                </bus-interface-list>
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
        public void InitMarshall_BuildsComIdMap()
        {
            var x = TauXMarshall.InitMarshall(TrdpXmlConfig.Parse(SampleXml));
            Assert.Equal(1, x.ComIdCount);
            Assert.NotNull(x.DatasetForComId(1001));
            Assert.Null(x.DatasetForComId(9999));
        }

        [Fact]
        public void MarshalByComId_ThenUnmarshal_RoundTrips()
        {
            var x = new TauXMarshall(TrdpXmlConfig.Parse(SampleXml));

            // DS4 = UINT32 + UINT16[3] + nested DS2 (CHAR8 + INT32)
            object[] input =
            {
                0x01020304u,
                (ushort)10, (ushort)20, (ushort)30,
                (byte)0x41,            // DS2.CHAR8
                -12345,                // DS2.INT32
            };

            byte[] wire = x.MarshalByComId(1001, input);
            // 4 + 3*2 + (1 + 4) = 15 Bytes, big-endian gepackt
            Assert.Equal(15, wire.Length);
            Assert.Equal(0x01, wire[0]); // UINT32 big-endian MSB zuerst

            object[] output = x.UnmarshalByComId(1001, wire);
            Assert.Equal(0x01020304u, output[0]);
            Assert.Equal((ushort)20, output[2]);
            Assert.Equal((byte)0x41, output[4]);
            Assert.Equal(-12345, output[5]);
        }

        [Fact]
        public void CalcDatasetSize_ByComIdAndByDsId_AreEqual()
        {
            var x = new TauXMarshall(TrdpXmlConfig.Parse(SampleXml));
            Assert.Equal(15, x.CalcDatasetSizeByComId(1001));
            Assert.Equal(15, x.CalcDatasetSize(2004));
        }

        [Fact]
        public void UnknownComId_ThrowsComIdErr()
        {
            var x = new TauXMarshall(TrdpXmlConfig.Parse(SampleXml));
            var ex = Assert.Throws<TauXMarshallException>(() => x.UnmarshalByComId(4711, new byte[15]));
            Assert.Equal(TrdpError.ComIdErr, ex.Error);
        }

        [Fact]
        public void EmptyMap_ThrowsInitErr()
        {
            var registry = new TrdpDatasetRegistry();
            var x = new TauXMarshall(registry, new Dictionary<uint, uint>());
            var ex = Assert.Throws<TauXMarshallException>(() => x.MarshalByComId(1001, new object[0]));
            Assert.Equal(TrdpError.InitErr, ex.Error);
        }

        [Fact]
        public void Ctor_FromRegistryAndExplicitMap_Works()
        {
            var registry = new TrdpDatasetRegistry();
            registry.Add(new TrdpDataset(7001,
                new TrdpDatasetElement(TrdpDataType.UInt16, 1, "a"),
                new TrdpDatasetElement(TrdpDataType.Int8, 2, "b")));

            var map = new Dictionary<uint, uint> { [500] = 7001 };
            var x = new TauXMarshall(registry, map);

            object[] input = { (ushort)0xABCD, (sbyte)-1, (sbyte)2 };
            byte[] wire = x.MarshalByComId(500, input);
            Assert.Equal(4, wire.Length); // 2 + 2*1
            Assert.Equal(0xAB, wire[0]);

            object[] output = x.UnmarshalByComId(500, wire);
            Assert.Equal((ushort)0xABCD, output[0]);
            Assert.Equal((sbyte)-1, output[1]);
            Assert.Equal((sbyte)2, output[2]);
        }
    }
}
