#!/usr/bin/env bash
# Downloads the bundled icon sets from the Iconify JSON packages on npm and from the Dashboard Icons repository
# and stores them, reduced to what the renderer needs, as gzip-compressed embedded resources in Res/.
# Requires git, npm, and node. Update a set by changing its version or commit below and running this script.
set -euo pipefail

SETS=(
    "mdi@1.2.3"
    "fa7-brands@1.2.4"
    "fa7-regular@1.2.3"
    "fa7-solid@1.2.5"
    "octicon@1.2.39"
    "simple-icons@1.2.100"
)
DASHBOARD_ICONS_COMMIT="6d31345986eba2067a63eb41c1a5a87d4e8e850a"
SVGO_VERSION="4.1.0"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESOURCE_DIR="$SCRIPT_DIR/Res"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT
mkdir -p "$RESOURCE_DIR"

for SET in "${SETS[@]}"; do
    NAME="${SET%@*}"
    PACKAGE_DIR="$WORK_DIR/$NAME"
    mkdir -p "$PACKAGE_DIR"
    TARBALL="$(cd "$PACKAGE_DIR" && npm pack "@iconify-json/$SET" --silent)"
    tar -xzf "$PACKAGE_DIR/$TARBALL" -C "$PACKAGE_DIR"

    node - "$PACKAGE_DIR/package" "$WORK_DIR/$NAME.json" <<'EOF'
const fs = require("fs");
const [packageDir, outputPath] = process.argv.slice(2);
const icons = JSON.parse(fs.readFileSync(`${packageDir}/icons.json`, "utf8"));
const info = JSON.parse(fs.readFileSync(`${packageDir}/info.json`, "utf8"));
const { version } = JSON.parse(fs.readFileSync(`${packageDir}/package.json`, "utf8"));
const reduced = {
    name: info.name,
    version: info.version ?? version,
    author: info.author?.name ?? "",
    license: info.license?.spdx ?? info.license?.title ?? "",
    licenseUrl: info.license?.url ?? "",
    width: icons.width ?? 16,
    height: icons.height ?? 16,
    icons: Object.fromEntries(Object.entries(icons.icons).map(([name, icon]) => [name, {
        body: icon.body,
        ...(icon.width !== undefined && { width: icon.width }),
        ...(icon.height !== undefined && { height: icon.height }),
        ...(icon.hidden && { hidden: true })
    }])),
    aliases: Object.fromEntries(Object.entries(icons.aliases ?? {}).map(([name, alias]) => [name, alias.parent]))
};
fs.writeFileSync(outputPath, JSON.stringify(reduced));
EOF

    gzip -9 -n -c "$WORK_DIR/$NAME.json" > "$RESOURCE_DIR/$NAME.json.gz"
    echo "Updated $NAME ($SET)"
done

# Dashboard Icons are SVG files in a Git repository; only the svg folder and the license are checked out.
DASHBOARD_DIR="$WORK_DIR/dashboard-icons"
git init -q "$DASHBOARD_DIR"
git -C "$DASHBOARD_DIR" remote add origin https://github.com/homarr-labs/dashboard-icons.git
git -C "$DASHBOARD_DIR" sparse-checkout set --no-cone /svg/ /LICENSE
git -C "$DASHBOARD_DIR" fetch -q --depth 1 --filter=blob:none origin "$DASHBOARD_ICONS_COMMIT"
git -C "$DASHBOARD_DIR" checkout -q FETCH_HEAD
npm install --silent --prefix "$WORK_DIR/svgo" "svgo@$SVGO_VERSION" > /dev/null
node "$SCRIPT_DIR/dashboard-icons.test.mjs" "$WORK_DIR/svgo"
node "$SCRIPT_DIR/dashboard-icons.mjs" "$DASHBOARD_DIR" "$WORK_DIR/svgo" "$WORK_DIR/dashboard-icons.json" "$DASHBOARD_ICONS_COMMIT"
gzip -9 -n -c "$WORK_DIR/dashboard-icons.json" > "$RESOURCE_DIR/dashboard-icons.json.gz"
cp "$DASHBOARD_DIR/LICENSE" "$RESOURCE_DIR/dashboard-icons.LICENSE"
echo "Updated dashboard-icons ($DASHBOARD_ICONS_COMMIT)"
