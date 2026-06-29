// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tlc_if.c (Session-Lifecycle:
// tlc_openSession/closeSession/process/getInterval, Topo-Counter, eigene IP).
// Vereinheitlicht PD- und MD-Session unter einem Handle (appHandle-Aequivalent).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using Trdp.Net.Md;
using Trdp.Net.Pd;

namespace Trdp.Net.Core
{
    /// <summary>
    /// DE: Vereinheitlichte TRDP-Session (Entsprechung zum tlc-appHandle). Buendelt PD- und
    /// MD-Session und treibt beide aus einem Verarbeitungszyklus. Aus EINEM Thread nutzen.
    /// </summary>
    public sealed class TrdpSession : IDisposable
    {
        /// <summary>DE: Process-Data-Session.</summary>
        public TrdpPdSession Pd { get; }

        /// <summary>DE: Message-Data-Session (null, wenn beim Oeffnen deaktiviert).</summary>
        public MdSession? Md { get; }

        /// <summary>DE: Eigene IP-Adresse (tlc_getOwnIpAddress).</summary>
        public IPAddress OwnIpAddress { get; }

        /// <summary>DE: ETB-Topozaehler (tlc_setETBTopoCount). Default fuer neue Publisher.</summary>
        public uint EtbTopoCount { get; set; }

        /// <summary>DE: Operational-Train-Topozaehler (tlc_setOpTrainTopoCount).</summary>
        public uint OpTrainTopoCount { get; set; }

        /// <summary>
        /// DE: Oeffnet eine Session (tlc_openSession). <paramref name="enableMd"/> aktiviert die
        /// MD-Session, <paramref name="enableMdTcp"/> zusaetzlich den MD-TCP-Server.
        /// </summary>
        public TrdpSession(IPAddress? bindAddress = null, bool enableMd = true, bool enableMdTcp = false)
        {
            OwnIpAddress = bindAddress ?? IPAddress.Any;
            Pd = new TrdpPdSession(bindAddress);
            Md = enableMd ? new MdSession(bindAddress, enableTcpServer: enableMdTcp) : null;
        }

        /// <summary>
        /// DE: Verarbeitungszyklus (tlc_process): treibt PD- und MD-Session. Aus dem
        /// App-/ExecutionLoop aufrufen.
        /// </summary>
        public void Process()
        {
            Pd.Process();
            Md?.Process();
        }

        /// <summary>
        /// DE: Legt einen PD-Publisher an und uebernimmt die Topo-Counter der Session
        /// (Komfort gegenueber <see cref="TrdpPdSession.Publish"/>).
        /// </summary>
        public PdPublisher Publish(uint comId, IPAddress destIp, int cycleTimeMs,
                                   ReadOnlySpan<byte> initialData = default, int destPort = TrdpConstants.PdUdpPort)
        {
            PdPublisher pub = Pd.Publish(comId, destIp, cycleTimeMs, initialData, destPort);
            pub.EtbTopoCnt = EtbTopoCount;
            pub.OpTrnTopoCnt = OpTrainTopoCount;
            return pub;
        }

        public void Dispose()
        {
            Pd.Dispose();
            Md?.Dispose();
        }
    }
}
