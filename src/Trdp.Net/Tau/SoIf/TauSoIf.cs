// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Portiert nach C# aus TCNOpen TRDP "Light": trdp/src/common/tau_so_if.c
// (Service-Oriented Interface: addService/delService/updService/getServicesList/freeServicesList
// ueber die Service-Registry des konsist-lokalen SRM via MD).
// Original-C: Copyright NewTec GmbH 2019.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using System.Threading;
using Trdp.Net.Core;
using Trdp.Net.Md;
using Trdp.Net.Vos;

namespace Trdp.Net.Tau.SoIf
{
    /// <summary>
    /// DE: Zugriff auf die service-orientierten Funktionen des SRM (Service Registry Manager).
    /// Portiert aus tau_so_if.c. Die Registry-Kommunikation laeuft ueber <see cref="TrdpSession.Md"/>
    /// (MD-Request/Reply).
    ///
    /// DE-Anmerkung zur Anpassung an die loop-getriebene MdSession: Das C-Original blockiert mit
    /// einem Semaphor (vos_semaTake) bis Reply oder Timeout. Da die C#-MdSession ueber
    /// <see cref="TrdpSession.Process"/> getrieben wird, bilden die "wait"-Varianten dies nach,
    /// indem sie in einer Schleife Process() pumpen, bis der Caller nicht mehr pending ist.
    /// Der Aufrufer kann stattdessen die nicht-blockierenden Ueberladungen nutzen und den
    /// zurueckgegebenen <see cref="MdCaller"/> selbst aus seinem ExecutionLoop bedienen.
    ///
    /// DE: Die SRM/ECSP-IP wird explizit uebergeben; die zugehoerige Standard-URI wird als destUri
    /// mitgesendet. Wer mit Namen statt IPs arbeitet, loest sie via TauDnr.IpFromUri auf und uebergibt
    /// die fertige IP (im C-Original macht das tau_ipFromURI intern).
    /// </summary>
    public static class TauSoIf
    {
        /// <summary>DE: Pump-Intervall (ms) der blockierenden "wait"-Varianten.</summary>
        public const int PumpIntervalMs = 2;

        /// <summary>
        /// DE: Service in die Registry des konsist-lokalen SRM aufnehmen (tau_addService).
        /// Bei <paramref name="waitForCompletion"/> == true blockiert der Aufruf bis Reply/Timeout
        /// und aktualisiert <paramref name="service"/> in place mit der Antwort (z. B. instanceId).
        /// </summary>
        public static TrdpError AddService(TrdpSession appHandle, IPAddress srmIp,
                                           SrmServiceInfo service, bool waitForCompletion,
                                           int srmPort = TrdpConstants.MdUdpPort)
            => RequestServices(SrmReqSelector.Add, appHandle, srmIp, service, waitForCompletion, srmPort);

        /// <summary>
        /// DE: Service aus der Registry entfernen (tau_delService). Wie im C-Original wird
        /// <paramref name="waitForCompletion"/> ignoriert; der Aufruf blockiert NICHT.
        /// </summary>
        public static TrdpError DelService(TrdpSession appHandle, IPAddress srmIp,
                                           SrmServiceInfo service, bool waitForCompletion,
                                           int srmPort = TrdpConstants.MdUdpPort)
        {
            _ = waitForCompletion; // DE: bewusst ignoriert (vgl. tau_delService).
            return RequestServices(SrmReqSelector.Del, appHandle, srmIp, service, false, srmPort);
        }

        /// <summary>
        /// DE: Service registrieren/aktualisieren (tau_updService). Identisch zu
        /// <see cref="AddService"/> (selber SRM_ADD-Pfad).
        /// </summary>
        public static TrdpError UpdService(TrdpSession appHandle, IPAddress srmIp,
                                           SrmServiceInfo service, bool waitForCompletion,
                                           int srmPort = TrdpConstants.MdUdpPort)
            => RequestServices(SrmReqSelector.Add, appHandle, srmIp, service, waitForCompletion, srmPort);

        /// <summary>
        /// DE: Liste der dem SRM bekannten Services abrufen (tau_getServicesList). Blockiert bis
        /// Reply/Timeout. <paramref name="filterEntry"/> ist optional (null = keine Filterung).
        /// </summary>
        /// <param name="appHandle">DE: Offene TRDP-Session.</param>
        /// <param name="srmIp">DE: IP des SRM/ECSP (bei Namen vorab via TauDnr.IpFromUri aufloesen).</param>
        /// <param name="servicesList">DE: Ergebnisliste (null bei Fehler).</param>
        /// <param name="noOfServices">DE: Anzahl Services in der Liste.</param>
        /// <param name="filterEntry">DE: Optionaler Filtereintrag.</param>
        public static TrdpError GetServicesList(TrdpSession appHandle, IPAddress srmIp,
                                                out SrmServiceEntries? servicesList,
                                                out uint noOfServices,
                                                SrmServiceEntries? filterEntry = null,
                                                int srmPort = TrdpConstants.MdUdpPort)
        {
            servicesList = null;
            noOfServices = 0u;

            if (appHandle is null || appHandle.Md is null || srmIp is null)
            {
                return TrdpError.ParamErr;
            }
            if (srmIp.Equals(IPAddress.Any))
            {
                // DE: entspricht VOS_INADDR_ANY -> URI war nicht aufloesbar.
                return TrdpError.UnresolvedErr;
            }

            MdSession md = appHandle.Md;
            byte[] data = filterEntry != null ? filterEntry.Encode() : Array.Empty<byte>();

            MdCaller caller = md.Request(
                SrmServiceRegistry.ReadReqComId, srmIp, data,
                replyTimeoutMs: ToMs(SrmServiceRegistry.ReadReqTimeoutUs),
                numReplies: 1, destUri: SrmServiceRegistry.ReadReqUri, destPort: srmPort);

            SrmServiceEntries? result = null;
            TrdpError err = TrdpError.NoErr;

            caller.ReplyReceived += (_, msg) =>
            {
                if (msg.MessageType != MdMessageType.Reply)
                {
                    return;
                }
                // DE: Das SRM-Reply traegt im Original die READ-Reply-ComId (113). Die hiesige
                // MdSession sendet Replies jedoch mit der Request-ComId zurueck und matcht ueber die
                // sessionId; daher wird datenbasiert ausgewertet (vgl. soMDCallback/READ_REP).
                if (msg.Data.Length >= SrmServiceEntries.HeaderSize + SrmServiceInfo.WireSize)
                {
                    result = SrmServiceEntries.Decode(msg.Data);
                }
                else
                {
                    err = TrdpError.NoDataErr;
                }
            };
            caller.TimedOut += _ => err = TrdpError.TimeoutErr;

            PumpUntilDone(appHandle, caller);

            if (err != TrdpError.NoErr)
            {
                return err;
            }
            if (result == null)
            {
                return TrdpError.NoDataErr;
            }

            servicesList = result;
            noOfServices = (uint)result.NoOfEntries;
            return TrdpError.NoErr;
        }

        /// <summary>
        /// DE: Speicher einer per <see cref="GetServicesList"/> erhaltenen Liste freigeben
        /// (tau_freeServicesList). Im C#-Port uebernimmt das der GC; die Methode existiert zur
        /// API-Treue und setzt die Referenz auf null.
        /// </summary>
        public static void FreeServicesList(ref SrmServiceEntries? servicesListBuffer)
        {
            servicesListBuffer = null;
        }

        // ── Intern: gemeinsame Request-Logik (requestServices) ──

        private static TrdpError RequestServices(SrmReqSelector selector, TrdpSession appHandle,
                                                 IPAddress srmIp, SrmServiceInfo service,
                                                 bool waitForCompletion, int srmPort)
        {
            if (appHandle is null || appHandle.Md is null || service is null || srmIp is null)
            {
                return TrdpError.ParamErr;
            }

            MdSession md = appHandle.Md;

            // DE: Request-Daten aufbauen: version=1.0, noOfEntries=1, serviceEntry[0]=service.
            var entries = new SrmServiceEntries { Version = new SrmShortVersion(1, 0) };
            entries.Entries.Add(service);
            byte[] data = entries.Encode();

            uint reqComId;
            uint repComId;
            string reqUri;
            uint timeoutUs;
            switch (selector)
            {
                case SrmReqSelector.Add:
                    reqComId  = SrmServiceRegistry.AddReqComId;
                    repComId  = SrmServiceRegistry.AddRepComId;
                    reqUri    = SrmServiceRegistry.AddReqUri;
                    timeoutUs = SrmServiceRegistry.AddReqTimeoutUs;
                    break;
                case SrmReqSelector.Del:
                    reqComId  = SrmServiceRegistry.DelReqComId;
                    repComId  = SrmServiceRegistry.DelRepComId;
                    reqUri    = SrmServiceRegistry.DelReqUri;
                    timeoutUs = SrmServiceRegistry.DelReqTimeoutUs;
                    break;
                default:
                    return TrdpError.ParamErr;
            }

            int timeoutMs = ToMs(timeoutUs);

            // DE: Nicht-blockierend (no-wait): Request senden und zurueckkehren. Entspricht dem
            // C-Pfad mit pContext == NULL (der Reply wird dann nicht ausgewertet).
            if (!waitForCompletion)
            {
                md.Request(reqComId, srmIp, data, replyTimeoutMs: timeoutMs,
                           numReplies: 1, destUri: reqUri, destPort: srmPort);
                return TrdpError.NoErr;
            }

            MdCaller caller = md.Request(reqComId, srmIp, data, replyTimeoutMs: timeoutMs,
                                         numReplies: 1, destUri: reqUri, destPort: srmPort);
            TrdpError result = TrdpError.NoErr;

            caller.ReplyReceived += (_, msg) =>
            {
                if (msg.MessageType != MdMessageType.Reply)
                {
                    return;
                }
                // DE: Das ADD-Reply traegt im Original die ADD-Reply-ComId (115) und kann geaenderte
                // Daten (z. B. die vergebene instanceId) enthalten. Die hiesige MdSession sendet
                // Replies mit der Request-ComId zurueck und matcht ueber die sessionId; daher wird
                // datenbasiert ausgewertet (vgl. soMDCallback). Bei DEL gibt es nur eine
                // Statusbestaetigung ohne Daten.
                if (selector == SrmReqSelector.Add && msg.Data.Length > 0)
                {
                    SrmServiceEntries reply = SrmServiceEntries.Decode(msg.Data);
                    if (reply.Entries.Count > 0)
                    {
                        service.CopyFrom(reply.Entries[0]);
                    }
                }
                result = TrdpError.NoErr;
            };
            caller.TimedOut += _ => result = TrdpError.TimeoutErr;

            PumpUntilDone(appHandle, caller);
            _ = repComId; // DE: zur Klarheit dokumentiert; Matching erfolgt via sessionId.
            return result;
        }

        // DE: Pumpt den Verarbeitungszyklus, bis der Caller nicht mehr auf Replies wartet
        // (Reply empfangen oder Timeout). Bildet das vos_semaTake des C-Originals nach.
        private static void PumpUntilDone(TrdpSession appHandle, MdCaller caller)
        {
            while (caller.IsPending)
            {
                appHandle.Process();
                if (caller.IsPending)
                {
                    Thread.Sleep(PumpIntervalMs);
                }
            }
        }

        // DE: us -> ms (aufgerundet, mind. 1 ms bei >0), fuer die MdSession-API.
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
