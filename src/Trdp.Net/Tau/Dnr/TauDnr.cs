// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": tau_dnr.c (TCN-DNS Namensaufloesung:
// tau_initDnr/tau_deInitDnr/tau_DNRstatus/tau_getOwnAddr/tau_uri2Addr/tau_ipFromURI/tau_addr2Uri
// sowie die Helfer readHostsFile, buildRequest, parseUpdateTCNResponse, updateTCNDNSentry,
// updateDNSentry, addEntry).
// Stack-Interna sind an TrdpSession/MdSession angepasst. Der Adresscache wird wie im Original
// nach URI sortiert gehalten (case-insensitiv) und per Binaersuche befragt.
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013-2021.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Trdp.Net.Core;
using Trdp.Net.Md;
using Trdp.Net.Vos;

namespace Trdp.Net.Tau.Dnr
{
    /// <summary>
    /// DE: TCN-DNS-Resolver (Port von tau_dnr.c). Loest URIs in IP-Adressen auf und umgekehrt.
    /// Entspricht dem an appHandle-&gt;pUser haengenden TAU_DNR_DATA_T; hier als eigenes Objekt,
    /// das eine <see cref="TrdpSession"/> referenziert. Nicht thread-safe; aus EINEM Zyklus nutzen.
    /// </summary>
    public sealed class TauDnr : IDisposable
    {
        // DE: Vergleicher fuer den URI-Cache — case-insensitiv (vgl. vos_strnicmp/compareURI).
        private static readonly IComparer<TauDnrEntry> UriComparer =
            Comparer<TauDnrEntry>.Create((a, b) =>
                string.Compare(a.Uri, b.Uri, StringComparison.OrdinalIgnoreCase));

        private readonly TrdpSession _appHandle;
        private readonly List<TauDnrEntry> _cache = new();
        private readonly string _hostName;

        private uint _dnsIpAddr;
        private ushort _dnsPort;
        private byte _timeout;             // Timeout in Sekunden (wie TAU_DNR_DATA_T.timeout)
        private TrdpDnrOptions _useTcnDns;
        private bool _disposed;

        /// <summary>DE: IP-Adresse des Resolvers (host-geordnet).</summary>
        public IPAddress DnsIpAddress => DnrIp.FromHostUint(_dnsIpAddr);

        /// <summary>DE: Port des Resolvers (53 fuer Standard-DNS, 17225 fuer TCN-DNS).</summary>
        public ushort DnsPort => _dnsPort;

        /// <summary>DE: Anzahl aktuell im Cache befindlicher Eintraege.</summary>
        public int CachedEntryCount => _cache.Count;

        /// <summary>DE: Lesender Blick auf den Cache (z. B. fuer Diagnose/Tests).</summary>
        public IReadOnlyList<TauDnrEntry> Cache => _cache;

        /// <summary>
        /// DE: Initialisiert den DNR-Resolver (Port von tau_initDnr).
        /// </summary>
        /// <param name="appHandle">DE: Offene TRDP-Session (tlc_openSession-Aequivalent).</param>
        /// <param name="dnsIpAddr">DE: DNS/ECSP-Adresse; null =&gt; Default 10.0.0.1.</param>
        /// <param name="dnsPort">DE: DNS-Port; 0 =&gt; 53 (Standard-DNS) bzw. 17225 (TCN-DNS).</param>
        /// <param name="hostsFileName">DE: Optionale Hosts-Datei als ECSP-Ersatz/-Ergaenzung.</param>
        /// <param name="dnsOptions">DE: Betriebsmodus (Thread-Modell bzw. Standard-DNS).</param>
        /// <param name="waitForDnr">DE: true =&gt; langes Timeout (empfohlen), false =&gt; kurz (Tests).</param>
        /// <param name="hostName">DE: Eigener Hostname (deviceName im Request). Default "unknown".
        /// TODO: TrdpSession fuehrt (noch) keinen stats.hostName.</param>
        public TauDnr(TrdpSession appHandle, IPAddress? dnsIpAddr, ushort dnsPort,
                      string? hostsFileName, TrdpDnrOptions dnsOptions, bool waitForDnr,
                      string hostName = "unknown")
        {
            _appHandle = appHandle ?? throw new ArgumentNullException(nameof(appHandle));
            _hostName = hostName ?? "unknown";

            uint addr = dnsIpAddr != null ? DnrIp.ToHostUint(dnsIpAddr) : 0u;
            _dnsIpAddr = addr == 0u ? TauDnrConstants.DefaultResolverAddr : addr;

            if (dnsOptions == TrdpDnrOptions.StandardDns)
            {
                _dnsPort = dnsPort == 0 ? TauDnrConstants.StandardDnsPort : dnsPort;
            }
            else
            {
                _dnsPort = dnsPort == 0 ? TauDnrConstants.TcnDnsPort : dnsPort;
            }

            _useTcnDns = dnsOptions;
            _timeout = waitForDnr ? TauDnrConstants.DnsTimeOutLong : TauDnrConstants.DnsTimeOutShort;

            if (!string.IsNullOrEmpty(hostsFileName))
            {
                // Fehler beim Lesen der Hosts-Datei wird ignoriert (wie im Original).
                if (ReadHostsFile(hostsFileName!) == TrdpError.NoErr)
                {
                    _timeout = TauDnrConstants.DnsTimeOutShort;
                }
            }
        }

        /// <summary>DE: Gibt Ressourcen frei (Port von tau_deInitDnr).</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _cache.Clear();
            _disposed = true;
        }

        /// <summary>DE: Liefert den DNR-Status (Port von tau_DNRstatus).</summary>
        public TrdpDnrState DnrStatus()
        {
            if (_disposed)
            {
                return TrdpDnrState.NotAvailable;
            }
            if (_timeout == TauDnrConstants.DnsTimeOutShort)
            {
                return TrdpDnrState.HostsFile;
            }
            if (_cache.Count > 0)
            {
                return TrdpDnrState.Active;
            }
            return TrdpDnrState.Unknown;
        }

        /// <summary>
        /// DE: Liefert die eigene IP-Adresse (Port von tau_getOwnAddr). Ist die Session-Adresse
        /// INADDR_ANY, wird das erste Interface mit MAC-Adresse herangezogen.
        /// </summary>
        public IPAddress GetOwnAddr()
        {
            IPAddress own = _appHandle.OwnIpAddress;
            if (!Equals(own, IPAddress.Any))
            {
                return own;
            }

            // Default-Interface: erstes Ethernet-Interface mit MAC nehmen.
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    byte[] mac = nic.GetPhysicalAddress().GetAddressBytes();
                    bool hasMac = false;
                    foreach (byte b in mac)
                    {
                        if (b != 0) { hasMac = true; break; }
                    }
                    if (!hasMac)
                    {
                        continue;
                    }
                    foreach (UnicastIPAddressInformation ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            return ua.Address;
                        }
                    }
                }
            }
            catch (NetworkInformationException)
            {
                // Faellt auf INADDR_ANY zurueck.
            }
            return IPAddress.Any;
        }

        /// <summary>
        /// DE: Wandelt eine URI in eine IP-Adresse (Port von tau_uri2Addr). <paramref name="uri"/>
        /// null =&gt; eigene Adresse. Erkennt punktierte IPs direkt; sonst Cache-/DNS-Aufloesung.
        /// </summary>
        public TrdpError Uri2Addr(out IPAddress addr, string? uri)
        {
            addr = IPAddress.Any;
            if (_disposed)
            {
                return TrdpError.ParamErr;
            }

            // Keine URI -> eigene Adresse.
            if (uri == null)
            {
                addr = GetOwnAddr();
                return TrdpError.NoErr;
            }

            // Punktierte IP-Adresse?
            uint dotted = DnrIp.DottedIp(uri);
            if (dotted != 0u)
            {
                addr = DnrIp.FromHostUint(dotted);
                return TrdpError.NoErr;
            }

            uint etb = _appHandle.EtbTopoCount;
            uint opTrn = _appHandle.OpTrainTopoCount;

            // Bis zu zweimal: Cache pruefen, ggf. aktualisieren, erneut versuchen.
            for (int i = 0; i < 2; i++)
            {
                TauDnrEntry? entry = FindEntry(uri);
                if (entry != null
                    && (entry.FixedEntry
                        || (entry.EtbTopoCnt == etb && entry.OpTrnTopoCnt == opTrn)
                        || (etb == 0u && opTrn == 0u))
                    && entry.IpAddr != 0u)
                {
                    addr = DnrIp.FromHostUint(entry.IpAddr);
                    return TrdpError.NoErr;
                }

                // Unbekannt oder veraltet -> aktualisieren.
                if (_useTcnDns != TrdpDnrOptions.StandardDns)
                {
                    UpdateTcnDnsEntry(entry, uri);
                }
                else
                {
                    UpdateDnsEntry(entry, uri);
                }
            }

            addr = IPAddress.Any;
            return TrdpError.UnresolvedErr;
        }

        /// <summary>DE: Wie <see cref="Uri2Addr"/>, gibt nur die Adresse zurueck (Port von tau_ipFromURI).</summary>
        public IPAddress IpFromUri(string? uri)
        {
            Uri2Addr(out IPAddress ip, uri);
            return ip;
        }

        /// <summary>
        /// DE: Wandelt eine IP-Adresse in den URI-Host-Teil (Port von tau_addr2Uri). Nutzt den
        /// Cache; eine Reverse-Anfrage ist im Original noch "tbd" und hier nicht implementiert.
        /// </summary>
        public TrdpError Addr2Uri(out string uri, IPAddress addr)
        {
            uri = string.Empty;
            if (_disposed || addr == null)
            {
                return TrdpError.ParamErr;
            }

            uint a = DnrIp.ToHostUint(addr);
            if (a != 0u)
            {
                uint etb = _appHandle.EtbTopoCount;
                uint opTrn = _appHandle.OpTrainTopoCount;
                foreach (TauDnrEntry e in _cache)
                {
                    if (e.IpAddr == a
                        && (etb == 0u || e.EtbTopoCnt == etb)
                        && (opTrn == 0u || e.OpTrnTopoCnt == opTrn))
                    {
                        uri = e.Uri;
                        return TrdpError.NoErr;
                    }
                }
                // TODO: Adresse nicht im Cache -> Reverse-Request (im Original ebenfalls "tbd").
            }
            return TrdpError.UnresolvedErr;
        }

        // ── Cache-Verwaltung ─────────────────────────────────────────────────────────────────

        // DE: Binaersuche im sortierten Cache (vgl. vos_bsearch + compareURI).
        private TauDnrEntry? FindEntry(string uri)
        {
            var key = new TauDnrEntry { Uri = uri };
            int idx = _cache.BinarySearch(key, UriComparer);
            return idx >= 0 ? _cache[idx] : null;
        }

        // DE: Cache nach URI sortieren (vgl. vos_qsort + compareURI).
        private void SortCache() => _cache.Sort(UriComparer);

        // DE: Neuen (unaufgeloesten) Eintrag in den Cache aufnehmen (Port von addEntry).
        private void AddEntry(string uri)
        {
            var entry = new TauDnrEntry
            {
                Uri = uri,
                IpAddr = 0u,
                EtbTopoCnt = _appHandle.EtbTopoCount,
                OpTrnTopoCnt = _appHandle.OpTrainTopoCount,
                FixedEntry = false,
            };
            if (_cache.Count >= TauDnrConstants.MaxNoCacheEntry)
            {
                // Cache voll: ersten ueberschreiben (wie im Original; "tbd: cacheGetOldest").
                _cache[0] = entry;
            }
            else
            {
                _cache.Add(entry);
            }
            SortCache();
        }

        // ── Hosts-Datei (readHostsFile) ──────────────────────────────────────────────────────

        // DE: Befuellt den Cache aus einer Hosts-Datei (Port von readHostsFile).
        private TrdpError ReadHostsFile(string hostsFileName)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(hostsFileName);
            }
            catch (IOException)
            {
                return TrdpError.ParamErr;
            }
            catch (UnauthorizedAccessException)
            {
                return TrdpError.ParamErr;
            }

            foreach (string raw in lines)
            {
                if (_cache.Count >= TauDnrConstants.MaxNoCacheEntry)
                {
                    break;
                }
                string line = raw;
                if (line.Length == 0 || line[0] == '#' || char.IsControl(line[0]))
                {
                    continue;
                }

                int idx = 0;
                int max = line.Length;

                // IP-Adresse vom Zeilenanfang lesen.
                uint ip = DnrIp.DottedIp(FirstToken(line));
                if (ip == 0u)
                {
                    continue;
                }
                // Adresse ueberspringen (Ziffern und Interpunktion).
                while (idx < max && (char.IsDigit(line[idx]) || char.IsPunctuation(line[idx])))
                {
                    idx++;
                }
                // Leerraum bis zur URI ueberspringen.
                while (idx < max && char.IsWhiteSpace(line[idx]))
                {
                    idx++;
                }
                int start = idx;
                while (idx < max && !char.IsWhiteSpace(line[idx]) && !char.IsControl(line[idx]) && line[idx] != '#')
                {
                    idx++;
                }
                string uri = idx > start ? line.Substring(start, idx - start) : string.Empty;
                if (uri.Length > 0)
                {
                    _cache.Add(new TauDnrEntry
                    {
                        Uri = uri,
                        IpAddr = ip,
                        EtbTopoCnt = 0u,
                        OpTrnTopoCnt = 0u,
                        FixedEntry = true,
                    });
                }
            }

            SortCache();
            return TrdpError.NoErr;
        }

        // DE: Erstes Token einer Zeile bis zum ersten Leerraum (zum IP-Parsen).
        private static string FirstToken(string line)
        {
            int i = 0;
            while (i < line.Length && !char.IsWhiteSpace(line[i]))
            {
                i++;
            }
            return line.Substring(0, i);
        }

        // ── TCN-DNS (updateTCNDNSentry / buildRequest / parseUpdateTCNResponse) ───────────────

        // DE: Baut die Request-Nutzlast aus allen aktualisierungsbeduerftigen Cache-Eintraegen
        // (Port von buildRequest) und liefert die zu fragenden URIs.
        private byte[] BuildRequest()
        {
            uint etb = _appHandle.EtbTopoCount;
            uint opTrn = _appHandle.OpTrainTopoCount;

            var uris = new List<string>();
            foreach (TauDnrEntry e in _cache)
            {
                if (uris.Count >= 255)
                {
                    break;
                }
                // Kein Update: feste Eintraege oder konsistlokale Adresse (gesetzt, beide Topo == 0).
                if (e.FixedEntry || (e.IpAddr != 0u && e.EtbTopoCnt == 0u && e.OpTrnTopoCnt == 0u))
                {
                    continue;
                }
                // Update noetig: keine Adresse oder Topozaehler stimmen nicht.
                if (e.IpAddr == 0u || e.EtbTopoCnt != etb || e.OpTrnTopoCnt != opTrn)
                {
                    uris.Add(e.Uri);
                }
            }

            return TauDnrWire.BuildRequestPayload(_hostName, etb, opTrn, uris);
        }

        // DE: Verarbeitet einen TCN-DNS-Reply und aktualisiert den Cache (Port von parseUpdateTCNResponse).
        private void ParseUpdateTcnResponse(ReadOnlySpan<byte> data)
        {
            TauDnrWire.ParseReply(data, out uint etbTopoCnt, out uint opTrnTopoCnt, out List<TcnUriRecord> records);

            foreach (TcnUriRecord rec in records)
            {
                if (rec.ResolvState == -1)
                {
                    continue;   // konnte nicht aufgeloest werden
                }
                TauDnrEntry? entry = FindEntry(rec.Uri);
                if (entry != null)
                {
                    entry.Uri = rec.Uri;
                    entry.IpAddr = rec.IpAddr;
                    entry.EtbTopoCnt = etbTopoCnt;
                    entry.OpTrnTopoCnt = opTrnTopoCnt;
                    entry.FixedEntry = false;
                }
                // Eintraege, die nicht angefragt wurden, werden ignoriert (wie im Original).
            }
            SortCache();
        }

        // DE: Fragt den TCN-DNS-Server per MD-Request ab (Port von updateTCNDNSentry).
        private void UpdateTcnDnsEntry(TauDnrEntry? entry, string uri)
        {
            MdSession? md = _appHandle.Md;
            if (md == null)
            {
                return;   // ohne MD-Session keine TCN-DNS-Aufloesung moeglich
            }

            // Neue URI? In den Cache aufnehmen (ggf. aeltesten verdraengen).
            if (entry == null)
            {
                AddEntry(uri);
            }

            byte[] payload = BuildRequest();
            if (payload.Length == 0)
            {
                return;
            }

            IPAddress dnsIp = DnrIp.FromHostUint(_dnsIpAddr);
            int timeoutMs = (int)(TauDnrConstants.TcnDnsReqTimeoutUs / 1000u);

            bool replied = false;
            MdCaller caller = md.Request(TauDnrConstants.TcnDnsReqComId, dnsIp, payload,
                                         timeoutMs, numReplies: 1, destPort: _dnsPort);
            caller.ReplyReceived += (_, msg) =>
            {
                if (msg.ComId == TauDnrConstants.TcnDnsRepComId || msg.ComId == TauDnrConstants.TcnDnsReqComId)
                {
                    // TODO: SDTv2-Pruefung des Telegramms (im Original ebenfalls "tbd").
                    ParseUpdateTcnResponse(msg.Data);
                }
                replied = true;
            };

            // DE: Antwort einsammeln. Wie TRDP_DNR_OWN_THREAD treiben wir tlc_process selbst.
            // TODO: Im COMMON_THREAD-Modus uebernaehme das ein separater Verarbeitungsthread;
            // dann darf hier nicht zusaetzlich Process() laufen.
            var sw = Stopwatch.StartNew();
            while (!replied && caller.IsPending && sw.ElapsedMilliseconds < timeoutMs)
            {
                _appHandle.Process();
                if (!replied)
                {
                    Thread.Sleep(1);
                }
            }
        }

        // ── Standard-DNS (updateDNSentry) ────────────────────────────────────────────────────

        // DE: Fragt einen Standard-DNS-Server ueber UDP ab (Port von updateDNSentry).
        private void UpdateDnsEntry(TauDnrEntry? entry, string uri)
        {
            ushort id = NextRequesterId();
            uint ipAddr = 0u;

            IPAddress dnsIp = DnrIp.FromHostUint(_dnsIpAddr);

            using VosSock sock = VosSock.OpenUdp(IPAddress.Any, 0);

            byte[] query = TauDnrWire.BuildDnsQuery(id, uri, out int querySize);
            try
            {
                sock.SendTo(query, dnsIp, _dnsPort);
            }
            catch (System.Net.Sockets.SocketException)
            {
                return;
            }

            // Auf Antwort warten (Timeout in Sekunden, wie pDNR->timeout).
            var buffer = new byte[TauDnrConstants.MaxDnsBufferSize];
            var sw = Stopwatch.StartNew();
            long timeoutMs = _timeout * 1000L;
            bool gotResponse = false;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (sock.TryReceive(buffer, out int len, out _, out _))
                {
                    if (len == 0)
                    {
                        continue;
                    }
                    gotResponse = TauDnrWire.ParseDnsResponse(buffer.AsSpan(0, len), querySize, out ipAddr);
                    break;
                }
                Thread.Sleep(1);
            }

            if (!gotResponse || ipAddr == 0u)
            {
                return;
            }

            uint etb = _appHandle.EtbTopoCount;
            uint opTrn = _appHandle.OpTrainTopoCount;

            if (entry != null && !entry.FixedEntry)
            {
                // Veralteten Eintrag ueberschreiben.
                entry.IpAddr = ipAddr;
                entry.EtbTopoCnt = etb;
                entry.OpTrnTopoCnt = opTrn;
            }
            else
            {
                // Neuer Eintrag.
                var newEntry = new TauDnrEntry
                {
                    Uri = uri,
                    IpAddr = ipAddr,
                    EtbTopoCnt = etb,
                    OpTrnTopoCnt = opTrn,
                    FixedEntry = false,
                };
                if (_cache.Count >= TauDnrConstants.MaxNoCacheEntry)
                {
                    _cache[0] = newEntry;
                }
                else
                {
                    _cache.Add(newEntry);
                }
                SortCache();
            }
        }

        // DE: Laufende Id zur Identifikation eigener Queries (vgl. sRequesterId).
        private ushort _requesterId = 1;
        private ushort NextRequesterId() => _requesterId++;
    }
}
