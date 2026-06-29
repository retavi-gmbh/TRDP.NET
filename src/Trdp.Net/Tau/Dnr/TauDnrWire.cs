// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": tau_dnr.c (DNS-Namenskodierung, Standard-DNS-
// Anfrage/Antwort) und tau_dnr_types.h (TCN-DNS Request/Reply-Telegramme, TCN_URI_T).
// Die Wire-Seite ist GEPACKT und BIG-ENDIAN. Host-Struct-Alignment wird NICHT uebernommen;
// es wird wertbasiert ueber TrdpWireReader/TrdpWireWriter (de)serialisiert.
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013-2021.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;
using System.Text;
using Trdp.Net.Marshalling;

namespace Trdp.Net.Tau.Dnr
{
    /// <summary>
    /// DE: Ein aufgeloester TCN-URI aus dem TCN-DNS-Reply — Port von <c>TCN_URI_T</c>.
    /// </summary>
    public readonly struct TcnUriRecord
    {
        /// <summary>DE: TCN-URI-String (ASCII).</summary>
        public string Uri { get; }
        /// <summary>DE: Aufloesestatus: -1 unbekannt, 0 OK.</summary>
        public short ResolvState { get; }
        /// <summary>DE: Aufgeloeste IP-Adresse (host-geordnet).</summary>
        public uint IpAddr { get; }
        /// <summary>DE: Endadresse eines Bereichs (0 wenn ungenutzt).</summary>
        public uint IpAddr2 { get; }

        public TcnUriRecord(string uri, short resolvState, uint ipAddr, uint ipAddr2)
        {
            Uri = uri;
            ResolvState = resolvState;
            IpAddr = ipAddr;
            IpAddr2 = ipAddr2;
        }
    }

    /// <summary>
    /// DE: Wire-(De)Serialisierung der TCN-DNS- und Standard-DNS-Telegramme.
    /// Alle Mehrbyte-Felder big-endian (vgl. vos_htons/vos_ntohl im Original).
    /// </summary>
    public static class TauDnrWire
    {
        // ── TCN-DNS Telegramm-Geometrie (gepackt) ────────────────────────────────────────────

        /// <summary>DE: Groesse eines TCN_URI_T: 80 + 2 + 2 + 4 + 4.</summary>
        public const int TcnUriSize = TauDnrConstants.MaxUriHostLen + 2 + 2 + 4 + 4;   // 92

        /// <summary>DE: Feste Kopfgroesse von TRDP_DNS_REQUEST_T/REPLY vor tcnUriList.</summary>
        public const int DnsTelegramHeaderSize = 2 + 2 + TauDnrConstants.MaxLabelLen + 4 + 4 + 1 + 1 + 1 + 1; // 32

        /// <summary>DE: Groesse des SDT-Trailers TRDP_ETB_CTRL_VDP_T (4+2+2+4+4).</summary>
        public const int SafetyTrailSize = 16;

        // ── TCN-DNS Request bauen (buildRequest) ─────────────────────────────────────────────

        /// <summary>
        /// DE: Baut die Nutzlast eines TCN-DNS-Request (TRDP_DNS_REQUEST_T) fuer die uebergebenen
        /// URIs. version=1.0, etbId=255 ("don't care"). Der SDT-Trailer wird als Nullbytes
        /// angehaengt (im Original noch "tbd"). Reihenfolge/Geometrie 1:1 zum C-Struct.
        /// </summary>
        public static byte[] BuildRequestPayload(string deviceName, uint etbTopoCnt, uint opTrnTopoCnt,
                                                 IReadOnlyList<string> uris)
        {
            if (uris == null)
            {
                throw new ArgumentNullException(nameof(uris));
            }
            int count = Math.Min(uris.Count, 255);
            // size = sizeof(TRDP_DNS_REQUEST_T) - (255 - tcnUriCnt) * sizeof(TCN_URI_T)
            int size = DnsTelegramHeaderSize + count * TcnUriSize + SafetyTrailSize;
            var buf = new byte[size];
            var w = new TrdpWireWriter(buf);

            w.PutUInt8(1);                 // version.ver = 1
            w.PutUInt8(0);                 // version.rel = 0
            w.PutInt16(0);                 // reserved01
            WriteFixedAscii(ref w, deviceName, TauDnrConstants.MaxLabelLen);  // deviceName (16, nicht term.)
            w.PutUInt32(etbTopoCnt);
            w.PutUInt32(opTrnTopoCnt);
            w.PutUInt8(255);               // etbId = don't care
            w.PutUInt8(0);                 // reserved02
            w.PutUInt8(0);                 // reserved03
            w.PutUInt8((byte)count);       // tcnUriCnt

            for (int i = 0; i < count; i++)
            {
                WriteFixedAscii(ref w, uris[i], TauDnrConstants.MaxUriHostLen);  // tcnUriStr[80]
                w.PutInt16(0);             // reserved01
                w.PutInt16(0);             // resolvState (Request: reserved = 0)
                w.PutUInt32(0);            // tcnUriIpAddr
                w.PutUInt32(0);            // tcnUriIpAddr2
            }
            // SafetyTrail (16 Byte) bleibt 0.
            return buf;
        }

        // ── TCN-DNS Reply parsen (parseUpdateTCNResponse) ────────────────────────────────────

        /// <summary>
        /// DE: Parst einen TCN-DNS-Reply (TRDP_DNS_REPLY_T). Liefert die Topozaehler des Reply
        /// und die Liste der aufgeloesten TCN-URIs. IP-Adressen werden host-geordnet geliefert
        /// (entspricht vos_ntohl im Original).
        /// </summary>
        public static void ParseReply(ReadOnlySpan<byte> data, out uint etbTopoCnt, out uint opTrnTopoCnt,
                                      out List<TcnUriRecord> records)
        {
            records = new List<TcnUriRecord>();
            etbTopoCnt = 0u;
            opTrnTopoCnt = 0u;

            if (data.Length < DnsTelegramHeaderSize)
            {
                return;
            }

            var r = new TrdpWireReader(data);
            r.GetUInt8();                  // version.ver
            r.GetUInt8();                  // version.rel
            r.GetInt16();                  // reserved01
            SkipBytes(ref r, TauDnrConstants.MaxLabelLen);  // deviceName
            etbTopoCnt = r.GetUInt32();
            opTrnTopoCnt = r.GetUInt32();
            r.GetUInt8();                  // etbId
            r.GetInt8();                   // dnsStatus
            r.GetUInt8();                  // reserved02
            byte tcnUriCnt = r.GetUInt8();

            for (int i = 0; i < tcnUriCnt; i++)
            {
                if (r.Remaining < TcnUriSize)
                {
                    break;
                }
                string uri = ReadFixedAscii(ref r, TauDnrConstants.MaxUriHostLen);
                r.GetInt16();              // reserved01
                short resolvState = r.GetInt16();
                uint ipAddr = r.GetUInt32();
                uint ipAddr2 = r.GetUInt32();
                records.Add(new TcnUriRecord(uri, resolvState, ipAddr, ipAddr2));
            }
        }

        // ── Standard-DNS: Namenskodierung (changetoDnsNameFormat) ────────────────────────────

        /// <summary>
        /// DE: Wandelt "www.newtec.de" in das DNS-Wire-Format 3www6newtec2de0
        /// (Laengen-Praefix je Label, abschliessendes 0-Byte). Port von changetoDnsNameFormat.
        /// </summary>
        public static byte[] EncodeDnsName(string host)
        {
            string h = (host ?? string.Empty) + ".";   // vos_strncat(pHost, ".")
            var outBytes = new List<byte>(h.Length + 1);
            int lockPos = 0;
            for (int i = 0; i < h.Length; i++)
            {
                if (h[i] == '.')
                {
                    outBytes.Add((byte)(i - lockPos));
                    for (; lockPos < i; lockPos++)
                    {
                        outBytes.Add((byte)h[lockPos]);
                    }
                    lockPos++;
                }
            }
            outBytes.Add(0);
            return outBytes.ToArray();
        }

        /// <summary>
        /// DE: Dekodiert einen DNS-Namen (mit Kompressionszeigern) aus <paramref name="packet"/>
        /// ab <paramref name="readerStart"/>. <paramref name="count"/> = Anzahl im Paket zu
        /// ueberspringender Bytes. Port von readName().
        /// </summary>
        public static string DecodeDnsName(ReadOnlySpan<byte> packet, int readerStart, out int count)
        {
            var name = new byte[TauDnrConstants.MaxNameSize];
            int p = 0;
            bool jumped = false;
            count = 1;
            int reader = readerStart;

            while (reader >= 0 && reader < packet.Length && packet[reader] != 0
                   && p < TauDnrConstants.MaxNameSize - 1)
            {
                if (packet[reader] >= 192)
                {
                    if (reader + 1 >= packet.Length)
                    {
                        break;
                    }
                    int offset = packet[reader] * 256 + packet[reader + 1] - 49152;   // 0xC000
                    reader = offset - 1;
                    jumped = true;
                }
                else
                {
                    name[p++] = packet[reader];
                }
                reader += 1;
                if (!jumped)
                {
                    count += 1;
                }
            }

            if (jumped)
            {
                count += 1;
            }

            // 3www6newtec2de0 -> www.newtec.de
            int len = p;       // strlen(pName)
            int i;
            for (i = 0; i < len && i < TauDnrConstants.MaxNameSize - 1; i++)
            {
                int cnt = name[i];
                for (int j = 0; j < cnt && i + 1 < TauDnrConstants.MaxNameSize; j++)
                {
                    name[i] = name[i + 1];
                    i += 1;
                }
                name[i] = (byte)'.';
            }

            int strLen = i >= 1 ? i - 1 : 0;     // letzten Punkt entfernen
            return Encoding.ASCII.GetString(name, 0, strLen);
        }

        // ── Standard-DNS: Query bauen (createSendQuery) ──────────────────────────────────────

        /// <summary>
        /// DE: Baut ein Standard-DNS-A-Query-Paket. <paramref name="querySize"/> = Laenge von
        /// (kodierter Name + 1) + 4 (qtype + qclass), wie im Original fuer parseResponse benoetigt.
        /// </summary>
        public static byte[] BuildDnsQuery(ushort id, string uri, out int querySize)
        {
            byte[] qname = EncodeDnsName(uri);     // schliesst 0-Byte ein
            // DNS-Header (12) + Name + 4 (qtype/qclass)
            var buf = new byte[12 + qname.Length + 4];
            var w = new TrdpWireWriter(buf);
            w.PutUInt16(id);       // id (big-endian)
            w.PutUInt8(0x01);      // param1: Recursion desired
            w.PutUInt8(0x00);      // param2
            w.PutUInt16(1);        // q_count
            w.PutUInt16(0);        // ans_count
            w.PutUInt16(0);        // auth_count
            w.PutUInt16(0);        // add_count
            for (int i = 0; i < qname.Length; i++)
            {
                w.PutUInt8(qname[i]);
            }
            w.PutUInt8(0); w.PutUInt8(1);   // QTYPE = A
            w.PutUInt8(0); w.PutUInt8(1);   // QCLASS = IN

            // *pSize = strlen(name)+1 + 4 ; name endet schon mit 0, qname.Length = strlen+1
            querySize = qname.Length + 4;
            return buf;
        }

        // ── Standard-DNS: Antwort parsen (parseResponse) ─────────────────────────────────────

        /// <summary>
        /// DE: Parst eine Standard-DNS-Antwort und liefert die erste/letzte IPv4-A-Adresse
        /// (host-geordnet). Port von parseResponse(). Liefert false, wenn keine A-Adresse.
        /// </summary>
        public static bool ParseDnsResponse(ReadOnlySpan<byte> packet, int querySize, out uint ipAddr)
        {
            ipAddr = 0u;
            bool found = false;
            if (packet.Length < 12)
            {
                return false;
            }

            // dns->ans_count @ Offset 6 (big-endian)
            int ansCount = (packet[6] << 8) | packet[7];

            int reader = 12 + querySize;   // hinter Header + Query

            for (int i = 0; i < ansCount; i++)
            {
                if (reader >= packet.Length)
                {
                    break;
                }
                DecodeDnsName(packet, reader, out int skip);
                reader += skip;
                if (reader + 10 > packet.Length)
                {
                    break;
                }
                // TAU_R_DATA_T: type(2) rclass(2) ttl(4) data_len(2)
                int type = (packet[reader] << 8) | packet[reader + 1];
                int dataLen = (packet[reader + 8] << 8) | packet[reader + 9];
                reader += 10;

                if (type == 1)   // A-Record
                {
                    if (dataLen != 4 || reader + 4 > packet.Length)
                    {
                        ipAddr = 0u;   // versprochene IPv4, aber falsche Laenge
                    }
                    else
                    {
                        ipAddr = ((uint)packet[reader] << 24) | ((uint)packet[reader + 1] << 16)
                                 | ((uint)packet[reader + 2] << 8) | packet[reader + 3];
                        found = true;
                    }
                    reader += dataLen;
                }
                else
                {
                    DecodeDnsName(packet, reader, out int skip2);
                    reader += skip2;
                }
            }
            return found;
        }

        // ── Hilfen ───────────────────────────────────────────────────────────────────────────

        private static void WriteFixedAscii(ref TrdpWireWriter w, string? s, int fieldLen)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(s ?? string.Empty);
            for (int i = 0; i < fieldLen; i++)
            {
                w.PutUInt8(i < bytes.Length ? bytes[i] : (byte)0);
            }
        }

        private static string ReadFixedAscii(ref TrdpWireReader r, int fieldLen)
        {
            var bytes = new byte[fieldLen];
            for (int i = 0; i < fieldLen; i++)
            {
                bytes[i] = r.GetUInt8();
            }
            int n = Array.IndexOf(bytes, (byte)0);
            if (n < 0)
            {
                n = fieldLen;
            }
            return Encoding.ASCII.GetString(bytes, 0, n);
        }

        private static void SkipBytes(ref TrdpWireReader r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                r.GetUInt8();
            }
        }
    }
}
