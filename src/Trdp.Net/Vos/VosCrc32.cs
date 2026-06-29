// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/vos/common/vos_utils.c (vos_crc32, fcs_table).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013-2021.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;

namespace Trdp.Net.Vos
{
    /// <summary>
    /// DE: Frame Check Sequence (FCS) der TRDP-PDUs — 1:1-Port von <c>vos_crc32()</c>.
    ///
    /// Algorithmus (siehe vos_utils.c:514): reflektierter CRC-32, table-driven,
    /// Polynom 0xEDB88320 (== fcs_table im Original), Aufruf mit Init = INITFCS
    /// (0xFFFFFFFF), Rueckgabe <c>~crc</c>. Das entspricht dem Standard-CRC-32
    /// (Pruefwert von "123456789" = 0xCBF43926).
    ///
    /// HINWEIS: NICHT die SDTv2-CRC (IEC 61375-2-3 Annex B.7) — diese ist bewusst
    /// nicht portiert.
    /// </summary>
    public static class VosCrc32
    {
        /// <summary>DE: Init-Wert INITFCS aus trdp_private.h.</summary>
        public const uint InitFcs = 0xFFFFFFFFu;

        // DE: fcs_table wird aus dem Polynom erzeugt — bitidentisch zur statischen
        //     Tabelle in vos_utils.c (vermeidet Abschreibfehler bei 256 Konstanten).
        private const uint Polynomial = 0xEDB88320u;
        private static readonly uint[] FcsTable = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1u) != 0u ? (crc >> 1) ^ Polynomial : crc >> 1;
                }
                table[i] = crc;
            }
            return table;
        }

        /// <summary>
        /// DE: Port von <c>vos_crc32(crc, pData, dataLen)</c>. Aktualisiert <paramref name="crc"/>
        /// ueber die Bytes und liefert den fertigen Wert (inkl. finalem ~). Fuer eine
        /// vollstaendige FCS mit <see cref="InitFcs"/> initialisieren.
        /// </summary>
        public static uint Compute(uint crc, ReadOnlySpan<byte> data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                crc = (crc >> 8) ^ FcsTable[(crc ^ data[i]) & 0xFFu];
            }
            return ~crc;
        }

        /// <summary>DE: Bequemlichkeit: vollstaendige FCS ueber <paramref name="data"/> mit Standard-Init.</summary>
        public static uint Compute(ReadOnlySpan<byte> data) => Compute(InitFcs, data);
    }
}
