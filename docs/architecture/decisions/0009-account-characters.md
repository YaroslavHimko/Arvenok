# Account sessions and character ownership

The existing Godot session adapter owns connection lifetime and composes the same world simulation. Account screens run before world presentation is constructed. The server admits a character only after login and ownership validation.

Hosting implements SQLite through Microsoft.Data.Sqlite. Each character retains the existing serialized gameplay aggregate behind ICharacterRepository. An account foreign key, unique normalized nickname and unique account slot constrained to 0–5 enforce ownership and capacity. Command receipts commit with creation and deletion. Saves update existing rows and cannot recreate deleted records. Legacy JSON files remain outside the account database.

All Godot multiplayer traffic uses verified TLS WebSockets. This changes the peer transport from UDP ENet to TCP while retaining RPCs, snapshots and dedicated authoritative simulation. Login occurs on the encrypted connection and account identity stays bound to that connection. There are no transferable entry tickets or client token files. Disconnect and logout revoke the binding. TCP congestion and Internet latency require separate load testing.

Passwords use the maintained .NET PBKDF2-HMAC-SHA256 implementation with a random 16-byte salt, 600,000 iterations and constant-time comparison. Encoded hashes carry their algorithm and work factor. This avoids an additional native password library in the Godot host. Login uses generic credential errors and throttling. [OWASP guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)

Development TLS uses a generated localhost certificate trusted explicitly by the local client. Remote installations supply their own certificate and private key, and clients use normal certificate and hostname validation. No certificate validation bypass is supported. [Godot TLS transport](https://docs.godotengine.org/en/stable/classes/class_websocketmultiplayerpeer.html)
