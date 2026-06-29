// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Managed shim fuer die VOS-Socket-Schicht von TCNOpen TRDP "Light"
// (trdp/src/vos/api/vos_sock.h, posix/vos_sock.c). Bewusst KEINE 1:1-Portierung:
// UDP wird ueber System.Net.Sockets abgebildet (siehe PORTING.md, Prinzip 2).
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Net;
using System.Net.Sockets;

namespace Trdp.Net.Vos
{
    /// <summary>
    /// DE: Duenner UDP-Socket-Wrapper als Ersatz fuer die VOS-Socket-API. Nicht-blockierend;
    /// der Empfang wird per <see cref="TryReceive"/> aus dem Verarbeitungszyklus gepollt
    /// (passt zum loop-getriebenen TRDP-Modell, vgl. tlc_process + vos_select).
    /// </summary>
    public sealed class VosSock : IDisposable
    {
        private readonly Socket _socket;
        private EndPoint _recvEp = new IPEndPoint(IPAddress.Any, 0);

        private VosSock(Socket socket) => _socket = socket;

        /// <summary>DE: Tatsaechlich gebundener lokaler Port (wichtig bei Bind auf 0).</summary>
        public int LocalPort => _socket.LocalEndPoint is IPEndPoint ep ? ep.Port : 0;

        /// <summary>DE: Oeffnet einen UDP-Socket und bindet ihn (vos_sockOpenUDP + vos_sockBind).</summary>
        /// <param name="bindAddress">Lokale IP zum Binden (null = INADDR_ANY).</param>
        /// <param name="port">Lokaler Port (z. B. 17224 fuer PD).</param>
        /// <param name="reuseAddr">SO_REUSEADDR — noetig, wenn mehrere Teilnehmer denselben PD-Port nutzen.</param>
        public static VosSock OpenUdp(IPAddress? bindAddress, int port, bool reuseAddr = true)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                Blocking = false,
            };
            if (reuseAddr)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }
            socket.EnableBroadcast = true;
            socket.Bind(new IPEndPoint(bindAddress ?? IPAddress.Any, port));
            return new VosSock(socket);
        }

        /// <summary>DE: Tritt einer Multicast-Gruppe bei (vos_sockJoinMC).</summary>
        public void JoinMulticast(IPAddress group, IPAddress? iface = null)
        {
            var opt = iface == null
                ? new MulticastOption(group)
                : new MulticastOption(group, iface);
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, opt);
        }

        /// <summary>DE: Setzt die TTL fuer ausgehende Multicast-Pakete.</summary>
        public void SetMulticastTtl(int ttl) =>
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, ttl);

        /// <summary>DE: Sendet ein Datagramm an Ziel-IP/Port (vos_sockSendUDP).</summary>
        public int SendTo(ReadOnlySpan<byte> data, IPAddress destIp, int destPort)
        {
            return _socket.SendTo(data, SocketFlags.None, new IPEndPoint(destIp, destPort));
        }

        /// <summary>
        /// DE: Nicht-blockierender Empfang (vos_sockReceiveUDP). Liefert false, wenn nichts
        /// anliegt. <paramref name="length"/> = Anzahl gelesener Bytes, <paramref name="srcIp"/>/
        /// <paramref name="srcPort"/> = Absenderadresse und -port.
        /// </summary>
        public bool TryReceive(Span<byte> buffer, out int length, out IPAddress srcIp, out int srcPort)
        {
            length = 0;
            srcIp = IPAddress.Any;
            srcPort = 0;

            if (_socket.Available <= 0)
            {
                return false;
            }

            try
            {
                length = _socket.ReceiveFrom(buffer, SocketFlags.None, ref _recvEp);
                var ep = (IPEndPoint)_recvEp;
                srcIp = ep.Address;
                srcPort = ep.Port;
                return length > 0;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                length = 0;
                return false;
            }
        }

        public void Dispose() => _socket.Dispose();
    }
}
