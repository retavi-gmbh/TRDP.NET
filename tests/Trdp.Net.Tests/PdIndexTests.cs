// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/trdp_pdindex.c
// (Aequivalenz-/Verhaltenstests fuer die indizierte Sende-Terminplanung).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Trdp.Net.Core;
using Trdp.Net.Pd;
using Trdp.Net.Pd.Index;
using Xunit;

namespace Trdp.Net.Tests
{
    public class PdIndexTests
    {
        // DE: Erzeugt einen Publisher (interner Ctor, via InternalsVisibleTo erreichbar)
        //     ohne realen Socket. Die Daten sind fuer die Terminplanung irrelevant.
        private static PdPublisher MakePub(uint comId, int cycleMs) =>
            new PdPublisher(comId, IPAddress.Loopback, TrdpConstants.PdUdpPort, cycleMs, new byte[] { 1 });

        /// <summary>
        /// DE: Kern-Aequivalenztest. Bei einer deterministisch (per Seed) erzeugten,
        /// gemischten Publisher-Menge (low/mid/high/ext) muss der schnelle, indizierte
        /// Pfad (GetDueAt) ueber viele Zeitpunkte dieselbe Menge faelliger Publisher
        /// liefern wie der lineare Vergleichsscan (GetDueAtReference).
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(42)]
        [InlineData(1234)]
        public void GetDueAt_Equals_LinearReference_OverManyTimePoints(int seed)
        {
            var rnd = new Random(seed);
            var index = new PdSendIndex();

            // DE: gemischte Klassen erzeugen.
            int n = 60;
            for (uint i = 0; i < n; i++)
            {
                int cls = rnd.Next(4);
                int cycle = cls switch
                {
                    0 => rnd.Next(1, 101),        // low   1..100ms
                    1 => rnd.Next(101, 1001),     // mid   101..1000ms
                    2 => rnd.Next(1001, 10000),   // high  1001..9999ms
                    _ => rnd.Next(10000, 40001),  // ext   >= 10000ms
                };
                index.AddPublisher(MakePub(1000u + i, cycle));
            }

            // DE: ueber zwei volle Perioden in 1ms-Schritten vergleichen.
            for (long t = 0; t <= 2L * PdSendIndex.PeriodMs; t++)
            {
                var fast = new HashSet<PdPublisher>(index.GetDueAt(t));
                var linear = new HashSet<PdPublisher>(index.GetDueAtReference(t));

                Assert.True(fast.SetEquals(linear),
                    $"Abweichung bei t={t}ms (seed={seed}): " +
                    $"schnell={fast.Count}, linear={linear.Count}");
            }
        }

        /// <summary>
        /// DE: Unabhaengige Periodizitaets-Pruefung (nicht ueber GetDueAtReference):
        /// ein einzelner low-Publisher mit teilerfremdem... bzw. teilendem Intervall T
        /// (1,2,4,5,10,20,25,50,100ms) wird innerhalb eines 100ms-Bereichs exakt 100/T-mal
        /// faellig, und zwar in gleichmaessigem Abstand T — also genau wie die lineare
        /// "alle T ms"-Variante der Session.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(20)]
        [InlineData(25)]
        [InlineData(50)]
        [InlineData(100)]
        public void LowPublisher_DivisorInterval_FiresEveryTms(int t)
        {
            var index = new PdSendIndex();
            var pub = MakePub(2000, t);
            index.AddPublisher(pub);

            // DE: faellige Zyklen im low-Superzyklus 0..99 einsammeln.
            var dueCycles = new List<int>();
            for (int c = 0; c < 100; c++)
            {
                if (index.GetDueAt(c).Contains(pub))
                {
                    dueCycles.Add(c);
                }
            }

            Assert.Equal(100 / t, dueCycles.Count);

            // DE: gleichmaessiger Abstand T zwischen aufeinanderfolgenden Sendezeitpunkten.
            for (int k = 1; k < dueCycles.Count; k++)
            {
                Assert.Equal(t, dueCycles[k] - dueCycles[k - 1]);
            }
        }

        /// <summary>
        /// DE: Ein mid-Publisher wird nur in der Mitte des low-Slots (cyc%10==5) und
        /// genau im 10ms-Raster bearbeitet (Port-Verhalten von trdp_pdSendIndexed, #419).
        /// </summary>
        [Fact]
        public void MidPublisher_OnlyDueAtTenMsGrid_OffsetFive()
        {
            var index = new PdSendIndex();
            var pub = MakePub(3000, 200); // mid: 200ms
            index.AddPublisher(pub);

            for (long t = 0; t < PdSendIndex.PeriodMs; t++)
            {
                if (index.GetDueAt(t).Contains(pub))
                {
                    Assert.Equal(5, (int)(t % 10)); // immer auf dem 10ms-Raster, Offset 5
                }
            }
        }

        /// <summary>
        /// DE: High-Publisher werden nur alle 100ms (idxLow==0) bearbeitet.
        /// </summary>
        [Fact]
        public void HighPublisher_OnlyDueAtHundredMsGrid()
        {
            var index = new PdSendIndex();
            var pub = MakePub(4000, 2000); // high: 2000ms
            index.AddPublisher(pub);

            int hits = 0;
            for (long t = 0; t < PdSendIndex.PeriodMs; t++)
            {
                if (index.GetDueAt(t).Contains(pub))
                {
                    Assert.Equal(0, (int)(t % 100));
                    hits++;
                }
            }
            Assert.True(hits > 0);
        }

        /// <summary>
        /// DE: Ext-Publisher (Intervall &gt;= 10000ms) feuert grob im 100ms-Raster und mit
        /// der quantisierten Langzeit-Periode; hier 20000ms => alle 200 100ms-Einheiten.
        /// </summary>
        [Fact]
        public void ExtPublisher_FiresAtCoarsePeriod()
        {
            var index = new PdSendIndex();
            var pub = MakePub(5000, 20000); // ext: 20000ms => 200 Einheiten
            index.AddPublisher(pub);

            var dueTimes = new List<long>();
            for (long t = 0; t <= 60000; t += 100) // nur 100ms-Raster pruefen
            {
                if (index.GetDueAt(t).Contains(pub))
                {
                    dueTimes.Add(t);
                }
            }

            Assert.True(dueTimes.Count >= 2);
            for (int k = 1; k < dueTimes.Count; k++)
            {
                Assert.Equal(20000, dueTimes[k] - dueTimes[k - 1]);
            }
        }

        /// <summary>
        /// DE: Publisher mit Zyklus &lt;= 0 werden ignoriert (PERF_IGNORE) — nie faellig,
        /// nicht im Index gezaehlt. Entspricht "if (pub.CycleTimeMs &lt;= 0) continue;".
        /// </summary>
        [Fact]
        public void ZeroCyclePublisher_IsIgnored()
        {
            var index = new PdSendIndex();
            var pub = MakePub(6000, 0);
            index.AddPublisher(pub);

            Assert.Equal(0, index.Count);
            for (long t = 0; t < 1000; t++)
            {
                Assert.DoesNotContain(pub, index.GetDueAt(t));
            }
        }

        /// <summary>
        /// DE: Auch bei sehr vielen low-Publishern (gemeinsame Slots, hohe Tiefe) bleiben
        /// schneller und linearer Pfad identisch — testet die Tiefen-Spaltenlogik
        /// (break-on-NULL) sowie eventuellen Jitter bei Tiefenueberlauf.
        /// </summary>
        [Fact]
        public void ManyLowPublishers_FastEqualsLinear()
        {
            var index = new PdSendIndex(maxDepth: 64);
            var rnd = new Random(99);
            for (uint i = 0; i < 120; i++)
            {
                index.AddPublisher(MakePub(7000u + i, rnd.Next(1, 51)));
            }

            for (long t = 0; t < 200; t++)
            {
                var fast = new HashSet<PdPublisher>(index.GetDueAt(t));
                var linear = new HashSet<PdPublisher>(index.GetDueAtReference(t));
                Assert.True(fast.SetEquals(linear), $"Abweichung bei t={t}ms");
            }
        }
    }
}
