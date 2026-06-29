// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tau_xsession.c
// (tau_xsession_load/init, publishTelegram/subscribeTelegram, setCom/getCom, cycle, ComId2DatasetId).
// Komfortschicht: oeffnet eine TrdpSession aus der XML-Config und richtet pro <telegram> Publisher
// bzw. Subscriber ein; set/get per ComId ueber den TrdpMarshaller.
//
// Hinweis zum Port: Das C-Original loest Publisher (destCnt) und Subscriber (srcCnt) ueber separate
// Quell-/Ziel-Listen auf. Die hier genutzte TrdpXmlConfig parst nur Destinations; daher gilt die
// Regel: Telegramm mit Destination(s) => Publisher, jedes Telegramm => zusaetzlich Subscriber (so dass
// GetComId/Self-Loopback und reine Empfangs-Telegramme funktionieren).
//
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Trdp.Net.Core;
using Trdp.Net.Marshalling;
using Trdp.Net.Pd;
using Trdp.Net.Xml;

namespace Trdp.Net.Tau.XSession
{
    /// <summary>
    /// DE: Komfortschicht ueber <see cref="TrdpSession"/> (Entsprechung zu tau_xsession). Liest eine
    /// TRDP-XML-Konfiguration, legt fuer jedes <c>&lt;telegram&gt;</c> einen PD-Publisher (Ziel aus
    /// Destination-URI, Zyklus aus <c>cycle</c>/1000) bzw. Subscriber an und bietet Lesen/Schreiben der
    /// Nutzdaten ueber die ComId mittels <see cref="TrdpMarshaller"/>. Aus EINEM Thread/Zyklus nutzen.
    /// </summary>
    public sealed class TauXSession : IDisposable
    {
        /// <summary>DE: Die zugrunde liegende vereinheitlichte TRDP-Session (appHandle-Aequivalent).</summary>
        public TrdpSession Session { get; }

        /// <summary>DE: Die geparste XML-Gerätekonfiguration.</summary>
        public TrdpXmlConfig Config { get; }

        private readonly TrdpMarshaller _marshaller;

        // DE: Pro ComId alle aus den Destinations erzeugten Publisher (publishTelegram iteriert Ziele).
        private readonly Dictionary<uint, List<PdPublisher>> _publishers = new();

        // DE: Pro ComId der Subscriber (subscribeTelegram).
        private readonly Dictionary<uint, PdSubscriber> _subscribers = new();

        // DE: Optionaler Resolver fuer nicht-numerische URIs (z. B. tau_dnr: uri => dnr.IpFromUri(uri)).
        private readonly Func<string, IPAddress?>? _uriResolver;

        private TauXSession(TrdpSession session, TrdpXmlConfig config, Func<string, IPAddress?>? uriResolver)
        {
            Session = session;
            Config = config;
            _uriResolver = uriResolver;
            _marshaller = new TrdpMarshaller(config.Datasets);
        }

        /// <summary>
        /// DE: Laedt die Konfiguration aus einer Datei (tau_xsession_load mit length==0) und richtet die
        /// Session samt Publisher/Subscriber ein.
        /// </summary>
        /// <param name="xmlPath">Pfad zur TRDP-XML-Konfigurationsdatei.</param>
        /// <param name="bindIp">Lokale IP zum Binden (null = INADDR_ANY).</param>
        /// <param name="uriResolver">Optional: loest nicht-numerische URIs auf (z. B. <c>uri =&gt; dnr.IpFromUri(uri)</c>).</param>
        public static TauXSession Load(string xmlPath, IPAddress? bindIp = null, Func<string, IPAddress?>? uriResolver = null)
            => LoadXml(File.ReadAllText(xmlPath), bindIp, uriResolver);

        /// <summary>
        /// DE: Laedt die Konfiguration aus einem XML-String (tau_xsession_load mit length>0) und richtet
        /// die Session samt Publisher/Subscriber ein.
        /// </summary>
        /// <param name="xml">XML-Konfiguration als String.</param>
        /// <param name="bindIp">Lokale IP zum Binden (null = INADDR_ANY).</param>
        public static TauXSession LoadXml(string xml, IPAddress? bindIp = null, Func<string, IPAddress?>? uriResolver = null)
        {
            TrdpXmlConfig config = TrdpXmlConfig.Parse(xml);
            // DE: Nur PD wird aus <telegram> konfiguriert; MD bleibt deaktiviert (keine md-parameter im Schema).
            var session = new TrdpSession(bindIp, enableMd: false);
            var x = new TauXSession(session, config, uriResolver);
            x.SetupTelegrams();
            return x;
        }

        // DE: Entspricht configureSession + publishTelegram/subscribeTelegram: pro Telegramm aufbauen.
        private void SetupTelegrams()
        {
            foreach (TrdpXmlTelegram tlg in Config.Telegrams)
            {
                // DE: cycle ist in Mikrosekunden konfiguriert; PD-Session rechnet in Millisekunden.
                int cycleMs = (int)(tlg.CycleTimeUs / 1000u);
                int timeoutMs = (int)(tlg.TimeoutUs / 1000u);

                // DE: Subscriber fuer jedes Telegramm (entspricht subscribeTelegram). Quellfilter aus der
                //     ersten aufloesbaren <source>-URI (sonst beliebige Quelle).
                IPAddress? sourceFilter = null;
                foreach (TrdpXmlSource src in tlg.Sources)
                {
                    sourceFilter = ResolveUri(src.Uri);
                    if (sourceFilter != null) break;
                }
                PdSubscriber sub = Session.Pd.Subscribe(tlg.ComId, timeoutMs, sourceFilter);
                _subscribers[tlg.ComId] = sub;

                // DE: Publisher fuer jede Destination (entspricht publishTelegram, das ueber pDest iteriert).
                var pubList = new List<PdPublisher>();
                foreach (TrdpXmlDestination dest in tlg.Destinations)
                {
                    IPAddress? destIp = ResolveUri(dest.Uri);
                    if (destIp == null)
                    {
                        // DE: Nicht aufloesbar (nicht-numerisch ohne uriResolver) -> kein Publisher.
                        continue;
                    }

                    PdPublisher pub = Session.Publish(tlg.ComId, destIp, cycleMs);
                    pubList.Add(pub);

                    // DE: Bei Multicast-Ziel der Gruppe beitreten, damit eigene/fremde Sends empfangen werden
                    //     (vgl. destMCIP-Join in subscribeTelegram).
                    if (IsMulticast(destIp))
                    {
                        Session.Pd.JoinMulticast(destIp);
                    }
                }
                if (pubList.Count > 0)
                {
                    _publishers[tlg.ComId] = pubList;
                }
            }
        }

        /// <summary>
        /// DE: Setzt die zu sendenden Nutzdaten eines Publish-Telegramms (tau_xsession_setCom): marshallt
        /// <paramref name="values"/> gemaess Dataset der ComId und legt sie auf alle Publisher der ComId.
        /// </summary>
        /// <param name="comId">ComId aus der Telegramm-Definition.</param>
        /// <param name="values">Flache Werteliste passend zum Dataset (siehe <see cref="TrdpMarshaller"/>).</param>
        public void SetComId(uint comId, IReadOnlyList<object> values)
        {
            if (!_publishers.TryGetValue(comId, out List<PdPublisher>? pubs))
            {
                throw new InvalidOperationException($"Kein Publisher fuer ComId {comId} konfiguriert.");
            }
            TrdpDataset ds = Config.DatasetForComId(comId)
                ?? throw new InvalidOperationException($"Kein Dataset fuer ComId {comId} gefunden.");

            byte[] wire = _marshaller.Marshal(ds, values);
            foreach (PdPublisher pub in pubs)
            {
                pub.SetData(wire);
            }
        }

        /// <summary>
        /// DE: Liest die zuletzt empfangenen Nutzdaten eines Subscribe-Telegramms (tau_xsession_getCom) und
        /// unmarshallt sie gemaess Dataset der ComId. Liefert <c>null</c>, wenn (noch) keine gueltigen Daten
        /// vorliegen (entspricht TRDP_NODATA_ERR/TRDP_TIMEOUT_ERR).
        /// </summary>
        public object[]? GetComId(uint comId)
        {
            return TryGetComId(comId, out object[] values) ? values : null;
        }

        /// <summary>
        /// DE: Wie <see cref="GetComId"/>, liefert aber <c>true</c>/<c>false</c> und die Werte ueber den
        /// out-Parameter.
        /// </summary>
        public bool TryGetComId(uint comId, out object[] values)
        {
            values = Array.Empty<object>();
            if (!_subscribers.TryGetValue(comId, out PdSubscriber? sub))
            {
                throw new InvalidOperationException($"Kein Subscriber fuer ComId {comId} konfiguriert.");
            }
            if (!sub.TryGetData(out byte[] data))
            {
                return false; // TRDP_NODATA_ERR / TRDP_TIMEOUT_ERR
            }
            TrdpDataset ds = Config.DatasetForComId(comId)
                ?? throw new InvalidOperationException($"Kein Dataset fuer ComId {comId} gefunden.");

            values = _marshaller.Unmarshal(ds, data);
            return true;
        }

        /// <summary>
        /// DE: Verarbeitungszyklus (tau_xsession_cycle / tlc_process): faellige Telegramme senden, eingehende
        /// empfangen, Timeouts pruefen. Aus dem App-/ExecutionLoop aufrufen.
        /// </summary>
        public void Process() => Session.Process();

        /// <summary>
        /// DE: Verarbeitet zyklisch fuer <paramref name="milliseconds"/> ms (Port von tau_xsession_cycle_until).
        /// </summary>
        public void ProcessFor(int milliseconds, int stepMs = 5)
        {
            long end = Environment.TickCount64 + milliseconds;
            while (Environment.TickCount64 < end)
            {
                Session.Process();
                System.Threading.Thread.Sleep(stepMs);
            }
        }

        /// <summary>
        /// DE: Verarbeitet, bis <paramref name="condition"/> erfuellt ist oder <paramref name="timeoutMs"/>
        /// ablaeuft (Port von tau_xsession_cycle_check). Liefert true bei erfuellter Bedingung.
        /// </summary>
        public bool ProcessUntil(Func<bool> condition, int timeoutMs, int stepMs = 5)
        {
            if (condition is null) throw new ArgumentNullException(nameof(condition));
            long end = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < end)
            {
                Session.Process();
                if (condition()) return true;
                System.Threading.Thread.Sleep(stepMs);
            }
            return condition();
        }

        /// <summary>DE: Liefert die Dataset-ID zu einer ComId (tau_xsession_ComId2DatasetId).</summary>
        public bool TryGetDatasetId(uint comId, out uint datasetId)
        {
            foreach (TrdpXmlTelegram tlg in Config.Telegrams)
            {
                if (tlg.ComId == comId)
                {
                    datasetId = tlg.DataSetId;
                    return true;
                }
            }
            datasetId = 0;
            return false;
        }

        /// <summary>DE: Zugriff auf den Subscriber einer ComId (oder null, wenn keiner konfiguriert ist).</summary>
        public PdSubscriber? GetSubscriber(uint comId)
            => _subscribers.TryGetValue(comId, out PdSubscriber? sub) ? sub : null;

        /// <summary>DE: Zugriff auf die Publisher einer ComId (leer, wenn keine konfiguriert sind).</summary>
        public IReadOnlyList<PdPublisher> GetPublishers(uint comId)
            => _publishers.TryGetValue(comId, out List<PdPublisher>? pubs)
                ? pubs
                : Array.Empty<PdPublisher>();

        // DE: URI -> IPAddress. Numerische IPv4-Strings werden direkt geparst; nicht-numerische URIs
        //     (Namen) werden ueber den optionalen uriResolver (z. B. tau_dnr) aufgeloest.
        private IPAddress? ResolveUri(string? uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return null;
            }
            string u = uri.Trim();
            // DE: Nur echte Dotted-Quad-Adressen direkt akzeptieren (IPAddress.Parse wuerde z. B. "1" akzeptieren).
            if (u.IndexOf('.') >= 0 && IPAddress.TryParse(u, out IPAddress? ip))
            {
                return ip;
            }
            // DE: Namensaufloesung via DNR/Resolver, falls bereitgestellt (uri => dnr.IpFromUri(uri)).
            return _uriResolver?.Invoke(u);
        }

        // DE: IPv4-Multicast-Erkennung (224.0.0.0/4), Aequivalent zu vos_isMulticast.
        private static bool IsMulticast(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return b.Length == 4 && b[0] >= 224 && b[0] <= 239;
        }

        /// <summary>DE: Schliesst die Session und gibt Ressourcen frei (tau_xsession_delete).</summary>
        public void Dispose() => Session.Dispose();
    }
}
