import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const directory = fs.mkdtempSync(path.join(os.tmpdir(), "mdu-icon-themes-"));
const shape = '<path d="M1 1h8v8H1z"/>';
const svg = body => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">${body}</svg>`;
const fixtures = {
    "reversed-light": `<g style="fill:black">${shape}</g>`,
    "reversed-dark": `<g fill="white">${shape}</g>`,
    "normal-light": `<g fill="#fff">${shape}</g>`,
    "normal-dark": shape,
    "partial": `<g fill="white">${shape}</g>`,
    "partial-light": shape,
    "orphan-dark": `<g fill="white">${shape}</g>`,
    "plain": shape,
    "white": `<g fill="white">${shape}</g>`,
    "styled": `<g style="fill:#000;fill-rule:evenodd">${shape}</g>`,
    "outlined": '<path fill="none" stroke="#000" d="M1 1h8v8"/>',
    "transparent": '<path fill="none" d="M0 0h10v10H0z"/>' + shape,
    "colored": `<g fill="black"><g fill="#2396ed">${shape}</g></g>`,
    "multicolor": shape + '<circle fill="#fff" cx="5" cy="5" r="2"/>',
    "gradient": '<defs><linearGradient id="a"><stop stop-color="#000"/><stop offset="1" stop-color="#fff"/></linearGradient></defs><path fill="url(#a)" d="M1 1h8v8H1z"/>',
    "masked": `<defs><mask id="a"><path fill="#fff" d="M0 0h10v10H0z"/></mask></defs><g mask="url(#a)">${shape}</g>`,
    "colored-pair-dark": `<g fill="white">${shape}</g>`,
    "colored-pair-light": `<g fill="#2396ed">${shape}</g>`,
    "linear-light": `<g fill="#fff">${shape}</g>`,
    "linear-dark": `<g fill="#222326">${shape}</g>`,
    "open-router-light": '<defs><clipPath id="a"><path d="M0 0h5v10H0z"/></clipPath></defs><path clip-path="url(#a)" fill="#fff" d="M0 0h10v10H0z"/>',
    "open-router-dark": '<defs><clipPath id="a"><path d="M0 0h5v10H0z"/></clipPath></defs><path clip-path="url(#a)" fill="#111" d="M0 0h10v10H0z"/>',
    "monarch-money-light": `<g fill="#ff692d">${shape}</g>`,
    "monarch-money-dark": shape
};
let icons;
try {
    fs.mkdirSync(path.join(directory, "svg"));
    for (const [name, body] of Object.entries(fixtures))
        fs.writeFileSync(path.join(directory, "svg", `${name}.svg`), svg(body));
    const result = spawnSync(process.execPath, [fileURLToPath(new URL("dashboard-icons.mjs", import.meta.url)),
        directory, path.resolve(process.argv[2]), path.join(directory, "icons.json"), "fixture"], { encoding: "utf8" });
    assert.equal(result.status, 0, result.stderr);
    icons = JSON.parse(fs.readFileSync(path.join(directory, "icons.json"))).icons;
} finally {
    fs.rmSync(directory, { recursive: true, force: true });
}

test("orders monochrome pairs by paint, preserving explicit files", () => {
    for (const name of ["reversed", "normal", "partial"]) {
        assert.match(icons[name].body, /#000/);
        assert.match(icons[name].dark.body, /#fff/);
    }
    assert.equal(icons.reversed.body, icons["reversed-light"].body);
    assert.equal(icons.reversed.dark.body, icons["reversed-dark"].body);
    assert.equal(icons.normal.body, icons["normal-dark"].body);
    assert.equal(icons.normal.dark.body, icons["normal-light"].body);
    assert.equal(icons["orphan-dark"].dark, undefined);
    assert.match(icons["orphan-dark"].body, /#fff/);
});

test("orders near-black variants and pairs with clip paths by brightness", () => {
    for (const name of ["linear", "open-router", "monarch-money"]) {
        assert.equal(icons[name].body, icons[`${name}-dark`].body);
        assert.equal(icons[name].dark.body, icons[`${name}-light`].body);
    }
});

test("adapts unpaired monochrome fills, styles and strokes", () => {
    for (const name of ["plain", "white", "styled", "outlined", "transparent"]) {
        assert.match(icons[name].body, /currentColor/);
        assert.equal(icons[name].dark, undefined);
    }
    assert.match(icons.outlined.body, /stroke="currentColor"/);
    assert.match(icons.outlined.body, /fill="none"/);
});

test("preserves colors and complex SVG paints", () => {
    for (const name of ["colored", "multicolor", "gradient", "masked", "colored-pair"])
        assert.doesNotMatch(icons[name].body, /currentColor/);
    assert.match(icons.colored.body, /#2396ed/);
    assert.equal(icons["colored-pair"].body, icons["colored-pair-dark"].body);
    assert.equal(icons["colored-pair"].dark.body, icons["colored-pair-light"].body);
});
