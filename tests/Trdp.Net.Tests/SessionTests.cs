// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System.Net;
using System.Threading;
using Trdp.Net.Core;
using Xunit;

namespace Trdp.Net.Tests
{
    public class SessionTests
    {
        [Fact]
        public void UnifiedSession_PdLoopback_AndStatistics()
        {
            const int port = 27300;
            using var rx = new TrdpSession(IPAddress.Loopback, enableMd: false);
            // rx-PD an festen Port binden:
            using var rxPd = new Trdp.Net.Pd.TrdpPdSession(IPAddress.Loopback, port);
            using var tx = new TrdpSession(IPAddress.Loopback, enableMd: false);

            var sub = rxPd.Subscribe(7777, timeoutMs: 1000);
            var pub = tx.Publish(7777, IPAddress.Loopback, cycleTimeMs: 10, destPort: port);
            pub.SetData(new byte[] { 0xAA });

            bool ok = false;
            for (int i = 0; i < 100 && !ok; i++)
            {
                tx.Process();
                rxPd.Process();
                if (sub.TryGetData(out _)) ok = true;
                Thread.Sleep(2);
            }

            Assert.True(ok);
            var stats = TrdpStatistics.From(tx);
            Assert.True(stats.PdPacketsSent > 0);
        }
    }
}
