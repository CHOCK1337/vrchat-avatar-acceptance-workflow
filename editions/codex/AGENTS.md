# Codex edition package

This repository is the distribution source for Avatar Workbench, not a user's Avatar project. Read `docs/AGENT_GUIDE.md` and `CODEX_RULES.md` for user-facing integration. The first user request defines scope; installing the package does not authorize editing an Avatar or sending a chat message.

Preserve source Avatars, user configuration, existing Skill, tasks and feedback. Do not alter global model, permission, login or MCP settings to make setup appear successful. Prefer project-scoped configuration and report exact conflicts. Do not start independent agents or new chats without the user's request.

`local/` contains private test/install data and is excluded from releases. `pack.py --full` produces an allowlisted clean ZIP plus MANIFEST and SHA-256. When changing Unity payload files, update `unity/AVATAR_WORKBENCH_MANIFEST.json` and preserve prior released hashes as accepted baselines. Run relevant tests in `verification/`, then verify the actual extracted ZIP.

Do not conflate installer tests, MCP discovery, Unity compile, desktop session delivery, menu operation, visible acceptance, SDK build or VRChat client results. Record each only at the level actually tested.
