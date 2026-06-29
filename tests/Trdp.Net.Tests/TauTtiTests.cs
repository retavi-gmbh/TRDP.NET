// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": Tests fuer trdp/src/common/tau_tti.c bzw.
// tau_tti_types.h (Wire-Parsing der Zugtopologie-Telegramme).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Collections.Generic;
using Trdp.Net.Marshalling;
using Trdp.Net.Tau.Tti;
using Xunit;

namespace Trdp.Net.Tests
{
    public class TauTtiTests
    {
        private static byte[] Uuid(byte seed)
        {
            var u = new byte[TauTtiConstants.UuidLen];
            for (int i = 0; i < u.Length; i++) u[i] = (byte)(seed + i);
            return u;
        }

        // ── Feste Strukturen: Groesse + Round-Trip ──

        [Fact]
        public void OpVehicle_RoundTrip_24Bytes()
        {
            var orig = new TrdpOpVehicle
            {
                VehId = "VEH01", OpVehNo = 3, IsLead = 2, LeadDir = 1,
                TrnVehNo = 7, VehOrient = 1, OwnOpCstNo = 5, Reserved01 = 0, Reserved02 = 0
            };
            var buf = new byte[64];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            Assert.Equal(24, w.Position);

            var r = new TrdpWireReader(buf.AsSpan(0, w.Position));
            TrdpOpVehicle back = TrdpOpVehicle.Read(ref r);
            Assert.Equal("VEH01", back.VehId);
            Assert.Equal(3, back.OpVehNo);
            Assert.Equal(2, back.IsLead);
            Assert.Equal(7, back.TrnVehNo);
            Assert.Equal(5, back.OwnOpCstNo);
        }

        [Fact]
        public void OpConsist_RoundTrip_20Bytes()
        {
            var orig = new TrdpOpConsist { CstUUID = Uuid(0x10), OpCstNo = 2, OpCstOrient = 1, TrnCstNo = 4 };
            var buf = new byte[64];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            Assert.Equal(20, w.Position);

            var r = new TrdpWireReader(buf.AsSpan(0, w.Position));
            TrdpOpConsist back = TrdpOpConsist.Read(ref r);
            Assert.Equal(Uuid(0x10), back.CstUUID);
            Assert.Equal(2, back.OpCstNo);
            Assert.Equal(4, back.TrnCstNo);
        }

        [Fact]
        public void Consist_RoundTrip_24Bytes_BigEndianTopo()
        {
            var orig = new TrdpConsist { CstUUID = Uuid(1), CstTopoCnt = 0x01020304u, TrnCstNo = 1, CstOrient = 1 };
            var buf = new byte[64];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            Assert.Equal(24, w.Position);
            // Big-Endian-Pruefung des cstTopoCnt direkt nach der UUID (Offset 16).
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, buf[16..20]);

            var r = new TrdpWireReader(buf.AsSpan(0, w.Position));
            TrdpConsist back = TrdpConsist.Read(ref r);
            Assert.Equal(0x01020304u, back.CstTopoCnt);
            Assert.Equal(1, back.TrnCstNo);
        }

        [Fact]
        public void EtbInfo_RoundTrip_4Bytes()
        {
            var orig = new TrdpEtbInfo { EtbId = 1, CnCnt = 8, Reserved01 = 0 };
            var buf = new byte[8];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            Assert.Equal(4, w.Position);

            var r = new TrdpWireReader(buf.AsSpan(0, w.Position));
            TrdpEtbInfo back = TrdpEtbInfo.Read(ref r);
            Assert.Equal(1, back.EtbId);
            Assert.Equal(8, back.CnCnt);
        }

        [Fact]
        public void FunctionInfo_RoundTrip_24Bytes()
        {
            var orig = new TrdpFunctionInfo
            {
                FctName = "FCT", FctId = 0x0123, Grp = true, CstVehNo = 2, EtbId = 0, CnId = 3
            };
            var buf = new byte[64];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            Assert.Equal(24, w.Position);

            var r = new TrdpWireReader(buf.AsSpan(0, w.Position));
            TrdpFunctionInfo back = TrdpFunctionInfo.Read(ref r);
            Assert.Equal("FCT", back.FctName);
            Assert.Equal(0x0123, back.FctId);
            Assert.True(back.Grp);
            Assert.Equal(2, back.CstVehNo);
            Assert.Equal(3, back.CnId);
        }

        [Fact]
        public void TrainNetDirEntry_RoundTrip_20Bytes()
        {
            var orig = new TrdpTrainNetDirEntry { CstUUID = Uuid(0x40), CstNetProp = 0xDEADBEEFu };
            var buf = new byte[32];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            Assert.Equal(20, w.Position);

            var r = new TrdpWireReader(buf.AsSpan(0, w.Position));
            TrdpTrainNetDirEntry back = TrdpTrainNetDirEntry.Read(ref r);
            Assert.Equal(0xDEADBEEFu, back.CstNetProp);
            Assert.Equal(Uuid(0x40), back.CstUUID);
        }

        [Fact]
        public void OpTrainDirState_RoundTrip_48Bytes()
        {
            var orig = new TrdpOpTrainDirState
            {
                Version = new TrdpShortVersion { Ver = 1, Rel = 0 },
                EtbId = 0, TrnDirState = 2, OpTrnDirState = 2,
                TrnId = "ICE75", TrnOperator = "db.de", OpTrnTopoCnt = 0xAABBCCDDu, Crc = 0x11223344u
            };
            Assert.Equal(48, TrdpOpTrainDirState.WireSize);

            var buf = new byte[64];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            Assert.Equal(48, w.Position);

            var r = new TrdpWireReader(buf.AsSpan(0, w.Position));
            TrdpOpTrainDirState back = TrdpOpTrainDirState.Read(ref r);
            Assert.Equal("ICE75", back.TrnId);
            Assert.Equal("db.de", back.TrnOperator);
            Assert.Equal(0xAABBCCDDu, back.OpTrnTopoCnt);
            Assert.Equal(0x11223344u, back.Crc);
            Assert.Equal(2, back.OpTrnDirState);
        }

        // ── Properties (TRDP_PROP_T) ──

        [Fact]
        public void Prop_NonEmpty_RoundTrip()
        {
            var orig = new TrdpProp
            {
                Ver = new TrdpShortVersion { Ver = 1, Rel = 2 },
                Len = 4,
                Prop = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }
            };
            var buf = new byte[32];
            var w = new TrdpWireWriter(buf);
            TrdpProp.Write(ref w, orig);
            Assert.Equal(8, w.Position); // 2 (ver) + 2 (len) + 4 (data)

            var r = new TrdpWireReader(buf.AsSpan(0, w.Position));
            TrdpProp? back = TrdpProp.Read(ref r);
            Assert.NotNull(back);
            Assert.Equal(4, back!.Len);
            Assert.Equal(orig.Prop, back.Prop);
        }

        [Fact]
        public void Prop_Empty_IsNull_FourHeaderBytes()
        {
            var buf = new byte[8];
            var w = new TrdpWireWriter(buf);
            TrdpProp.Write(ref w, null);
            Assert.Equal(4, w.Position); // nur Kopf (ver+rel+len=0)

            var r = new TrdpWireReader(buf.AsSpan(0, w.Position));
            TrdpProp? back = TrdpProp.Read(ref r);
            Assert.Null(back);
        }

        // ── Variabel lange Verzeichnisse ──

        [Fact]
        public void OpTrainDir_Variable_RoundTrip()
        {
            var orig = new TrdpOpTrainDir
            {
                Version = new TrdpShortVersion { Ver = 1, Rel = 0 },
                EtbId = 0, OpTrnOrient = 1, OpCstCnt = 2, OpVehCnt = 3,
                OpTrnTopoCnt = 0x0A0B0C0Du
            };
            orig.OpCstList.Add(new TrdpOpConsist { CstUUID = Uuid(1), OpCstNo = 1, OpCstOrient = 1, TrnCstNo = 1 });
            orig.OpCstList.Add(new TrdpOpConsist { CstUUID = Uuid(2), OpCstNo = 2, OpCstOrient = 2, TrnCstNo = 2 });
            for (byte i = 1; i <= 3; i++)
                orig.OpVehList.Add(new TrdpOpVehicle { VehId = "V" + i, OpVehNo = i, OwnOpCstNo = 1, VehOrient = 1 });

            var buf = new byte[1024];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            // 8 + 2*20 + 3 + 1 + 3*24 + 4 = 128
            Assert.Equal(128, w.Position);

            TrdpOpTrainDir back = TrdpOpTrainDir.Read(buf.AsSpan(0, w.Position));
            Assert.Equal(2, back.OpCstCnt);
            Assert.Equal(3, back.OpVehCnt);
            Assert.Equal(2, back.OpCstList.Count);
            Assert.Equal(3, back.OpVehList.Count);
            Assert.Equal(0x0A0B0C0Du, back.OpTrnTopoCnt);
            Assert.Equal("V3", back.OpVehList[2].VehId);
            Assert.Equal(Uuid(2), back.OpCstList[1].CstUUID);
        }

        [Fact]
        public void TrainDir_Variable_RoundTrip()
        {
            var orig = new TrdpTrainDir
            {
                Version = new TrdpShortVersion { Ver = 1, Rel = 0 },
                EtbId = 0, CstCnt = 2, TrnTopoCnt = 0x12345678u
            };
            orig.CstList.Add(new TrdpConsist { CstUUID = Uuid(1), CstTopoCnt = 0xAAAA0001u, TrnCstNo = 1, CstOrient = 1 });
            orig.CstList.Add(new TrdpConsist { CstUUID = Uuid(2), CstTopoCnt = 0xAAAA0002u, TrnCstNo = 2, CstOrient = 2 });

            var buf = new byte[256];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            // 4 + 2*24 + 4 = 56
            Assert.Equal(56, w.Position);

            TrdpTrainDir back = TrdpTrainDir.Read(buf.AsSpan(0, w.Position));
            Assert.Equal(2, back.CstCnt);
            Assert.Equal(0x12345678u, back.TrnTopoCnt);
            Assert.Equal(0xAAAA0002u, back.CstList[1].CstTopoCnt);
        }

        [Fact]
        public void TrainNetDir_Variable_RoundTrip()
        {
            var orig = new TrdpTrainNetDir { EntryCnt = 2, EtbTopoCnt = 0xCAFEBABEu };
            orig.TrnNetDir.Add(new TrdpTrainNetDirEntry { CstUUID = Uuid(1), CstNetProp = 0x00000001u });
            orig.TrnNetDir.Add(new TrdpTrainNetDirEntry { CstUUID = Uuid(2), CstNetProp = 0x00000002u });

            var buf = new byte[256];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            // 4 + 2*20 + 4 = 48
            Assert.Equal(48, w.Position);

            TrdpTrainNetDir back = TrdpTrainNetDir.Read(buf.AsSpan(0, w.Position));
            Assert.Equal(2, back.EntryCnt);
            Assert.Equal(0xCAFEBABEu, back.EtbTopoCnt);
            Assert.Equal(0x00000002u, back.TrnNetDir[1].CstNetProp);
        }

        [Fact]
        public void StatusInfo_Pd100_RoundTrip_72Bytes()
        {
            var orig = new TrdpOpTrainDirStatusInfo
            {
                State = new TrdpOpTrainDirState
                {
                    Version = new TrdpShortVersion { Ver = 1, Rel = 0 },
                    EtbId = 0, TrnDirState = 2, OpTrnDirState = 2,
                    TrnId = "IC346", TrnOperator = "sncf.fr",
                    OpTrnTopoCnt = 0x01020304u, Crc = 0x05060708u
                },
                EtbTopoCnt = 0x0A0B0C0Du, OwnOpCstNo = 3, OwnTrnCstNo = 4, Reserved02 = 0,
                SafetyTrail = new TrdpEtbCtrlVdp
                {
                    UserDataVersion = new TrdpShortVersion { Ver = 1, Rel = 0 },
                    SafeSeqCount = 0, SafetyCode = 0
                }
            };

            var buf = new byte[128];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            Assert.Equal(72, w.Position); // 48 (state) + 4 + 1 + 1 + 2 + 16 (etbCtrlVdp)

            TrdpOpTrainDirStatusInfo back = TrdpOpTrainDirStatusInfo.Read(buf.AsSpan(0, w.Position));
            Assert.Equal("IC346", back.State.TrnId);
            Assert.Equal("sncf.fr", back.State.TrnOperator);
            Assert.Equal(0x0A0B0C0Du, back.EtbTopoCnt);
            Assert.Equal(3, back.OwnOpCstNo);
            Assert.Equal(4, back.OwnTrnCstNo);
            Assert.Equal(0x01020304u, back.State.OpTrnTopoCnt);
        }

        [Fact]
        public void ConsistInfo_Full_RoundTrip()
        {
            var orig = new TrdpConsistInfo
            {
                Version = new TrdpShortVersion { Ver = 1, Rel = 0 },
                CstClass = 1,
                CstId = "cst.db.de", CstType = "type1", CstOwner = "db.de",
                CstUUID = Uuid(0x20),
                CstProp = new TrdpProp { Ver = new TrdpShortVersion { Ver = 1, Rel = 0 }, Len = 4, Prop = new byte[] { 1, 2, 3, 4 } },
                EtbCnt = 1, VehCnt = 2, FctCnt = 1, CltrCstCnt = 1,
                CstTopoCnt = 0x99887766u
            };
            orig.EtbInfoList.Add(new TrdpEtbInfo { EtbId = 0, CnCnt = 4 });
            orig.VehInfoList.Add(new TrdpVehicleInfo
            {
                VehId = "veh1", VehType = "t", VehOrient = 1, CstVehNo = 1, TractVeh = 2,
                VehProp = new TrdpProp { Ver = new TrdpShortVersion { Ver = 1, Rel = 0 }, Len = 2, Prop = new byte[] { 0xAB, 0xCD } }
            });
            orig.VehInfoList.Add(new TrdpVehicleInfo
            {
                VehId = "veh2", VehType = "t", VehOrient = 1, CstVehNo = 2, TractVeh = 1, VehProp = null
            });
            orig.FctInfoList.Add(new TrdpFunctionInfo { FctName = "dev", FctId = 0x0010, Grp = false, CstVehNo = 1, EtbId = 0, CnId = 0 });
            orig.CltrCstInfoList.Add(new TrdpCltrCstInfo { CltrCstUUID = Uuid(0x30), CltrCstOrient = 1, CltrCstNo = 1 });

            var buf = new byte[4096];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);

            TrdpConsistInfo back = TrdpConsistInfo.Read(buf.AsSpan(0, w.Position));

            Assert.Equal("cst.db.de", back.CstId);
            Assert.Equal("db.de", back.CstOwner);
            Assert.Equal(Uuid(0x20), back.CstUUID);
            Assert.NotNull(back.CstProp);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, back.CstProp!.Prop);
            Assert.Equal(1, back.EtbInfoList.Count);
            Assert.Equal(2, back.VehInfoList.Count);
            Assert.Equal("veh1", back.VehInfoList[0].VehId);
            Assert.NotNull(back.VehInfoList[0].VehProp);
            Assert.Equal(new byte[] { 0xAB, 0xCD }, back.VehInfoList[0].VehProp!.Prop);
            Assert.Null(back.VehInfoList[1].VehProp);
            Assert.Equal(1, back.FctInfoList.Count);
            Assert.Equal("dev", back.FctInfoList[0].FctName);
            Assert.Equal(1, back.CltrCstInfoList.Count);
            Assert.Equal(0x99887766u, back.CstTopoCnt);
        }

        [Fact]
        public void ReadCompleteReply_RoundTrip()
        {
            var orig = new TrdpReadCompleteReply
            {
                State = new TrdpOpTrainDirState
                {
                    Version = new TrdpShortVersion { Ver = 1 }, TrnId = "T", TrnOperator = "O",
                    OpTrnTopoCnt = 0x11u, Crc = 0x22u
                },
                OpTrnDir = new TrdpOpTrainDir { Version = new TrdpShortVersion { Ver = 1 }, OpCstCnt = 0, OpVehCnt = 0, OpTrnTopoCnt = 0x11u },
                TrnDir = new TrdpTrainDir { Version = new TrdpShortVersion { Ver = 1 }, CstCnt = 0, TrnTopoCnt = 0x33u },
                TrnNetDir = new TrdpTrainNetDir { EntryCnt = 0, EtbTopoCnt = 0x44u }
            };

            var buf = new byte[1024];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);

            TrdpReadCompleteReply back = TrdpReadCompleteReply.Read(buf.AsSpan(0, w.Position));
            Assert.Equal(0x11u, back.State.OpTrnTopoCnt);
            Assert.Equal(0x33u, back.TrnDir.TrnTopoCnt);
            Assert.Equal(0x44u, back.TrnNetDir.EtbTopoCnt);
        }

        [Fact]
        public void Label_Padding_And_Truncation()
        {
            // Genau 16 Zeichen: kein Terminator auf dem Draht, wird vollstaendig zurueckgelesen.
            var orig = new TrdpOpVehicle { VehId = "ABCDEFGHIJKLMNOP" }; // 16 chars
            var buf = new byte[32];
            var w = new TrdpWireWriter(buf);
            orig.Write(ref w);
            var r = new TrdpWireReader(buf.AsSpan(0, w.Position));
            TrdpOpVehicle back = TrdpOpVehicle.Read(ref r);
            Assert.Equal("ABCDEFGHIJKLMNOP", back.VehId);
        }
    }
}
