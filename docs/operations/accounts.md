# Accounts and character selection

Build from the repository root and start the dedicated server before the client.

```powershell
dotnet restore Arvenok.sln --locked-mode
dotnet build Arvenok.sln --configuration Debug --no-restore
godot --headless --editor --path apps/game --quit
godot --headless --path apps/game -- --server
# In a second terminal
godot --path apps/game
```

Register a username with 3–32 ASCII letters, digits or underscores, starting with a letter. Passwords require 12–128 characters. Password fields are masked by default. Login opens six character slots. Create a Warrior, Mage or Archer with a globally unique nickname of 3–16 ASCII letters, digits or underscores, starting with a letter. Nicknames are unique regardless of case. Administrative names are reserved.

Select a card and enter the world. The server restores that character's class, progression, items, equipment, skills and last saved walkable position. Characters retain separate hotbar preferences on this computer. Character selection in the world menu saves and despawns the current character. Logout also revokes its account session. A second connection can log in to the same account, but only one character from that account can enter the world.

Delete character opens a confirmation identifying the character and its permanent data loss. Type the exact nickname. Deletion requires ownership and an offline character. The transaction removes the full owned aggregate, frees the slot and allows the nickname to be reused. Old character IDs cannot restore deleted state.

## Storage and transport

The database is `.local/server/<shard>/accounts.sqlite3`. SQLite WAL and FULL synchronization protect committed account, character, item and command receipt transactions. Stop the server before copying the database and its sidecars for backup, or use SQLite's online backup API. Keep the server directory private. The repository's existing character data validation and inventory receipt rules apply to SQLite saves.

The local server generates `.local/tls/localhost.crt` and `localhost.key` on first start. Local clients explicitly trust that certificate. It expires after one year. Stop the server and replace both files together when rotating the development certificate, then restart clients. Never share the private key with clients.

The default transport is verified TLS WebSockets on TCP port 7777. Every account request and gameplay RPC uses that connection. Account identity is held on the server against the connection, with a four-hour expiry. Logout and disconnect revoke it. Passwords and session credentials are excluded from client preference files. Password hashes use PBKDF2-HMAC-SHA256 with 600,000 iterations and individual random salts.

For a remote server, set `ARVENOK_TLS_CERT` to its PEM certificate chain and `ARVENOK_TLS_KEY` to its PEM private key before launching with the desired `--bind`. The certificate must match the client `--host`. Clients use platform certificate validation, or an explicitly supplied PEM trust certificate through `ARVENOK_TLS_CA`. Hostname verification always applies. Public hosting still requires its actual certificate, access controls and measured Internet testing.

Failed connections show an unavailable/TLS message. Use Reconnect, then log in. A request timeout closes the connection so late replies cannot enter the world. Reconnect and inspect the character list to determine whether a timed-out creation or deletion committed.

## Legacy development records

Anonymous JSON characters and their resume-token indexes are obsolete. They are never imported into accounts. The account server does not read them for admission. The three records found during this integration were backed up under `.local/backups/legacy-20261121-031323`, with a dry-run manifest containing IDs, SHA-256 checksums and dependent record counts. They contain 26 inventory stacks, two equipped items and three recipe stacks.

Stop the server to take a fresh backup and preview.

```powershell
./tools/legacy-characters.ps1 -Backup
```

Review the manifest and obtain explicit approval for the listed IDs before applying cleanup. The operation moves only those verified ownerless character aggregates and their old credential indexes into the backup's quarantine directory. The account database is outside this operation. A repeated run moves zero characters.

```powershell
./tools/legacy-characters.ps1 -Quarantine -Manifest .local/backups/legacy-YYYYMMDD-HHMMSS/dry-run.json
```

The script accepts only the repository's development server directories, takes the exclusive server lock, verifies that source hashes still match the backup and preserves unrelated records. If interrupted, rerun with the same approved manifest to finish credential-index quarantine. Permanent disposal of the backup is a separate deliberate action.

## Verification

```powershell
./tools/validate.ps1
./tools/validate.ps1 -Configuration Release -UpdateLocks
./tools/smoke-game.ps1
./tools/smoke-accounts.ps1 -Capture
./tools/test-legacy-cleanup.ps1
```

The account tests cover password hashing, case normalization, concurrent nickname allocation, concurrent sixth-slot creation, ownership, command replay, deletion, foreign keys, saved advanced skills/equipment and restart restoration. The native account scenario drives two accounts plus a competing connection through registration, six slots, foreign-ID rejection, shared movement, character switching, exact deletion confirmation, logout and restart. It drops creation and deletion acknowledgements, reconnects and retries the same command IDs to verify exactly one result. It separately rejects plaintext WebSockets, an untrusted certificate and a mismatched hostname. Captures show Login and six occupied slots.

Debug and Release passed 117 regression checks with zero compiler warnings or errors. The TLS world run compared 18 matching visual effects and 26 matching wandering-monster snapshots. Cleanup tests used isolated synthetic records, preserved an account-owned record and its index, verified the backup and moved zero records on the second run. Cleanup of the three original development characters remains pending explicit approval.

The world smoke now registers accounts and selects characters over TLS before running its existing movement, combat, animation and monster replication assertions. Earlier inventory, economy, progression and skill-specific native scripts still use legacy JSON fixture setup and need adaptation before reuse with account storage. Their engine-independent regression coverage continues in validate.ps1.

Tests run locally. Internet latency, load and exported clients require separate verification. The sandboxed Windows engine reports a root certificate store diagnostic, while explicit local certificate validation and its rejection tests run successfully.
