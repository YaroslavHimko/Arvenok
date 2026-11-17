# Local operation

Build the root solution and import `apps/game/project.godot`. The scripts in `tools` resolve paths relative to themselves, so they work from any working directory. `run-game.ps1` accepts `-Mode client|server`, `-Godot <executable>` and `-SessionArguments @('--port','7778')`.

Configuration priority is command line, then environment, then default. Put application arguments after Godot's `--` separator.

| CLI | Environment | Default | Meaning |
| --- | --- | --- | --- |
| `--server` | none | client | Run authoritative session; dedicated-server exports also select this mode |
| `--host` | `ARVENOK_HOST` | `127.0.0.1` | Client destination, hostname or IP |
| `--bind` | `ARVENOK_BIND` | `127.0.0.1` | IPv4 interface to listen on |
| `--port` | `ARVENOK_PORT` | `7777` | UDP port, 1 through 65535 |
| `--region` | `ARVENOK_REGION` | `local` | Lowercase log label, up to 63 characters |
| `--shard` | `ARVENOK_SHARD` | `arena-001` | Lowercase log label, up to 63 characters |

Unknown/duplicate options or invalid values fail before a peer starts. A startup configuration error exits with code 2; a server listen failure exits with code 1. An interactive client keeps its status visible on connection failure. Current session capacity is fixed at two players by `ArenaRules`; raising it requires gameplay/replication tests and capacity measurements.

For a deliberate trusted-LAN test, run the server with `--server --bind 0.0.0.0`, allow UDP only from the test network, and connect with `--host <server-address>`. The loopback default does not accept remote clients. This transport has no account authentication, encrypted session admission or rate limits; production exposure is a later milestone.

`smoke-game.ps1` starts hidden headless server/client processes on an ephemeral loopback UDP port, waits for two replicated fighters on both clients, and terminates only processes it created. It writes logs under ignored `artifacts/smoke/<run-id>`. It checks startup and replication, not combat input, visual rendering or internet behavior. The separate regression harness covers combat rules.

If scripts are blocked by Windows execution policy, use the process-scoped invocation in the root README. In restricted automation, set `DOTNET_CLI_HOME` to a writable task-local cache before invoking dotnet; no global configuration change is needed. Godot's `.NET` executable is required; some renamed console wrappers expect a companion filename, so use the working main executable directly.

After a scene/script move, rebuild and run a Godot editor import to refresh generated metadata. Preserve script `.uid` files. Old generated root `.godot` caches can be removed manually after closing the old editor entry; they are not source files. If the port is occupied, choose another port consistently for server and clients. On disconnect, the client clears its displayed state; there is no automatic reconnect yet.
