// Converts the SVG files of Dashboard Icons (https://github.com/homarr-labs/dashboard-icons) into the reduced
// icon set format of update-icon-sets.sh. Called by that script with the checkout, the SVGO module, and the output path.
//
// The SVGs are inlined into rendered pages, so each one is optimized with SVGO, its IDs and class names get a
// unique prefix, and its styles become attributes. Files that are too large, embed images or scripts, link to
// other files, or keep a style element are left out. Icons with "-light" or "-dark" variants get the variant for
// dark themes as "dark".
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";

const [checkoutDir, svgoDir, outputPath, commit] = process.argv.slice(2);
const maxSize = 20_000;
const require = createRequire(path.join(svgoDir, "package.json"));
const { optimize } = await import(pathToFileURL(require.resolve("svgo")).href);

const svgDir = path.join(checkoutDir, "svg");
const presentationAttributes = /^(fill|fill-opacity|fill-rule|clip-rule|stroke|stroke-[a-z]+|opacity|color|style|font-[a-z]+|text-anchor|transform)$/;
const unsafeContent = /<(script|image|foreignObject|iframe|a|style)\b|\son[a-z]+\s*=|(?:xlink:)?href\s*=\s*"(?!#)/i;
const skipped = {};
const skip = (reason) => skipped[reason] = (skipped[reason] ?? 0) + 1;

// Inspect effective paints, not every color declaration: a group's default black may be overridden by colored
// children. Keep complex SVGs (masks, gradients, filters, references) out of monochrome normalization.
function paintProfile(root, ignoreClipPath = false) {
    const paints = [];
    let simple = true;
    function visit(node, fill = "#000", stroke = "none") {
        if (node.type === "element") {
            if (["title", "desc", "metadata"].includes(node.name)) return;
            if (ignoreClipPath && ["defs", "clipPath"].includes(node.name)) return;
            if (!["svg", "g", "path", "rect", "circle", "ellipse", "polygon", "polyline", "line"].includes(node.name)
                || ["mask", "filter"].some(key => key in node.attributes)
                || !ignoreClipPath && "clip-path" in node.attributes) {
                simple = false;
                return;
            }
            const styles = Object.fromEntries((node.attributes.style ?? "").split(";")
                .filter(value => value.includes(":"))
                .map(value => value.split(":").map(part => part.trim())));
            if (["mask", "filter"].some(key => key in styles)
                || !ignoreClipPath && "clip-path" in styles) simple = false;
            fill = styles.fill ?? node.attributes.fill ?? fill;
            stroke = styles.stroke ?? node.attributes.stroke ?? stroke;
            if (node.name !== "svg" && node.name !== "g") {
                for (const paint of node.name === "line" ? [stroke] : [fill, stroke]) {
                    const color = paint.trim().toLowerCase();
                    if (color === "none") continue;
                    const hex = color === "black" ? "#000000" : color === "white" ? "#ffffff" : color;
                    const channels = hex.match(/^#([\da-f]{2})([\da-f]{2})([\da-f]{2})$/i)
                        ?? hex.match(/^#([\da-f])([\da-f])([\da-f])$/i)?.slice(1).map(value => value.repeat(2));
                    if (!channels) {
                        simple = false;
                        continue;
                    }
                    const rgb = channels.slice(1).map(value => Number.parseInt(value, 16) / 255);
                    const [minimum, maximum] = [Math.min(...rgb), Math.max(...rgb)];
                    const luminance = rgb.reduce((sum, value, index) =>
                        sum + value * [0.2126, 0.7152, 0.0722][index], 0);
                    paints.push({ brightness: maximum, neutral: maximum - minimum < 0.04, luminance });
                }
            }
        }
        for (const child of node.children ?? []) visit(child, fill, stroke);
    }
    visit(root);
    return simple && paints.length > 0 ? paints : null;
}

function monochromePaint(root) {
    const paints = paintProfile(root);
    if (!paints || paints.some(paint => !paint.neutral)) return null;
    const [minimum, maximum] = [Math.min(...paints.map(paint => paint.luminance)), Math.max(...paints.map(paint => paint.luminance))];
    return maximum <= 0.03 ? "black" : minimum >= 0.97 ? "white" : null;
}

// The upstream -light/-dark suffixes also describe background themes for some logos. Use the whole neutral paint
// range to detect those pairs, ignoring clip paths, while keeping the stricter monochrome test above for recoloring.
function isLightLogo(root) {
    const paints = paintProfile(root, true);
    return paints?.every(paint => paint.brightness >= 0.88) ?? false;
}

function isDarkLogo(root) {
    const paints = paintProfile(root, true);
    return paints?.every(paint => paint.brightness <= 0.2) ?? false;
}

const glyphs = new Map();
for (const file of fs.readdirSync(svgDir).filter((file) => file.endsWith(".svg")).sort()) {
    const name = file.slice(0, -4);
    if (!/^[a-z0-9-]+$/.test(name)) { skip("name"); continue; }
    const source = fs.readFileSync(path.join(svgDir, file), "utf8");
    if (source.length > maxSize) { skip("size"); continue; }

    let svg;
    let monochrome;
    let profileRoot;
    try {
        svg = optimize(source, {
            multipass: true,
            plugins: [
                { name: "preset-default", params: { overrides: { inlineStyles: { onlyMatchedOnce: false } } } },
                "removeDimensions",
                "removeScripts",
                { name: "prefixIds", params: { prefix: `di-${name}`, delim: "-" } },
                { name: "inspectPaints", fn: root => { profileRoot = root; monochrome = monochromePaint(root); } }
            ]
        }).data;
    } catch {
        skip("invalid"); continue;
    }

    const root = svg.match(/^<svg\b([^>]*)>([\s\S]*)<\/svg>$/);
    if (!root) { skip("invalid"); continue; }
    const attributes = Object.fromEntries([...root[1].matchAll(/([\w:-]+)="([^"]*)"/g)].map((match) => [match[1], match[2]]));
    const viewBox = (attributes.viewBox ?? "").trim().split(/[\s,]+/).map(Number);
    if (viewBox.length !== 4 || viewBox.some(Number.isNaN) || viewBox[2] <= 0 || viewBox[3] <= 0) { skip("viewBox"); continue; }
    if (unsafeContent.test(root[2])) { skip("content"); continue; }

    // Keep the root's presentation attributes and SVG's default black fill on a group.
    const groupAttributes = Object.entries(attributes).filter(([attribute]) => presentationAttributes.test(attribute));
    if (!groupAttributes.some(([attribute, value]) => attribute === "fill" || (attribute === "style" && /fill\s*:/.test(value))))
        groupAttributes.unshift(["fill", "#000"]);
    const group = groupAttributes.map(([attribute, value]) => ` ${attribute}="${value}"`).join("");
    const [left, top, width, height] = viewBox;
    glyphs.set(name, { body: `<g${group}>${root[2]}</g>`, left, top, width, height, monochrome, root: profileRoot });
}

// Most "-light" files are drawn in light colors for dark themes, and "-dark" files in dark colors for light themes.
// Some instead name the background theme; correct reversed black/white pairs by their effective paints below.
const themedVariants = new Map();
for (const [name, glyph] of glyphs) {
    if (/-(light|dark)$/.test(name)) continue;
    const light = glyphs.get(`${name}-dark`) ?? glyph;
    const dark = glyphs.get(`${name}-light`) ?? glyph;
    if (light !== dark) themedVariants.set(name, { light, dark });
}
// Logos with only "-light" and "-dark" files get a shortcode without suffix that switches with the theme.
for (const name of glyphs.keys()) {
    const base = name.match(/^(.+)-dark$/)?.[1];
    if (base && !glyphs.has(base) && glyphs.has(`${base}-light`))
        themedVariants.set(base, { light: glyphs.get(name), dark: glyphs.get(`${base}-light`) });
}
for (const [name, { light, dark }] of themedVariants) {
    if (isLightLogo(light.root) && isDarkLogo(dark.root))
        themedVariants.set(name, { light: dark, dark: light });
}
const isVariant = (name) => {
    const base = name.match(/^(.+)-(light|dark)$/)?.[1];
    return base !== undefined && themedVariants.has(base);
};

const iconData = (glyph) => ({
    body: glyph.body,
    ...(glyph.left !== 0 && { left: glyph.left }),
    ...(glyph.top !== 0 && { top: glyph.top }),
    width: glyph.width,
    height: glyph.height
});

const icons = {};
for (const name of [...new Set([...glyphs.keys(), ...themedVariants.keys()])].sort()) {
    const themed = themedVariants.get(name);
    let glyph = themed?.light ?? glyphs.get(name);
    // Preserve explicit suffix variants. Unsuffixed black/white logos without a pair follow the page text color.
    if (!themed && !/-(light|dark)$/.test(name) && glyph.monochrome) {
        glyph = { ...glyph, body: glyph.body.replace(
            /\b(fill|stroke)(="|\s*:\s*)(#(?:000000|ffffff|000|fff)|black|white)(?=[";\s])/gi,
            "$1$2currentColor") };
    }
    icons[name] = {
        ...iconData(glyph),
        ...(themed && { dark: iconData(themed.dark) }),
        // These shortcodes stay usable, but the search omits the variants and Docker.
        ...((isVariant(name) || name === "docker") && { hidden: true })
    };
}

fs.writeFileSync(outputPath, JSON.stringify({
    name: "Dashboard Icons",
    version: commit.slice(0, 7),
    author: "Bjorn Lammers, Meier Lukas, Thomas Camlong and Homarr Labs",
    license: "Apache-2.0",
    licenseUrl: "https://github.com/homarr-labs/dashboard-icons/blob/main/LICENSE",
    width: 512,
    height: 512,
    icons,
    aliases: {}
}));
console.log(`Converted ${glyphs.size} Dashboard Icons, ${themedVariants.size} with light and dark variants; skipped ${JSON.stringify(skipped)}`);
