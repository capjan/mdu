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

const glyphs = new Map();
for (const file of fs.readdirSync(svgDir).filter((file) => file.endsWith(".svg")).sort()) {
    const name = file.slice(0, -4);
    if (!/^[a-z0-9-]+$/.test(name)) { skip("name"); continue; }
    const source = fs.readFileSync(path.join(svgDir, file), "utf8");
    if (source.length > maxSize) { skip("size"); continue; }

    let svg;
    try {
        svg = optimize(source, {
            multipass: true,
            plugins: [
                { name: "preset-default", params: { overrides: { inlineStyles: { onlyMatchedOnce: false } } } },
                "removeDimensions",
                "removeScripts",
                { name: "prefixIds", params: { prefix: `di-${name}`, delim: "-" } }
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

    // Keep the root's presentation attributes on a group. The monochrome Material Design logo has no theme
    // variants, so let its unfilled shapes follow the text color; preserve other logos' original black default.
    const groupAttributes = Object.entries(attributes).filter(([attribute]) => presentationAttributes.test(attribute));
    if (!groupAttributes.some(([attribute, value]) => attribute === "fill" || (attribute === "style" && /fill\s*:/.test(value))))
        groupAttributes.unshift(["fill", name === "material-design-icons" ? "currentColor" : "#000"]);
    const group = groupAttributes.map(([attribute, value]) => ` ${attribute}="${value}"`).join("");
    const [left, top, width, height] = viewBox;
    glyphs.set(name, { body: `<g${group}>${root[2]}</g>`, left, top, width, height });
}

// Most "-light" files are drawn in light colors for dark themes, and "-dark" files in dark colors for light themes.
// Mermaid instead names the background theme, so its pair is corrected below.
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
themedVariants.set("mermaid", { light: glyphs.get("mermaid-light"), dark: glyphs.get("mermaid-dark") });
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
    icons[name] = {
        ...iconData(themed?.light ?? glyphs.get(name)),
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
