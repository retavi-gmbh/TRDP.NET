// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tau_marshall.c (marshallDs/unmarshallDs).
// Wertbasiert statt struct-basiert: die Host-Struct-Alignment-Seite des Originals entfaellt im
// managed Port (siehe PORTING.md); die Wire-Seite (gepackt, big-endian) ist 1:1 abgebildet.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;

namespace Trdp.Net.Marshalling
{
    /// <summary>
    /// DE: Wandelt TRDP-Datasets zwischen einer flachen Werteliste und dem gepackten
    /// Big-Endian-Wire-Format. Unterstuetzt Skalare, feste Arrays (Count &gt; 1) und
    /// verschachtelte Datasets. Variable Arrays (Count == 0) folgen (siehe PORTING.md).
    ///
    /// Werteliste (flach): ein Element mit Count = N belegt N aufeinanderfolgende Werte;
    /// ein verschachteltes Element mit Count = N belegt N Wertgruppen des Sub-Datasets.
    /// </summary>
    public sealed class TrdpMarshaller
    {
        private readonly TrdpDatasetRegistry _registry;

        public TrdpMarshaller(TrdpDatasetRegistry? registry = null)
        {
            _registry = registry ?? new TrdpDatasetRegistry();
        }

        /// <summary>DE: Serialisiert <paramref name="values"/> gemaess <paramref name="dataset"/> ins Wire-Format.</summary>
        public byte[] Marshal(TrdpDataset dataset, IReadOnlyList<object> values)
        {
            // DE: Groesse wertabhaengig bestimmen (variable Arrays!), dann schreiben.
            int vi = 0;
            int size = MeasureDataset(dataset, values, ref vi, 1);

            if (vi != values.Count)
            {
                throw new ArgumentException(
                    $"Zu viele Werte: {values.Count} angegeben, {vi} vom Dataset belegt.", nameof(values));
            }

            var buffer = new byte[size];
            var writer = new TrdpWireWriter(buffer);
            vi = 0;
            WriteDataset(ref writer, dataset, values, ref vi, 1);
            return buffer;
        }

        /// <summary>DE: Deserialisiert Wire-Daten gemaess <paramref name="dataset"/> in eine flache Werteliste.</summary>
        public object[] Unmarshal(TrdpDataset dataset, ReadOnlySpan<byte> wire)
        {
            var values = new List<object>();
            var reader = new TrdpWireReader(wire);
            ReadDataset(ref reader, dataset, values, 1);
            return values.ToArray();
        }

        /// <summary>
        /// DE: Gepackte Wire-Groesse eines Datasets mit FESTER Struktur (wirft bei variablen Arrays).
        /// Fuer wertabhaengige Groessen (variable Arrays) siehe <see cref="Marshal"/>.
        /// </summary>
        public int ComputeFixedSize(TrdpDataset dataset)
        {
            int size = 0;
            foreach (TrdpDatasetElement el in dataset.Elements)
            {
                if (el.IsVariable)
                {
                    throw new NotSupportedException(
                        "ComputeFixedSize unterstuetzt keine variablen Arrays (Count == 0). Nutze Marshal().");
                }
                if (el.IsNested)
                {
                    size += (int)el.Count * ComputeFixedSize(_registry.Get(el.Type));
                }
                else
                {
                    size += (int)el.Count * TrdpDataTypeInfo.WireSize((TrdpDataType)el.Type);
                }
            }
            return size;
        }

        /// <summary>
        /// DE: Wertabhaengige gepackte Wire-Groesse — auch fuer Datasets mit variablen Arrays
        /// (Count == 0). Gegenstueck zu <see cref="ComputeFixedSize"/> fuer den variablen Fall.
        /// </summary>
        public int ComputeSize(TrdpDataset dataset, IReadOnlyList<object> values)
        {
            int vi = 0;
            return MeasureDataset(dataset, values, ref vi, 1);
        }

        // DE: Maximale Verschachtelungstiefe (TAU_MAX_DS_LEVEL) — verhindert StackOverflow bei
        // fehlkonfigurierten/zyklischen Datasets, wie marshallDs mit TRDP_STATE_ERR.
        private const int MaxDsLevel = 5;

        // DE: var_size ist im C-Original eine LOKALE Variable je marshallDs-Aufruf (0-initialisiert);
        // Rekursion in nested Datasets bekommt ein frisches var_size und beeinflusst das Eltern-var_size
        // NICHT. Daher hier ebenfalls lokal (kein ref durch die Rekursion). vi bleibt der globale Index.
        private int MeasureDataset(TrdpDataset dataset, IReadOnlyList<object> values, ref int vi, int level)
        {
            if (level > MaxDsLevel) throw new InvalidOperationException($"Dataset-Verschachtelung zu tief (> {MaxDsLevel}).");
            uint varSize = 0;
            int size = 0;
            foreach (TrdpDatasetElement el in dataset.Elements)
            {
                uint count = el.IsVariable ? varSize : el.Count;   // variable: Anzahl aus Vorgaenger
                for (uint n = 0; n < count; n++)
                {
                    if (el.IsNested)
                    {
                        size += MeasureDataset(_registry.Get(el.Type), values, ref vi, level + 1);
                    }
                    else
                    {
                        if (vi >= values.Count)
                            throw new ArgumentException("Zu wenige Werte fuer das Dataset.", nameof(values));
                        if (n == 0) UpdateVarSize((TrdpDataType)el.Type, values[vi], ref varSize);
                        size += TrdpDataTypeInfo.WireSize((TrdpDataType)el.Type);
                        vi++;
                    }
                }
            }
            return size;
        }

        private void WriteDataset(ref TrdpWireWriter w, TrdpDataset dataset, IReadOnlyList<object> values,
                                  ref int vi, int level)
        {
            if (level > MaxDsLevel) throw new InvalidOperationException($"Dataset-Verschachtelung zu tief (> {MaxDsLevel}).");
            uint varSize = 0;
            foreach (TrdpDatasetElement el in dataset.Elements)
            {
                uint count = el.IsVariable ? varSize : el.Count;
                for (uint n = 0; n < count; n++)
                {
                    if (el.IsNested)
                    {
                        WriteDataset(ref w, _registry.Get(el.Type), values, ref vi, level + 1);
                    }
                    else
                    {
                        if (vi >= values.Count)
                            throw new ArgumentException("Zu wenige Werte fuer das Dataset.", nameof(values));
                        if (n == 0) UpdateVarSize((TrdpDataType)el.Type, values[vi], ref varSize);
                        WriteScalar(ref w, (TrdpDataType)el.Type, values[vi++]);
                    }
                }
            }
        }

        private void ReadDataset(ref TrdpWireReader r, TrdpDataset dataset, List<object> values, int level)
        {
            if (level > MaxDsLevel) throw new InvalidOperationException($"Dataset-Verschachtelung zu tief (> {MaxDsLevel}).");
            uint varSize = 0;
            foreach (TrdpDatasetElement el in dataset.Elements)
            {
                uint count = el.IsVariable ? varSize : el.Count;
                for (uint n = 0; n < count; n++)
                {
                    if (el.IsNested)
                    {
                        ReadDataset(ref r, _registry.Get(el.Type), values, level + 1);
                    }
                    else
                    {
                        object v = ReadScalar(ref r, (TrdpDataType)el.Type);
                        if (n == 0) UpdateVarSize((TrdpDataType)el.Type, v, ref varSize);
                        values.Add(v);
                    }
                }
            }
        }

        // DE: var_size = *pSrc im Original — aber nur fuer Elemente mit Wire-Groesse 1/2/4 gesetzt
        // (die 64-bit-/TIMEDATE48/64-Faelle aktualisieren var_size NICHT). REAL32 wird als rohe
        // 32-Bit-IEEE754-Repraesentation interpretiert (wie *(UINT32*)pSrc32 im C-Code).
        private static void UpdateVarSize(TrdpDataType type, object value, ref uint varSize)
        {
            switch (TrdpDataTypeInfo.WireSize(type))
            {
                case 1:
                case 2:
                case 4:
                    varSize = type == TrdpDataType.Real32
                        ? BitConverter.SingleToUInt32Bits(Convert.ToSingle(value))
                        : ToUInt(value);
                    break;
                // 6 (TIMEDATE48) und 8 (INT64/UINT64/REAL64/TIMEDATE64): var_size unveraendert.
            }
        }

        // DE: Wert bitweise als UINT32 interpretieren (var_size = *pSrc im Original; unchecked,
        // nicht Convert — sonst werfen negative/grosse Werte). Nur der erste Wert je Element zaehlt.
        private static uint ToUInt(object value) => value switch
        {
            bool b   => b ? 1u : 0u,
            char c   => c,
            byte v   => v,
            sbyte v  => (byte)v,
            ushort v => v,
            short v  => (ushort)v,
            uint v   => v,
            int v    => unchecked((uint)v),
            ulong v  => unchecked((uint)v),
            long v   => unchecked((uint)v),
            _        => unchecked((uint)Convert.ToInt64(value)),
        };

        private static void WriteScalar(ref TrdpWireWriter w, TrdpDataType type, object value)
        {
            switch (type)
            {
                case TrdpDataType.BitSet8:    w.PutBool8(value is bool b ? b : Convert.ToByte(value) != 0); break;
                case TrdpDataType.Char8:      w.PutUInt8(Convert.ToByte(value)); break;
                case TrdpDataType.Int8:       w.PutInt8(Convert.ToSByte(value)); break;
                case TrdpDataType.UInt8:      w.PutUInt8(Convert.ToByte(value)); break;
                case TrdpDataType.Utf16:      w.PutChar16(value is char c ? c : (char)Convert.ToUInt16(value)); break;
                case TrdpDataType.Int16:      w.PutInt16(Convert.ToInt16(value)); break;
                case TrdpDataType.UInt16:     w.PutUInt16(Convert.ToUInt16(value)); break;
                case TrdpDataType.Int32:      w.PutInt32(Convert.ToInt32(value)); break;
                case TrdpDataType.UInt32:     w.PutUInt32(Convert.ToUInt32(value)); break;
                case TrdpDataType.Real32:     w.PutReal32(Convert.ToSingle(value)); break;
                case TrdpDataType.TimeDate32: w.PutTimeDate32(Convert.ToUInt32(value)); break;
                case TrdpDataType.Int64:      w.PutInt64(Convert.ToInt64(value)); break;
                case TrdpDataType.UInt64:     w.PutUInt64(Convert.ToUInt64(value)); break;
                case TrdpDataType.Real64:     w.PutReal64(Convert.ToDouble(value)); break;
                case TrdpDataType.TimeDate48: w.PutTimeDate48((TrdpTimeDate48)value); break;
                case TrdpDataType.TimeDate64: w.PutTimeDate64((TrdpTimeDate64)value); break;
                default: throw new ArgumentOutOfRangeException(nameof(type), $"Kein Basistyp: {type}");
            }
        }

        private static object ReadScalar(ref TrdpWireReader r, TrdpDataType type) => type switch
        {
            TrdpDataType.BitSet8    => r.GetBool8(),
            TrdpDataType.Char8      => r.GetUInt8(),
            TrdpDataType.Int8       => r.GetInt8(),
            TrdpDataType.UInt8      => r.GetUInt8(),
            TrdpDataType.Utf16      => r.GetChar16(),
            TrdpDataType.Int16      => r.GetInt16(),
            TrdpDataType.UInt16     => r.GetUInt16(),
            TrdpDataType.Int32      => r.GetInt32(),
            TrdpDataType.UInt32     => r.GetUInt32(),
            TrdpDataType.Real32     => r.GetReal32(),
            TrdpDataType.TimeDate32 => r.GetTimeDate32(),
            TrdpDataType.Int64      => r.GetInt64(),
            TrdpDataType.UInt64     => r.GetUInt64(),
            TrdpDataType.Real64     => r.GetReal64(),
            TrdpDataType.TimeDate48 => r.GetTimeDate48(),
            TrdpDataType.TimeDate64 => r.GetTimeDate64(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), $"Kein Basistyp: {type}"),
        };
    }
}
