# TRDP.NET

**A pure C# / .NET port of the TCNOpen TRDP "Light" stack — the Train Real-time Data Protocol (TRDP), IEC 61375-2-3.**

Process Data (PD) and Message Data (MD) over UDP/TCP, no native dependencies, runs anywhere .NET runs (x64 / ARM, Windows / Linux — including industrial PCs and the Kunbus Revolution Pi).

> **Status: 🚧 alpha.** The complete TRDP "Light" stack **and** the full TAU application layer are ported and unit-tested (**125 tests**): FCS, PD/MD headers, VOS UDP+TCP, **PD publish/subscribe**, **MD over UDP+TCP**, **marshalling** (scalars/fixed+variable arrays/nested/strings), **XML config**, unified session, statistics, the TAU services (TTI, DNR, ECSP control, service interface), XML/comId marshalling, session-from-XML, and the indexed PD send timer. Validated live against the TCNOpen C reference. **Everything except the SDTv2 safety layer is ported** — see [`PORTING.md`](PORTING.md).

---

## Why

TRDP is the standard real-time data protocol for modern train networks (TCN / ECN / ETB). The reference implementation [TCNOpen TRDP](https://sourceforge.net/projects/tcnopen/) is excellent but written in C for embedded targets. **TRDP.NET brings it to managed .NET** — no P/Invoke, no native build per platform, directly consumable from C# applications and frameworks.

## Quickstart (Process Data)

```csharp
using System.Net;
using Trdp.Net.Pd;

// One session per network interface; drive it from your main/control loop.
using var session = new TrdpPdSession(bindAddress: IPAddress.Any);

// Publish comId 1000 every 100 ms to a destination (unicast or multicast).
var pub = session.Publish(comId: 1000, destIp: IPAddress.Parse("239.1.1.1"), cycleTimeMs: 100);
pub.SetData(new byte[] { 0x01, 0x02, 0x03, 0x04 });

// Subscribe to comId 2000 with a 1 s timeout supervision.
var sub = session.Subscribe(comId: 2000, timeoutMs: 1000);
sub.DataReceived += s => Console.WriteLine($"got {s.LastData!.Length} bytes, seq {s.SequenceCounter}");
sub.Timeout += _ => Console.WriteLine("comId 2000 timed out");

while (running)
{
    session.Process();      // sends due telegrams, receives & dispatches, checks timeouts
    Thread.Sleep(5);
}
```

The loop-driven `Process()` model (no internal threads) maps cleanly onto a PLC-style
cyclic executor.

## Scope

| Area | Status |
|------|--------|
| FCS / CRC-32 (`vos_crc32`) | ✅ ported + tested |
| PD / MD headers (wire format) | ✅ ported + tested |
| Error model (`TRDP_ERR_T`) | ✅ ported |
| VOS socket layer (UDP, BCL shim) | ✅ ported |
| Process Data (PD) publish/subscribe | ✅ core (cyclic send, receive, seq check, timeout) |
| Message Data (MD) over UDP | ✅ notify / request-reply / confirm / listener |
| Message Data (MD) over TCP | ✅ non-blocking listener/connect + stream reassembly |
| Marshalling (`tau_marshall`) | ✅ scalars, fixed & variable arrays, nested datasets |
| XML configuration (`tau_xml`) | ✅ datasets, telegrams, com-parameters |
| **SDTv2 safety layer** | ❌ **intentionally excluded** |

### Application utilities (TAU)
| Area | Status |
|------|--------|
| Unified session (`tlc_if`) + statistics | ✅ |
| Train Topology Info (`tau_tti`) | ✅ all 18 API fns (value-based wire parsing) |
| TCN-DNS (`tau_dnr`) | ✅ uri↔addr, label resolution |
| ECSP control (`tau_ctrl`) | ✅ set/get/confirm |
| Service interface (`tau_so_if`) | ✅ add/del/upd/list |
| XML/comId marshalling (`tau_xmarshall`) | ✅ marshal/unmarshal by comId |
| Session-from-XML (`tau_xsession`) | ✅ publishers/subscribers per telegram |
| Indexed PD send timer (`trdp_pdindex`) | ✅ optional, equivalence-tested |

> ⚠️ **Not safety-certified.** The SDTv2 safety layer (IEC 61375-2-3 Annex B) is deliberately **not** ported. Do **not** use TRDP.NET to implement safety-related (SIL) functions. See [`NOTICE`](NOTICE).

### Validation
Validated **live against the TCNOpen C reference** (built locally): the reference `sendHello`
publishes PD that TRDP.NET receives and decodes correctly (header, sequence, FCS). See the
"Validation backlog" in [`PORTING.md`](PORTING.md). For wire-level inspection use Wireshark
with the TRDP dissector (`trdp/spy` in TCNOpen).

## License

**Mozilla Public License 2.0 (MPL-2.0).** TRDP.NET is a source-code translation of the MPL-2.0-licensed TCNOpen TRDP stack and therefore — as a derivative work — remains under MPL-2.0. Original copyright: Bombardier Transportation Inc. or its subsidiaries and others, 2013-2021. See [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE).

Not affiliated with or endorsed by TCNOpen, NewTec GmbH, or the IEC. "TRDP" and "TCNOpen" are used descriptively to identify the implemented protocol.

## Build & test

```bash
dotnet test
```

Targets `net8.0`. No external runtime dependencies.

## Repository layout

```
src/Trdp.Net/           # the library
  Vos/                  # platform abstraction (BCL-backed): CRC, types
  Core/                 # wire types: constants, PD/MD headers
tests/Trdp.Net.Tests/   # xUnit tests, validated against the original stack
```

## Acknowledgements

This project would not exist without the [TCNOpen](https://sourceforge.net/projects/tcnopen/) initiative (hosted on SourceForge) and its contributors (notably Bernd Loehr, NewTec GmbH).
