// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.
//
// Regressionstests zu den Review-Befunden gegen die C-Referenz (tau_marshall.c):
//  - var_size darf NICHT ueber nested-Dataset-Grenzen lecken (lokal je Aufruf).
//  - var_size wird nur fuer Elemente mit Wire-Groesse 1/2/4 aktualisiert (nicht 64-bit/TIMEDATE48/64).
//  - Verschachtelungstiefe ist begrenzt (TAU_MAX_DS_LEVEL = 5).

using System;
using Trdp.Net.Marshalling;
using Xunit;

namespace Trdp.Net.Tests
{
    public class MarshallVarSizeTests
    {
        [Fact]
        public void VarSize_DoesNotLeakAcrossNestedDataset()
        {
            // child = [UINT8]; parent = [UINT16 count][nested child][INT8 variabel]
            var child = new TrdpDataset(5101, new TrdpDatasetElement(TrdpDataType.UInt8, 1));
            var parent = new TrdpDataset(5100,
                new TrdpDatasetElement(TrdpDataType.UInt16, 1),   // count = 2
                new TrdpDatasetElement(5101, 1),                  // nested (UInt8 = 99 -> darf count NICHT auf 99 setzen)
                new TrdpDatasetElement(TrdpDataType.Int8, 0));    // variabel: nutzt parent-varSize = 2

            var reg = new TrdpDatasetRegistry();
            reg.Add(child); reg.Add(parent);
            var m = new TrdpMarshaller(reg);

            object[] input = { (ushort)2, (byte)99, (sbyte)10, (sbyte)20 };
            byte[] wire = m.Marshal(parent, input);

            // 2 (UINT16) + 1 (child UINT8) + 2 (INT8[2]) = 5  — NICHT 2+1+99
            Assert.Equal(5, wire.Length);

            object[] outp = m.Unmarshal(parent, wire);
            Assert.Equal(4, outp.Length);
            Assert.Equal((ushort)2, outp[0]);
            Assert.Equal((byte)99, outp[1]);
            Assert.Equal((sbyte)10, outp[2]);
            Assert.Equal((sbyte)20, outp[3]);
        }

        [Fact]
        public void VarSize_NotUpdatedBy64BitElement()
        {
            // [UINT32 count=3][INT64 x][UINT8 variabel] -> count bleibt 3 (INT64 aendert var_size nicht)
            var ds = new TrdpDataset(5102,
                new TrdpDatasetElement(TrdpDataType.UInt32, 1),
                new TrdpDatasetElement(TrdpDataType.Int64, 1),
                new TrdpDatasetElement(TrdpDataType.UInt8, 0));
            var m = new TrdpMarshaller();

            object[] input = { 3u, 123L, (byte)1, (byte)2, (byte)3 };
            byte[] wire = m.Marshal(ds, input);
            Assert.Equal(4 + 8 + 3, wire.Length);

            object[] outp = m.Unmarshal(ds, wire);
            Assert.Equal(5, outp.Length);
            Assert.Equal(3u, outp[0]);
            Assert.Equal(123L, outp[1]);
            Assert.Equal((byte)3, outp[4]);
        }

        [Fact]
        public void Nesting_TooDeep_Throws()
        {
            // Selbst-referenzielles Dataset -> Tiefe > 5 -> kontrollierter Fehler statt StackOverflow.
            var reg = new TrdpDatasetRegistry();
            var ds = new TrdpDataset(5103, new TrdpDatasetElement(5103, 1));
            reg.Add(ds);
            var m = new TrdpMarshaller(reg);

            Assert.Throws<InvalidOperationException>(() => m.ComputeSize(ds, new object[] { (byte)0 }));
        }
    }
}
