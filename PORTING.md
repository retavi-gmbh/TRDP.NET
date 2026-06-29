# Porting map: TCNOpen TRDP (C) → TRDP.NET (C#)

Living document tracking the port of the TCNOpen TRDP "Light" stack to managed C#.
Upstream reference: TCNOpen on SourceForge — https://sourceforge.net/projects/tcnopen/ (MPL-2.0).

## Principles

1. **Wire-faithful where it counts.** Frame layouts, FCS, sequence/topo handling,
   timeouts and state machines are ported 1:1 so on-the-wire behavior is identical.
   Validated against the original (e.g. CRC of `"123456789"` = `0xCBF43926`) and,
   where possible, against real captures (Wireshark TRDP dissector).
2. **VOS → BCL, not line-by-line.** The VOS layer (`vos_sock`, `vos_thread`,
   `vos_mem`) is platform glue. In .NET it maps onto `System.Net.Sockets`,
   threads/tasks and GC — so VOS is reimplemented as a thin managed shim, not a
   literal transliteration of pointer/pool code.
3. **No safety.** The `SDTv2/` subtree (IEC 61375-2-3 Annex B) is out of scope.
4. **Every ported file keeps the MPL header** and cites its C origin.

## Status

| C source | C# target | Status |
|----------|-----------|--------|
| `vos/common/vos_utils.c` (`vos_crc32`, `fcs_table`) | `Vos/VosCrc32.cs` | ✅ done |
| `api/trdp_types.h` (`TRDP_ERR_T`) | `Vos/VosTypes.cs` | ✅ done (errors) |
| `api/iec61375-2-3.h`, `common/trdp_private.h` (defines) | `Core/TrdpConstants.cs` | ✅ ports/versions/msgTypes |
| `common/trdp_private.h` (`PD_HEADER_T`) | `Core/PdHeader.cs` | ✅ done |
| `common/trdp_private.h` (`MD_HEADER_T`) | `Core/MdHeader.cs` | ✅ done |
| `api/trdp_types.h` (config/structs rest) | `Core/TrdpTypes.cs` | ⬜ todo |
| `vos/api/vos_sock.h`, `posix/vos_sock.c` | `Vos/VosSock.cs` | ✅ done (BCL UDP shim) |
| `vos/api/vos_thread.h`, `posix/vos_thread.c` | (drop — loop-driven `Process()`) | ✅ n/a |
| `vos/common/vos_mem.c` | (drop — use GC) | ✅ n/a |
| `common/trdp_pdcom.c` (PD framing/recv/timeout) | `Pd/PdPublisher.cs`, `Pd/PdSubscriber.cs` | ✅ done (core) |
| `common/tlp_if.c` (PD API) | `Pd/TrdpPdSession.cs` | ✅ done (publish/subscribe/put/get/process) |
| `common/trdp_pdindex.c` (HIGH_PERF index) | — | ⬜ later (perf opt) |
| `common/trdp_mdcom.c` (MD framing/recv/dispatch, TCP reassembly) | `Md/MdSession.cs` + `Md/Md*.cs` + `Vos/VosTcp.cs` | ✅ UDP **and TCP** |
| `common/tlm_if.c` (MD API) | `Md/MdSession.cs` | ✅ notify/request/reply/replyQuery/confirm/listener (UDP+TCP) |
| `vos/.../vos_sock.c` (TCP listen/accept/connect) | `Vos/VosTcp.cs` | ✅ non-blocking shim |
| `common/tlc_if.c` (session/lifecycle) | `Core/TrdpSession.cs` | ⬜ todo |
| `common/trdp_utils.c` (helpers) | `Core/TrdpUtils.cs` | ⬜ todo (as needed) |
| `common/trdp_stats.c` | `Core/TrdpStats.cs` | ⬜ todo |
| `common/tau_marshall.c` | `Marshalling/Trdp*.cs` | ✅ scalars/arrays/nested **+ variable-size arrays** (value-based) |
| `common/tau_xml.c`, `trdp_xml.c` | `Xml/TrdpXmlConfig.cs` | ✅ datasets/telegrams/com-parameters (System.Xml.Linq) |
| `SDTv2/**` | — | ❌ excluded (safety) |
| `common/tau_tti.c`, `tau_dnr.c`, `tau_ctrl.c`, `tau_so_if.c` | — | ⬜ later (beyond core) |

## Suggested order

1. **VOS shim** (`VosSock`, `VosThread`) — everything depends on it.
2. **PD path** (`trdp_pdcom` + `tlp_if`) — highest value, simplest: cyclic publish/subscribe.
3. **MD path** (`trdp_mdcom` + `tlm_if`) — request/reply, sessions, UDP+TCP.
4. **Marshalling** (`tau_marshall`) — typed dataset encode/decode.
5. **XML config** (`tau_xml`) — load comId/dataset/interface config.

## Notes / gotchas found while porting

- **FCS endianness quirk:** the header `frameCheckSum` is stored **little-endian**
  on the wire (`MAKE_LE` in the original), while every other header field is
  big-endian. `PdHeader`/`MdHeader` handle this; see the LE round-trip test.
- **PD has no dataset CRC:** `trdp_packetSizePD = sizeof(PD_HEADER_T) + dataSize`
  (40 + data, no trailing FCS, no padding). Only the header is CRC-protected.
- **Threading dropped:** the C VOS thread/select loop is replaced by a loop-driven
  `TrdpPdSession.Process()` (call from the app/ExecutionLoop) — matches retavi's
  synchronous I/O model.
- **MD data padded to 4 bytes** (`trdp_packetSizeMD`), `datasetLength` stays the
  unpadded size. MD has only a header FCS (no data CRC), same LE quirk as PD.
- **MD replies go to the request's source IP *and* source port** (not the fixed MD
  port) — otherwise ephemeral-port clients never receive the reply.
- **MD/TCP** is ported (non-blocking listener/connect + stream reassembly via
  `datasetLength`). Open MD items: server-side confirm-timeout supervision and
  request retries.
- **Marshalling is value-based, not struct-based:** the C marshaller's *source* side
  walks a natively-aligned host C struct (`alignConstPtr`/`ALIGNOF`). That has no
  managed equivalent and is intentionally dropped — TRDP.NET marshals a flat value
  list instead. Only the *wire* side is ported 1:1 (packed, big-endian).
- **Variable-size arrays** are ported: the count comes from the value of the
  preceding scalar element (`var_size = *pSrc`), modelled with a `varSize` carried
  through the value-walk. Open marshalling item: CHAR8/UTF16 string helpers.

## Application/utility layer (TAU) — ported

| C source | Purpose | Status |
|----------|---------|--------|
| `common/tlc_if.c` | unified session lifecycle (PD+MD) | ✅ `Core/TrdpSession.cs` |
| `common/trdp_stats.c` | statistics counters | ✅ `Core/TrdpStatistics.cs` (+ PD/MD counters) |
| `common/tau_tti.c` (~2600) | train topology information | ✅ `Tau/Tti/*` (18 API fns; SC-32 + unsubscribe = TODO) |
| `common/tau_dnr.c` (~1360) | TCN-DNS name resolution | ✅ `Tau/Dnr/*` (reverse-lookup = tbd as in C) |
| `common/tau_ctrl.c` | ECSP control interface | ✅ `Tau/Ctrl/*` (unpublish = TODO) |
| `common/tau_so_if.c` | service-oriented interface | ✅ `Tau/SoIf/*` |

| `common/tau_xmarshall.c` | XML/comId-driven marshalling | ✅ `Tau/XMarshall/*` (wrapper over TrdpMarshaller) |
| `common/tau_xsession.c` | session-from-XML helper | ✅ `Tau/XSession/*` (PD publishers/subscribers per telegram) |
| `common/trdp_pdindex.c` | HIGH_PERF indexed PD send timer | ✅ `Pd/Index/PdSendIndex.cs` (equivalence-tested vs. linear) |

Cross-cutting items closed since: `tlp_unsubscribe`/`tlp_unpublish`/`tlm_delListener`
added to the PD/MD sessions and wired into `TauCtrl`/`TauTti` deinit; MD header
topo-counters populated; CHAR8/UTF16 string helpers (`TrdpStrings`); `Session.Port`
now reports the actually-bound port.

### Deliberately excluded — safety only
- **SDTv2** (IEC 61375-2-3 Annex B) and its **SC-32** safety checksum (`vos_sc32`):
  out of scope by design; this library is **not** safety-certified.

### Non-safety gaps — now closed
- ✅ **DNR/URI resolution wiring:** `TauXSession.Load/LoadXml` take an optional
  `Func<string,IPAddress?> uriResolver` (e.g. `uri => dnr.IpFromUri(uri)`); numeric
  IPs still parse directly.
- ✅ **Value-based `CalcDatasetSize`:** `TrdpMarshaller.ComputeSize(ds, values)` is
  public and handles variable arrays; `TauXMarshall.CalcDatasetSize[ByComId](id, values)`.
- ✅ **Source-URI lists:** XML `<source>` parsed into `TrdpXmlTelegram.Sources`;
  `TauXSession` applies the first resolvable source URI as the subscriber's filter.
- ✅ **`cycle_*` variants:** `TauXSession.ProcessFor(ms)` / `ProcessUntil(cond, timeout)`.

### Genuinely N/A in managed code
- `tau_xmarshall_map` per-target type/alignment mangling: no managed equivalent —
  values are typed CLR objects, not a raw host struct buffer (the only thing that
  needed local re-alignment in C).

**Net result: everything except the SDTv2 safety layer (and its SC-32 checksum) is ported.**

## Conformance review vs. the C reference

A line-by-line review against the TCNOpen C source surfaced the following. **Fixed**
(with regression tests):

- **PD: 4-byte data padding** (`trdp_packetSizePD`) — PD frames are now padded to a
  4-byte boundary like MD; `datasetLength` stays unpadded.
- **PD: receive sequence logic** — only a publisher restart (`seq==0`) or a real forward
  step (`seq>curSeqCnt`) is accepted; old/re-ordered/duplicate telegrams are dropped and
  the counter no longer runs backward (`trdp_pdcom.c:908-916`).
- **PD + MD: protocol-version check** now masks the high byte (`& 0xFF00`,
  `TRDP_PROTOCOL_VERSION_CHECK_MASK`) so future `0x01xx` minor versions are accepted.
- **PD + MD: topo-counter validation** on receive (`trdp_validTopoCounters`): expected
  counter `0` = accept any, otherwise the frame's counter must match.
- **MD: infinite reply-timeout** is encoded on the wire as `0` (not the API sentinel
  `0xFFFFFFFF`) — a replier reads `0 && Mr` as infinite (`tlm_if.c:340`, `trdp_mdcom.c:1781`).
- **MD: confirm (Mc)** goes to the fixed MD UDP port (the replyPort special-case applies
  only to Mp/Mq, `trdp_mdcom.c:2343-2356`).
- **MD: listener dispatch** stops at the first matching comId listener (`break`).

**Verified correct (no change needed):** CRC/FCS algorithm + table, PD/MD header
sizes/offsets/endianness (FCS LE quirk), all constants/msgTypes, `TRDP_ERR_T`, and **every
audited TAU wire struct** (SRM 64 B, ECSP 40/40/40/1556 B, OP_VEHICLE 24 B, PD100 72 B,
TCN_URI 92 B, …). Marshalling wire sizes incl. TIMEDATE48/64.

**False positive (verified, NOT changed):** a reviewer claimed the initial PD sequence
counter is `0xFFFFFFFF` (first telegram `seq==0`). The C `curSeqCnt` is zero-initialized
(no such assignment exists) and pre-incremented → first telegram `seq==1`, exactly as the
port. Left unchanged.

**Deliberate deviations (documented, not bugs):**
- PD pull (Pr/Pp/Pe), `toBehavior` zero-on-timeout, and per-(source,comId) sequence
  tracking are not implemented (single-stream supervision; pass a source filter for
  multi-publisher comIds).
- MD: server-side confirm handling/timeout, request retries, `Me`-on-no-listener,
  listener URI/IP/MC filtering, TCP idle-connection cleanup, and `confirmTimeout` carried
  in the Mq header are not (yet) ported.
- PD subscribe `timeoutMs==0` means *no* supervision (managed-API choice); the C default
  of 100 ms applies only when you pass it explicitly.

## Validation backlog

- [x] Loopback interop within the library (PdPublisher → UDP → PdSubscriber).
- [x] **Live interop against the TCNOpen C reference** (built locally on Linux):
      `sendHello` → TRDP.NET `PdSubscriber` received & parsed 50 PD telegrams
      (comId/seq/payload correct, header FCS accepted → little-endian quirk confirmed
      against the reference). Send direction transitively covered (same FCS routine).
- [ ] Observe TRDP.NET → reference receive directly (blocked only by the demo's
      buffered stdout; use Wireshark + TRDP dissector for an independent view).
- [ ] Validate MD request/reply and a marshalled dataset against the reference.
- [ ] Round-trip a dataset through `tau_marshall` C output vs. C# output.
- [ ] Interop test: C# publisher ↔ TCNOpen subscriber and vice versa.
