// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/trdp_types.h (TRDP_DATA_TYPE_T)
// und trdp/src/common/tau_marshall.c (Wire-Groessen je Typ).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;

namespace Trdp.Net.Marshalling
{
    /// <summary>
    /// DE: TRDP-Basisdatentypen (TRDP_DATA_TYPE_T). Werte &gt; <see cref="MaxType"/> sind
    /// IDs verschachtelter Datasets.
    /// </summary>
    public enum TrdpDataType : uint
    {
        Invalid    = 0,
        BitSet8    = 1,   // == Bool8
        Char8      = 2,
        Utf16      = 3,
        Int8       = 4,
        Int16      = 5,
        Int32      = 6,
        Int64      = 7,
        UInt8      = 8,
        UInt16     = 9,
        UInt32     = 10,
        UInt64     = 11,
        Real32     = 12,
        Real64     = 13,
        TimeDate32 = 14,
        TimeDate48 = 15,
        TimeDate64 = 16,
    }

    /// <summary>DE: Hilfen rund um TRDP-Datentypen.</summary>
    public static class TrdpDataTypeInfo
    {
        /// <summary>DE: Bool8 ist ein Alias auf BitSet8 (TRDP_BOOL8 == TRDP_BITSET8).</summary>
        public const TrdpDataType Bool8 = TrdpDataType.BitSet8;

        /// <summary>DE: Groesster Basistyp; Typ-IDs darueber sind Dataset-Referenzen (TRDP_TYPE_MAX).</summary>
        public const uint MaxType = 30;

        /// <summary>DE: True, wenn die Typ-ID ein verschachteltes Dataset referenziert.</summary>
        public static bool IsNestedDataset(uint type) => type > MaxType;

        /// <summary>
        /// DE: Gepackte Wire-Groesse eines Basistyps in Bytes (siehe tau_marshall.c).
        /// </summary>
        public static int WireSize(TrdpDataType type) => type switch
        {
            TrdpDataType.BitSet8    => 1,
            TrdpDataType.Char8      => 1,
            TrdpDataType.Int8       => 1,
            TrdpDataType.UInt8      => 1,
            TrdpDataType.Utf16      => 2,
            TrdpDataType.Int16      => 2,
            TrdpDataType.UInt16     => 2,
            TrdpDataType.Int32      => 4,
            TrdpDataType.UInt32     => 4,
            TrdpDataType.Real32     => 4,
            TrdpDataType.TimeDate32 => 4,
            TrdpDataType.TimeDate48 => 6,   // u32 Sekunden + u16 Ticks
            TrdpDataType.TimeDate64 => 8,   // u32 Sekunden + u32 Mikrosekunden
            TrdpDataType.Int64      => 8,
            TrdpDataType.UInt64     => 8,
            TrdpDataType.Real64     => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(type), $"Kein Basistyp: {type}"),
        };
    }

    /// <summary>DE: 48-Bit-TCN-Zeit: 32-Bit-UNIX-Sekunden + 16-Bit-Ticks (TIMEDATE48).</summary>
    public readonly struct TrdpTimeDate48
    {
        public readonly uint Seconds;
        public readonly ushort Ticks;
        public TrdpTimeDate48(uint seconds, ushort ticks) { Seconds = seconds; Ticks = ticks; }
    }

    /// <summary>DE: 64-Bit-Zeit: 32-Bit-UNIX-Sekunden + 32-Bit-Mikrosekunden (TIMEDATE64).</summary>
    public readonly struct TrdpTimeDate64
    {
        public readonly uint Seconds;
        public readonly uint Microseconds;
        public TrdpTimeDate64(uint seconds, uint microseconds) { Seconds = seconds; Microseconds = microseconds; }
    }
}
