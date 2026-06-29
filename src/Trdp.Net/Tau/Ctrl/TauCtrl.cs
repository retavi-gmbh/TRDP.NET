// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tau_ctrl.c
// (ECSP-Control-Schnittstelle: tau_initEcspCtrl/terminateEcspCtrl, tau_setEcspCtrl,
//  tau_getEcspStat, tau_requestEcspConfirm/requestEcspConfirmReply).
// Original-C: Copyright Bombardier Transportation Inc. or its subsidiaries and others, 2013-2021.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using Trdp.Net.Core;
using Trdp.Net.Md;
using Trdp.Net.Pd;
using Trdp.Net.Vos;

namespace Trdp.Net.Tau.Ctrl
{
    /// <summary>
    /// DE: ECSP-Control-Schnittstelle (ETB-Control nach IEC 61375-2-3), portiert aus tau_ctrl.c.
    /// Publiziert zyklisch das ECSP-Control-Telegramm (PD, ComId 120), abonniert das
    /// ECSP-Status-Telegramm (PD, ComId 121) und stellt Confirm-/Korrektur-Anfragen ueber MD
    /// (ComId 122/123).
    ///
    /// DE-Anpassung an die TrdpSession-API: Das C-Original ist ein Singleton mit statischem
    /// Zustand (priv_pubHandle, priv_subHandle, priv_ecspConfReply ...). Hier wird der Zustand
    /// in einer Instanz gehalten (eine pro Session/ECSP). Aus EINEM Thread/ExecutionLoop nutzen
    /// (wie die uebrige Session). Die Verarbeitung wird ueber <see cref="TrdpSession.Process"/>
    /// getrieben.
    /// </summary>
    public sealed class TauCtrl
    {
        private PdPublisher? _ctrlPublisher;     // priv_pubHandle
        private PdSubscriber? _statSubscriber;   // priv_subHandle
        private IPAddress _ecspIpAddr = IPAddress.Any;   // priv_ecspIpAddr
        private bool _initialised;               // priv_ecspCtrlInitialised

        // DE: zuletzt empfangene Confirm-Antwort + zugehoerige MD-Info (priv_ecspConfReply*).
        private EcspConfReply? _ecspConfReply;
        private MdMessage? _ecspConfReplyMdInfo;
        private Action<MdCaller, MdMessage>? _pfEcspConfReplyCb;

        /// <summary>DE: True, sobald <see cref="InitEcspCtrl"/> erfolgreich war.</summary>
        public bool IsInitialised => _initialised;

        /// <summary>DE: PD-Subscriber des Status-Telegramms (entspricht pPdInfo-Quelle in tau_getEcspStat).</summary>
        public PdSubscriber? StatSubscriber => _statSubscriber;

        /// <summary>DE: PD-Publisher des Control-Telegramms.</summary>
        public PdPublisher? CtrlPublisher => _ctrlPublisher;

        /// <summary>
        /// DE: Initialisiert die ECSP-Control-Schnittstelle (tau_initEcspCtrl): Publish des
        /// Control-Telegramms (zyklisch, 1 s) an <paramref name="ecspIpAddr"/> und Subscribe des
        /// Status-Telegramms (Timeout 5 s).
        /// </summary>
        /// <param name="appHandle">DE: Offene TRDP-Session.</param>
        /// <param name="ecspIpAddr">DE: IP-Adresse des ECSP (Ziel des Control-Telegramms).</param>
        public TrdpError InitEcspCtrl(TrdpSession appHandle, IPAddress ecspIpAddr)
        {
            if (appHandle == null || ecspIpAddr == null)
            {
                return TrdpError.ParamErr;
            }

            _ecspIpAddr = ecspIpAddr;

            // DE: Reply-Zustand zuruecksetzen (memset im C-Original).
            _ecspConfReply = null;
            _ecspConfReplyMdInfo = null;
            _pfEcspConfReplyCb = null;

            // DE: tlp_publish — Control-Telegramm, ecnTopoCounter/opTopoCounter = 0 (wie C),
            // ohne Initialdaten (gesendet wird erst nach SetEcspCtrl).
            PdPublisher pub = appHandle.Pd.Publish(
                EcspCtrlConstants.EcspCtrlComId,
                ecspIpAddr,
                cycleTimeMs: ToMs(EcspCtrlConstants.EcspCtrlCycleUs));
            // DE: Wie im C-Original explizit 0 (consist-local), nicht die Session-Topozaehler.
            pub.EtbTopoCnt = 0u;
            pub.OpTrnTopoCnt = 0u;
            _ctrlPublisher = pub;

            // DE: tlp_subscribe — Status-Telegramm, Timeout 5 s (TRDP_TO_SET_TO_ZERO).
            _statSubscriber = appHandle.Pd.Subscribe(
                EcspCtrlConstants.EcspStatComId,
                timeoutMs: ToMs(EcspCtrlConstants.EcspStatTimeoutUs));

            _initialised = true;
            return TrdpError.NoErr;
        }

        /// <summary>
        /// DE: Schliesst die ECSP-Control-Schnittstelle (tau_terminateEcspCtrl): deregistriert
        /// Publisher/Subscriber (tlp_unpublish/tlp_unsubscribe) und setzt den Zustand zurueck.
        /// </summary>
        public TrdpError TerminateEcspCtrl(TrdpSession appHandle)
        {
            if (!_initialised)
            {
                return TrdpError.NoInitErr;
            }

            // DE: Echte Deregistrierung ueber die Remove-APIs der Session.
            if (_ctrlPublisher != null) appHandle.Pd.Unpublish(_ctrlPublisher);
            if (_statSubscriber != null) appHandle.Pd.Unsubscribe(_statSubscriber);

            _initialised = false;
            _ecspConfReply = null;
            _ecspConfReplyMdInfo = null;
            _pfEcspConfReplyCb = null;
            _ctrlPublisher = null;
            _statSubscriber = null;
            return TrdpError.NoErr;
        }

        /// <summary>
        /// DE: Setzt die ECSP-Control-Information (tau_setEcspCtrl). Marshallt das Telegramm
        /// big-endian und uebergibt es dem Publisher (tlp_put); gesendet wird im naechsten Zyklus.
        /// </summary>
        public TrdpError SetEcspCtrl(TrdpSession appHandle, EcspCtrl ecspCtrl)
        {
            _ = appHandle;
            if (!_initialised || _ctrlPublisher == null)
            {
                return TrdpError.NoInitErr;
            }
            if (ecspCtrl == null)
            {
                return TrdpError.ParamErr;
            }

            _ctrlPublisher.SetData(ecspCtrl.Encode());
            return TrdpError.NoErr;
        }

        /// <summary>
        /// DE: Liefert die zuletzt empfangene ECSP-Status-Information (tau_getEcspStat). Liest die
        /// abonnierten Daten und unmarshallt sie big-endian.
        /// </summary>
        /// <param name="appHandle">DE: Offene TRDP-Session.</param>
        /// <param name="ecspStat">DE: Ergebnis (null bei Fehler/ohne Daten).</param>
        /// <returns>NoErr, NoInitErr, NoDataErr oder TimeoutErr.</returns>
        public TrdpError GetEcspStat(TrdpSession appHandle, out EcspStat? ecspStat)
        {
            _ = appHandle;
            ecspStat = null;

            if (!_initialised || _statSubscriber == null)
            {
                return TrdpError.NoInitErr;
            }

            // DE: tlp_get — gueltige (nicht abgelaufene) Daten?
            if (_statSubscriber.TryGetData(out byte[] data) && data.Length >= EcspStat.WireSize)
            {
                ecspStat = EcspStat.Decode(data);
                return TrdpError.NoErr;
            }

            // DE: Unterscheidung wie tlp_get: schon einmal Daten, jetzt abgelaufen -> Timeout,
            // sonst noch keine Daten -> NoData.
            return _statSubscriber.LastData != null ? TrdpError.TimeoutErr : TrdpError.NoDataErr;
        }

        /// <summary>
        /// DE: Stellt eine ECSP-Bestaetigungs-/Korrektur-Anfrage (tau_requestEcspConfirm). Sendet
        /// einen MD-Request (ComId 122); die Antwort (ComId 123) wird ueber den internen Callback
        /// gespeichert und an <paramref name="pfCbFunction"/> weitergereicht.
        ///
        /// DE-Anpassung: Die C-MD-Callback-Signatur wird durch das ereignisbasierte
        /// <see cref="MdCaller.ReplyReceived"/> der C#-MdSession ersetzt; das User-Ref des
        /// C-Originals wird hier nicht benoetigt (Matching erfolgt ueber die Session-ID des
        /// zurueckgegebenen <see cref="MdCaller"/>).
        /// </summary>
        /// <param name="appHandle">DE: Offene TRDP-Session (MD aktiviert).</param>
        /// <param name="pfCbFunction">DE: Optionaler Callback bei Eintreffen der Antwort.</param>
        /// <param name="ecspConfRequest">DE: Anfragedaten.</param>
        /// <param name="caller">DE: Handle der laufenden MD-Anfrage (null bei Fehler).</param>
        public TrdpError RequestEcspConfirm(TrdpSession appHandle,
                                            Action<MdCaller, MdMessage>? pfCbFunction,
                                            EcspConfRequest ecspConfRequest,
                                            out MdCaller? caller)
        {
            caller = null;

            if (!_initialised)
            {
                return TrdpError.NoInitErr;
            }
            if (ecspConfRequest == null || appHandle?.Md == null)
            {
                return TrdpError.ParamErr;
            }

            // DE: Reply-Zustand zuruecksetzen (memset im C-Original).
            _ecspConfReply = null;
            _ecspConfReplyMdInfo = null;
            _pfEcspConfReplyCb = pfCbFunction;

            byte[] telegram = ecspConfRequest.Encode();

            // DE: tlm_request — etbTopoCnt/opTrnTopoCnt = 0, numReplies = 1, Timeout 3 s.
            MdCaller c = appHandle.Md.Request(
                EcspCtrlConstants.EcspConfReqComId,
                _ecspIpAddr,
                telegram,
                replyTimeoutMs: ToMs(EcspCtrlConstants.EcspConfReplyTimeoutUs),
                numReplies: 1);

            // DE: entspricht ecspConfRepMDCallback (#373).
            c.ReplyReceived += OnConfReplyReceived;

            caller = c;
            return TrdpError.NoErr;
        }

        /// <summary>
        /// DE: Liefert die zuletzt empfangene Confirm-/Korrektur-Antwort (tau_requestEcspConfirmReply).
        /// </summary>
        /// <param name="appHandle">DE: Offene TRDP-Session.</param>
        /// <param name="msg">DE: MD-Info der Antwort (null, falls noch keine empfangen).</param>
        /// <param name="ecspConfReply">DE: Antwortdaten (null, falls noch keine empfangen).</param>
        public TrdpError RequestEcspConfirmReply(TrdpSession appHandle,
                                                 out MdMessage? msg,
                                                 out EcspConfReply? ecspConfReply)
        {
            _ = appHandle;
            if (!_initialised)
            {
                msg = null;
                ecspConfReply = null;
                return TrdpError.NoInitErr;
            }

            msg = _ecspConfReplyMdInfo;
            ecspConfReply = _ecspConfReply;
            return TrdpError.NoErr;
        }

        // DE: Interner Reply-Handler (ecspConfRepMDCallback). Verarbeitet nur erfolgreiche
        // Replies mit ComId 123 und korrekter Datengroesse; unmarshallt manuell (#356).
        private void OnConfReplyReceived(MdCaller caller, MdMessage msg)
        {
            // DE: pMsg->resultCode == TRDP_NO_ERR -> kein Error-Reply (Me).
            if (msg.MessageType != MdMessageType.Reply && msg.MessageType != MdMessageType.ReplyQuery)
            {
                return;
            }
            if (msg.ComId != EcspCtrlConstants.EcspConfRepComId)
            {
                return;
            }
            if (msg.Data.Length != EcspConfReply.WireSize)
            {
                return;
            }

            _ecspConfReply = EcspConfReply.Decode(msg.Data);
            _ecspConfReplyMdInfo = msg;

            _pfEcspConfReplyCb?.Invoke(caller, msg);
        }

        // DE: us -> ms (aufgerundet, mind. 1 ms bei >0) fuer die PD-/MD-API.
        private static int ToMs(uint micros)
        {
            if (micros == 0u)
            {
                return 0;
            }
            long ms = (micros + 999u) / 1000u;
            return ms > int.MaxValue ? int.MaxValue : (int)ms;
        }
    }
}
