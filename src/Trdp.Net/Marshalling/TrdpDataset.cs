// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/trdp_types.h
// (TRDP_DATASET_T, TRDP_DATASET_ELEMENT_T).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;

namespace Trdp.Net.Marshalling
{
    /// <summary>
    /// DE: Ein Dataset-Element (TRDP_DATASET_ELEMENT_T): Typ + Anzahl Items.
    /// <see cref="Type"/> ist entweder ein Basistyp (&lt;= 30) oder eine Dataset-ID (&gt; 30).
    /// <see cref="Count"/> == 0 bedeutet variable Groesse (TRDP_VAR_SIZE — noch nicht unterstuetzt).
    /// </summary>
    public sealed class TrdpDatasetElement
    {
        public uint Type { get; }
        public uint Count { get; }
        public string? Name { get; }

        public TrdpDatasetElement(uint type, uint count = 1, string? name = null)
        {
            Type = type;
            Count = count;
            Name = name;
        }

        public TrdpDatasetElement(TrdpDataType type, uint count = 1, string? name = null)
            : this((uint)type, count, name) { }

        public bool IsNested => TrdpDataTypeInfo.IsNestedDataset(Type);
        public bool IsVariable => Count == 0;
    }

    /// <summary>
    /// DE: Ein Dataset (TRDP_DATASET_T): ID + geordnete Elementliste.
    /// </summary>
    public sealed class TrdpDataset
    {
        public uint Id { get; }
        public IReadOnlyList<TrdpDatasetElement> Elements { get; }

        public TrdpDataset(uint id, params TrdpDatasetElement[] elements)
        {
            Id = id;
            Elements = elements ?? Array.Empty<TrdpDatasetElement>();
        }
    }

    /// <summary>
    /// DE: Verzeichnis bekannter Datasets (fuer verschachtelte Referenzen), analog zur
    /// Dataset-DB im Original (tau_marshall: findDs).
    /// </summary>
    public sealed class TrdpDatasetRegistry
    {
        private readonly Dictionary<uint, TrdpDataset> _byId = new();

        public void Add(TrdpDataset dataset) => _byId[dataset.Id] = dataset;

        public bool TryGet(uint id, out TrdpDataset dataset) => _byId.TryGetValue(id, out dataset!);

        public TrdpDataset Get(uint id) =>
            _byId.TryGetValue(id, out var ds)
                ? ds
                : throw new KeyNotFoundException($"Unbekanntes Dataset (ID {id}).");
    }
}
