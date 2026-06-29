// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/trdp_pdindex.c
// (HIGH_PERF_INDEXED: perf_table_category, indexCreatePubTable, distribute,
//  trdp_pdSendIndexed) und trdp_pdindex.h (Zyklusklassen-Konstanten, Base-10).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;

namespace Trdp.Net.Pd.Index
{
    /// <summary>
    /// DE: Indizierte Sende-Terminplanung fuer PD-Publisher (Port der HIGH_PERF_INDEXED
    /// Slot-Logik aus trdp_pdindex.c). Statt die Sendewarteschlange linear zu durchlaufen
    /// (wie <see cref="TrdpPdSession"/>), werden die Publisher beim Hinzufuegen ueber
    /// Zeitschlitz-Tabellen (eine je Zyklusklasse) verteilt; <see cref="GetDueAt"/> liefert
    /// dann in O(k) (k = Tiefe des aktiven Slots) statt O(n) die faelligen Publisher.
    ///
    /// DE: Das beobachtbare Verhalten ist identisch zur linearen Variante: jeder Publisher
    /// wird mit seiner Zykluszeit gesendet. <see cref="GetDueAtReference"/> ist die naive
    /// O(n)-Vergleichsvariante (linearer Scan) und liefert nachweislich (Aequivalenz-Test)
    /// dieselbe "faellig"-Menge wie <see cref="GetDueAt"/>.
    ///
    /// DE: Eigenstaendige Hilfsklasse — kein Umbau der bestehenden Session. Nicht thread-safe.
    /// </summary>
    public sealed class PdSendIndex
    {
        // ─── Zyklusklassen-Konstanten (Base 10, vgl. trdp_pdindex.h) ───────────────────
        // DE: Alle Zeiten hier in MILLISEKUNDEN (das Original rechnet in µs). Die Slot-Anzahl
        //     je Tabelle ist Bereich/Slot-Zyklus = 100.

        /// <summary>DE: Slot-Zyklus der low-Tabelle (1ms), Bereich 1..100ms.</summary>
        private const int LowSlotCycleMs = 1;
        /// <summary>DE: Slot-Zyklus der mid-Tabelle (10ms), Bereich 101..1000ms.</summary>
        private const int MidSlotCycleMs = 10;
        /// <summary>DE: Slot-Zyklus der high-Tabelle (100ms), Bereich 1001..10000ms.</summary>
        private const int HighSlotCycleMs = 100;

        /// <summary>DE: Anzahl Zeitschlitze je Tabelle (rangeMax / slotCycle = 100).</summary>
        private const int Slots = 100;

        /// <summary>DE: Obergrenze low-Klasse (TRDP_LOW_CYCLE_LIMIT, 100ms).</summary>
        private const int LowLimitMs = 100;
        /// <summary>DE: Obergrenze mid-Klasse (TRDP_MID_CYCLE_LIMIT, 1000ms).</summary>
        private const int MidLimitMs = 1000;
        /// <summary>DE: Obergrenze high-Klasse (TRDP_HIGH_CYCLE_LIMIT, 10000ms); darueber: ext.</summary>
        private const int HighLimitMs = 10000;

        /// <summary>DE: Gesamtperiode des Index (highCat.slots * highCat.slotCycle = 10000ms).</summary>
        public const int PeriodMs = Slots * HighSlotCycleMs;

        // DE: Gating-Konstanten aus trdp_pdSendIndexed:
        //  - mid wird bearbeitet, wenn (idxLow % (MID/LOW)) == (MID/LOW/2)  => cyc%10 == 5
        //  - high (und ext) werden bearbeitet, wenn idxLow == 0             => cyc%100 == 0
        private const int MidGateMod = MidSlotCycleMs / LowSlotCycleMs;   // 10
        private const int MidGatePhase = MidGateMod / 2;                  // 5

        /// <summary>DE: Telegramm-Zyklusklassen (vgl. PERF_TABLE_TYPE_T).</summary>
        private enum PerfClass
        {
            /// <summary>DE: nicht zaehlen (Zyklus 0 / Pull / TSN).</summary>
            Ignore,
            Low,
            Mid,
            High,
            /// <summary>DE: extrem lange Intervalle (&gt;= 10000ms).</summary>
            Ext
        }

        // ─── Eine Zeitschlitz-Tabelle (vgl. TRDP_HP_CAT_SLOT_T) ────────────────────────
        private sealed class CatTable
        {
            public readonly int SlotCycleMs;
            public readonly int Depth;
            // DE: 2D-Array [slot][depth] mit Publisher-Verweisen (null = leer).
            public readonly PdPublisher?[,] Grid;

            public CatTable(int slotCycleMs, int depth)
            {
                SlotCycleMs = slotCycleMs;
                Depth = depth;
                Grid = new PdPublisher?[Slots, depth];
            }
        }

        // DE: Registrierter Publisher samt Klasse und tatsaechlich belegten Slots
        //     (Letzteres fuer den linearen Vergleichsscan, unabhaengig vom Grid).
        private sealed class Entry
        {
            public required PdPublisher Pub;
            public PerfClass Cls;
            // DE: tatsaechlich belegte Slot-Indizes (low/mid/high). Spiegelt auch den Jitter
            //     wider, falls die Tiefe ueberlief (idx++ in Distribute).
            public readonly HashSet<int> OccupiedSlots = new();
            // DE: ext-Parameter (in 100ms-Einheiten): Periode und Phase.
            public int ExtPeriodUnits;
            public int ExtPhaseUnits;
        }

        private readonly CatTable _low;
        private readonly CatTable _mid;
        private readonly CatTable _high;
        private readonly List<Entry> _entries = new();
        private int _extCounter; // DE: zum gleichmaessigen Verteilen der ext-Phasen.

        /// <summary>
        /// DE: Legt einen Index an. <paramref name="maxDepth"/> begrenzt die Tiefe je Slot
        /// (Original: max. 255). Bei Ueberlauf weicht <c>Distribute</c> in den naechsten Slot
        /// aus (Jitter), das beobachtbare Ergebnis bleibt zwischen schnellem und linearem
        /// Pfad konsistent.
        /// </summary>
        public PdSendIndex(int maxDepth = 255)
        {
            if (maxDepth < 1 || maxDepth > 255)
                throw new ArgumentOutOfRangeException(nameof(maxDepth), "Tiefe muss 1..255 sein (vgl. trdp_pdindex.c).");

            _low = new CatTable(LowSlotCycleMs, maxDepth);
            _mid = new CatTable(MidSlotCycleMs, maxDepth);
            _high = new CatTable(HighSlotCycleMs, maxDepth);
        }

        /// <summary>DE: Anzahl der im Index verwalteten (zyklischen + ext) Publisher.</summary>
        public int Count => _entries.Count;

        /// <summary>
        /// DE: Fuegt einen Publisher in die passende Zeitschlitz-Tabelle ein
        /// (vgl. perf_table_category + distribute aus trdp_indexCreatePubTables).
        /// Publisher mit Zyklus &lt;= 0 werden ignoriert (PERF_IGNORE), wie in der
        /// linearen Variante (<c>if (pub.CycleTimeMs &lt;= 0) continue;</c>).
        /// </summary>
        public void AddPublisher(PdPublisher pub)
        {
            ArgumentNullException.ThrowIfNull(pub);

            PerfClass cls = Categorize(pub.CycleTimeMs);
            if (cls == PerfClass.Ignore)
            {
                return; // DE: nicht terminiert (manuell/on-demand)
            }

            var entry = new Entry { Pub = pub, Cls = cls };

            switch (cls)
            {
                case PerfClass.Low:
                    Distribute(_low, pub, entry.OccupiedSlots);
                    break;
                case PerfClass.Mid:
                    Distribute(_mid, pub, entry.OccupiedSlots);
                    break;
                case PerfClass.High:
                    Distribute(_high, pub, entry.OccupiedSlots);
                    break;
                case PerfClass.Ext:
                    // DE: Vereinfachung ggue. C (dort absolute timeToGo-Pruefung):
                    //     ext wird hier als grob (100ms) quantisierter Zyklus modelliert,
                    //     ebenfalls nur alle 100ms (idxLow==0) bearbeitet. Phasen werden
                    //     reihum verteilt. Beobachtbar: feuert ~alle ExtPeriodUnits*100ms.
                    entry.ExtPeriodUnits = pub.CycleTimeMs / HighSlotCycleMs; // >= 100
                    entry.ExtPhaseUnits = _extCounter % entry.ExtPeriodUnits;
                    _extCounter++;
                    break;
            }

            _entries.Add(entry);
        }

        /// <summary>
        /// DE: Liefert die zum Zeitpunkt <paramref name="nowMs"/> faelligen Publisher —
        /// schneller Pfad (Port von trdp_pdSendIndexed): nur die aktiven Slot-Spalten der
        /// drei Tabellen lesen (+ Gating + ext). Reihenfolge: low, dann mid, dann high/ext.
        /// </summary>
        public IReadOnlyList<PdPublisher> GetDueAt(long nowMs)
        {
            var due = new List<PdPublisher>();
            int cyc = (int)Mod(nowMs, PeriodMs);

            // DE: low-Tabelle wird in jedem 1ms-Schritt bedient.
            int idxLow = cyc % Slots;
            CollectColumn(_low, idxLow, due);

            // DE: mid-Tabelle: an die Mitte des low-Zeitschlitzes gelegt (#419), cyc%10==5.
            if (idxLow % MidGateMod == MidGatePhase)
            {
                int idxMid = (cyc / MidSlotCycleMs) % Slots;
                CollectColumn(_mid, idxMid, due);
            }

            // DE: high-Tabelle (und ext) nur alle 100ms (idxLow == 0).
            if (idxLow == 0)
            {
                int idxHigh = (cyc / HighSlotCycleMs) % Slots;
                CollectColumn(_high, idxHigh, due);

                long unit = nowMs / HighSlotCycleMs; // 100ms-Einheit
                foreach (Entry e in _entries)
                {
                    if (e.Cls == PerfClass.Ext &&
                        Mod(unit, e.ExtPeriodUnits) == e.ExtPhaseUnits)
                    {
                        due.Add(e.Pub);
                    }
                }
            }

            return due;
        }

        /// <summary>
        /// DE: Naiver linearer Vergleichsscan (O(n)) — durchlaeuft ALLE Publisher und
        /// entscheidet je Publisher anhand seiner belegten Slots/Parameter, ob er zum
        /// Zeitpunkt <paramref name="nowMs"/> faellig ist. Bewusst unabhaengig vom Grid und
        /// von <see cref="GetDueAt"/> implementiert: dient als Referenz fuer den
        /// Aequivalenz-Test (schneller Pfad == linearer Pfad).
        /// </summary>
        public IReadOnlyList<PdPublisher> GetDueAtReference(long nowMs)
        {
            var due = new List<PdPublisher>();
            int cyc = (int)Mod(nowMs, PeriodMs);

            foreach (Entry e in _entries)
            {
                switch (e.Cls)
                {
                    case PerfClass.Low:
                        if (e.OccupiedSlots.Contains(cyc % Slots))
                            due.Add(e.Pub);
                        break;

                    case PerfClass.Mid:
                        if (cyc % MidGateMod == MidGatePhase &&
                            e.OccupiedSlots.Contains((cyc / MidSlotCycleMs) % Slots))
                            due.Add(e.Pub);
                        break;

                    case PerfClass.High:
                        if (cyc % HighSlotCycleMs == 0 &&
                            e.OccupiedSlots.Contains((cyc / HighSlotCycleMs) % Slots))
                            due.Add(e.Pub);
                        break;

                    case PerfClass.Ext:
                        if (cyc % HighSlotCycleMs == 0 &&
                            Mod(nowMs / HighSlotCycleMs, e.ExtPeriodUnits) == e.ExtPhaseUnits)
                            due.Add(e.Pub);
                        break;
                }
            }

            return due;
        }

        // ─── interne Hilfen ────────────────────────────────────────────────────────────

        /// <summary>
        /// DE: Bestimmt die Zyklusklasse anhand der Zykluszeit (ms) — Port von
        /// perf_table_category (Base 10). TSN/Pull wird hier nicht modelliert.
        /// </summary>
        private static PerfClass Categorize(int cycleTimeMs)
        {
            if (cycleTimeMs <= 0)
            {
                return PerfClass.Ignore;          // DE: 0-Intervall => nur manuell/Pull
            }
            if (cycleTimeMs <= LowLimitMs)
            {
                return PerfClass.Low;             // 1..100ms
            }
            if (cycleTimeMs <= MidLimitMs)
            {
                return PerfClass.Mid;             // 101..1000ms
            }
            if (cycleTimeMs < HighLimitMs)
            {
                return PerfClass.High;            // 1001..9999ms
            }
            return PerfClass.Ext;                 // >= 10000ms
        }

        /// <summary>
        /// DE: Verteilt einen Publisher gleichmaessig ueber die Slot-Tabelle (Port von
        /// distribute()). Sucht rueckwaerts einen freien Slot, traegt den Publisher dann
        /// alle <c>stride</c> Slots <c>count</c>-mal ein. Bei voller Tiefe weicht die Logik
        /// auf den naechsten Slot aus (zusaetzlicher Jitter). Die tatsaechlich belegten
        /// Slots werden in <paramref name="occupied"/> gesammelt.
        /// </summary>
        private static void Distribute(CatTable cat, PdPublisher pub, HashSet<int> occupied)
        {
            int interval = pub.CycleTimeMs;

            // DE: Position spaetestens hier, sonst entstuende eine Luecke.
            int maxStartIdx = interval / cat.SlotCycleMs;
            // DE: So oft muss der Publisher im Array vorkommen.
            int count = Slots * cat.SlotCycleMs / interval;
            int stride = maxStartIdx;

            if (maxStartIdx < 1) maxStartIdx = 1;
            if (count < 1) count = 1;
            if (stride < 1) stride = 1;

            // DE: Kontrolle (vgl. C): darf das Array nicht ueberlaufen.
            if (maxStartIdx * count > Slots)
            {
                throw new InvalidOperationException(
                    $"Konfigurationsproblem: PD-Intervall {interval}ms passt nicht (startIdx {maxStartIdx}, count {count}).");
            }

            // DE: ersten freien Slot suchen (rueckwaerts ab maxStartIdx-1).
            int startIdx = 0;
            int depthIdx = 0;
            bool found = false;
            for (depthIdx = 0; depthIdx < cat.Depth; depthIdx++)
            {
                for (startIdx = maxStartIdx - 1; startIdx >= 0; startIdx--)
                {
                    if (cat.Grid[startIdx, depthIdx] == null)
                    {
                        found = true;
                        break;
                    }
                }
                if (found)
                {
                    break;
                }
            }

            if (!found || startIdx < 0 || depthIdx >= cat.Depth)
            {
                throw new InvalidOperationException("Kein Platz im Index (Tiefe erschoepft).");
            }

            // DE: Eintragen — aeussere Schleife Slot, innere Schleife Tiefe.
            int depthStart = depthIdx;
            for (int idx = startIdx; idx < Slots && count > 0;)
            {
                bool done = false;
                for (int d = depthStart; d < cat.Depth; d++)
                {
                    if (cat.Grid[idx, d] == null)
                    {
                        cat.Grid[idx, d] = pub;
                        occupied.Add(idx);
                        depthStart = 0;
                        count--;
                        done = true;
                        break;
                    }
                }
                if (done)
                {
                    idx += stride;       // DE: zum naechsten Soll-Slot springen
                }
                else
                {
                    idx++;               // DE: Tiefe erschoepft => Jitter, naechster Slot
                }
            }
        }

        /// <summary>
        /// DE: Liest die Tiefenspalte eines Slots bis zum ersten leeren Eintrag (Port der
        /// depth-Schleife mit break-on-NULL aus trdp_pdSendIndexed). Eintraege werden je
        /// Slot tiefenkontinuierlich ab 0 belegt, daher ist break-on-NULL korrekt.
        /// </summary>
        private static void CollectColumn(CatTable cat, int slot, List<PdPublisher> due)
        {
            for (int d = 0; d < cat.Depth; d++)
            {
                PdPublisher? p = cat.Grid[slot, d];
                if (p == null)
                {
                    break;
                }
                due.Add(p);
            }
        }

        /// <summary>DE: nicht-negativer Modulo (immer 0..mod-1).</summary>
        private static long Mod(long value, long mod)
        {
            long r = value % mod;
            return r < 0 ? r + mod : r;
        }
    }
}
