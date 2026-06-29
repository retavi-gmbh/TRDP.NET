// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System.Text;
using Trdp.Net.Vos;
using Xunit;

namespace Trdp.Net.Tests
{
    public class VosCrc32Tests
    {
        [Fact]
        public void Crc_Of_123456789_MatchesStandardCheckValue()
        {
            // DE: Standard-CRC-32-Pruefwert (vos_crc32 mit INITFCS, ~crc).
            byte[] data = Encoding.ASCII.GetBytes("123456789");
            uint crc = VosCrc32.Compute(data);
            Assert.Equal(0xCBF43926u, crc);
        }

        [Fact]
        public void Crc_OfEmpty_IsZero()
        {
            // DE: ~(INITFCS) == 0 ueber 0 Bytes.
            uint crc = VosCrc32.Compute(System.ReadOnlySpan<byte>.Empty);
            Assert.Equal(0u, crc);
        }
    }
}
