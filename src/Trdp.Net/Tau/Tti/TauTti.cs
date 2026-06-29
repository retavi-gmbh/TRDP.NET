// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tau_tti.c (TTI-Subsystem: TTDB/ECSP-
// Consumer, PD-100-Abo, MD-Requests/Listener, lokaler Datenspeicher und tau_*-Zugriffsfunktionen).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2016-2020.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.
//
// DE: Anpassungen ggue. dem C-Stack:
//  - appHandle->pTTDB wird zur Instanz dieser Klasse (haelt Abos, Listener, Verzeichnisse, Cache).
//  - tlp_subscribe/tlm_addListener/tlm_request laufen ueber TrdpSession (.Pd / .Md).
//  - "Manuelles Unmarshalling" (vos_ntohl nach memcpy) entfaellt: TrdpWireReader liest Big-Endian
//    direkt in Host-Werte (genau das wertbasierte Vorgehen).
//  - Die SC-32-Pruefsumme (vos_sc32) ist im Port noch nicht verfuegbar -> CRC-Pruefung der PD-100-
//    und CSTINFO-Telegramme ist mit TODO markiert und wird (noch) nicht erzwungen.

using System;
using System.Collections.Generic;
using System.Net;
using Trdp.Net.Core;
using Trdp.Net.Md;
using Trdp.Net.Pd;
using Trdp.Net.Vos;

namespace Trdp.Net.Tau.Tti
{
    /// <summary>
    /// DE: Zugtopologie-Zugriff (Train Topology Information). Konsumiert die TTDB-/ECSP-Telegramme
    /// nach IEC 61375-2-3 und stellt die tau_*-Abfragefunktionen bereit. Wegen des asynchronen
    /// Verhaltens liefern viele Methoden beim ersten Aufruf <see cref="TrdpError.NoDataErr"/>; nach
    /// 1..3 s erneut aufrufen (3 s ist der Standard-MD-Reply-Timeout).
    /// Aus EINEM Thread/ExecutionLoop nutzen (wie die TrdpSession).
    /// </summary>
    public sealed class TauTti : IDisposable
    {
        private readonly TrdpSession _session;

        // DE: ECSP-Zieladresse fuer MD-Requests (beim Init uebergeben). Wer mit URIs statt IPs
        // arbeitet, loest sie via TauDnr.IpFromUri auf und uebergibt die fertige IP an InitTtiAccess.
        private IPAddress _ecspIp = IPAddress.Loopback;

        // ── Abos / Listener (Entsprechung der Handles in TAU_TTDB_T) ──
        private PdSubscriber? _pd100Sub1;
        private PdSubscriber? _pd100Sub2;
        private MdListener? _md101Listener1;

        // ── Lokaler Datenspeicher (TAU_TTDB_T) ──
        private readonly TrdpOpTrainDirStatusInfo _opTrnState = new();
        private TrdpOpTrainDir _opTrnDir = new();
        private TrdpTrainDir _trnDir = new();
        private TrdpTrainNetDir _trnNetDir = new();
        private readonly List<TrdpConsistInfo> _cstInfo = new(); // Cache, max. TRDP_MAX_CST_CNT

        private int _savedOwnCstIndex = -1;
        private bool _initialised;

        /// <summary>DE: Wird ausgeloest, wenn eine Inauguration (Topo-Zaehler-Aenderung) erkannt wurde.</summary>
        public event Action? InaugurationDetected;

        public TauTti(TrdpSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>DE: Letzter empfangener PD-100-Statusinfo (Host-Darstellung).</summary>
        public TrdpOpTrainDirStatusInfo OpTrnStatusInfo => _opTrnState;

        // =====================================================================
        //  Init / DeInit
        // =====================================================================

        /// <summary>
        /// DE: Initialisiert den TTI-Zugriff (tau_initTTIaccess). Abonniert PD 100 auf beiden
        /// Multicast-Adressen und richtet den MD-Listener fuer ComID 101 (OP_DIR_INFO-Notification) ein.
        /// </summary>
        /// <param name="ecspIpAddr">ECSP-IP fuer MD-Requests (Ersatz fuer URI/DNR-Aufloesung).</param>
        public TrdpError InitTtiAccess(IPAddress ecspIpAddr)
        {
            if (_initialised)
            {
                return TrdpError.InitErr;
            }
            _ecspIp = ecspIpAddr ?? throw new ArgumentNullException(nameof(ecspIpAddr));

            // PD 100 auf beiden MC-Zielen abonnieren (vgl. tlp_subscribe in tau_initTTIaccess).
            _session.Pd.JoinMulticast(TauTtiConstants.TtdbStatusDestIp);
            _session.Pd.JoinMulticast(TauTtiConstants.TtdbStatusDestIpEtb0);
            _pd100Sub1 = _session.Pd.Subscribe(TauTtiConstants.TtdbStatusComId,
                                               TauTtiConstants.TtdbStatusTimeoutUs / 1000);
            _pd100Sub2 = _session.Pd.Subscribe(TauTtiConstants.TtdbStatusComId,
                                               TauTtiConstants.TtdbStatusTimeoutUs / 1000);
            _pd100Sub1.DataReceived += sub => PdCallback(sub);
            _pd100Sub2.DataReceived += sub => PdCallback(sub);

            // MD 101 (Push der OP_TRAIN_DIRECTORY als Notification).
            if (_session.Md == null)
            {
                return TrdpError.InitErr;
            }
            _md101Listener1 = _session.Md.AddListener(TauTtiConstants.TtdbOpDirInfoComId);
            _md101Listener1.Received += ctx => MdHandleData(ctx.Message.ComId, ctx.Message.Data);

            _initialised = true;
            return TrdpError.NoErr;
        }

        /// <summary>DE: Gibt die TTI-Ressourcen frei (tau_deInitTTI): meldet PD-Subscriber/MD-Listener
        /// ab (tlp_unsubscribe/tlm_delListener) und loest die Referenzen.</summary>
        public void DeInitTti()
        {
            if (_pd100Sub1 != null) _session.Pd.Unsubscribe(_pd100Sub1);
            if (_pd100Sub2 != null) _session.Pd.Unsubscribe(_pd100Sub2);
            if (_md101Listener1 != null) _session.Md?.RemoveListener(_md101Listener1);
            _cstInfo.Clear();
            _pd100Sub1 = null;
            _pd100Sub2 = null;
            _md101Listener1 = null;
            _initialised = false;
        }

        public void Dispose() => DeInitTti();

        // =====================================================================
        //  Empfangs-Callbacks
        // =====================================================================

        /// <summary>DE: PD-Empfang (ttiPDCallback): wertet den PD-100-Statusinfo aus.</summary>
        private void PdCallback(PdSubscriber sub)
        {
            if (!sub.TryGetData(out byte[] data)) return;
            if (sub.ComId != TauTtiConstants.TtdbStatusComId) return;

            TrdpOpTrainDirStatusInfo telegram;
            try
            {
                telegram = TrdpOpTrainDirStatusInfo.Read(data);
            }
            catch (Exception)
            {
                return; // malformed
            }

            // TODO: SC-32-Pruefung (vos_sc32 ueber state, len-4) ist nicht portiert -> wird nicht erzwungen.

            bool changed = false;

            // Statusinfo lokal uebernehmen.
            _opTrnState.State = telegram.State;
            _opTrnState.EtbTopoCnt = telegram.EtbTopoCnt;
            _opTrnState.OwnOpCstNo = telegram.OwnOpCstNo;
            _opTrnState.OwnTrnCstNo = telegram.OwnTrnCstNo;
            _opTrnState.Reserved02 = telegram.Reserved02;
            _opTrnState.SafetyTrail = telegram.SafetyTrail;

            // etbTopoCnt geaendert? -> alle abgeleiteten Verzeichnisse/Caches invalidieren.
            if (_session.EtbTopoCount != _opTrnState.EtbTopoCnt)
            {
                changed = true;
                _session.EtbTopoCount = _opTrnState.EtbTopoCnt;
                _trnDir = new TrdpTrainDir();      // cstCnt = 0
                _trnNetDir = new TrdpTrainNetDir(); // entryCnt = 0
                _cstInfo.Clear();
                _savedOwnCstIndex = -1;
            }

            // opTrnTopoCnt geaendert? -> OP-Directory invalidieren.
            if (_session.OpTrainTopoCount != _opTrnState.State.OpTrnTopoCnt)
            {
                changed = true;
                _session.OpTrainTopoCount = _opTrnState.State.OpTrnTopoCnt;
                _opTrnDir = new TrdpOpTrainDir(); // opCstCnt = 0
            }

            if (changed)
            {
                InaugurationDetected?.Invoke();
            }
        }

        /// <summary>DE: MD-Empfang (ttiMDCallback): Reply/Notification je ComID in den Speicher uebernehmen.</summary>
        private void MdHandleData(uint comId, byte[] data)
        {
            try
            {
                if (comId == TauTtiConstants.TtdbOpDirInfoComId ||      // 101 Notification
                    comId == TauTtiConstants.TtdbOpDirInfoRepComId)     // 109 Reply
                {
                    StoreOpTrnDir(data);
                }
                else if (comId == TauTtiConstants.TtdbTrnDirRepComId)   // 103
                {
                    StoreTrnDir(data);
                }
                else if (comId == TauTtiConstants.TtdbNetDirRepComId)   // 107
                {
                    StoreTrnNetDir(data);
                }
                else if (comId == TauTtiConstants.TtdbReadCmpltRepComId) // 111
                {
                    // TODO: READ_COMPLETE_REPLY wird sequentiell (variabel) geparst; der C-Code nutzt
                    // hier feste Sub-Struct-Offsets. Pruefung gegen reale ECSP-Telegramme ausstehend.
                    var reply = TrdpReadCompleteReply.Read(data);
                    _opTrnState.State = reply.State;
                    _session.OpTrainTopoCount = reply.State.OpTrnTopoCnt;
                    _opTrnDir = reply.OpTrnDir;
                    _trnDir = reply.TrnDir;
                    _trnNetDir = reply.TrnNetDir;
                }
                else if (comId == TauTtiConstants.TtdbStatCstRepComId)  // 105
                {
                    // TODO: SC-32-Pruefung der cstTopoCnt nicht portiert.
                    StoreCstInfo(data);
                }
            }
            catch (Exception)
            {
                // Defekte Telegramme verwerfen (vgl. TRDP_PACKET_ERR-Pfade im C).
            }
        }

        private bool StoreOpTrnDir(byte[] data)
        {
            TrdpOpTrainDir dir = TrdpOpTrainDir.Read(data);
            _opTrnDir = dir;
            bool changed = _session.OpTrainTopoCount != dir.OpTrnTopoCnt;
            _session.OpTrainTopoCount = dir.OpTrnTopoCnt;
            if (changed) InaugurationDetected?.Invoke();
            return changed;
        }

        private void StoreTrnDir(byte[] data) => _trnDir = TrdpTrainDir.Read(data);

        private void StoreTrnNetDir(byte[] data) => _trnNetDir = TrdpTrainNetDir.Read(data);

        private void StoreCstInfo(byte[] data)
        {
            TrdpConsistInfo info = TrdpConsistInfo.Read(data);

            // Vorhandenen Eintrag mit gleicher UUID ersetzen, sonst anhaengen (vgl. ttiStoreCstInfo).
            for (int i = 0; i < _cstInfo.Count; i++)
            {
                if (UuidEquals(_cstInfo[i].CstUUID, info.CstUUID))
                {
                    _cstInfo[i] = info;
                    return;
                }
            }
            if (_cstInfo.Count < TauTtiConstants.MaxCstCnt)
            {
                _cstInfo.Add(info);
            }
        }

        // =====================================================================
        //  MD-Requests an die ECSP
        // =====================================================================

        /// <summary>DE: Fordert ein TTDB-Telegramm an (ttiRequestTTDBdata) und parst die Reply asynchron.</summary>
        private void RequestTtdbData(uint comId, byte[]? cstUUID)
        {
            if (_session.Md == null) return;

            (uint reqComId, uint repComId, string uri, int toUs, byte[] payload) = comId switch
            {
                TauTtiConstants.TtdbOpDirInfoReqComId => (TauTtiConstants.TtdbOpDirInfoReqComId,
                    TauTtiConstants.TtdbOpDirInfoRepComId, TauTtiConstants.TtdbOpDirInfoReqUri,
                    TauTtiConstants.TtdbOpDirInfoReqTimeoutUs, new byte[] { 0 }),
                TauTtiConstants.TtdbTrnDirReqComId => (TauTtiConstants.TtdbTrnDirReqComId,
                    TauTtiConstants.TtdbTrnDirRepComId, TauTtiConstants.TtdbTrnDirReqUri,
                    TauTtiConstants.TtdbTrnDirReqTimeoutUs, new byte[] { 0 }),
                TauTtiConstants.TtdbNetDirReqComId => (TauTtiConstants.TtdbNetDirReqComId,
                    TauTtiConstants.TtdbNetDirRepComId, TauTtiConstants.TtdbNetDirReqUri,
                    TauTtiConstants.TtdbNetDirReqTimeoutUs, new byte[] { 0 }),
                TauTtiConstants.TtdbReadCmpltReqComId => (TauTtiConstants.TtdbReadCmpltReqComId,
                    TauTtiConstants.TtdbReadCmpltRepComId, TauTtiConstants.TtdbReadCmpltReqUri,
                    TauTtiConstants.TtdbReadCmpltReqTimeoutUs, new byte[] { 0 }),
                TauTtiConstants.TtdbStatCstReqComId => (TauTtiConstants.TtdbStatCstReqComId,
                    TauTtiConstants.TtdbStatCstRepComId, TauTtiConstants.TtdbStatCstReqUri,
                    TauTtiConstants.TtdbStatCstReqTimeoutUs, cstUUID ?? new byte[TauTtiConstants.UuidLen]),
                _ => (0u, 0u, string.Empty, 0, Array.Empty<byte>())
            };

            if (reqComId == 0u) return;

            MdCaller caller = _session.Md.Request(reqComId, _ecspIp, payload, toUs / 1000,
                                                  numReplies: 1, destUri: uri);
            caller.ReplyReceived += (_, msg) => MdHandleData(repComId, msg.Data);

            // Sicherstellen, dass der Request raus geht (vgl. tlc_process am Ende von ttiRequestTTDBdata).
            _session.Process();
        }

        private TrdpError RequestTtdbDataByLabel(string? cstLabel)
        {
            (TrdpError err, byte[]? uuid) = GetUuidFromLabel(cstLabel);
            if (err != TrdpError.NoErr)
            {
                return err;
            }
            RequestTtdbData(TauTtiConstants.TtdbStatCstReqComId, uuid);
            return TrdpError.NoDataErr;
        }

        // =====================================================================
        //  Interne Aufloesung (UUID / Cache)
        // =====================================================================

        private (TrdpError, byte[]?) GetUuidFromLabel(string? cstLabel)
        {
            if (cstLabel == null)
            {
                return GetOwnCstUuid();
            }

            if (_opTrnDir.OpCstCnt == 0)
            {
                RequestTtdbData(TauTtiConstants.TtdbOpDirInfoReqComId, null);
                return (TrdpError.NoDataErr, null);
            }

            // Fahrzeug mit passender vehId im OP_TRAIN_DIR suchen; dessen Consist-UUID liefern.
            foreach (TrdpOpVehicle veh in _opTrnDir.OpVehList)
            {
                if (string.Equals(veh.VehId, cstLabel, StringComparison.OrdinalIgnoreCase))
                {
                    byte opCstNo = veh.OwnOpCstNo;
                    foreach (TrdpOpConsist cst in _opTrnDir.OpCstList)
                    {
                        if (opCstNo == cst.OpCstNo)
                        {
                            return (TrdpError.NoErr, (byte[])cst.CstUUID.Clone());
                        }
                    }
                }
            }
            return (TrdpError.UnresolvedErr, null);
        }

        private (TrdpError, byte[]?) GetOwnCstUuid()
        {
            if (_trnDir.CstCnt == 0)
            {
                _savedOwnCstIndex = -1;
                RequestTtdbData(TauTtiConstants.TtdbTrnDirReqComId, null);
                return (TrdpError.NoDataErr, null);
            }

            if (_savedOwnCstIndex >= 0 && _savedOwnCstIndex < _trnDir.CstList.Count)
            {
                return (TrdpError.NoErr, (byte[])_trnDir.CstList[_savedOwnCstIndex].CstUUID.Clone());
            }

            for (int i = 0; i < _trnDir.CstList.Count; i++)
            {
                if (_opTrnState.OwnTrnCstNo == _trnDir.CstList[i].TrnCstNo)
                {
                    _savedOwnCstIndex = i;
                    return (TrdpError.NoErr, (byte[])_trnDir.CstList[i].CstUUID.Clone());
                }
            }
            // Wie im C: TRDP_NO_ERR, aber UUID bleibt 0 (nichts gefunden).
            return (TrdpError.NoErr, new byte[TauTtiConstants.UuidLen]);
        }

        private (TrdpError, TrdpConsistInfo?) GetCstInfoByUuid(byte[]? cstUUID)
        {
            byte[]? reqUuid = cstUUID;
            if (cstUUID == null)
            {
                (TrdpError err, byte[]? own) = GetOwnCstUuid();
                if (err != TrdpError.NoErr) return (err, null);
                reqUuid = own;
            }

            foreach (TrdpConsistInfo info in _cstInfo)
            {
                if (UuidEquals(info.CstUUID, reqUuid)) return (TrdpError.NoErr, info);
            }
            return (TrdpError.NoErr, null);
        }

        private (TrdpError, TrdpConsistInfo?) GetCstInfoByLabel(string? cstLabel)
        {
            if (cstLabel == null)
            {
                return GetCstInfoByUuid(null);
            }
            foreach (TrdpConsistInfo info in _cstInfo)
            {
                if (string.Equals(info.CstId, cstLabel, StringComparison.OrdinalIgnoreCase))
                    return (TrdpError.NoErr, info);
            }
            return (TrdpError.NoErr, null);
        }

        // =====================================================================
        //  Oeffentliche tau_*-API
        // =====================================================================

        /// <summary>DE: tau_getOpTrnDirectory — operationeller Zustand + OP-Directory.</summary>
        public TrdpError GetOpTrnDirectory(out TrdpOpTrainDirState? state, out TrdpOpTrainDir? opTrnDir)
        {
            state = null;
            opTrnDir = null;
            if (_opTrnDir.OpCstCnt == 0)
            {
                RequestTtdbData(TauTtiConstants.TtdbOpDirInfoReqComId, null);
                return TrdpError.NoDataErr;
            }
            state = _opTrnState.State;
            opTrnDir = _opTrnDir;
            return TrdpError.NoErr;
        }

        /// <summary>DE: tau_getOpTrnDirectoryStatusInfo — Kopie des letzten PD-100-Telegramms (Host-Werte).</summary>
        public TrdpError GetOpTrnDirectoryStatusInfo(out TrdpOpTrainDirStatusInfo info)
        {
            info = _opTrnState;
            return TrdpError.NoErr;
        }

        /// <summary>DE: tau_getTrnDirectory — Train Directory.</summary>
        public TrdpError GetTrnDirectory(out TrdpTrainDir? trnDir)
        {
            trnDir = null;
            if (_trnDir.CstCnt == 0)
            {
                RequestTtdbData(TauTtiConstants.TtdbTrnDirReqComId, null);
                return TrdpError.NoDataErr;
            }
            trnDir = _trnDir;
            return TrdpError.NoErr;
        }

        /// <summary>DE: tau_getTTI — alle vier Verzeichnisse; fehlende werden angefordert.</summary>
        public TrdpError GetTTI(out TrdpOpTrainDirState? state, out TrdpOpTrainDir? opTrnDir,
                                out TrdpTrainDir? trnDir, out TrdpTrainNetDir? trnNetDir)
        {
            TrdpError ret = TrdpError.NoErr;

            state = _opTrnState.State;
            if (state.OpTrnTopoCnt == 0) ret = TrdpError.NoDataErr;

            opTrnDir = _opTrnDir;
            if (_opTrnDir.OpCstCnt == 0)
            {
                RequestTtdbData(TauTtiConstants.TtdbOpDirInfoReqComId, null);
                ret = TrdpError.NoDataErr;
            }

            trnDir = _trnDir;
            if (_trnDir.CstCnt == 0)
            {
                RequestTtdbData(TauTtiConstants.TtdbTrnDirReqComId, null);
                ret = TrdpError.NoDataErr;
            }

            trnNetDir = _trnNetDir;
            if (_trnNetDir.EntryCnt == 0)
            {
                RequestTtdbData(TauTtiConstants.TtdbNetDirReqComId, null);
                ret = TrdpError.NoDataErr;
            }
            return ret;
        }

        /// <summary>DE: tau_getTrnCstCnt — Gesamtzahl Consists im Zug.</summary>
        public TrdpError GetTrnCstCnt(out ushort trnCstCnt)
        {
            trnCstCnt = 0;
            if (_trnDir.CstCnt == 0)
            {
                RequestTtdbData(TauTtiConstants.TtdbTrnDirReqComId, null);
                return TrdpError.NoDataErr;
            }
            trnCstCnt = _trnDir.CstCnt;
            return TrdpError.NoErr;
        }

        /// <summary>DE: tau_getTrnVehCnt — Gesamtzahl Fahrzeuge im Zug.</summary>
        public TrdpError GetTrnVehCnt(out ushort trnVehCnt)
        {
            trnVehCnt = 0;
            if (_opTrnDir.OpCstCnt == 0)
            {
                RequestTtdbData(TauTtiConstants.TtdbOpDirInfoReqComId, null);
                return TrdpError.NoDataErr;
            }
            trnVehCnt = _opTrnDir.OpVehCnt;
            return TrdpError.NoErr;
        }

        /// <summary>DE: tau_getCstVehCnt — Anzahl Fahrzeuge in einem Consist (null = eigener).</summary>
        public TrdpError GetCstVehCnt(out ushort cstVehCnt, string? cstLabel)
        {
            cstVehCnt = 0;
            (TrdpError err, TrdpConsistInfo? found) = GetCstInfoByLabel(cstLabel);
            if (err != TrdpError.NoErr) return err;
            if (found != null)
            {
                cstVehCnt = found.VehCnt;
                return TrdpError.NoErr;
            }
            return RequestTtdbDataByLabel(cstLabel);
        }

        /// <summary>DE: tau_getCstFctCnt — Anzahl Funktionen in einem Consist (null = eigener).</summary>
        public TrdpError GetCstFctCnt(out ushort cstFctCnt, string? cstLabel)
        {
            cstFctCnt = 0;
            (TrdpError err, TrdpConsistInfo? found) = GetCstInfoByLabel(cstLabel);
            if (err != TrdpError.NoErr) return err;
            if (found != null)
            {
                cstFctCnt = found.FctCnt;
                return TrdpError.NoErr;
            }
            return RequestTtdbDataByLabel(cstLabel);
        }

        /// <summary>DE: tau_getCstFctInfo — Funktionsliste eines Consists (bis maxFctCnt).</summary>
        public TrdpError GetCstFctInfo(out IReadOnlyList<TrdpFunctionInfo> fctInfo, string? cstLabel, ushort maxFctCnt)
        {
            fctInfo = Array.Empty<TrdpFunctionInfo>();
            if (maxFctCnt == 0) return TrdpError.ParamErr;

            (TrdpError err, TrdpConsistInfo? found) = GetCstInfoByLabel(cstLabel);
            if (err != TrdpError.NoErr) return err;
            if (found != null)
            {
                int count = Math.Min(found.FctInfoList.Count, maxFctCnt);
                fctInfo = found.FctInfoList.GetRange(0, count);
                return TrdpError.NoErr;
            }
            return RequestTtdbDataByLabel(cstLabel);
        }

        /// <summary>DE: tau_getVehInfo — Fahrzeuginformation (vehLabel null = erstes/eigenes Fahrzeug).</summary>
        public TrdpError GetVehInfo(out TrdpVehicleInfo? vehInfo, string? vehLabel, string? cstLabel)
        {
            vehInfo = null;
            (TrdpError err, TrdpConsistInfo? found) = GetCstInfoByLabel(cstLabel);
            if (err != TrdpError.NoErr) return err;

            if (found != null)
            {
                foreach (TrdpVehicleInfo veh in found.VehInfoList)
                {
                    if (vehLabel == null || string.Equals(vehLabel, veh.VehId, StringComparison.OrdinalIgnoreCase))
                    {
                        vehInfo = veh;     // Referenz auf Cache-Eintrag (C kopiert in frisch allokierten Puffer).
                        return TrdpError.NoErr;
                    }
                }
                return TrdpError.ParamErr;
            }
            return RequestTtdbDataByLabel(cstLabel);
        }

        /// <summary>DE: tau_getCstInfo — komplette Consist-Information ueber das Label.</summary>
        public TrdpError GetCstInfo(out TrdpConsistInfo? cstInfo, string? cstLabel)
        {
            cstInfo = null;
            (TrdpError err, TrdpConsistInfo? found) = GetCstInfoByLabel(cstLabel);
            if (err != TrdpError.NoErr) return err;
            if (found != null)
            {
                cstInfo = found;  // Referenz auf Cache-Eintrag.
                return TrdpError.NoErr;
            }
            return RequestTtdbDataByLabel(cstLabel);
        }

        /// <summary>DE: tau_getStaticCstInfo — Consist-Information ueber die UUID.</summary>
        public TrdpError GetStaticCstInfo(out TrdpConsistInfo? cstInfo, byte[]? cstUUID)
        {
            cstInfo = null;
            (TrdpError err, TrdpConsistInfo? found) = GetCstInfoByUuid(cstUUID);
            if (err != TrdpError.NoErr) return err;

            if (found != null)
            {
                cstInfo = found;
                return TrdpError.NoErr;
            }

            if (cstUUID == null)
            {
                (TrdpError ownErr, byte[]? ownUuid) = GetOwnCstUuid();
                if (ownErr != TrdpError.NoErr) return ownErr;
                RequestTtdbData(TauTtiConstants.TtdbStatCstReqComId, ownUuid);
            }
            else
            {
                if (_trnDir.CstCnt == 0)
                {
                    RequestTtdbData(TauTtiConstants.TtdbTrnDirReqComId, null);
                }
                else
                {
                    bool inDir = false;
                    foreach (TrdpConsist c in _trnDir.CstList)
                    {
                        if (UuidEquals(c.CstUUID, cstUUID)) { inDir = true; break; }
                    }
                    if (inDir) RequestTtdbData(TauTtiConstants.TtdbStatCstReqComId, cstUUID);
                    else return TrdpError.ParamErr;
                }
            }
            return TrdpError.NoDataErr;
        }

        /// <summary>DE: tau_getVehOrient — Fahrzeug- und Consist-Orientierung.</summary>
        public TrdpError GetVehOrient(out byte vehOrient, out byte cstOrient, string? vehLabel, string? cstLabel)
        {
            vehOrient = 0;
            cstOrient = 0;
            _ = vehLabel; // im C ignoriert

            (TrdpError err, TrdpConsistInfo? found) = GetCstInfoByLabel(cstLabel);
            if (err != TrdpError.NoErr) return err;

            if (_opTrnDir.OpCstCnt == 0)
            {
                RequestTtdbData(TauTtiConstants.TtdbOpDirInfoReqComId, null);
                return TrdpError.NoDataErr;
            }

            if (found != null)
            {
                foreach (TrdpOpConsist opCst in _opTrnDir.OpCstList)
                {
                    if (UuidEquals(opCst.CstUUID, found.CstUUID))
                    {
                        cstOrient = opCst.OpCstOrient;
                        foreach (TrdpOpVehicle veh in _opTrnDir.OpVehList)
                        {
                            if (veh.OwnOpCstNo == opCst.OpCstNo)
                            {
                                vehOrient = veh.VehOrient;
                                return TrdpError.NoErr;
                            }
                        }
                    }
                }
                return TrdpError.NoErr;
            }
            return RequestTtdbDataByLabel(cstLabel);
        }

        /// <summary>
        /// DE: tau_getOwnIds — eigene Bezeichner (Device/Vehicle/Consist) per "Who am I".
        /// Leitet die Device-/Funktions-ID aus den unteren 12 Bit der eigenen IP ab.
        /// </summary>
        public TrdpError GetOwnIds(out string? devId, out string? vehId, out string? cstId)
        {
            devId = null; vehId = null; cstId = null;

            (TrdpError err, TrdpConsistInfo? found) = GetCstInfoByLabel(null);
            if (err != TrdpError.NoErr) return err;
            if (found == null)
            {
                return RequestTtdbDataByLabel(null);
            }

            TrdpError retVal = TrdpError.NoDataErr;
            ushort ownIp = (ushort)(IpToUInt(_session.OwnIpAddress) & 0x00000FFFu);

            foreach (TrdpFunctionInfo fct in found.FctInfoList)
            {
                if (ownIp == fct.FctId && !fct.Grp)
                {
                    devId = fct.FctName;
                    int vehNo = fct.CstVehNo;
                    if (vehNo >= 1 && vehNo <= found.VehInfoList.Count)
                    {
                        vehId = found.VehInfoList[vehNo - 1].VehId;
                    }
                    retVal = TrdpError.NoErr;
                    break;
                }
            }

            if (retVal == TrdpError.NoErr)
            {
                cstId = found.CstId;
            }
            return retVal;
        }

        /// <summary>DE: tau_getOwnOpCstNo — eigene operationelle Consist-Nummer (0 bei Fehler).</summary>
        public byte GetOwnOpCstNo() => _opTrnState.OwnOpCstNo;

        /// <summary>DE: tau_getOwnTrnCstNo — eigene Zug-Consist-Nummer (0 bei Fehler).</summary>
        public byte GetOwnTrnCstNo() => _opTrnState.OwnTrnCstNo;

        // =====================================================================
        //  Hilfsfunktionen
        // =====================================================================

        private static bool UuidEquals(byte[]? a, byte[]? b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static uint IpToUInt(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            if (b.Length != 4) return 0u;
            return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        }
    }
}
