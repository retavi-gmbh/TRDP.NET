// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/api/tau_tti_types.h (Datentypen der
// Zugtopologie) sowie trdp/src/common/tau_tti.c (wire->host Parsing in ttiStore*/ttiCreateCstInfoEntry).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2014-2020.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.
//
// DE: Die C-Strukturen sind GNU_PACKED (gepackt, Big-Endian auf dem Draht). Wo der C-Code mit
// nativer Host-Ausrichtung/Pointern arbeitet, wird hier WERTBASIERT ueber TrdpWireReader/-Writer
// (Big-Endian) gelesen/geschrieben. Pointer-Listen werden zu C#-Listen.

using System;
using System.Collections.Generic;
using System.Text;
using Trdp.Net.Marshalling;

namespace Trdp.Net.Tau.Tti
{
    /// <summary>DE: Hilfsfunktionen fuer wiederkehrende Wire-Elemente (Labels, UUIDs, Byte-Bloecke).</summary>
    internal static class TtiWire
    {
        public static byte[] ReadBytes(ref TrdpWireReader r, int count)
        {
            var b = new byte[count];
            for (int i = 0; i < count; i++) b[i] = r.GetUInt8();
            return b;
        }

        public static void WriteBytes(ref TrdpWireWriter w, byte[] data, int count)
        {
            for (int i = 0; i < count; i++) w.PutUInt8(i < (data?.Length ?? 0) ? data![i] : (byte)0);
        }

        /// <summary>DE: Liest ein TRDP_NET_LABEL_T (16 Byte, kein Terminator) als String (an '\0' abgeschnitten).</summary>
        public static string ReadLabel(ref TrdpWireReader r)
        {
            byte[] b = ReadBytes(ref r, TauTtiConstants.NetLabelLen);
            int len = Array.IndexOf(b, (byte)0);
            if (len < 0) len = TauTtiConstants.NetLabelLen;
            return Encoding.ASCII.GetString(b, 0, len);
        }

        /// <summary>DE: Schreibt einen String als TRDP_NET_LABEL_T (16 Byte, nullgepaddet).</summary>
        public static void WriteLabel(ref TrdpWireWriter w, string? label)
        {
            byte[] raw = label != null ? Encoding.ASCII.GetBytes(label) : Array.Empty<byte>();
            for (int i = 0; i < TauTtiConstants.NetLabelLen; i++)
                w.PutUInt8(i < raw.Length ? raw[i] : (byte)0);
        }

        public static byte[] ReadUuid(ref TrdpWireReader r) => ReadBytes(ref r, TauTtiConstants.UuidLen);

        public static void WriteUuid(ref TrdpWireWriter w, byte[]? uuid) => WriteBytes(ref w, uuid ?? Array.Empty<byte>(), TauTtiConstants.UuidLen);
    }

    /// <summary>DE: Versionsinformation fuer Kommunikationspuffer (TRDP_SHORT_VERSION_T, 2 Byte).</summary>
    public struct TrdpShortVersion
    {
        public byte Ver;
        public byte Rel;

        public static TrdpShortVersion Read(ref TrdpWireReader r) => new TrdpShortVersion { Ver = r.GetUInt8(), Rel = r.GetUInt8() };
        public readonly void Write(ref TrdpWireWriter w) { w.PutUInt8(Ver); w.PutUInt8(Rel); }
    }

    /// <summary>DE: Anwendungsdefinierte Eigenschaften (TRDP_PROP_T): Version + Laenge + Rohdaten.</summary>
    public sealed class TrdpProp
    {
        public TrdpShortVersion Ver;
        /// <summary>DE: Laenge in Oktetten (0..32768). Auf dem Draht stets vorhanden.</summary>
        public ushort Len;
        /// <summary>DE: Eigenschaftsdaten (Len Byte).</summary>
        public byte[] Prop = Array.Empty<byte>();

        /// <summary>
        /// DE: Liest den 4-Byte-Kopf (Version+Laenge) und ggf. Len Datenbytes. Gibt null zurueck,
        /// wenn Len == 0 (entspricht pCstProp/pVehProp == NULL im C-Code).
        /// </summary>
        public static TrdpProp? Read(ref TrdpWireReader r)
        {
            var p = new TrdpProp { Ver = TrdpShortVersion.Read(ref r), Len = r.GetUInt16() };
            if (p.Len > TauTtiConstants.MaxPropLen)
                throw new InvalidOperationException("TRDP_PROP_T: len ausserhalb des gueltigen Bereichs.");
            if (p.Len == 0) return null;
            p.Prop = TtiWire.ReadBytes(ref r, p.Len);
            return p;
        }

        /// <summary>DE: Schreibt einen ggf. null-Prop. null => 4 Nullbytes (Version 0, Laenge 0).</summary>
        public static void Write(ref TrdpWireWriter w, TrdpProp? p)
        {
            if (p == null) { w.PutUInt8(0); w.PutUInt8(0); w.PutUInt16(0); return; }
            p.Ver.Write(ref w);
            w.PutUInt16(p.Len);
            TtiWire.WriteBytes(ref w, p.Prop, p.Len);
        }
    }

    /// <summary>DE: ETB-Information (TRDP_ETB_INFO_T, 4 Byte).</summary>
    public sealed class TrdpEtbInfo
    {
        public byte EtbId;
        public byte CnCnt;
        public ushort Reserved01;

        public static TrdpEtbInfo Read(ref TrdpWireReader r) =>
            new TrdpEtbInfo { EtbId = r.GetUInt8(), CnCnt = r.GetUInt8(), Reserved01 = r.GetUInt16() };

        public void Write(ref TrdpWireWriter w) { w.PutUInt8(EtbId); w.PutUInt8(CnCnt); w.PutUInt16(Reserved01); }
    }

    /// <summary>DE: Information zu Wagen eines geschlossenen Zuges (TRDP_CLTR_CST_INFO_T, 20 Byte).</summary>
    public sealed class TrdpCltrCstInfo
    {
        public byte[] CltrCstUUID = new byte[TauTtiConstants.UuidLen];
        public byte CltrCstOrient;
        public byte CltrCstNo;
        public ushort Reserved01;

        public static TrdpCltrCstInfo Read(ref TrdpWireReader r) => new TrdpCltrCstInfo
        {
            CltrCstUUID = TtiWire.ReadUuid(ref r),
            CltrCstOrient = r.GetUInt8(),
            CltrCstNo = r.GetUInt8(),
            Reserved01 = r.GetUInt16()
        };

        public void Write(ref TrdpWireWriter w)
        {
            TtiWire.WriteUuid(ref w, CltrCstUUID);
            w.PutUInt8(CltrCstOrient);
            w.PutUInt8(CltrCstNo);
            w.PutUInt16(Reserved01);
        }
    }

    /// <summary>DE: Funktions-/Geraeteinformation (TRDP_FUNCTION_INFO_T, 24 Byte auf dem Draht).</summary>
    public sealed class TrdpFunctionInfo
    {
        public string FctName = string.Empty;
        public ushort FctId;
        public bool Grp;
        public byte Reserved01;
        public byte CstVehNo;
        public byte EtbId;
        public byte CnId;
        public byte Reserved02;

        public static TrdpFunctionInfo Read(ref TrdpWireReader r) => new TrdpFunctionInfo
        {
            FctName = TtiWire.ReadLabel(ref r),
            FctId = r.GetUInt16(),
            Grp = r.GetUInt8() != 0,
            Reserved01 = r.GetUInt8(),
            CstVehNo = r.GetUInt8(),
            EtbId = r.GetUInt8(),
            CnId = r.GetUInt8(),
            Reserved02 = r.GetUInt8()
        };

        public void Write(ref TrdpWireWriter w)
        {
            TtiWire.WriteLabel(ref w, FctName);
            w.PutUInt16(FctId);
            w.PutUInt8(Grp ? (byte)1 : (byte)0);
            w.PutUInt8(Reserved01);
            w.PutUInt8(CstVehNo);
            w.PutUInt8(EtbId);
            w.PutUInt8(CnId);
            w.PutUInt8(Reserved02);
        }
    }

    /// <summary>DE: Fahrzeuginformation (TRDP_VEHICLE_INFO_T). Enthaelt optionale statische Eigenschaften.</summary>
    public sealed class TrdpVehicleInfo
    {
        public string VehId = string.Empty;
        public string VehType = string.Empty;
        public byte VehOrient;
        public byte CstVehNo;
        public byte TractVeh;   // ANTIVALENT8
        public byte Reserved01;
        public TrdpProp? VehProp;

        public static TrdpVehicleInfo Read(ref TrdpWireReader r)
        {
            var v = new TrdpVehicleInfo
            {
                VehId = TtiWire.ReadLabel(ref r),
                VehType = TtiWire.ReadLabel(ref r),
                VehOrient = r.GetUInt8(),
                CstVehNo = r.GetUInt8(),
                TractVeh = r.GetUInt8(),
                Reserved01 = r.GetUInt8()
            };
            v.VehProp = TrdpProp.Read(ref r);
            return v;
        }

        public void Write(ref TrdpWireWriter w)
        {
            TtiWire.WriteLabel(ref w, VehId);
            TtiWire.WriteLabel(ref w, VehType);
            w.PutUInt8(VehOrient);
            w.PutUInt8(CstVehNo);
            w.PutUInt8(TractVeh);
            w.PutUInt8(Reserved01);
            TrdpProp.Write(ref w, VehProp);
        }
    }

    /// <summary>
    /// DE: Statische Consist-Information (TRDP_CONSIST_INFO_T). Variabel langes Telegramm
    /// (CSTINFO, ComID 105). Parsing entspricht ttiCreateCstInfoEntry().
    /// </summary>
    public sealed class TrdpConsistInfo
    {
        public TrdpShortVersion Version;
        public byte CstClass;
        public byte Reserved01;
        public string CstId = string.Empty;
        public string CstType = string.Empty;
        public string CstOwner = string.Empty;
        public byte[] CstUUID = new byte[TauTtiConstants.UuidLen];
        public uint Reserved02;
        public TrdpProp? CstProp;
        public ushort Reserved03;
        public ushort EtbCnt;
        public List<TrdpEtbInfo> EtbInfoList = new();
        public ushort Reserved04;
        public ushort VehCnt;
        public List<TrdpVehicleInfo> VehInfoList = new();
        public ushort Reserved05;
        public ushort FctCnt;
        public List<TrdpFunctionInfo> FctInfoList = new();
        public ushort Reserved06;
        public ushort CltrCstCnt;
        public List<TrdpCltrCstInfo> CltrCstInfoList = new();
        public uint CstTopoCnt;

        public static TrdpConsistInfo Read(ReadOnlySpan<byte> data)
        {
            var r = new TrdpWireReader(data);
            return Read(ref r);
        }

        public static TrdpConsistInfo Read(ref TrdpWireReader r)
        {
            var c = new TrdpConsistInfo
            {
                Version = TrdpShortVersion.Read(ref r),
                CstClass = r.GetUInt8(),
                Reserved01 = r.GetUInt8(),
                CstId = TtiWire.ReadLabel(ref r),
                CstType = TtiWire.ReadLabel(ref r),
                CstOwner = TtiWire.ReadLabel(ref r),
                CstUUID = TtiWire.ReadUuid(ref r),
                Reserved02 = r.GetUInt32()
            };
            c.CstProp = TrdpProp.Read(ref r);
            c.Reserved03 = r.GetUInt16();

            c.EtbCnt = r.GetUInt16();
            for (int i = 0; i < c.EtbCnt; i++) c.EtbInfoList.Add(TrdpEtbInfo.Read(ref r));

            c.Reserved04 = r.GetUInt16();
            c.VehCnt = r.GetUInt16();
            for (int i = 0; i < c.VehCnt; i++) c.VehInfoList.Add(TrdpVehicleInfo.Read(ref r));

            c.Reserved05 = r.GetUInt16();
            c.FctCnt = r.GetUInt16();
            for (int i = 0; i < c.FctCnt; i++) c.FctInfoList.Add(TrdpFunctionInfo.Read(ref r));

            c.Reserved06 = r.GetUInt16();
            c.CltrCstCnt = r.GetUInt16();
            for (int i = 0; i < c.CltrCstCnt; i++) c.CltrCstInfoList.Add(TrdpCltrCstInfo.Read(ref r));

            c.CstTopoCnt = r.GetUInt32();
            return c;
        }

        public void Write(ref TrdpWireWriter w)
        {
            Version.Write(ref w);
            w.PutUInt8(CstClass);
            w.PutUInt8(Reserved01);
            TtiWire.WriteLabel(ref w, CstId);
            TtiWire.WriteLabel(ref w, CstType);
            TtiWire.WriteLabel(ref w, CstOwner);
            TtiWire.WriteUuid(ref w, CstUUID);
            w.PutUInt32(Reserved02);
            TrdpProp.Write(ref w, CstProp);
            w.PutUInt16(Reserved03);

            w.PutUInt16(EtbCnt);
            foreach (TrdpEtbInfo e in EtbInfoList) e.Write(ref w);

            w.PutUInt16(Reserved04);
            w.PutUInt16(VehCnt);
            foreach (TrdpVehicleInfo v in VehInfoList) v.Write(ref w);

            w.PutUInt16(Reserved05);
            w.PutUInt16(FctCnt);
            foreach (TrdpFunctionInfo f in FctInfoList) f.Write(ref w);

            w.PutUInt16(Reserved06);
            w.PutUInt16(CltrCstCnt);
            foreach (TrdpCltrCstInfo cl in CltrCstInfoList) cl.Write(ref w);

            w.PutUInt32(CstTopoCnt);
        }
    }

    /// <summary>DE: TCN-Consist-Struktur (TRDP_CONSIST_T, 24 Byte, GNU_PACKED).</summary>
    public sealed class TrdpConsist
    {
        public byte[] CstUUID = new byte[TauTtiConstants.UuidLen];
        public uint CstTopoCnt;
        public byte TrnCstNo;
        public byte CstOrient;
        public ushort Reserved01;

        public static TrdpConsist Read(ref TrdpWireReader r) => new TrdpConsist
        {
            CstUUID = TtiWire.ReadUuid(ref r),
            CstTopoCnt = r.GetUInt32(),
            TrnCstNo = r.GetUInt8(),
            CstOrient = r.GetUInt8(),
            Reserved01 = r.GetUInt16()
        };

        public void Write(ref TrdpWireWriter w)
        {
            TtiWire.WriteUuid(ref w, CstUUID);
            w.PutUInt32(CstTopoCnt);
            w.PutUInt8(TrnCstNo);
            w.PutUInt8(CstOrient);
            w.PutUInt16(Reserved01);
        }
    }

    /// <summary>DE: TCN Train Directory (TRDP_TRAIN_DIR_T). Variabel: nur cstCnt Eintraege auf dem Draht.</summary>
    public sealed class TrdpTrainDir
    {
        public TrdpShortVersion Version;
        public byte EtbId;
        public byte CstCnt;
        public List<TrdpConsist> CstList = new();
        public uint TrnTopoCnt;

        public static TrdpTrainDir Read(ReadOnlySpan<byte> data)
        {
            var r = new TrdpWireReader(data);
            return Read(ref r);
        }

        public static TrdpTrainDir Read(ref TrdpWireReader r)
        {
            var t = new TrdpTrainDir
            {
                Version = TrdpShortVersion.Read(ref r),
                EtbId = r.GetUInt8(),
                CstCnt = r.GetUInt8()
            };
            if (t.CstCnt > TauTtiConstants.MaxCstCnt)
                throw new InvalidOperationException("TRDP_TRAIN_DIR_T: cstCnt > TRDP_MAX_CST_CNT.");
            for (int i = 0; i < t.CstCnt; i++) t.CstList.Add(TrdpConsist.Read(ref r));
            t.TrnTopoCnt = r.GetUInt32();
            return t;
        }

        public void Write(ref TrdpWireWriter w)
        {
            Version.Write(ref w);
            w.PutUInt8(EtbId);
            w.PutUInt8(CstCnt);
            foreach (TrdpConsist c in CstList) c.Write(ref w);
            w.PutUInt32(TrnTopoCnt);
        }
    }

    /// <summary>DE: Operationelle Fahrzeugstruktur (TRDP_OP_VEHICLE_T, 24 Byte, GNU_PACKED).</summary>
    public sealed class TrdpOpVehicle
    {
        public string VehId = string.Empty;
        public byte OpVehNo;
        public byte IsLead;     // ANTIVALENT8
        public byte LeadDir;
        public byte TrnVehNo;
        public byte VehOrient;
        public byte OwnOpCstNo;
        public byte Reserved01;
        public byte Reserved02;

        public static TrdpOpVehicle Read(ref TrdpWireReader r) => new TrdpOpVehicle
        {
            VehId = TtiWire.ReadLabel(ref r),
            OpVehNo = r.GetUInt8(),
            IsLead = r.GetUInt8(),
            LeadDir = r.GetUInt8(),
            TrnVehNo = r.GetUInt8(),
            VehOrient = r.GetUInt8(),
            OwnOpCstNo = r.GetUInt8(),
            Reserved01 = r.GetUInt8(),
            Reserved02 = r.GetUInt8()
        };

        public void Write(ref TrdpWireWriter w)
        {
            TtiWire.WriteLabel(ref w, VehId);
            w.PutUInt8(OpVehNo);
            w.PutUInt8(IsLead);
            w.PutUInt8(LeadDir);
            w.PutUInt8(TrnVehNo);
            w.PutUInt8(VehOrient);
            w.PutUInt8(OwnOpCstNo);
            w.PutUInt8(Reserved01);
            w.PutUInt8(Reserved02);
        }
    }

    /// <summary>DE: Operationelle Consist-Struktur (TRDP_OP_CONSIST_T, 20 Byte, GNU_PACKED).</summary>
    public sealed class TrdpOpConsist
    {
        public byte[] CstUUID = new byte[TauTtiConstants.UuidLen];
        public byte OpCstNo;
        public byte OpCstOrient;
        public byte TrnCstNo;
        public byte Reserved01;

        public static TrdpOpConsist Read(ref TrdpWireReader r) => new TrdpOpConsist
        {
            CstUUID = TtiWire.ReadUuid(ref r),
            OpCstNo = r.GetUInt8(),
            OpCstOrient = r.GetUInt8(),
            TrnCstNo = r.GetUInt8(),
            Reserved01 = r.GetUInt8()
        };

        public void Write(ref TrdpWireWriter w)
        {
            TtiWire.WriteUuid(ref w, CstUUID);
            w.PutUInt8(OpCstNo);
            w.PutUInt8(OpCstOrient);
            w.PutUInt8(TrnCstNo);
            w.PutUInt8(Reserved01);
        }
    }

    /// <summary>
    /// DE: Operationeller Train-Directory-Zustand (TRDP_OP_TRAIN_DIR_STATE_T, 48 Byte, GNU_PACKED).
    /// Letzte 4 Byte sind die SC-32-Pruefsumme ueber die ersten 44 Byte.
    /// </summary>
    public sealed class TrdpOpTrainDirState
    {
        public TrdpShortVersion Version;
        public byte Reserved01;
        public byte Reserved02;
        public byte EtbId;
        public byte TrnDirState;
        public byte OpTrnDirState;
        public byte Reserved03;
        public string TrnId = string.Empty;
        public string TrnOperator = string.Empty;
        public uint OpTrnTopoCnt;
        public uint Crc;

        public const int WireSize = 48;

        public static TrdpOpTrainDirState Read(ref TrdpWireReader r) => new TrdpOpTrainDirState
        {
            Version = TrdpShortVersion.Read(ref r),
            Reserved01 = r.GetUInt8(),
            Reserved02 = r.GetUInt8(),
            EtbId = r.GetUInt8(),
            TrnDirState = r.GetUInt8(),
            OpTrnDirState = r.GetUInt8(),
            Reserved03 = r.GetUInt8(),
            TrnId = TtiWire.ReadLabel(ref r),
            TrnOperator = TtiWire.ReadLabel(ref r),
            OpTrnTopoCnt = r.GetUInt32(),
            Crc = r.GetUInt32()
        };

        public void Write(ref TrdpWireWriter w)
        {
            Version.Write(ref w);
            w.PutUInt8(Reserved01);
            w.PutUInt8(Reserved02);
            w.PutUInt8(EtbId);
            w.PutUInt8(TrnDirState);
            w.PutUInt8(OpTrnDirState);
            w.PutUInt8(Reserved03);
            TtiWire.WriteLabel(ref w, TrnId);
            TtiWire.WriteLabel(ref w, TrnOperator);
            w.PutUInt32(OpTrnTopoCnt);
            w.PutUInt32(Crc);
        }
    }

    /// <summary>DE: ETBCTRL-VDP-Sicherheitsanhang (TRDP_ETB_CTRL_VDP_T, 16 Byte, GNU_PACKED).</summary>
    public sealed class TrdpEtbCtrlVdp
    {
        public uint Reserved01;
        public ushort Reserved02;
        public TrdpShortVersion UserDataVersion;
        public uint SafeSeqCount;
        public uint SafetyCode;

        public const int WireSize = 16;

        public static TrdpEtbCtrlVdp Read(ref TrdpWireReader r) => new TrdpEtbCtrlVdp
        {
            Reserved01 = r.GetUInt32(),
            Reserved02 = r.GetUInt16(),
            UserDataVersion = TrdpShortVersion.Read(ref r),
            SafeSeqCount = r.GetUInt32(),
            SafetyCode = r.GetUInt32()
        };

        public void Write(ref TrdpWireWriter w)
        {
            w.PutUInt32(Reserved01);
            w.PutUInt16(Reserved02);
            UserDataVersion.Write(ref w);
            w.PutUInt32(SafeSeqCount);
            w.PutUInt32(SafetyCode);
        }
    }

    /// <summary>
    /// DE: Operationeller Train-Directory-Statusinfo (TRDP_OP_TRAIN_DIR_STATUS_INFO_T, 72 Byte) =
    /// PD-100-Telegramm. ACHTUNG: Werte hier in Host-Darstellung (Big-Endian gelesen).
    /// </summary>
    public sealed class TrdpOpTrainDirStatusInfo
    {
        public TrdpOpTrainDirState State = new();
        public uint EtbTopoCnt;
        public byte OwnOpCstNo;
        public byte OwnTrnCstNo;
        public ushort Reserved02;
        public TrdpEtbCtrlVdp SafetyTrail = new();

        public static TrdpOpTrainDirStatusInfo Read(ReadOnlySpan<byte> data)
        {
            var r = new TrdpWireReader(data);
            return Read(ref r);
        }

        public static TrdpOpTrainDirStatusInfo Read(ref TrdpWireReader r) => new TrdpOpTrainDirStatusInfo
        {
            State = TrdpOpTrainDirState.Read(ref r),
            EtbTopoCnt = r.GetUInt32(),
            OwnOpCstNo = r.GetUInt8(),
            OwnTrnCstNo = r.GetUInt8(),
            Reserved02 = r.GetUInt16(),
            SafetyTrail = TrdpEtbCtrlVdp.Read(ref r)
        };

        public void Write(ref TrdpWireWriter w)
        {
            State.Write(ref w);
            w.PutUInt32(EtbTopoCnt);
            w.PutUInt8(OwnOpCstNo);
            w.PutUInt8(OwnTrnCstNo);
            w.PutUInt16(Reserved02);
            SafetyTrail.Write(ref w);
        }
    }

    /// <summary>DE: Operationelles Train Directory (TRDP_OP_TRAIN_DIR_T). Variabel; Parsing = ttiStoreOpTrnDir.</summary>
    public sealed class TrdpOpTrainDir
    {
        public TrdpShortVersion Version;
        public byte EtbId;
        public byte OpTrnOrient;
        public byte Reserved01;
        public byte Reserved02;
        public byte Reserved03;
        public byte OpCstCnt;
        public List<TrdpOpConsist> OpCstList = new();
        public byte Reserved04;
        public byte Reserved05;
        public byte Reserved06;
        public byte OpVehCnt;
        public List<TrdpOpVehicle> OpVehList = new();
        public uint OpTrnTopoCnt;

        public static TrdpOpTrainDir Read(ReadOnlySpan<byte> data)
        {
            var r = new TrdpWireReader(data);
            return Read(ref r);
        }

        public static TrdpOpTrainDir Read(ref TrdpWireReader r)
        {
            var t = new TrdpOpTrainDir
            {
                Version = TrdpShortVersion.Read(ref r),
                EtbId = r.GetUInt8(),
                OpTrnOrient = r.GetUInt8(),
                Reserved01 = r.GetUInt8(),
                Reserved02 = r.GetUInt8(),
                Reserved03 = r.GetUInt8(),
                OpCstCnt = r.GetUInt8()
            };
            if (t.OpCstCnt > TauTtiConstants.MaxCstCnt)
                throw new InvalidOperationException("TRDP_OP_TRAIN_DIR_T: opCstCnt > TRDP_MAX_CST_CNT.");
            for (int i = 0; i < t.OpCstCnt; i++) t.OpCstList.Add(TrdpOpConsist.Read(ref r));

            t.Reserved04 = r.GetUInt8();
            t.Reserved05 = r.GetUInt8();
            t.Reserved06 = r.GetUInt8();
            t.OpVehCnt = r.GetUInt8();
            for (int i = 0; i < t.OpVehCnt; i++) t.OpVehList.Add(TrdpOpVehicle.Read(ref r));

            t.OpTrnTopoCnt = r.GetUInt32();
            return t;
        }

        public void Write(ref TrdpWireWriter w)
        {
            Version.Write(ref w);
            w.PutUInt8(EtbId);
            w.PutUInt8(OpTrnOrient);
            w.PutUInt8(Reserved01);
            w.PutUInt8(Reserved02);
            w.PutUInt8(Reserved03);
            w.PutUInt8(OpCstCnt);
            foreach (TrdpOpConsist c in OpCstList) c.Write(ref w);
            w.PutUInt8(Reserved04);
            w.PutUInt8(Reserved05);
            w.PutUInt8(Reserved06);
            w.PutUInt8(OpVehCnt);
            foreach (TrdpOpVehicle v in OpVehList) v.Write(ref w);
            w.PutUInt32(OpTrnTopoCnt);
        }
    }

    /// <summary>DE: Eintrag des Train Network Directory (TRDP_TRAIN_NET_DIR_ENTRY_T, 20 Byte, GNU_PACKED).</summary>
    public sealed class TrdpTrainNetDirEntry
    {
        public byte[] CstUUID = new byte[TauTtiConstants.UuidLen];
        /// <summary>DE: Consist-Netzeigenschaften (Bitfeld, siehe IEC 61375-2-5).</summary>
        public uint CstNetProp;

        public static TrdpTrainNetDirEntry Read(ref TrdpWireReader r) => new TrdpTrainNetDirEntry
        {
            CstUUID = TtiWire.ReadUuid(ref r),
            CstNetProp = r.GetUInt32()
        };

        public void Write(ref TrdpWireWriter w)
        {
            TtiWire.WriteUuid(ref w, CstUUID);
            w.PutUInt32(CstNetProp);
        }
    }

    /// <summary>DE: Train Network Directory (TRDP_TRAIN_NET_DIR_T). Variabel; Parsing = ttiStoreTrnNetDir.</summary>
    public sealed class TrdpTrainNetDir
    {
        public ushort Reserved01;
        public ushort EntryCnt;
        public List<TrdpTrainNetDirEntry> TrnNetDir = new();
        public uint EtbTopoCnt;

        public static TrdpTrainNetDir Read(ReadOnlySpan<byte> data)
        {
            var r = new TrdpWireReader(data);
            return Read(ref r);
        }

        public static TrdpTrainNetDir Read(ref TrdpWireReader r)
        {
            var t = new TrdpTrainNetDir
            {
                Reserved01 = r.GetUInt16(),
                EntryCnt = r.GetUInt16()
            };
            if (t.EntryCnt > TauTtiConstants.MaxCstCnt)
                throw new InvalidOperationException("TRDP_TRAIN_NET_DIR_T: entryCnt > TRDP_MAX_CST_CNT.");
            for (int i = 0; i < t.EntryCnt; i++) t.TrnNetDir.Add(TrdpTrainNetDirEntry.Read(ref r));
            t.EtbTopoCnt = r.GetUInt32();
            return t;
        }

        public void Write(ref TrdpWireWriter w)
        {
            w.PutUInt16(Reserved01);
            w.PutUInt16(EntryCnt);
            foreach (TrdpTrainNetDirEntry e in TrnNetDir) e.Write(ref w);
            w.PutUInt32(EtbTopoCnt);
        }
    }

    /// <summary>DE: Komplette TTDB-Antwort (TRDP_READ_COMPLETE_REPLY_T, ComID 111).</summary>
    public sealed class TrdpReadCompleteReply
    {
        public TrdpOpTrainDirState State = new();
        public TrdpOpTrainDir OpTrnDir = new();
        public TrdpTrainDir TrnDir = new();
        public TrdpTrainNetDir TrnNetDir = new();

        public static TrdpReadCompleteReply Read(ReadOnlySpan<byte> data)
        {
            var r = new TrdpWireReader(data);
            return new TrdpReadCompleteReply
            {
                State = TrdpOpTrainDirState.Read(ref r),
                OpTrnDir = TrdpOpTrainDir.Read(ref r),
                TrnDir = TrdpTrainDir.Read(ref r),
                TrnNetDir = TrdpTrainNetDir.Read(ref r)
            };
        }

        public void Write(ref TrdpWireWriter w)
        {
            State.Write(ref w);
            OpTrnDir.Write(ref w);
            TrnDir.Write(ref w);
            TrnNetDir.Write(ref w);
        }
    }
}
