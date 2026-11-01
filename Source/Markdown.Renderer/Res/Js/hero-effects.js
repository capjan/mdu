(() => {
    const hero = window.mduHero ??= { effects: Object.create(null), controllers: new WeakMap() };
    const header = `
precision highp float;
uniform vec2 uRes;
uniform float uTime;
uniform vec3 iBackground, iForeground, iAccent;
vec2 point(vec2 cell, float t) {
    float phase = dot(cell, vec2(1.73, 2.91));
    return cell + 0.5 + 0.23 * vec2(sin(t + phase), cos(t * 0.7 + phase * 1.3));
}
float segment(vec2 p, vec2 a, vec2 b) {
    vec2 d = b - a;
    return length(p - a - d * clamp(dot(p - a, d) / dot(d, d), 0.0, 1.0));
}
`;
    hero.effects.connections = {
        fragment: header + `
void main() {
    vec2 p = (gl_FragCoord.xy - 0.5 * uRes) / uRes.y * 7.0;
    vec2 cell = floor(p);
    float t = uTime * 0.3;
    float dots = 0.0, lines = 0.0;
    float aa = 7.0 / uRes.y;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 id = cell + vec2(float(x), float(y));
            vec2 a = point(id, t);
            dots = max(dots, 1.0 - smoothstep(0.025, 0.025 + aa, length(p - a)));
            vec2 b = point(id + vec2(1.0, 0.0), t);
            vec2 c = point(id + vec2(0.0, 1.0), t);
            vec2 d = point(id + vec2(1.0, 1.0), t);
            vec2 bend = vec2(0.0, 0.12 * sin(t + id.x));
            float distanceToLine = min(segment(p, a, (a + b) * 0.5 + bend),
                segment(p, (a + b) * 0.5 + bend, b));
            distanceToLine = min(distanceToLine, segment(p, a, c));
            float diagonal = segment(p, a, d);
            lines = max(lines, (1.0 - smoothstep(0.003, 0.003 + aa, distanceToLine)) * 0.28);
            lines = max(lines, (1.0 - smoothstep(0.002, 0.002 + aa, diagonal)) * 0.10);
        }
    }
    vec3 color = mix(iBackground, mix(iForeground, iAccent, 0.12), max(lines, dots * 0.58));
    gl_FragColor = vec4(color, 1.0);
}`
    };
    hero.effects.flow = {
        fragment: header + `
void main() {
    vec2 p = (gl_FragCoord.xy - 0.5 * uRes) / uRes.y;
    float t = uTime * 0.16;
    float field = p.y * 9.0 + 1.3 * sin(p.x * 2.7 + t)
        + 0.7 * sin(p.x * 5.0 - p.y * 2.5 - t * 1.4);
    float band = abs(fract(field) - 0.5);
    float aa = 14.0 / uRes.y;
    float ink = (1.0 - smoothstep(0.012, 0.012 + aa, band)) * 0.24;
    vec3 color = mix(iBackground, mix(iForeground, iAccent, 0.18), ink);
    gl_FragColor = vec4(color, 1.0);
}`
    };
    hero.effects.voronoi = {
        fragment: header + `
void main() {
    vec2 p = (gl_FragCoord.xy - 0.5 * uRes) / uRes.y * 6.0;
    vec2 cell = floor(p), closest = vec2(0.0);
    float first = 100.0, second = 100.0;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 id = cell + vec2(float(x), float(y));
            float d = length(p - point(id, uTime * 0.23));
            if (d < first) { second = first; first = d; closest = id; }
            else { second = min(second, d); }
        }
    }
    float shade = 0.025 + 0.055 * (0.5 + 0.5 * sin(dot(closest, vec2(1.3, 2.7))));
    float edge = 1.0 - smoothstep(0.012, 0.012 + 8.0 / uRes.y, second - first);
    vec3 color = mix(iBackground, mix(iForeground, iAccent, 0.12), shade + edge * 0.17);
    gl_FragColor = vec4(color, 1.0);
}`
    };
})();
