// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Managed shim fuer die TCP-Seite der VOS-Socket-Schicht von TCNOpen TRDP "Light"
// (trdp/src/vos/.../vos_sock.c, MD-TCP in trdp_mdcom.c: Listen/Accept/Connect + Frame-Reassembly).
// UDP-aequivalent in VosSock.cs; hier nicht-blockierend, loop-getrieben.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Trdp.Net.Vos
{
    /// <summary>
    /// DE: Bestimmt aus den bisher gepufferten Bytes die Gesamtlaenge des naechsten Frames
    /// (0 = Header noch unvollstaendig). Eigener Delegat, da <c>ReadOnlySpan</c> nicht als
    /// Typargument von <c>Func&lt;&gt;</c> erlaubt ist.
    /// </summary>
    public delegate int FrameLengthDelegate(ReadOnlySpan<byte> buffer);

    /// <summary>
    /// DE: Eine TCP-Verbindung mit interner Reassembly. MD-Frames werden aus dem Bytestrom
    /// rekonstruiert, sobald Header (fester Teil) + Nutzlast vollstaendig vorliegen.
    /// </summary>
    public sealed class VosTcpConnection : IDisposable
    {
        private readonly Socket _socket;
        private byte[] _buffer = new byte[4096];
        private int _length;

        public IPAddress RemoteIp { get; }
        public int RemotePort { get; }
        public bool Connected => _socket.Connected;

        internal VosTcpConnection(Socket socket)
        {
            _socket = socket;
            _socket.Blocking = false;
            var ep = (IPEndPoint)socket.RemoteEndPoint!;
            RemoteIp = ep.Address;
            RemotePort = ep.Port;
        }

        /// <summary>DE: Verbindet sich (blockierend mit Timeout) zu einem Ziel.</summary>
        public static VosTcpConnection Connect(IPAddress destIp, int destPort, int connectTimeoutMs = 5000)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            IAsyncResult ar = socket.BeginConnect(new IPEndPoint(destIp, destPort), null, null);
            if (!ar.AsyncWaitHandle.WaitOne(connectTimeoutMs))
            {
                socket.Dispose();
                throw new TimeoutException($"TCP-Connect zu {destIp}:{destPort} nach {connectTimeoutMs} ms abgelaufen.");
            }
            socket.EndConnect(ar);
            return new VosTcpConnection(socket);
        }

        /// <summary>DE: Sendet einen kompletten Frame.</summary>
        public void Send(ReadOnlySpan<byte> data)
        {
            int sent = 0;
            while (sent < data.Length)
            {
                sent += _socket.Send(data.Slice(sent), SocketFlags.None);
            }
        }

        /// <summary>
        /// DE: Liest verfuegbare Bytes und liefert true, sobald ein vollstaendiger Frame vorliegt.
        /// <paramref name="frameLength"/> bestimmt aus den Pufferdaten die Gesamt-Framelaenge
        /// (oder 0, falls Header noch unvollstaendig).
        /// </summary>
        public bool TryReadFrame(FrameLengthDelegate frameLength, out ReadOnlySpan<byte> frame)
        {
            frame = default;

            // Verfuegbare Bytes anhaengen.
            int avail = _socket.Available;
            if (avail > 0)
            {
                EnsureCapacity(_length + avail);
                int read = _socket.Receive(_buffer.AsSpan(_length, avail), SocketFlags.None);
                _length += read;
            }

            int need = frameLength(_buffer.AsSpan(0, _length));
            if (need <= 0 || _length < need)
            {
                return false;
            }

            frame = _buffer.AsSpan(0, need);
            // Frame "konsumieren": Rest nach vorne schieben (nach Auswertung durch den Aufrufer
            // ist der Span noch gueltig, weil wir erst beim naechsten Aufruf kompaktieren).
            _pendingConsume = need;
            return true;
        }

        private int _pendingConsume;

        /// <summary>DE: Gibt den zuletzt gelieferten Frame frei (vor dem naechsten TryReadFrame aufrufen).</summary>
        public void ConsumeFrame()
        {
            if (_pendingConsume <= 0) return;
            int rest = _length - _pendingConsume;
            if (rest > 0)
            {
                Array.Copy(_buffer, _pendingConsume, _buffer, 0, rest);
            }
            _length = rest;
            _pendingConsume = 0;
        }

        private void EnsureCapacity(int needed)
        {
            if (_buffer.Length >= needed) return;
            int newSize = _buffer.Length * 2;
            while (newSize < needed) newSize *= 2;
            Array.Resize(ref _buffer, newSize);
        }

        public void Dispose() => _socket.Dispose();
    }

    /// <summary>
    /// DE: Nicht-blockierender TCP-Listener (vos_sockListen/Accept).
    /// </summary>
    public sealed class VosTcpListener : IDisposable
    {
        private readonly Socket _socket;

        private VosTcpListener(Socket socket) => _socket = socket;

        public static VosTcpListener Open(IPAddress? bindAddress, int port, int backlog = 10)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(bindAddress ?? IPAddress.Any, port));
            socket.Listen(backlog);
            return new VosTcpListener(socket);
        }

        /// <summary>DE: Lokaler Port (nuetzlich bei Bind auf Port 0 in Tests).</summary>
        public int Port => ((IPEndPoint)_socket.LocalEndPoint!).Port;

        /// <summary>DE: Nimmt eine wartende Verbindung an, falls vorhanden.</summary>
        public bool TryAccept(out VosTcpConnection connection)
        {
            connection = null!;
            try
            {
                if (!_socket.Poll(0, SelectMode.SelectRead))
                {
                    return false;
                }
                Socket s = _socket.Accept();
                connection = new VosTcpConnection(s);
                return true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                return false;
            }
        }

        public void Dispose() => _socket.Dispose();
    }
}
