// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": tau_dnr.c / tau_dnr.h / tau_dnr_types.h
// (TCN-DNS Namensaufloesung: Typen, Konstanten, Cache-Eintrag).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013-2021.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;

namespace Trdp.Net.Tau.Dnr
{
    /// <summary>DE: DNR-Status — 1:1-Port von <c>TRDP_DNR_STATE_T</c> (tau_dnr.h).</summary>
    public enum TrdpDnrState
    {
        /// <summary>DE: Aktiviert, aber Cache leer (TRDP_DNR_UNKNOWN).</summary>
        Unknown = 0,
        /// <summary>DE: Nicht verfuegbar (TRDP_DNR_NOT_AVAILABLE).</summary>
        NotAvailable = 1,
        /// <summary>DE: Aktiv, Cache hat Eintraege (TRDP_DNR_ACTIVE).</summary>
        Active = 2,
        /// <summary>DE: Hosts-Datei genutzt, statischer Modus (TRDP_DNR_HOSTSFILE).</summary>
        HostsFile = 3,
    }

    /// <summary>DE: DNR-Optionen — 1:1-Port von <c>TRDP_DNR_OPTS_T</c> (tau_dnr.h).</summary>
    public enum TrdpDnrOptions
    {
        /// <summary>DE: tlc_process laeuft in separatem Thread (Default, TRDP_DNR_COMMON_THREAD).</summary>
        CommonThread = 0,
        /// <summary>DE: Nur fuer Single-Thread-Systeme; intern tlc_process aufrufen (TRDP_DNR_OWN_THREAD).</summary>
        OwnThread = 1,
        /// <summary>DE: Standard-DNS statt TCN-DNS verwenden (TRDP_DNR_STANDARD_DNS).</summary>
        StandardDns = 2,
    }

    /// <summary>
    /// DE: DNR-Konstanten aus tau_dnr.c / iec61375-2-3.h. Wire-relevante Werte (Ports, ComIds,
    /// Timeouts, Laengen) sind 1:1 uebernommen.
    /// </summary>
    public static class TauDnrConstants
    {
        /// <summary>DE: Maximale Anzahl Cache-Eintraege (TAU_MAX_NO_CACHE_ENTRY).</summary>
        public const int MaxNoCacheEntry = 50;

        /// <summary>DE: Label-Laenge inkl. terminierender '0' (TRDP_MAX_LABEL_LEN).</summary>
        public const int MaxLabelLen = 16;

        /// <summary>DE: URI-Host-Teil inkl. terminierender '0' = 5 * Label (TRDP_MAX_URI_HOST_LEN).</summary>
        public const int MaxUriHostLen = 5 * MaxLabelLen;   // 80

        /// <summary>DE: ComId der TCN-DNS-Anfrage (TCN_DNS_REQ_COMID).</summary>
        public const uint TcnDnsReqComId = 140u;

        /// <summary>DE: ComId der TCN-DNS-Antwort (TCN_DNS_REP_COMID).</summary>
        public const uint TcnDnsRepComId = 141u;

        /// <summary>DE: TCN-DNS-Request-Timeout in Mikrosekunden (TCN_DNS_REQ_TO_US = 3 s).</summary>
        public const uint TcnDnsReqTimeoutUs = 3000000u;

        /// <summary>DE: Standard-Port fuer TCN-DNS.</summary>
        public const ushort TcnDnsPort = 17225;

        /// <summary>DE: Standard-Port fuer Standard-DNS.</summary>
        public const ushort StandardDnsPort = 53;

        /// <summary>DE: Default-Resolver-Adresse 10.0.0.1, wenn keine angegeben (0x0a000001).</summary>
        public const uint DefaultResolverAddr = 0x0a000001u;

        /// <summary>DE: Timeout (s) fuer DNS-Antwort ohne Hosts-Datei (TAU_DNS_TIME_OUT_LONG).</summary>
        public const byte DnsTimeOutLong = 10;

        /// <summary>DE: Timeout (s) fuer DNS-Antwort mit Hosts-Datei (TAU_DNS_TIME_OUT_SHORT).</summary>
        public const byte DnsTimeOutShort = 1;

        /// <summary>DE: Max. Puffergroesse fuer Standard-DNS (TAU_MAX_DNS_BUFFER_SIZE).</summary>
        public const int MaxDnsBufferSize = 1500;

        /// <summary>DE: Max. Namensgroesse beim Dekodieren (TAU_MAX_NAME_SIZE).</summary>
        public const int MaxNameSize = 256;
    }

    /// <summary>
    /// DE: Ein DNR-Cache-Eintrag — Port von <c>TAU_DNR_ENTRY_T</c> (tau_dnr.h).
    /// IP-Adresse intern als host-geordnete <see cref="uint"/> (wie TRDP_IP_ADDR_T).
    /// </summary>
    public sealed class TauDnrEntry
    {
        /// <summary>DE: URI-Host-Teil (ASCII), max. 79 Zeichen.</summary>
        public string Uri { get; set; } = string.Empty;

        /// <summary>DE: Aufgeloeste IP-Adresse (host-geordnet); 0 = Platzhalter/unbekannt.</summary>
        public uint IpAddr { get; set; }

        /// <summary>DE: ETB-Topozaehler, zu dem der Eintrag gilt.</summary>
        public uint EtbTopoCnt { get; set; }

        /// <summary>DE: Operational-Train-Topozaehler, zu dem der Eintrag gilt.</summary>
        public uint OpTrnTopoCnt { get; set; }

        /// <summary>DE: True, wenn aus der Hosts-Datei (nie verfaellt, nie aktualisiert).</summary>
        public bool FixedEntry { get; set; }

        /// <summary>DE: Komfort: IP-Adresse als <see cref="IPAddress"/>.</summary>
        public IPAddress IpAddress => DnrIp.FromHostUint(IpAddr);
    }

    /// <summary>
    /// DE: Hilfsfunktionen fuer IP-Adresskonvertierung. TRDP_IP_ADDR_T ist eine host-geordnete
    /// 32-Bit-Zahl: 10.0.8.35 == (10 &lt;&lt; 24) | (0 &lt;&lt; 16) | (8 &lt;&lt; 8) | 35.
    /// </summary>
    internal static class DnrIp
    {
        /// <summary>DE: <see cref="IPAddress"/> -> host-geordnete uint (wie TRDP_IP_ADDR_T).</summary>
        public static uint ToHostUint(IPAddress ip)
        {
            byte[] b = ip.MapToIPv4().GetAddressBytes();   // Netzwerk-Reihenfolge a.b.c.d
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        /// <summary>DE: host-geordnete uint -> <see cref="IPAddress"/>.</summary>
        public static IPAddress FromHostUint(uint v)
        {
            return new IPAddress(new[]
            {
                (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v,
            });
        }

        /// <summary>
        /// DE: Port von <c>vos_dottedIP()</c>: punktierte IPv4 -> host-geordnete uint; 0 bei
        /// ungueltiger Eingabe (auch fuer Hostnamen). TODO: inet_aton akzeptiert zusaetzlich
        /// hex/oktal und Kurzformen; hier nur strikte 4er-Dezimalform.
        /// </summary>
        public static uint DottedIp(string? s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 0u;
            }
            string[] parts = s.Split('.');
            if (parts.Length != 4)
            {
                return 0u;
            }
            uint result = 0u;
            foreach (string p in parts)
            {
                if (p.Length == 0 || p.Length > 3)
                {
                    return 0u;
                }
                foreach (char c in p)
                {
                    if (c < '0' || c > '9')
                    {
                        return 0u;
                    }
                }
                if (!int.TryParse(p, out int octet) || octet < 0 || octet > 255)
                {
                    return 0u;
                }
                result = (result << 8) | (uint)octet;
            }
            return result;
        }
    }
}
