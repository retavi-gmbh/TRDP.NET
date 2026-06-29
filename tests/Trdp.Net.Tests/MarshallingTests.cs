// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using Trdp.Net.Marshalling;
using Xunit;

namespace Trdp.Net.Tests
{
    public class MarshallingTests
    {
        [Fact]
        public void Wire_IsPackedBigEndian()
        {
            var buf = new byte[5];
            var w = new TrdpWireWriter(buf);
            w.PutUInt8(0xAB);
            w.PutUInt32(0x01020304);
            Assert.Equal(5, w.Position);
            // Gepackt (kein Alignment-Padding) und big-endian:
            Assert.Equal(new byte[] { 0xAB, 0x01, 0x02, 0x03, 0x04 }, buf);
        }

        [Fact]
        public void Wire_AllScalars_RoundTrip()
        {
            var buf = new byte[64];
            var w = new TrdpWireWriter(buf);
            w.PutBool8(true);
            w.PutInt16(-12345);
            w.PutUInt32(0xDEADBEEF);
            w.PutReal32(3.14f);
            w.PutInt64(-1);
            w.PutReal64(2.718281828);
            w.PutTimeDate48(new TrdpTimeDate48(0x11223344, 0x5566));
            w.PutTimeDate64(new TrdpTimeDate64(0x01020304, 0x05060708));

            var r = new TrdpWireReader(buf.AsSpan(0, w.Position));
            Assert.True(r.GetBool8());
            Assert.Equal((short)-12345, r.GetInt16());
            Assert.Equal(0xDEADBEEFu, r.GetUInt32());
            Assert.Equal(3.14f, r.GetReal32());
            Assert.Equal(-1L, r.GetInt64());
            Assert.Equal(2.718281828, r.GetReal64(), 9);
            var td48 = r.GetTimeDate48();
            Assert.Equal(0x11223344u, td48.Seconds);
            Assert.Equal((ushort)0x5566, td48.Ticks);
            var td64 = r.GetTimeDate64();
            Assert.Equal(0x01020304u, td64.Seconds);
            Assert.Equal(0x05060708u, td64.Microseconds);
        }

        [Fact]
        public void TimeDate48_Is6BytesOnWire()
        {
            var buf = new byte[6];
            var w = new TrdpWireWriter(buf);
            w.PutTimeDate48(new TrdpTimeDate48(0x01020304, 0xAABB));
            Assert.Equal(6, w.Position);
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0xAA, 0xBB }, buf);
        }

        [Fact]
        public void Marshaller_ScalarsAndArray_RoundTrip()
        {
            var ds = new TrdpDataset(1001,
                new TrdpDatasetElement(TrdpDataType.UInt32, 1, "id"),
                new TrdpDatasetElement(TrdpDataType.UInt16, 3, "samples"),  // festes Array
                new TrdpDatasetElement(TrdpDataType.Real32, 1, "value"),
                new TrdpDatasetElement(TrdpDataType.BitSet8, 1, "flag"));

            var marshaller = new TrdpMarshaller();
            object[] input = { 0xCAFEu, (ushort)1, (ushort)2, (ushort)3, 1.5f, true };

            byte[] wire = marshaller.Marshal(ds, input);
            Assert.Equal(4 + 3 * 2 + 4 + 1, wire.Length);

            object[] output = marshaller.Unmarshal(ds, wire);
            Assert.Equal(input.Length, output.Length);
            Assert.Equal(0xCAFEu, output[0]);
            Assert.Equal((ushort)1, output[1]);
            Assert.Equal((ushort)3, output[3]);
            Assert.Equal(1.5f, output[4]);
            Assert.Equal(true, output[5]);
        }

        [Fact]
        public void Marshaller_NestedDataset_RoundTrip()
        {
            var child = new TrdpDataset(2002,
                new TrdpDatasetElement(TrdpDataType.UInt8, 1),
                new TrdpDatasetElement(TrdpDataType.UInt16, 1));
            var parent = new TrdpDataset(2001,
                new TrdpDatasetElement(TrdpDataType.UInt32, 1),
                new TrdpDatasetElement(2002, 2));  // verschachtelt, 2x

            var registry = new TrdpDatasetRegistry();
            registry.Add(child);
            registry.Add(parent);
            var marshaller = new TrdpMarshaller(registry);

            object[] input =
            {
                0x11223344u,             // parent uint32
                (byte)0xA1, (ushort)0xB2B2,  // child[0]
                (byte)0xC3, (ushort)0xD4D4,  // child[1]
            };

            byte[] wire = marshaller.Marshal(parent, input);
            Assert.Equal(4 + 2 * (1 + 2), wire.Length);

            object[] output = marshaller.Unmarshal(parent, wire);
            Assert.Equal(0x11223344u, output[0]);
            Assert.Equal((byte)0xA1, output[1]);
            Assert.Equal((ushort)0xB2B2, output[2]);
            Assert.Equal((byte)0xC3, output[3]);
            Assert.Equal((ushort)0xD4D4, output[4]);
        }

        [Fact]
        public void ComputeFixedSize_RejectsVariableArray()
        {
            var ds = new TrdpDataset(3001, new TrdpDatasetElement(TrdpDataType.UInt8, 0)); // Count==0
            var marshaller = new TrdpMarshaller();
            Assert.Throws<NotSupportedException>(() => marshaller.ComputeFixedSize(ds));
        }

        [Fact]
        public void Marshaller_VariableArray_RoundTrips()
        {
            // DE: Count-Element (UINT16) gefolgt von variablem CHAR8-Array (Count==0).
            var ds = new TrdpDataset(3002,
                new TrdpDatasetElement(TrdpDataType.UInt16, 1, "count"),
                new TrdpDatasetElement(TrdpDataType.Char8, 0, "chars")); // var_size aus "count"

            var marshaller = new TrdpMarshaller();
            object[] input = { (ushort)3, (byte)'A', (byte)'B', (byte)'C' };

            byte[] wire = marshaller.Marshal(ds, input);
            Assert.Equal(2 + 3, wire.Length); // UINT16 + 3x CHAR8

            object[] output = marshaller.Unmarshal(ds, wire);
            Assert.Equal(4, output.Length);
            Assert.Equal((ushort)3, output[0]);
            Assert.Equal((byte)'A', output[1]);
            Assert.Equal((byte)'C', output[3]);
        }
    }
}
