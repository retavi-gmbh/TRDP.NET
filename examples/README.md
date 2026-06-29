# TRDP.NET — Examples

Runnable demos for TRDP.NET, bundled as a single console tool with sub-commands
(analogous to TCNOpen's `sendHello` / `receiveHello`).

```bash
cd examples/Trdp.Net.Examples
# locally: run on the installed runtime (net10.0); on a net8 box use -f net8.0
dotnet run -f net10.0 -- <command> [args]
```

| Command | What it does |
|---------|--------------|
| `pd-pub  [destIp=239.1.1.1] [comId=1000] [cycleMs=100]` | cyclic **PD publisher** |
| `pd-sub  [bindIp=0.0.0.0] [comId=1000]` | **PD subscriber** (prints received telegrams, timeouts) |
| `md-server [port=17225] [comId=2000]` | **MD listener** that replies `ACK: <req>` |
| `md-client [destIp=127.0.0.1] [port=17225] [comId=2000]` | **MD request** → prints the reply |
| `marshal` | typed **dataset** marshal/unmarshal (offline, prints the wire bytes) |
| `xml` | load a **TRDP XML config** and print telegrams/datasets (offline) |

## Offline demos (no network)

```bash
dotnet run -f net10.0 -- marshal
dotnet run -f net10.0 -- xml
```

## PD loopback (two terminals)

```bash
# terminal 1 — subscriber on a loopback alias
dotnet run -f net10.0 -- pd-sub 127.0.0.2 1000
# terminal 2 — publisher to that alias
dotnet run -f net10.0 -- pd-pub 127.0.0.2 1000 100
```

## Interop with the TCNOpen C reference

Build the reference (see the repo root README), then mix and match — TRDP.NET and
the C stack speak the same wire format:

```bash
# C reference sends, TRDP.NET receives:
./sendHello -o 127.0.0.1 -t 127.0.0.2 -c 1000 -s 100000 -d "Hi"
dotnet run -f net10.0 -- pd-sub 127.0.0.2 1000

# TRDP.NET sends, C reference receives:
./receiveHello -o 127.0.0.2 -c 1000
dotnet run -f net10.0 -- pd-pub 127.0.0.2 1000 100
```

> Note: on one host, two TRDP endpoints both binding UDP 17224 should use distinct
> loopback addresses (127.0.0.1 vs 127.0.0.2) to avoid delivery ambiguity.
