// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tau_xmarshall.c
// (tau_xinitMarshall, tau_xmarshall, tau_xunmarshall, tau_xcalcDatasetSize[ByComId]).
//
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013.
//             Derivat von tau_marshall.c, Thorsten Schulz, Universitaet Rostock.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.
//
// DE: tau_xmarshall.c ist XML-getriebenes Marshalling: aus der XML-Konfiguration wird eine
//     comId->Dataset-Map aufgebaut (tau_xinitMarshall), danach wird per comId marshallt /
//     unmarshallt und die Dataset-Groesse berechnet.
//
//     Wesentlicher Unterschied zum managed Port: Der eigentliche Byte-Transfer (gepackt,
//     big-endian) erledigt bereits TrdpMarshaller. Die im C-Original ueber pTypeMap konfigurierbare
//     LOKALE Typ-/Alignment-Mangling-Seite (Scade: alle Ganzzahlen als int) entfaellt im managed
//     Port: dort sind Werte typisierte CLR-Objekte (object[]), nicht ein roher Host-Struct-Buffer,
//     daher gibt es keine lokale Ausrichtung anzupassen. Wire-Seite ist 1:1 identisch zu tau_marshall.
//     Dieses Modul ist somit ein duenner Wrapper: comId->Dataset-Aufloesung + Delegation.

using System;
using System.Collections.Generic;
using Trdp.Net.Marshalling;
using Trdp.Net.Vos;
using Trdp.Net.Xml;

namespace Trdp.Net.Tau.XMarshall
{
    /// <summary>
    /// DE: XML-getriebenes Marshalling per comId (Port von tau_xmarshall.c).
    /// Loest comId -&gt; Dataset ueber eine aus der Konfiguration aufgebaute Map auf und
    /// delegiert das eigentliche (Un-)Marshalling an <see cref="TrdpMarshaller"/>.
    /// </summary>
    public sealed class TauXMarshall
    {
        // DE: Entspricht TAU_XMAX_DS_LEVEL aus tau_xmarshall.h (max. Verschachtelungstiefe).
        public const int MaxDsLevel = 5;

        private readonly TrdpDatasetRegistry _registry;
        private readonly TrdpMarshaller _marshaller;

        // DE: comId -> datasetId, entspricht sComIdDsIdMap (TRDP_COMID_DSID_MAP_T[]) im Original.
        private readonly Dictionary<uint, uint> _comIdToDsId;

        /// <summary>
        /// DE: Konstruktor aus einer geparsten XML-Konfiguration (entspricht tau_xinitMarshall:
        /// Datasets + comId-&gt;datasetId-Map werden uebernommen).
        /// </summary>
        public TauXMarshall(TrdpXmlConfig config)
        {
            if (config is null) throw new ArgumentNullException(nameof(config));

            _registry = config.Datasets;
            _marshaller = new TrdpMarshaller(_registry);

            // DE: comId -> datasetId aus den Telegrammen aufbauen (analog pComIdDsIdMap).
            _comIdToDsId = new Dictionary<uint, uint>();
            foreach (TrdpXmlTelegram tlg in config.Telegrams)
            {
                // DE: Mehrfache Telegramme zur selben comId: erstes gewinnt (wie sortierte bsearch-Map).
                if (!_comIdToDsId.ContainsKey(tlg.ComId))
                    _comIdToDsId[tlg.ComId] = tlg.DataSetId;
            }
        }

        /// <summary>
        /// DE: Konstruktor aus einer Dataset-Registry und einer expliziten comId-&gt;datasetId-Map
        /// (entspricht tau_xinitMarshall mit pComIdDsIdMap + pDataset[]).
        /// </summary>
        public TauXMarshall(TrdpDatasetRegistry registry, IReadOnlyDictionary<uint, uint> comIdToDatasetId)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (comIdToDatasetId is null) throw new ArgumentNullException(nameof(comIdToDatasetId));

            _marshaller = new TrdpMarshaller(_registry);
            _comIdToDsId = new Dictionary<uint, uint>(comIdToDatasetId.Count);
            foreach (KeyValuePair<uint, uint> kv in comIdToDatasetId)
                _comIdToDsId[kv.Key] = kv.Value;
        }

        /// <summary>
        /// DE: Initialisiert die Marshalling-Schicht aus einer XML-Konfiguration
        /// (Namensgebung wie tau_xinitMarshall). Komfort-Factory; identisch zum Konstruktor.
        /// </summary>
        public static TauXMarshall InitMarshall(TrdpXmlConfig config) => new(config);

        /// <summary>DE: Anzahl bekannter comId-&gt;Dataset-Zuordnungen (entspricht sNumComId).</summary>
        public int ComIdCount => _comIdToDsId.Count;

        /// <summary>
        /// DE: Marshallt eine flache Werteliste fuer das zur <paramref name="comId"/> gehoerende
        /// Dataset ins gepackte Big-Endian-Wire-Format (Port von tau_xmarshall).
        /// </summary>
        public byte[] MarshalByComId(uint comId, IReadOnlyList<object> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            TrdpDataset ds = FindDatasetByComId(comId);
            return _marshaller.Marshal(ds, values);
        }

        /// <summary>
        /// DE: Unmarshallt Wire-Daten fuer das zur <paramref name="comId"/> gehoerende Dataset
        /// in eine flache Werteliste (Port von tau_xunmarshall).
        /// </summary>
        public object[] UnmarshalByComId(uint comId, ReadOnlySpan<byte> wire)
        {
            TrdpDataset ds = FindDatasetByComId(comId);
            return _marshaller.Unmarshal(ds, wire);
        }

        /// <summary>
        /// DE: Gepackte Wire-Groesse des zur <paramref name="comId"/> gehoerenden Datasets mit FESTER
        /// Struktur (Port von tau_xcalcDatasetSizeByComId). Bei variablen Arrays die Ueberladung mit
        /// Werten nutzen.
        /// </summary>
        public int CalcDatasetSizeByComId(uint comId)
        {
            TrdpDataset ds = FindDatasetByComId(comId);
            return _marshaller.ComputeFixedSize(ds);
        }

        /// <summary>
        /// DE: Wertabhaengige Wire-Groesse fuer die <paramref name="comId"/> — auch fuer Datasets mit
        /// variablen Arrays (Port von tau_xcalcDatasetSize via Dry-Run-Unmarshall).
        /// </summary>
        public int CalcDatasetSizeByComId(uint comId, IReadOnlyList<object> values)
        {
            TrdpDataset ds = FindDatasetByComId(comId);
            return _marshaller.ComputeSize(ds, values);
        }

        /// <summary>
        /// DE: Gepackte Wire-Groesse des Datasets <paramref name="datasetId"/> (feste Struktur).
        /// </summary>
        public int CalcDatasetSize(uint datasetId)
        {
            TrdpDataset ds = FindDataset(datasetId);
            return _marshaller.ComputeFixedSize(ds);
        }

        /// <summary>DE: Wertabhaengige Wire-Groesse des Datasets <paramref name="datasetId"/> (variable Arrays).</summary>
        public int CalcDatasetSize(uint datasetId, IReadOnlyList<object> values)
        {
            TrdpDataset ds = FindDataset(datasetId);
            return _marshaller.ComputeSize(ds, values);
        }

        /// <summary>
        /// DE: Liefert das zur comId gehoerende Dataset (Port von findDSFromComId).
        /// Liefert <c>null</c> statt Ausnahme, falls die comId unbekannt ist.
        /// </summary>
        public TrdpDataset? DatasetForComId(uint comId)
        {
            if (_comIdToDsId.TryGetValue(comId, out uint dsId) && _registry.TryGet(dsId, out var ds))
                return ds;
            return null;
        }

        // ── interne Aufloesung (findDSFromComId / findDs mit Fehlerabbildung) ──

        private TrdpDataset FindDatasetByComId(uint comId)
        {
            // DE: !sNumEntries -> TRDP_INIT_ERR im Original.
            if (_comIdToDsId.Count == 0)
                throw new TauXMarshallException(TrdpError.InitErr, "XMarshall nicht initialisiert (keine comId-Map).");

            if (!_comIdToDsId.TryGetValue(comId, out uint dsId))
                throw new TauXMarshallException(TrdpError.ComIdErr, $"ComID {comId} unbekannt.");

            return FindDataset(dsId);
        }

        private TrdpDataset FindDataset(uint datasetId)
        {
            if (_registry.TryGet(datasetId, out var ds))
                return ds;
            // DE: findDs() liefert NULL -> Aufrufer gibt TRDP_COMID_ERR zurueck.
            throw new TauXMarshallException(TrdpError.ComIdErr, $"DatasetID {datasetId} unbekannt.");
        }
    }
}
