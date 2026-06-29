// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Tests fuer den C#-Port von tau_ctrl.c (ECSP-Control): Encode/Decode der ECSP-Control-/
// Status-/Confirm-Datasets ueber TrdpWireReader/Writer; gepacktes Big-Endian-Wire-Format.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using Trdp.Net.Marshalling;
using Trdp.Net.Tau.Ctrl;
using Xunit;

namespace Trdp.Net.Tests
{
    public class TauCtrlTests
    {
        // ── Wire-Groessen (= sizeof der gepackten C-Strukturen) ──

        [Fact]
        public void WireSizes_MatchPackedCStructs()
        {
            Assert.Equal(16, EtbCtrlVdp.WireSize);
            Assert.Equal(24, OpVehicle.WireSize);
            Assert.Equal(40, EcspCtrl.WireSize);
            Assert.Equal(40, EcspStat.WireSize);
            Assert.Equal(40, EcspConfReply.WireSize);
            Assert.Equal(28, EcspConfRequest.HeaderSize);
            // 28 + 63*24 + 16 = 1556
            Assert.Equal(1556, EcspConfRequest.FixedWireSize);
        }

        // ── EtbCtrlVdp (safetyTrail) ──

        [Fact]
        public void EtbCtrlVdp_RoundTrips_BigEndian()
        {
            var src = new EtbCtrlVdp
            {
                Reserved01 = 0x01020304u,
                Reserved02 = 0x0506,
                UserDataVersion = new EcspShortVersion(1, 0),
                SafeSeqCount = 0x0A0B0C0Du,
                SafetyCode = 0xDEADBEEFu,
            };

            var buf = new byte[EtbCtrlVdp.WireSize];
            var w = new TrdpWireWriter(buf);
            src.Encode(ref w);
            Assert.Equal(EtbCtrlVdp.WireSize, w.Position);

            // Big-endian Pruefung der ersten 4 Byte (reserved01).
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, buf[0..4]);
            // reserved02
            Assert.Equal(new byte[] { 0x05, 0x06 }, buf[4..6]);
            // userDataVersion: ver, rel
            Assert.Equal(1, buf[6]);
            Assert.Equal(0, buf[7]);

            var r = new TrdpWireReader(buf);
            EtbCtrlVdp back = EtbCtrlVdp.Decode(ref r);
            Assert.Equal(src.Reserved01, back.Reserved01);
            Assert.Equal(src.Reserved02, back.Reserved02);
            Assert.Equal(src.UserDataVersion.Ver, back.UserDataVersion.Ver);
            Assert.Equal(src.UserDataVersion.Rel, back.UserDataVersion.Rel);
            Assert.Equal(src.SafeSeqCount, back.SafeSeqCount);
            Assert.Equal(src.SafetyCode, back.SafetyCode);
        }

        // ── ECSP-Control-Telegramm ──

        [Fact]
        public void EcspCtrl_RoundTrips_AndLayout()
        {
            var src = new EcspCtrl
            {
                Version = new EcspShortVersion(1, 0),
                Reserved01 = 0,
                LeadVehOfCst = 7,
                DeviceName = "devECSC",
                Inhibit = 1,
                LeadingReq = 1,
                LeadingDir = 2,
                SleepReq = 0,
                SafetyTrail = new EtbCtrlVdp { SafeSeqCount = 42u, SafetyCode = 0x12345678u },
            };

            byte[] buf = src.Encode();
            Assert.Equal(EcspCtrl.WireSize, buf.Length);

            // Layout: ver(0), rel(1), reserved01(2), leadVehOfCst(3), deviceName(4..19),
            //         inhibit(20), leadingReq(21), leadingDir(22), sleepReq(23), safetyTrail(24..39)
            Assert.Equal(1, buf[0]);
            Assert.Equal(0, buf[1]);
            Assert.Equal(7, buf[3]);
            Assert.Equal((byte)'d', buf[4]);
            Assert.Equal((byte)'C', buf[10]);   // "devECSC"[6]
            Assert.Equal(0, buf[11]);           // Null-Padding nach 7 Zeichen
            Assert.Equal(1, buf[20]);
            Assert.Equal(2, buf[22]);

            EcspCtrl back = EcspCtrl.Decode(buf);
            Assert.Equal(src.LeadVehOfCst, back.LeadVehOfCst);
            Assert.Equal("devECSC", back.DeviceName);
            Assert.Equal(src.Inhibit, back.Inhibit);
            Assert.Equal(src.LeadingDir, back.LeadingDir);
            Assert.Equal(42u, back.SafetyTrail.SafeSeqCount);
            Assert.Equal(0x12345678u, back.SafetyTrail.SafetyCode);
        }

        // ── ECSP-Status-Telegramm ──

        [Fact]
        public void EcspStat_RoundTrips_AndBigEndianFields()
        {
            var src = new EcspStat
            {
                Version = new EcspShortVersion(1, 0),
                Reserved01 = 0,
                Lifesign = 0x1234,
                EcspState = 1,
                EtbInhibit = 2,
                EtbLength = 1,
                EtbShort = 0,
                Reserved02 = 0,
                EtbLeadState = 9,
                EtbLeadDir = 1,
                TtdbSrvState = 1,
                DnsSrvState = 2,
                TrnDirState = 2,
                OpTrnDirState = 4,
                SleepCtrlState = 3,
                SleepReqCnt = 5,
                OpTrnTopoCnt = 0xAABBCCDDu,
                SafetyTrail = new EtbCtrlVdp(),
            };

            byte[] buf = src.Encode();
            Assert.Equal(EcspStat.WireSize, buf.Length);

            // lifesign big-endian @ Offset 4..5
            Assert.Equal(new byte[] { 0x12, 0x34 }, buf[4..6]);
            // opTrnTopoCnt big-endian @ Offset 20..23
            Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, buf[20..24]);

            EcspStat back = EcspStat.Decode(buf);
            Assert.Equal(0x1234, back.Lifesign);
            Assert.Equal(1, back.EcspState);
            Assert.Equal(9, back.EtbLeadState);
            Assert.Equal(4, back.OpTrnDirState);
            Assert.Equal(5, back.SleepReqCnt);
            Assert.Equal(0xAABBCCDDu, back.OpTrnTopoCnt);
        }

        // ── ECSP-Confirm-Reply ──

        [Fact]
        public void EcspConfReply_RoundTrips()
        {
            var src = new EcspConfReply
            {
                Version = new EcspShortVersion(1, 0),
                Status = 0,
                Reserved01 = 0,
                DeviceName = "ecsc01",
                ReqSafetyCode = 0xCAFEBABEu,
                SafetyTrail = new EtbCtrlVdp { Reserved01 = 0x11u },
            };

            byte[] buf = src.Encode();
            Assert.Equal(EcspConfReply.WireSize, buf.Length);
            // reqSafetyCode big-endian @ Offset 20..23 (2 + 1 + 1 + 16)
            Assert.Equal(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, buf[20..24]);

            EcspConfReply back = EcspConfReply.Decode(buf);
            Assert.Equal("ecsc01", back.DeviceName);
            Assert.Equal(0xCAFEBABEu, back.ReqSafetyCode);
            Assert.Equal(0x11u, back.SafetyTrail.Reserved01);
        }

        // ── OpVehicle ──

        [Fact]
        public void OpVehicle_RoundTrips()
        {
            var src = new OpVehicle
            {
                VehId = "UIC-12345",
                OpVehNo = 3,
                IsLead = 1,
                LeadDir = 2,
                TrnVehNo = 4,
                VehOrient = 1,
                OwnOpCstNo = 2,
                Reserved01 = 0,
                Reserved02 = 0,
            };

            var buf = new byte[OpVehicle.WireSize];
            var w = new TrdpWireWriter(buf);
            src.Encode(ref w);
            Assert.Equal(OpVehicle.WireSize, w.Position);

            var r = new TrdpWireReader(buf);
            OpVehicle back = OpVehicle.Decode(ref r);
            Assert.Equal("UIC-12345", back.VehId);
            Assert.Equal(3, back.OpVehNo);
            Assert.Equal(2, back.LeadDir);
            Assert.Equal(4, back.TrnVehNo);
            Assert.Equal(2, back.OwnOpCstNo);
        }

        // ── ECSP-Confirm-Request (variable Liste + #356 safetyTrail-Position) ──

        [Fact]
        public void EcspConfRequest_FixedEncode_RoundTrips_WithVehicles()
        {
            var src = new EcspConfRequest
            {
                Version = new EcspShortVersion(1, 0),
                Command = 1,
                Reserved01 = 0,
                DeviceName = "devECSC",
                OpTrnTopoCnt = 0x01020304u,
                Reserved02 = 0,
                SafetyTrail = new EtbCtrlVdp { SafeSeqCount = 0u, SafetyCode = 0x99u },
            };
            src.ConfVehList.Add(new OpVehicle { VehId = "veh1", OpVehNo = 1, TrnVehNo = 1 });
            src.ConfVehList.Add(new OpVehicle { VehId = "veh2", OpVehNo = 2, TrnVehNo = 2 });

            byte[] buf = src.Encode();
            Assert.Equal(EcspConfRequest.FixedWireSize, buf.Length);

            // confVehCnt big-endian @ Offset 26..27
            Assert.Equal(new byte[] { 0x00, 0x02 }, buf[26..28]);
            // opTrnTopoCnt big-endian @ Offset 20..23
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, buf[20..24]);

            EcspConfRequest back = EcspConfRequest.Decode(buf);
            Assert.Equal(2, back.ConfVehList.Count);
            Assert.Equal((ushort)2, back.ConfVehCnt);
            Assert.Equal("veh1", back.ConfVehList[0].VehId);
            Assert.Equal("veh2", back.ConfVehList[1].VehId);
            Assert.Equal(0x99u, back.SafetyTrail.SafetyCode);
            Assert.Equal(0x01020304u, back.OpTrnTopoCnt);
        }

        [Fact]
        public void EcspConfRequest_SafetyTrail_FollowsVehicles_Not_FixedEnd()
        {
            // #356: safetyTrail liegt direkt hinter den belegten Fahrzeugen, NICHT am Array-Ende.
            var src = new EcspConfRequest { Command = 1, DeviceName = "x" };
            src.ConfVehList.Add(new OpVehicle { VehId = "v", OpVehNo = 1 });
            src.SafetyTrail = new EtbCtrlVdp { SafetyCode = 0x44434241u }; // "ABCD" big-endian

            byte[] buf = src.Encode();

            // Position des safetyTrail = HeaderSize + 1*24 = 52; safetyCode @ +12 = 64.
            int trailOffset = EcspConfRequest.HeaderSize + OpVehicle.WireSize;
            int safetyCodeOffset = trailOffset + 12;
            Assert.Equal(new byte[] { 0x44, 0x43, 0x42, 0x41 },
                         buf[safetyCodeOffset..(safetyCodeOffset + 4)]);

            // Der Rest hinter dem safetyTrail (Padding) muss 0 sein.
            for (int i = trailOffset + EtbCtrlVdp.WireSize; i < buf.Length; i++)
            {
                Assert.Equal(0, buf[i]);
            }
        }

        [Fact]
        public void EcspConfRequest_CompactEncode_HasNoPadding()
        {
            var src = new EcspConfRequest { Command = 1 };
            src.ConfVehList.Add(new OpVehicle { VehId = "v", OpVehNo = 1 });

            byte[] compact = src.EncodeCompact();
            Assert.Equal(EcspConfRequest.HeaderSize + OpVehicle.WireSize + EtbCtrlVdp.WireSize,
                         compact.Length);
            Assert.Equal(src.CompactWireSize, compact.Length);

            EcspConfRequest back = EcspConfRequest.Decode(compact);
            Assert.Single(back.ConfVehList);
        }

        [Fact]
        public void EcspConfRequest_TooManyVehicles_Throws()
        {
            var src = new EcspConfRequest();
            for (int i = 0; i < EcspCtrlConstants.MaxVehCnt + 1; i++)
            {
                src.ConfVehList.Add(new OpVehicle());
            }
            Assert.Throws<InvalidOperationException>(() => src.Encode());
        }

        // ── Label-Behandlung ──

        [Fact]
        public void DeviceName_LongerThan16_IsTruncated()
        {
            var src = new EcspCtrl { DeviceName = "0123456789ABCDEFGHIJ" }; // 20 Zeichen
            byte[] buf = src.Encode();
            EcspCtrl back = EcspCtrl.Decode(buf);
            Assert.Equal("0123456789ABCDEF", back.DeviceName); // 16 Zeichen, kein Terminator
        }

        // ── Konstanten ──

        [Fact]
        public void Constants_MatchIec61375()
        {
            Assert.Equal(120u, EcspCtrlConstants.EcspCtrlComId);
            Assert.Equal(121u, EcspCtrlConstants.EcspStatComId);
            Assert.Equal(122u, EcspCtrlConstants.EcspConfReqComId);
            Assert.Equal(123u, EcspCtrlConstants.EcspConfRepComId);
            Assert.Equal(63, EcspCtrlConstants.MaxVehCnt);
        }
    }
}
