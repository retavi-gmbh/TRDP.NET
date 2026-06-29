// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using Trdp.Net.Core;
using Xunit;

namespace Trdp.Net.Tests
{
    public class HeaderTests
    {
        [Fact]
        public void PdHeader_WriteParse_RoundTrips()
        {
            var original = new PdHeader
            {
                SequenceCounter = 0x01020304,
                ProtocolVersion = TrdpConstants.ProtocolVersion,
                MsgType = TrdpConstants.MsgTypePd,
                ComId = 1000,
                EtbTopoCnt = 0,
                OpTrnTopoCnt = 0,
                DatasetLength = 32,
                Reserved = 0,
                ReplyComId = 0,
                ReplyIpAddress = 0,
                FrameCheckSum = 0,
            };

            Span<byte> buffer = stackalloc byte[PdHeader.Size];
            original.Write(buffer);
            var parsed = PdHeader.Parse(buffer);

            Assert.Equal(original.SequenceCounter, parsed.SequenceCounter);
            Assert.Equal(original.ProtocolVersion, parsed.ProtocolVersion);
            Assert.Equal(original.MsgType, parsed.MsgType);
            Assert.Equal(original.ComId, parsed.ComId);
            Assert.Equal(original.DatasetLength, parsed.DatasetLength);
        }

        [Fact]
        public void PdHeader_Fcs_ComputeThenVerify_Succeeds()
        {
            var h = new PdHeader
            {
                SequenceCounter = 42,
                ProtocolVersion = TrdpConstants.ProtocolVersion,
                MsgType = TrdpConstants.MsgTypePd,
                ComId = 12345,
                DatasetLength = 16,
            };

            Span<byte> buffer = stackalloc byte[PdHeader.Size];
            h.Write(buffer);
            h.UpdateFrameCheckSum(buffer);

            Assert.True(PdHeader.VerifyFrameCheckSum(buffer));

            // DE: Ein gekipptes Bit muss die Pruefung scheitern lassen.
            buffer[10] ^= 0x01;
            Assert.False(PdHeader.VerifyFrameCheckSum(buffer));
        }

        [Fact]
        public void MdHeader_Fcs_ComputeThenVerify_Succeeds()
        {
            var h = new MdHeader
            {
                SequenceCounter = 7,
                ProtocolVersion = TrdpConstants.ProtocolVersion,
                MsgType = TrdpConstants.MsgTypeMr,
                ComId = 2000,
                DatasetLength = 8,
                ReplyTimeout = 1_000_000,
                SessionId = new byte[16],
                SourceUri = System.Text.Encoding.ASCII.GetBytes("dev@consist"),
                DestinationUri = System.Text.Encoding.ASCII.GetBytes("dev@train"),
            };

            Span<byte> buffer = stackalloc byte[MdHeader.Size];
            h.Write(buffer);
            h.UpdateFrameCheckSum(buffer);

            Assert.True(MdHeader.VerifyFrameCheckSum(buffer));
            Assert.Equal(MdHeader.Size, buffer.Length);

            var parsed = MdHeader.Parse(buffer);
            Assert.Equal(h.ComId, parsed.ComId);
            Assert.Equal(h.ReplyTimeout, parsed.ReplyTimeout);
        }
    }
}
