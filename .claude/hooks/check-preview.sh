#!/usr/bin/env bash
# Stop hook: if Shared/Services/ToolCatalog.cs changed on this branch (vs origin/main, committed or not)
# but no Claude/Previews/*.html changed, ask Claude to update and open the preview Artifact.
# Fires once per stop (skips when stop_hook_active is true) so it can't loop.
input=$(cat)
if printf '%s' "$input" | grep -q '"stop_hook_active"[[:space:]]*:[[:space:]]*true'; then exit 0; fi
cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || exit 0
base=$(git merge-base HEAD origin/main 2>/dev/null) || exit 0
changed=$( { git diff --name-only "$base"; git ls-files --others --exclude-standard; } 2>/dev/null | sort -u)
printf '%s\n' "$changed" | grep -qx 'Shared/Services/ToolCatalog.cs' || exit 0
printf '%s\n' "$changed" | grep -q '^Claude/Previews/.*\.html$' && exit 0
cat <<'JSON'
{"decision":"block","reason":"ToolCatalog.cs changed on this branch but no Claude/Previews/*.html did. Per root CLAUDE.md (UI previews): update the tool's preview, publish it as an Artifact, and open it for the owner before finishing. If this change truly needs no preview, say why."}
JSON
