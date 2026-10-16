#!/usr/bin/env bash
# Publishes the project to GitHub: creates the repository, pushes the sources
# and uploads the APK as a release asset.
#
# Needs a token in ~/.github_token with the "public_repo" scope.
#   https://github.com/settings/tokens/new
#
set -euo pipefail

TOKEN_FILE="$HOME/.github_token"
REPO="BrainshEditor"
TAG="v1.0.0"

cd "$(dirname "$0")"

[ -f "$TOKEN_FILE" ] || { echo "нет файла $TOKEN_FILE"; exit 1; }
TOKEN=$(cat "$TOKEN_FILE")
[ -n "$TOKEN" ] || { echo "файл с токеном пуст"; exit 1; }

api() { curl -sS -H "Authorization: Bearer $TOKEN" -H "Accept: application/vnd.github+json" "$@"; }

echo "=== кто мы ==="
LOGIN=$(api https://api.github.com/user | python3 -c "import sys,json; print(json.load(sys.stdin)['login'])")
echo "логин: $LOGIN"

echo "=== репозиторий ==="
if api -o /dev/null -w "%{http_code}" "https://api.github.com/repos/$LOGIN/$REPO" | grep -q 200; then
    echo "уже существует, пропускаем создание"
else
    api -X POST https://api.github.com/user/repos \
        -d "{\"name\":\"$REPO\",\"description\":\"Редактор кода для Android с запуском программ на языке Brainsh\",\"private\":false,\"auto_init\":false}" \
        | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('full_name', d))"
fi

echo "=== коммит и тег ==="
git remote remove origin 2>/dev/null || true
git remote add origin "https://x-access-token:$TOKEN@github.com/$LOGIN/$REPO.git"
git branch -M main
git tag -f "$TAG" -m "Brainsh Editor 1.0" >/dev/null
git push -u origin main --quiet
git push origin "$TAG" --quiet
echo "код запушен"

echo "=== релиз $TAG ==="
python3 - <<'PY' > /tmp/brainsh_release.json
import json
notes = open("release/RELEASE_NOTES.md", encoding="utf-8").read()
json.dump({
    "tag_name": "v1.0.0",
    "name": "Brainsh Editor 1.0",
    "body": notes,
    "draft": False,
    "prerelease": False,
}, open("/tmp/brainsh_release.json", "w", encoding="utf-8"))
PY

UPLOAD=$(api -X POST "https://uploads.github.com/repos/$LOGIN/$REPO/releases/$TAG/assets?name=BrainshEditor-1.0.apk" \
    --data-binary "@release/BrainshEditor-1.0.apk" \
    -H "Content-Type: application/vnd.android.package-archive")

echo "$UPLOAD" | python3 -c "
import sys, json
d = json.load(sys.stdin)
print('релиз готов:', d.get('html_url', d))
"

rm -f "$TOKEN_FILE" && echo "файл с токеном удалён"