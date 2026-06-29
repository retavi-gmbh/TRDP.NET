// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tau_xml.c (XML-Konfigurations-Parser).
// Statt libxml2 wird System.Xml.Linq verwendet; das Schema (device/data-set-list/data-set/element,
// bus-interface/telegram, com-parameter) entspricht trdp-config.xsd.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Trdp.Net.Marshalling;

namespace Trdp.Net.Xml
{
    /// <summary>DE: Eine Destination eines Telegramms (Ziel-URI/IP).</summary>
    public sealed class TrdpXmlDestination
    {
        public uint Id { get; init; }
        public string? Uri { get; init; }
    }

    /// <summary>DE: Eine Quelle eines Telegramms (erlaubte Absender-URI/IP).</summary>
    public sealed class TrdpXmlSource
    {
        public uint Id { get; init; }
        public string? Uri { get; init; }
    }

    /// <summary>DE: Ein konfiguriertes Telegramm (comId &lt;-&gt; dataSetId + PD-Parameter).</summary>
    public sealed class TrdpXmlTelegram
    {
        public string? Name { get; init; }
        public uint ComId { get; init; }
        public uint DataSetId { get; init; }
        public uint ComParameterId { get; init; }
        public uint CycleTimeUs { get; init; }
        public uint TimeoutUs { get; init; }
        public bool Marshall { get; init; }
        public IReadOnlyList<TrdpXmlDestination> Destinations { get; init; } = Array.Empty<TrdpXmlDestination>();
        public IReadOnlyList<TrdpXmlSource> Sources { get; init; } = Array.Empty<TrdpXmlSource>();
    }

    /// <summary>DE: Default-Kommunikationsparameter (com-parameter).</summary>
    public sealed class TrdpXmlComParameter
    {
        public uint Id { get; init; }
        public uint Qos { get; init; }
        public uint Ttl { get; init; }
    }

    /// <summary>
    /// DE: Geparste TRDP-XML-Gerätekonfiguration: Datasets (als <see cref="TrdpDatasetRegistry"/>),
    /// Telegramme und com-parameter. Entspricht den von tau_xml bereitgestellten Tabellen.
    /// </summary>
    public sealed class TrdpXmlConfig
    {
        public string? HostName { get; private set; }
        public string? LeaderName { get; private set; }
        public string? DeviceType { get; private set; }

        public TrdpDatasetRegistry Datasets { get; } = new();
        public IReadOnlyList<TrdpDataset> DatasetList { get; private set; } = Array.Empty<TrdpDataset>();
        public IReadOnlyList<TrdpXmlTelegram> Telegrams { get; private set; } = Array.Empty<TrdpXmlTelegram>();
        public IReadOnlyList<TrdpXmlComParameter> ComParameters { get; private set; } = Array.Empty<TrdpXmlComParameter>();

        /// <summary>DE: Liefert das Dataset zu einer ComId (über das passende Telegramm).</summary>
        public TrdpDataset? DatasetForComId(uint comId)
        {
            TrdpXmlTelegram? tlg = Telegrams.FirstOrDefault(t => t.ComId == comId);
            if (tlg == null) return null;
            return Datasets.TryGet(tlg.DataSetId, out var ds) ? ds : null;
        }

        /// <summary>DE: Lädt und parst eine TRDP-XML-Konfigurationsdatei (tau_prepareXmlDoc + readXml*).</summary>
        public static TrdpXmlConfig Load(string path) => Parse(File.ReadAllText(path));

        /// <summary>DE: Parst eine TRDP-XML-Konfiguration aus einem String.</summary>
        public static TrdpXmlConfig Parse(string xml)
        {
            var doc = XDocument.Parse(xml);
            XElement device = doc.Root?.Name.LocalName == "device"
                ? doc.Root
                : doc.Descendants().First(e => e.Name.LocalName == "device");

            var config = new TrdpXmlConfig
            {
                HostName = (string?)device.Attribute("host-name"),
                LeaderName = (string?)device.Attribute("leader-name"),
                DeviceType = (string?)device.Attribute("type"),
            };

            config.ParseDatasets(device);
            config.ParseComParameters(device);
            config.ParseTelegrams(device);
            return config;
        }

        private void ParseDatasets(XElement device)
        {
            var list = new List<TrdpDataset>();
            foreach (XElement dsEl in device.Descendants().Where(e => e.Name.LocalName == "data-set"))
            {
                uint id = AttrUInt(dsEl, "id", 0);
                var elements = new List<TrdpDatasetElement>();
                foreach (XElement elEl in dsEl.Elements().Where(e => e.Name.LocalName == "element"))
                {
                    string typeStr = (string?)elEl.Attribute("type") ?? "";
                    uint count = AttrUInt(elEl, "array-size", 1);
                    string? name = (string?)elEl.Attribute("name");
                    elements.Add(new TrdpDatasetElement(ParseType(typeStr), count, name));
                }
                var ds = new TrdpDataset(id, elements.ToArray());
                Datasets.Add(ds);
                list.Add(ds);
            }
            DatasetList = list;
        }

        private void ParseComParameters(XElement device)
        {
            var list = new List<TrdpXmlComParameter>();
            foreach (XElement cp in device.Descendants().Where(e => e.Name.LocalName == "com-parameter"))
            {
                list.Add(new TrdpXmlComParameter
                {
                    Id = AttrUInt(cp, "id", 0),
                    Qos = AttrUInt(cp, "qos", 0),
                    Ttl = AttrUInt(cp, "ttl", 0),
                });
            }
            ComParameters = list;
        }

        private void ParseTelegrams(XElement device)
        {
            var list = new List<TrdpXmlTelegram>();
            foreach (XElement tlg in device.Descendants().Where(e => e.Name.LocalName == "telegram"))
            {
                XElement? pd = tlg.Elements().FirstOrDefault(e => e.Name.LocalName == "pd-parameter");

                var destinations = tlg.Elements()
                    .Where(e => e.Name.LocalName == "destination")
                    .Select(d => new TrdpXmlDestination
                    {
                        Id = AttrUInt(d, "id", 0),
                        Uri = (string?)d.Attribute("uri"),
                    })
                    .ToList();

                var sources = tlg.Elements()
                    .Where(e => e.Name.LocalName == "source")
                    .Select(s => new TrdpXmlSource
                    {
                        Id = AttrUInt(s, "id", 0),
                        Uri = (string?)s.Attribute("uri"),
                    })
                    .ToList();

                list.Add(new TrdpXmlTelegram
                {
                    Name = (string?)tlg.Attribute("name"),
                    ComId = AttrUInt(tlg, "com-id", 0),
                    DataSetId = AttrUInt(tlg, "data-set-id", 0),
                    ComParameterId = AttrUInt(tlg, "com-parameter-id", 0),
                    CycleTimeUs = pd != null ? AttrUInt(pd, "cycle", 0) : 0,
                    TimeoutUs = pd != null ? AttrUInt(pd, "timeout", 0) : 0,
                    Marshall = pd != null && string.Equals((string?)pd.Attribute("marshall"), "on", StringComparison.OrdinalIgnoreCase),
                    Destinations = destinations,
                    Sources = sources,
                });
            }
            Telegrams = list;
        }

        // ── Hilfen ──

        private static uint AttrUInt(XElement el, string name, uint defaultValue)
        {
            string? s = (string?)el.Attribute(name);
            if (string.IsNullOrWhiteSpace(s)) return defaultValue;
            return uint.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : defaultValue;
        }

        // DE: Mappt einen Typ-String (z. B. "UINT32") oder eine numerische Dataset-ID auf eine Typ-ID.
        private static uint ParseType(string typeStr)
        {
            typeStr = typeStr.Trim();
            if (uint.TryParse(typeStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint num))
            {
                return num; // Basistyp-ID (<=30) oder verschachtelte Dataset-ID (>30)
            }
            return (uint)NameToType(typeStr);
        }

        private static TrdpDataType NameToType(string name) => name.ToUpperInvariant() switch
        {
            "BOOL8" or "BITSET8" => TrdpDataType.BitSet8,
            "CHAR8"      => TrdpDataType.Char8,
            "UTF16"      => TrdpDataType.Utf16,
            "INT8"       => TrdpDataType.Int8,
            "INT16"      => TrdpDataType.Int16,
            "INT32"      => TrdpDataType.Int32,
            "INT64"      => TrdpDataType.Int64,
            "UINT8"      => TrdpDataType.UInt8,
            "UINT16"     => TrdpDataType.UInt16,
            "UINT32"     => TrdpDataType.UInt32,
            "UINT64"     => TrdpDataType.UInt64,
            "REAL32"     => TrdpDataType.Real32,
            "REAL64"     => TrdpDataType.Real64,
            "TIMEDATE32" => TrdpDataType.TimeDate32,
            "TIMEDATE48" => TrdpDataType.TimeDate48,
            "TIMEDATE64" => TrdpDataType.TimeDate64,
            _ => throw new FormatException($"Unbekannter TRDP-Datentyp: '{name}'"),
        };
    }
}
