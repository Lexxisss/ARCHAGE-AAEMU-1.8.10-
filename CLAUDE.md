# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

This is a fork of the [AAEmu](https://github.com/AAEmu/AAEmu) ArcheAge server emulator (Login server + Game server, written in C#/.NET 8), with a heavy, targeted patch applied to the **skill/buff system** for client version **1.8.1.0 Kakao KR**. The patch was produced by reverse-engineering the target client (`x2game.dll`, `x2game-dev.dll`, `x2game-dev_dedicate.dll`) and reconciling it against two SQLite reference databases.

`_skill_recovery_reports/` documents this patch (in Russian) and is the primary source of truth for *why* the skill system code looks the way it does:
- `SKILL_SYSTEM_REVERSE_REPORT_RU.md` — full reverse-engineering report: what was fixed, what protocol opcodes were confirmed/rewritten, and — critically — **section 9, "what is still not confirmed"** (e.g. `dynamic_unit_modifiers` interpolation semantics, `execute_effect_on_fire` staging, several unconfirmed opcodes like `SCSkillUpgraded`/`CSBuyPriestBuff` which must stay at `0xFFF` and not be registered/sent).
- `skill_protocol_map.json` — machine-readable map of C2S/S2C skill/buff packet opcodes with `status` (`confirmed`/etc.) and `source_modified` flags. Check this before touching packet opcodes or field order in `Core/Packets/**/CS*Skill*`, `SC*Skill*`, `SC*Buff*`.
- `skill_database_audit.json` — diff/audit between the two reference SQLite databases (row/field counts per table).
- `VALIDATION.md` / `VALIDATION_OUTPUT.txt` — what has and hasn't actually been verified. Static checks (brace/string balance, opcode presence, SQLite hashes) were done; **`dotnet build`, unit/integration tests, and a real client network run were not** at patch-authoring time. Treat skill/buff behavior as unverified until built and tested.

**When modifying anything under `Models/Game/Skills/`, `Core/Managers/SkillManager.cs`, `Core/Managers/FormulaManager.cs`, `Utils/DB/SkillSQLiteCatalog.cs`, or the skill/buff packets in `Core/Packets/`, read the relevant section of the reverse report first** — there is a specific, evidenced reason for most of the non-obvious logic there (e.g. no artificial cast-start delay, reagents validated before mana/cooldown are spent, relation resources consumed once per effect-relation rather than per AOE target, channeling cancel not double-freeing `TlId`).

## Data dependencies for the skill system

`SkillManager` and `FormulaManager` no longer read formulas/skill data from `compact.sqlite3`. They use `SQLite.CreateSkillConnection()` (`Utils/DB/SQLite.cs` → `SkillSQLiteCatalog.Create`), which builds an in-memory, read-only virtual catalog over two files that must be placed in `AAEmu.Game/Data/` (not included in the repo):
- `1.8.1.0-Kakao-KR.sqlite` (primary)
- `base.sqlite3` (fallback — supplies only missing/NULL fields and rows absent from the primary)

Both are opened `mode=ro&immutable=1`; the catalog is `PRAGMA query_only = ON` after construction. The server's main SQLite/MySQL connections are untouched by this — only `SkillManager`/`FormulaManager` use the skill catalog. Startup should log `Skill SQLite catalogue ready`; its absence means these two files are missing or unreadable.

## Build

Requires .NET 8 SDK (`global.json` pins `8.0.418`, roll-forward `latestPatch`). Despite `Docs/getstarted.md` referencing .NET 6, the actual `TargetFramework` across all projects is `net8.0`.

```bash
dotnet restore AAEmu.sln
dotnet build AAEmu.sln -c Release
```

Solution projects: `AAEmu.Commons` (shared/network/DB primitives), `AAEmu.Login` (login server), `AAEmu.Game` (game server — the bulk of the code), `AAEmu.UnitTests`, `AAEmu.IntegrationTests`.

## Tests

xUnit + Moq.

```bash
dotnet test AAEmu.UnitTests/AAEmu.UnitTests.csproj
dotnet test AAEmu.IntegrationTests/AAEmu.IntegrationTests.csproj
dotnet test AAEmu.UnitTests/AAEmu.UnitTests.csproj --filter "FullyQualifiedName~SomeTestClass.SomeTestMethod"
```

## Running locally

Both servers read config from `Config.json` (copy from `exampleconfig.json` next to the built binary, e.g. `AAEmu.Game/bin/Debug/net8.0/`). Requires a MySQL 8 instance with the `aaemu_game` and `aaemu_login` schemas (see `SQL/`, apply `SQL/updates/*` in date order), plus an ArcheAge 1.2-era client's `game_pak` referenced from `AAEmu.Game/bin/.../Configurations/ClientData.json`, plus the two skill-system SQLite files above in `AAEmu.Game/Data/`. Full walkthrough in `Docs/getstarted.md` (note: MySQL/db setup portions are accurate; .NET version portion is stale — use .NET 8).

Convenience scripts: `Scripts/StartLoginServer.bat`, `Scripts/StartGameServer.bat` / `Scripts/start AAEmu.Login.bat` / `start AAEmu.Game.bat` (Windows), `Scripts/start.sh` (`docker-compose up --build -d`), `Scripts/stop.sh`, `Scripts/stop-reset-db.sh`, `Scripts/clear-caches.sh`.

Docker: `docker-compose.yaml` builds `login`, `game`, and a `db` (MySQL 8.0.12) + `adminer` (localhost:8080) container. Env vars come from `.env` (`BUILD_CONFIGURATION`, `BUILD_FRAMEWORK`, `BUILD_RUNTIME`, `DB_USER`/`DB_PASSWORD`). `game` mounts `AAEmu.Game/bin/.../ClientData` and `Data` read-only into the container, so those must be built/populated on the host first. `Docs/docker-help.txt` covers making a character GM via Adminer (`characters.access_level = 100`).

## Architecture (AAEmu.Game)

- **Managers** (`Core/Managers/`) are process-wide singletons via `Singleton<T>` (e.g. `SkillManager.Instance`, `QuestManager.Instance`). They own loading of static/reference data (from SQLite or MySQL) and runtime coordination for one subsystem each (skills, quests, items, housing, factions, teams, duels, etc.). Most game logic flows through a manager rather than directly between game objects.
- **Packets** (`Core/Packets/`) are organized by direction: `C2G`/`C2S` (client→game), `G2C`/`S2C` (game→client), `G2L`/`L2G` (game↔login server), `Stream` (the separate stream-network port), `Proxy`. Each packet is a `GamePacket` subclass constructed with an opcode from `CSOffsets`/`SCOffsets` and implements `Read`/`Write` against a `PacketStream`. When adding or changing a packet, get the opcode and wire field order right first — see the skill-system caveat above for why this matters.
- **Models/Game/** mirrors the game's domain areas (Skills, Items, Units, Quests, Housing, Factions, Char, NPChar, Slaves, Trading, Auction, Duels, Indun (instances), TowerDefs, etc.) — data/behavior classes used by the managers and packet handlers.
- **GM/console commands** live in `Scripts/Commands/` (one file per command, e.g. `AddBuff.cs`, `Damage.cs`, `BuildHouse.cs`) and are picked up via `CommandManager`; `Scripts/SubCommands/` holds subcommand groups.
- **Two databases**: a read-only reference database (originally `compact.sqlite3`; for the skill system this is now the two-file `SkillSQLiteCatalog` described above) holds ArcheAge's static game data; a read/write MySQL database (`aaemu_game` + `aaemu_login` schemas) holds all player/world state. `AAEmu.Login` only talks to `aaemu_login`+account data; `AAEmu.Game` uses both databases.
- **AAEmu.Commons** holds cross-cutting infrastructure shared by both servers: networking primitives (`Network/`), `Singleton<T>`, DB helpers, crypto, the `.aapak`/AAPak client-archive reader (`Utils/AAPak`), config/XML utilities.

## Conventions

- Standard `.editorconfig`-enforced C# style (4-space indent, PascalCase for public/static members) — no project-specific deviations beyond the defaults.
- Commit messages should describe what the commit *does* to the code (present tense), not what you did — see `CONTRIBUTING.md`.
