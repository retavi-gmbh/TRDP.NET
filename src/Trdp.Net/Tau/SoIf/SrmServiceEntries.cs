// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/trdp_serviceRegistry.h
// (SRM_SERVICE_ENTRIES_T, DSID 113) und die netcpy-Konvertierung aus tau_so_if.c.
// Original-C: Copyright 2019 Bombardier Transportation & NewTec GmbH.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;
using Trdp.Net.Marshalling;

namespace Trdp.Net.Tau.SoIf
{
    /// <summary>
    /// DE: Request/Reply-Container der konsist-lokalen SRM-Schnittstelle (SRM_SERVICE_ENTRIES_T,
    /// DSID 113). Wire-Format: 2 Byte version + 2 Byte noOfEntries + n * 64 Byte Eintraege,
    /// alles gepackt und big-endian.
    /// </summary>
    public sealed class SrmServiceEntries
    {
        /// <summary>DE: Gepackte Wire-Groesse des Kopfes (version + noOfEntries) in Byte.</summary>
        public const int HeaderSize = 4;

        /// <summary>DE: Telegrammversion (1.0). Wird vom Stack auf 1 gesetzt (vgl. requestServices).</summary>
        public SrmShortVersion Version { get; set; } = new SrmShortVersion(1, 0);

        /// <summary>DE: Service-Eintraege (serviceEntry[]).</summary>
        public List<SrmServiceInfo> Entries { get; } = new();

        /// <summary>DE: Anzahl Eintraege im Wire-Feld noOfEntries (entspricht <see cref="Entries"/>.Count).</summary>
        public int NoOfEntries => Entries.Count;

        /// <summary>DE: Gesamte Wire-Groesse dieser Struktur in Byte.</summary>
        public int WireSize => HeaderSize + Entries.Count * SrmServiceInfo.WireSize;

        /// <summary>DE: Serialisiert die Struktur in einen neuen, gepackten Big-Endian-Puffer.</summary>
        public byte[] Encode()
        {
            var buf = new byte[WireSize];
            var w = new TrdpWireWriter(buf);
            Encode(ref w);
            return buf;
        }

        /// <summary>DE: Serialisiert die Struktur in den vorhandenen Writer.</summary>
        public void Encode(ref TrdpWireWriter w)
        {
            w.PutUInt8(Version.Ver);
            w.PutUInt8(Version.Rel);
            // DE: noOfEntries wird (wie netcpy) big-endian geschrieben.
            w.PutUInt16((ushort)Entries.Count);
            foreach (SrmServiceInfo info in Entries)
            {
                info.Encode(ref w);
            }
        }

        /// <summary>
        /// DE: Deserialisiert aus einem gepackten Big-Endian-Puffer. Es werden so viele Eintraege
        /// gelesen, wie das noOfEntries-Feld angibt, jedoch hoechstens so viele, wie der Puffer
        /// hergibt (defensiv gegen abgeschnittene Telegramme).
        /// </summary>
        public static SrmServiceEntries Decode(ReadOnlySpan<byte> data)
        {
            if (data.Length < HeaderSize)
            {
                throw new ArgumentException(
                    $"SRM_SERVICE_ENTRIES_T zu kurz ({data.Length} < {HeaderSize}).", nameof(data));
            }

            var r = new TrdpWireReader(data);
            byte ver = r.GetUInt8();
            byte rel = r.GetUInt8();
            int declared = r.GetUInt16();

            var entries = new SrmServiceEntries { Version = new SrmShortVersion(ver, rel) };
            for (int i = 0; i < declared && r.Remaining >= SrmServiceInfo.WireSize; i++)
            {
                entries.Entries.Add(SrmServiceInfo.Decode(ref r));
            }
            return entries;
        }
    }
}
