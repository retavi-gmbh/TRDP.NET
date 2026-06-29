// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// C#-Port: Copyright 2026 retavi GmbH. Lizenz: MPL-2.0.
//
// TRDP.NET-Beispiele. Aufruf:  dotnet run -f net10.0 -- <befehl> [args]
//   pd-pub  [destIp=239.1.1.1] [comId=1000] [cycleMs=100]   zyklischer PD-Publisher
//   pd-sub  [bindIp=0.0.0.0]   [comId=1000]                 PD-Subscriber
//   md-server [port=17225]     [comId=2000]                 MD-Listener, antwortet
//   md-client [destIp=127.0.0.1] [port=17225] [comId=2000]  MD-Request
//   marshal                                                 Dataset marshal/unmarshal (offline)
//   xml                                                      XML-Config laden & anzeigen (offline)

using System;
using System.Net;
using System.Text;
using System.Threading;
using Trdp.Net.Core;
using Trdp.Net.Marshalling;
using Trdp.Net.Md;
using Trdp.Net.Pd;
using Trdp.Net.Xml;

internal static class Program
{
    private static volatile bool _running = true;

    private static int Main(string[] args)
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _running = false; };
        string cmd = args.Length > 0 ? args[0] : "help";
        try
        {
            return cmd switch
            {
                "pd-pub"    => PdPub(args),
                "pd-sub"    => PdSub(args),
                "md-server" => MdServer(args),
                "md-client" => MdClient(args),
                "marshal"   => MarshalDemo(),
                "xml"       => XmlDemo(),
                _           => Help(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fehler: {ex.Message}");
            return 1;
        }
    }

    // ── PD: zyklisch senden (vgl. TCNOpen sendHello) ───────────────────────────
    private static int PdPub(string[] a)
    {
        IPAddress dest = IPAddress.Parse(Arg(a, 1, "239.1.1.1"));
        uint comId = uint.Parse(Arg(a, 2, "1000"));
        int cycleMs = int.Parse(Arg(a, 3, "100"));

        using var session = new TrdpPdSession();
        var pub = session.Publish(comId, dest, cycleMs);
        Console.WriteLine($"PD-Publisher: comId {comId} -> {dest}:17224 alle {cycleMs} ms. Strg+C beendet.");

        uint counter = 0;
        while (_running)
        {
            pub.SetData(Encoding.ASCII.GetBytes($"Counter: {counter:D8}"));
            counter++;
            // mehrere Zyklen lang senden, bevor neue Daten gesetzt werden:
            for (int i = 0; i < 10 && _running; i++) { session.Process(); Thread.Sleep(cycleMs); }
        }
        Console.WriteLine($"Gesendet: {session.PacketsSent} Telegramme.");
        return 0;
    }

    // ── PD: empfangen (vgl. TCNOpen receiveHello) ──────────────────────────────
    private static int PdSub(string[] a)
    {
        IPAddress bind = IPAddress.Parse(Arg(a, 1, "0.0.0.0"));
        uint comId = uint.Parse(Arg(a, 2, "1000"));

        using var session = new TrdpPdSession(bind);
        var sub = session.Subscribe(comId, timeoutMs: 2000);
        sub.DataReceived += s =>
            Console.WriteLine($"[RX] comId={s.ComId} seq={s.SequenceCounter} " +
                              $"\"{Encoding.ASCII.GetString(s.LastData!).TrimEnd('\0')}\"");
        sub.Timeout += _ => Console.WriteLine($"[TIMEOUT] comId {comId}");

        Console.WriteLine($"PD-Subscriber: comId {comId} an {bind}:17224. Strg+C beendet.");
        while (_running) { session.Process(); Thread.Sleep(5); }
        return 0;
    }

    // ── MD: Server (Listener, antwortet) ───────────────────────────────────────
    private static int MdServer(string[] a)
    {
        int port = int.Parse(Arg(a, 1, "17225"));
        uint comId = uint.Parse(Arg(a, 2, "2000"));

        using var session = new MdSession(IPAddress.Any, port);
        var listener = session.AddListener(comId);
        listener.Received += ctx =>
        {
            string req = Encoding.ASCII.GetString(ctx.Message.Data);
            Console.WriteLine($"[REQ] comId={ctx.Message.ComId} von {ctx.Message.SourceIp}: \"{req}\"");
            if (ctx.ReplyExpected)
            {
                ctx.Reply(Encoding.ASCII.GetBytes($"ACK: {req}"));
            }
        };
        Console.WriteLine($"MD-Server: comId {comId} auf Port {port}. Strg+C beendet.");
        while (_running) { session.Process(); Thread.Sleep(5); }
        return 0;
    }

    // ── MD: Client (Request -> Reply) ──────────────────────────────────────────
    private static int MdClient(string[] a)
    {
        IPAddress dest = IPAddress.Parse(Arg(a, 1, "127.0.0.1"));
        int port = int.Parse(Arg(a, 2, "17225"));
        uint comId = uint.Parse(Arg(a, 3, "2000"));

        using var session = new MdSession(IPAddress.Any, 0);
        bool done = false;
        var caller = session.Request(comId, dest, Encoding.ASCII.GetBytes("Ping"),
                                     replyTimeoutMs: 2000, destPort: port);
        caller.ReplyReceived += (_, m) =>
        {
            Console.WriteLine($"[REPLY] \"{Encoding.ASCII.GetString(m.Data)}\" (status {m.ReplyStatus})");
            done = true;
        };
        caller.TimedOut += _ => { Console.WriteLine("[TIMEOUT] keine Antwort"); done = true; };

        Console.WriteLine($"MD-Client: Request comId {comId} -> {dest}:{port} ...");
        while (_running && !done) { session.Process(); Thread.Sleep(5); }
        return 0;
    }

    // ── Marshalling: typisiertes Dataset (offline) ─────────────────────────────
    private static int MarshalDemo()
    {
        // Beispiel-Telegramm: UINT32 Zeitstempel + INT16[3] Messwerte + BOOL8 gueltig.
        var ds = new TrdpDataset(1000,
            new TrdpDatasetElement(TrdpDataType.UInt32, 1, "timestamp"),
            new TrdpDatasetElement(TrdpDataType.Int16, 3, "values"),
            new TrdpDatasetElement(TrdpDataType.BitSet8, 1, "valid"));

        var marshaller = new TrdpMarshaller();
        object[] input = { 0x0102_0304u, (short)-1, (short)2, (short)3, true };

        byte[] wire = marshaller.Marshal(ds, input);
        Console.WriteLine($"Dataset 1000 -> {wire.Length} Bytes (gepackt, big-endian):");
        Console.WriteLine("  " + BitConverter.ToString(wire));

        object[] output = marshaller.Unmarshal(ds, wire);
        Console.WriteLine("Zurueck-gelesen:");
        Console.WriteLine($"  timestamp=0x{(uint)output[0]:X8}  values=[{output[1]},{output[2]},{output[3]}]  valid={output[4]}");
        return 0;
    }

    // ── XML-Config laden & anzeigen (offline) ──────────────────────────────────
    private static int XmlDemo()
    {
        const string xml = """
            <device host-name="demo" type="example">
              <bus-interface-list><bus-interface network-id="1" name="en0">
                <telegram name="t1" com-id="1000" data-set-id="1000">
                  <pd-parameter cycle="100000" marshall="on"/>
                  <destination id="1" uri="239.1.1.1"/>
                </telegram>
              </bus-interface></bus-interface-list>
              <data-set-list>
                <data-set name="Sensor" id="1000">
                  <element name="timestamp" type="UINT32"/>
                  <element name="values" type="INT16" array-size="3"/>
                  <element name="valid" type="BOOL8"/>
                </data-set>
              </data-set-list>
            </device>
            """;

        TrdpXmlConfig cfg = TrdpXmlConfig.Parse(xml);
        Console.WriteLine($"Geraet: host={cfg.HostName} type={cfg.DeviceType}");
        foreach (var tlg in cfg.Telegrams)
        {
            Console.WriteLine($"  Telegramm '{tlg.Name}': comId {tlg.ComId} -> dataset {tlg.DataSetId}, " +
                              $"cycle {tlg.CycleTimeUs} us, marshall={tlg.Marshall}");
            foreach (var d in tlg.Destinations) Console.WriteLine($"    -> {d.Uri}");
        }
        foreach (var ds in cfg.DatasetList)
        {
            Console.WriteLine($"  Dataset {ds.Id}: {ds.Elements.Count} Elemente, " +
                              $"feste Groesse {new TrdpMarshaller(cfg.Datasets).ComputeFixedSize(ds)} Bytes");
        }
        return 0;
    }

    private static string Arg(string[] a, int i, string def) => a.Length > i ? a[i] : def;

    private static int Help()
    {
        Console.WriteLine(
            "TRDP.NET Beispiele:\n" +
            "  dotnet run -f net10.0 -- pd-pub  [destIp] [comId] [cycleMs]\n" +
            "  dotnet run -f net10.0 -- pd-sub  [bindIp] [comId]\n" +
            "  dotnet run -f net10.0 -- md-server [port] [comId]\n" +
            "  dotnet run -f net10.0 -- md-client [destIp] [port] [comId]\n" +
            "  dotnet run -f net10.0 -- marshal\n" +
            "  dotnet run -f net10.0 -- xml");
        return 0;
    }
}
