// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/trdp_stats.c, api/trdp_types.h
// (TRDP_STATISTICS_T). Schlanker Snapshot der zur Laufzeit gefuehrten Zaehler.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using Trdp.Net.Md;
using Trdp.Net.Pd;

namespace Trdp.Net.Core
{
    /// <summary>
    /// DE: Momentaufnahme der TRDP-Statistik (Teil-Port von TRDP_STATISTICS_T).
    /// </summary>
    public sealed class TrdpStatistics
    {
        public long PdPacketsSent { get; init; }
        public long PdPacketsReceived { get; init; }
        public long MdMessagesSent { get; init; }
        public long MdMessagesReceived { get; init; }

        /// <summary>DE: Erstellt einen Snapshot aus einer vereinheitlichten Session (tlc_getStatistics).</summary>
        public static TrdpStatistics From(TrdpSession session) => new()
        {
            PdPacketsSent = session.Pd.PacketsSent,
            PdPacketsReceived = session.Pd.PacketsReceived,
            MdMessagesSent = session.Md?.MessagesSent ?? 0,
            MdMessagesReceived = session.Md?.MessagesReceived ?? 0,
        };

        /// <summary>DE: Snapshot direkt aus PD-/MD-Session.</summary>
        public static TrdpStatistics From(TrdpPdSession pd, MdSession? md = null) => new()
        {
            PdPacketsSent = pd.PacketsSent,
            PdPacketsReceived = pd.PacketsReceived,
            MdMessagesSent = md?.MessagesSent ?? 0,
            MdMessagesReceived = md?.MessagesReceived ?? 0,
        };
    }
}
