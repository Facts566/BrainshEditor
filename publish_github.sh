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
TAG="${TAG:-v1.0.0}"
APK_NAME="${APK_NAME:-BrainshEditor-1.0.apk}"

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

# На сервере могут появиться коммиты, которых нет здесь: файл LICENSE, добавленный
# через веб-интерфейс GitHub, например. Без этого пункт push ниже будет отклонён
# как не ускоренный, а проглоченная ошибка продолжила бы публикацию вслепую.
# Свою историю не переписываем — уже опубликованные теги указывают на эти
# коммиты, поэтому сливаем.
git fetch origin main --quiet || true
if git rev-parse --verify --quiet origin/main >/dev/null &&
   ! git merge-base --is-ancestor origin/main HEAD; then
    git merge origin/main --quiet -m "Слияние с веткой main на GitHub"
    echo "ветка слита с сервером"
fi

# Ошибка пуша обязана останавливать скрипт: set -e и никаких "|| echo".
git push -u origin main --quiet
git tag -f "$TAG" -m "$TAG" >/dev/null
git push origin "$TAG" --force --quiet
echo "код запушен"

echo "=== релиз $TAG ==="
python3 - <<'PY' > /tmp/brainsh_release.json
import json, os
notes = open("release/RELEASE_NOTES.md", encoding="utf-8").read()
json.dump({
    "tag_name": os.environ.get("TAG", "v1.0.0"),
    "name": "Brainsh Editor " + os.environ.get("TAG", "v1.0.0").lstrip("v"),
    "body": notes,
    "draft": False,
    "prerelease": False,
}, open("/tmp/brainsh_release.json", "w", encoding="utf-8"))
PY

# создать релиз (идемпотентно: если уже есть — пропускаем)
if ! api "https://api.github.com/repos/$LOGIN/$REPO/releases/tags/$TAG" | grep -q '"id"'; then
    api -X POST "https://api.github.com/repos/$LOGIN/$REPO/releases" \
        -H "Content-Type: application/json" \
        --data-binary @/tmp/brainsh_release.json \
        | python3 -c "import sys,json; d=json.load(sys.stdin); print('релиз создан:', d.get('html_url', d))"
else
    echo "релиз $TAG уже существует"
fi

# загрузка вложений идёт по числовому id релиза, а не по тегу
RELEASE_ID=$(api "https://api.github.com/repos/$LOGIN/$REPO/releases/tags/$TAG" \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")
echo "id релиза: $RELEASE_ID"

# старое вложение с таким именем удаляем, иначе повторная загрузка не пройдёт
api "https://api.github.com/repos/$LOGIN/$REPO/releases/$RELEASE_ID/assets" \
  | python3 -c "
import sys, json
for a in json.load(sys.stdin):
    if a['name'] == os.environ.get('APK_NAME', 'BrainshEditor-1.0.apk'):
        print(a['id'])
" | while read -r OLD; do
        [ -n "$OLD" ] && api -X DELETE "https://api.github.com/repos/$LOGIN/$REPO/releases/assets/$OLD" \
            && echo "старое вложение удалено"
    done

UPLOAD=$(api --max-time 900 -X POST "https://uploads.github.com/repos/$LOGIN/$REPO/releases/$RELEASE_ID/assets?name=$APK_NAME" \
    --data-binary "@release/$APK_NAME" \
    -H "Content-Type: application/vnd.android.package-archive")

echo "$UPLOAD" | python3 -c "
import sys, json
d = json.load(sys.stdin)
print('релиз готов:', d.get('html_url', d))
"

git remote set-url origin "https://github.com/$LOGIN/$REPO.git"
rm -f "$TOKEN_FILE" && echo "токен убран из remote и удалён с диска"