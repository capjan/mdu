(() => {
    // Lighting moods of the beach scene. Colors are sRGB; the shader works in linear light.
    const presets = {
        day: {
            skyTop: "#1650b0", skyHorizon: "#8fc2ea", sun: [0.5, 0.62, 0.6], sunColor: "#fff3df", sunPower: 2.6,
            sunSize: 0.0, water: "#05456e", shallow: "#22bdbb", sand: "#ecd8b0", stars: 0, clouds: 0.45, exposure: 0.82
        },
        sunset: {
            skyTop: "#1f2a63", skyHorizon: "#d9785a", sun: [0.2, 0.045, 0.98], sunColor: "#ffa560", sunPower: 3.0,
            sunSize: 0.99985, water: "#0a2440", shallow: "#25797f", sand: "#b8957f", stars: 0.12, clouds: 0.85, exposure: 0.72
        },
        night: {
            skyTop: "#030716", skyHorizon: "#1d3158", sun: [0.1, 0.13, 0.98], sunColor: "#d2e0ff", sunPower: 0.9,
            sunSize: 0.9999, water: "#030f20", shallow: "#0f4a5a", sand: "#8d8a9c", stars: 1, clouds: 0.45, exposure: 1.15
        }
    };
    // How much a custom water or sand color is dimmed in each mood.
    const moodLight = { day: 1, sunset: 0.75, night: 0.22 };

    const fragmentSource = `
precision highp float;
uniform vec2 uRes;
uniform float uTime, uWave, uIntro, uStars, uClouds, uExposure, uSunSize;
uniform vec2 uLook;
uniform vec3 uSun, uSunCol, uSkyTop, uSkyHor, uDeep, uShallow, uSand;

#define SHORE 9.0
#define SLOPE 0.05
#define PERIOD 8.5
#define WAVELEN 10.5

float hash(float n) { return fract(sin(n) * 43758.5453); }
float hash2(vec2 p) {
    vec3 q = fract(vec3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return fract((q.x + q.y) * q.z);
}
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash2(i), hash2(i + vec2(1.0, 0.0)), u.x),
               mix(hash2(i + vec2(0.0, 1.0)), hash2(i + vec2(1.0, 1.0)), u.x), u.y);
}
const mat2 ROT = mat2(1.6, 1.2, -1.2, 1.6);
float fbm(vec2 p) {
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { s += a * noise(p); p = ROT * p; a *= 0.5; }
    return s;
}
float fbm3(vec2 p) {
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 3; i++) { s += a * noise(p); p = ROT * p; a *= 0.5; }
    return s;
}

// World size of one pixel at the point being sampled. Detail finer than that is faded out
// so that it cannot shimmer while the scene moves.
float gFoot = 0.0;
float lod(float k) { return 1.0 - smoothstep(0.8, 2.5, gFoot * k); }

// Phase offset along the shore: bends the wave crests and staggers the swash.
float crestOffset(float x, float t) {
    return 0.42 * noise(vec2(x * 0.045, t * 0.012)) + 0.16 * noise(vec2(x * 0.13 + 7.0, t * 0.025));
}
// Every wave of a set has its own size.
float waveSize(float n) { return 0.6 + 0.75 * hash(n * 12.9898 + 4.1); }

// Breaking surf: steep face towards the beach, long back towards the sea.
// u is 0 on a crest and grows seawards; d is the distance from the mean shoreline.
float surf(vec2 p, float t, out float u, out float d, out float n) {
    d = p.y - SHORE;
    float psi = d / WAVELEN + t / PERIOD + crestOffset(p.x, t);
    n = floor(psi);
    u = psi - n;
    float face = smoothstep(0.9 - clamp(gFoot / WAVELEN * 1.5, 0.0, 0.3), 1.0, u);
    float h = waveSize(n) * exp(-u * 3.5) * (1.0 - face) + waveSize(n + 1.0) * face * face;
    float envelope = smoothstep(80.0, 14.0, d) * mix(0.3, 1.0, smoothstep(0.0, 9.0, d)) * smoothstep(-4.0, 0.0, d);
    return h * envelope * 0.26 * uWave;
}

// Run-up and backwash of the water sheet on the sand.
float swashHigh(float n) { return 0.035 + 0.075 * waveSize(n) * uWave; }
float swash(float x, float t, out float phase, out float n) {
    float s = t / PERIOD + crestOffset(x, t);
    n = floor(s);
    phase = s - n;
    float up = 1.0 - pow(1.0 - clamp(phase / 0.24, 0.0, 1.0), 2.2);
    float down = pow(clamp((phase - 0.24) / 0.76, 0.0, 1.0), 1.35);
    float level = phase < 0.24 ? up : 1.0 - down;
    return mix(-0.055, swashHigh(n), level) + 0.008 * (noise(vec2(x * 0.7, t * 0.4)) - 0.5);
}

float sandHeight(vec2 p) {
    float d = p.y - SHORE;
    float ripples = 0.006 * sin(p.x * 1.9 + p.y * 0.7 + 3.0 * noise(p * 0.45)) * smoothstep(-8.0, 3.0, d);
    return -d * SLOPE + 0.12 * (noise(p * 0.06) - 0.5) + ripples;
}

float chop(vec2 p, float t) {
    float r = 0.020 * lod(1.3) * sin(dot(p, vec2(0.15, 0.99)) * 1.3 + t * 1.15);
    r += 0.012 * lod(2.1) * sin(dot(p, vec2(-0.6, 0.8)) * 2.1 + t * 1.5);
    r += 0.008 * lod(3.3) * sin(dot(p, vec2(0.8, 0.6)) * 3.3 + t * 1.9);
    r += 0.005 * lod(5.1) * sin(dot(p, vec2(-0.25, 0.97)) * 5.1 + t * 2.3);
    return r * (0.4 + 0.6 * uWave);
}

float waterHeight(vec2 p, float t, float detail) {
    float u, d, n, phase, sn;
    float h = surf(p, t, u, d, n);
    float level = swash(p.x, t, phase, sn);
    float calm = smoothstep(-2.0, 10.0, d);
    float w = mix(level, 0.0, smoothstep(0.0, 9.0, d)) + h + chop(p, t) * mix(0.25, 1.0, calm);
    if (detail > 0.0) {
        w += detail * 0.018 * (fbm3(p * vec2(0.9, 1.7) + vec2(0.0, t * 0.35)) - 0.5);
        w += detail * detail * 0.006 * (noise(p * 4.0 + vec2(t * 0.6, -t * 0.9)) - 0.5);
    }
    return w;
}

float heightAt(vec2 p, float t) { return max(sandHeight(p), waterHeight(p, t, 0.0)); }

float march(vec3 ro, vec3 rd, float t) {
    float dist = 0.2;
    float prevDist = dist;
    float prevF = ro.y - heightAt(ro.xz, t);
    float pixel = 1.0 / (1.75 * uRes.y * max(-rd.y, 0.03));
    for (int i = 0; i < 110; i++) {
        vec3 pos = ro + rd * dist;
        gFoot = dist * pixel;
        float f = pos.y - heightAt(pos.xz, t);
        if (f < 0.0) {
            float a = prevDist, b = dist;
            for (int j = 0; j < 5; j++) {
                float m = mix(a, b, prevF / (prevF - f + 1e-5));
                m = clamp(m, a + (b - a) * 0.05, b - (b - a) * 0.05);
                float fm = ro.y + rd.y * m - heightAt((ro + rd * m).xz, t);
                if (fm < 0.0) { b = m; f = fm; } else { a = m; prevF = fm; }
            }
            return mix(a, b, 0.5);
        }
        prevDist = dist;
        prevF = f;
        dist += clamp(0.5 * f / max(-rd.y, 0.035), 0.01 + dist * 0.004, 0.35 + dist * 0.06);
        if (dist > 260.0) break;
    }
    return -1.0;
}

vec3 sky(vec3 rd, bool direct) {
    float y = max(rd.y, 0.0);
    vec3 col = mix(uSkyHor, uSkyTop, 1.0 - exp(-y * 7.5));
    float sd = max(dot(rd, uSun), 0.0);
    col += uSunCol * (0.035 * pow(sd, 3.0) + 0.14 * pow(sd, 22.0) + 0.45 * pow(sd, 280.0));
    if (rd.y > 0.0) {
        vec2 cp = rd.xz / (rd.y + 0.09) * 0.55 + vec2(uTime * 0.004, uTime * 0.0015);
        float c = fbm(cp + 0.3 * fbm(cp * 1.7));
        c = smoothstep(0.6, 0.95, c) * smoothstep(0.0, 0.14, rd.y) * uClouds;
        vec3 cloud = uSkyHor * mix(1.15, 0.9, smoothstep(0.0, 0.5, rd.y)) + uSkyTop * 0.15;
        cloud += uSunCol * (0.08 + 0.55 * pow(sd, 8.0)) * (0.4 + 0.6 * uSun.y);
        col = mix(col, cloud, c * 0.7);
        // Reflected stars would sparkle on the moving water, so only the open sky has them.
        if (direct && uStars > 0.0) {
            vec2 q = rd.xz / (rd.y + 0.35) * 70.0;
            vec2 cell = floor(q);
            float s = hash2(cell);
            vec2 jitter = vec2(hash2(cell + 3.1), hash2(cell + 7.7)) - 0.5;
            // Stars cover at least two pixels so they do not twinkle against the pixel grid.
            float radius = max(0.18, 2.0 * 70.0 / ((rd.y + 0.35) * 1.75 * uRes.y));
            float star = smoothstep(radius, 0.0, length(fract(q) - 0.5 - jitter * 0.3)) * step(0.985, s) * min(1.0, 0.18 / radius);
            star *= 0.6 + 0.4 * sin(uTime * (1.5 + 3.0 * s) + s * 40.0);
            col += vec3(0.9, 0.95, 1.0) * star * uStars * (1.0 - c) * smoothstep(0.03, 0.25, rd.y) * 1.6;
        }
    }
    if (direct && uSunSize > 0.0) {
        float disk = smoothstep(uSunSize, uSunSize + 0.00004, sd);
        col += uSunCol * disk * 1.4;
    }
    return col;
}

vec3 fogColor(vec3 rd) {
    float sd = max(dot(rd, uSun), 0.0);
    return uSkyHor * 0.95 + uSunCol * (0.06 * pow(sd, 3.0) + 0.18 * pow(sd, 16.0));
}

// Thin foam threads along the zero line of a noise field. A thread is never drawn thinner than
// the pixel it falls into; wider threads get dimmer, so the foam keeps its brightness without flickering.
float thread(float n, float width) {
    return (1.0 - smoothstep(0.0, width, abs(n * 2.0 - 1.0))) * min(1.0, 0.14 / width);
}
float lace(vec2 p, float scale) {
    float f = gFoot * scale * 3.0;
    return thread(noise(p), 0.14 + f) + 0.6 * thread(noise(p * 2.3 + 11.0), 0.1 + f * 2.3);
}

void main() {
    vec2 uv = (gl_FragCoord.xy - 0.5 * uRes) / uRes.y;
    float t = uTime;
    float intro = uIntro * uIntro * (3.0 - 2.0 * uIntro);

    float yaw = 0.5 + uLook.x * 0.045 + 0.02 * sin(t * 0.045);
    float pitch = mix(0.0, -0.105, intro) + uLook.y * 0.02 + 0.004 * sin(t * 0.21);
    vec3 ro = vec3(0.0, mix(1.1, 3.0, intro) + 0.025 * sin(t * 0.33), 0.0);
    vec3 fw = vec3(sin(yaw) * cos(pitch), sin(pitch), cos(yaw) * cos(pitch));
    vec3 ri = normalize(cross(vec3(0.0, 1.0, 0.0), fw));
    vec3 up = cross(fw, ri);
    vec3 rd = normalize(fw * 1.75 + ri * uv.x + up * uv.y);

    vec3 col;
    float hit = rd.y < 0.0 ? march(ro, rd, t) : -1.0;
    if (hit < 0.0 && rd.y < 0.0) hit = ro.y / -rd.y;

    if (hit < 0.0) {
        col = sky(rd, true);
    } else {
        vec3 p = ro + rd * hit;
        gFoot = hit / (1.75 * uRes.y * max(-rd.y, 0.03));
        float detail = 1.0 - smoothstep(0.015, 0.2, gFoot);
        float sand = sandHeight(p.xz);
        float water = waterHeight(p.xz, t, detail);
        float depth = water - sand;
        float u, d, n, phase, sn;
        float wave = surf(p.xz, t, u, d, n);
        float level = swash(p.x, t, phase, sn);
        float e = max(0.03, gFoot);
        vec3 amb = uSkyTop * 0.32 + uSkyHor * 0.28;
        vec3 foamCol = vec3(0.94, 0.97, 1.0);
        // Water turns into sand over about one pixel, so the waterline does not crawl.
        float edge = max(0.004, gFoot * SLOPE * 1.5);
        float waterAmount = smoothstep(-edge, edge, depth);
        vec3 waterCol = vec3(0.0);
        vec3 sandCol = vec3(0.0);

        if (waterAmount > 0.0) {
            float wd = max(depth, 0.0);
            vec3 nor = normalize(vec3(
                waterHeight(p.xz - vec2(e, 0.0), t, detail) - waterHeight(p.xz + vec2(e, 0.0), t, detail),
                2.0 * e,
                waterHeight(p.xz - vec2(0.0, e), t, detail) - waterHeight(p.xz + vec2(0.0, e), t, detail)));
            nor = normalize(mix(vec3(0.0, 1.0, 0.0), nor, 0.35 + 0.65 * detail));

            // Foam: whitewater behind breaking crests, lace on the surf, bubbles on the swash sheet.
            // Every term continues smoothly from one wave into the next.
            vec2 drift = vec2(p.x * 0.8, p.z * 1.5 + t * (WAVELEN / PERIOD) * 1.5);
            float tex = fbm3(drift * 1.1);
            float face = smoothstep(0.86, 1.0, u);
            float broken = smoothstep(12.0, 6.5, d) * smoothstep(-6.0, 0.0, d);
            float white = (waveSize(n) * exp(-u * 6.5) * (1.0 - face) + waveSize(n + 1.0) * face) * broken;
            float foam = clamp(white * smoothstep(0.32, 0.72, tex + white * 0.3), 0.0, 1.0);
            float cap = (smoothstep(0.95, 1.0, u) + exp(-u * 25.0)) * smoothstep(20.0, 12.0, d) * smoothstep(6.0, 12.0, d);
            foam = max(foam, cap * smoothstep(0.4, 0.7, tex) * 0.8);
            float trail = exp(-u * 1.8) * (1.0 - face) + face;
            float mesh = lace(drift * 1.6, 2.4) * broken * (0.35 + 0.65 * trail);
            foam = max(foam, mesh * 0.6 * detail);
            float sheet = smoothstep(0.1, 0.0, wd) * smoothstep(1.0, -4.0, d);
            float bubbles = lace(vec2(p.x * 1.4, p.z * 2.2) + vec2(0.0, t * 0.5), 2.2) + smoothstep(0.55, 0.8, tex);
            float uprush = 0.35 + 0.55 * (1.0 - smoothstep(0.0, 0.5, phase)) * smoothstep(0.0, 0.04, phase);
            foam = max(foam, sheet * bubbles * uprush);
            foam = max(foam, smoothstep(0.025, 0.0, wd) * smoothstep(0.35, 0.7, tex + 0.25) * 0.9);
            foam = clamp(foam, 0.0, 1.0);

            float ndv = max(dot(nor, -rd), 0.0);
            float fresnel = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);
            vec3 r = reflect(rd, nor);
            r.y = abs(r.y);
            vec3 refl = sky(r, false);
            float diffuse = 0.55 + 0.45 * max(dot(nor, uSun), 0.0);
            vec3 sandLit = uSand * (amb + uSunCol * max(uSun.y, 0.08) * 0.85);
            vec3 body = mix(sandLit * 0.75, uShallow * (amb + uSunCol * 0.35 * diffuse), smoothstep(0.0, 0.3, wd));
            body = mix(body, uDeep * (amb + uSunCol * 0.2 * diffuse), smoothstep(0.25, 3.0, wd));
            float crest = clamp(wave / (0.26 * max(uWave, 0.2)), 0.0, 1.0) * smoothstep(0.7, 0.93, u) * (1.0 - smoothstep(0.96, 1.0, u));
            float backlight = pow(max(dot(rd, uSun), 0.0), 2.0);
            body += uShallow * uSunCol * crest * (0.12 + 0.7 * backlight);
            waterCol = mix(body, refl, fresnel);
            // Glints widen with distance instead of turning into single sparkling pixels.
            float glint = max(dot(r, uSun), 0.0);
            float shine = mix(60.0, 600.0, detail);
            float spec = min(pow(glint, shine) * shine * 0.02, 6.0) + pow(glint, 40.0) * 0.15;
            waterCol += uSunCol * spec * (1.0 - foam);
            vec3 foamLit = foamCol * (amb * 1.25 + uSunCol * (0.3 + 0.45 * max(dot(nor, uSun), 0.0)));
            waterCol = mix(waterCol, foamLit, foam);
        }

        if (waterAmount < 1.0) {
            vec3 nor = normalize(vec3(
                sandHeight(p.xz - vec2(e, 0.0)) - sandHeight(p.xz + vec2(e, 0.0)),
                2.0 * e,
                sandHeight(p.xz - vec2(0.0, e)) - sandHeight(p.xz + vec2(0.0, e))));
            // Wet sand reaches up to the highest run-up of this and the previous wave.
            float reached = phase < 0.24 ? level : swashHigh(sn);
            float wetLine = max(reached, swashHigh(sn - 1.0) * (1.0 - smoothstep(0.3, 1.0, phase) * 0.6));
            float wet = smoothstep(wetLine + 0.03, wetLine - 0.025, sand);
            wet *= mix(0.6, 1.0, smoothstep(wetLine, level, sand));
            float grain = mix(noise(p.xz * 2.6), 0.5, smoothstep(0.1, 0.35, gFoot * 2.6));
            vec3 albedo = uSand * (0.82 + 0.22 * grain + 0.12 * noise(p.xz * 0.35));
            albedo *= mix(1.0, 0.52, wet);
            sandCol = albedo * (amb + uSunCol * max(dot(nor, uSun), 0.0) * 0.95);
            vec3 r = reflect(rd, nor);
            float fresnel = 0.04 + 0.96 * pow(1.0 - max(dot(nor, -rd), 0.0), 5.0);
            sandCol = mix(sandCol, sky(vec3(r.x, abs(r.y), r.z), false), fresnel * wet * 0.7);
            sandCol += uSunCol * pow(max(dot(r, uSun), 0.0), 220.0) * wet * wet * 2.0;
            float residue = smoothstep(0.02, 0.0, abs(sand - wetLine + 0.006)) * lace(p.xz * vec2(1.3, 2.4), 2.4);
            float fading = (1.0 - smoothstep(0.4, 1.0, phase)) * smoothstep(0.24, 0.3, phase);
            vec3 foamLit = foamCol * (amb * 1.2 + uSunCol * 0.5 * max(uSun.y, 0.1));
            sandCol = mix(sandCol, foamLit, clamp(residue, 0.0, 1.0) * 0.55 * fading * detail);
        }

        col = mix(sandCol, waterCol, waterAmount);
        float fog = 1.0 - exp(-hit * 0.0028 - max(hit - 110.0, 0.0) * 0.012);
        col = mix(col, fogColor(rd), fog);
    }

    col *= uExposure;
    col = clamp((col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14), 0.0, 1.0);
    col = pow(col, vec3(1.0 / 2.2));
    vec2 q = gl_FragCoord.xy / uRes;
    col *= 0.78 + 0.22 * pow(16.0 * q.x * q.y * (1.0 - q.x) * (1.0 - q.y), 0.18);
    // Static dither against banding in the dark gradients.
    col += (hash2(gl_FragCoord.xy) - 0.5) / 255.0;
    gl_FragColor = vec4(col, 1.0);
}`;

    const srgbToLinear = (hex) => {
        let value = hex.replace("#", "");
        if (value.length === 3) value = value.split("").map((c) => c + c).join("");
        return [0, 2, 4].map((i) => {
            const c = parseInt(value.slice(i, i + 2), 16) / 255;
            return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
        });
    };
    const normalize = (v) => {
        const length = Math.hypot(v[0], v[1], v[2]);
        return v.map((c) => c / length);
    };

    const resolveMood = (hero, { dark }) => {
        const time = hero.dataset.heroTime;
        return time === "day" || time === "sunset" || time === "night" ? time : dark ? "night" : "day";
    };
    const moodUniforms = (hero, colors) => {
        const mood = resolveMood(hero, colors);
        const preset = presets[mood];
        const color = (name, fallback) => {
            const custom = hero.dataset[`hero${name[0].toUpperCase()}${name.slice(1)}`];
            if (!custom) return srgbToLinear(fallback);
            return srgbToLinear(custom).map((c) => c * moodLight[mood]);
        };
        return {
            uSkyTop: srgbToLinear(preset.skyTop),
            uSkyHor: srgbToLinear(preset.skyHorizon),
            uSun: normalize(preset.sun),
            uSunCol: srgbToLinear(preset.sunColor).map((c) => c * preset.sunPower),
            uDeep: color("water", preset.water),
            uShallow: color("shallow", preset.shallow),
            uSand: color("sand", preset.sand),
            uStars: [preset.stars],
            uClouds: [preset.clouds],
            uExposure: [preset.exposure],
            uSunSize: [preset.sunSize]
        };
    };

    const hero = window.mduHero ??= { effects: Object.create(null), controllers: new WeakMap() };
    hero.effects.beach = {
        fragment: fragmentSource, uniforms: moodUniforms,
        colors(banner, colors) {
            const preset = presets[resolveMood(banner, colors)];
            return Object.fromEntries(["water", "shallow", "sand"].map(name =>
                [name, banner.dataset[`hero${name[0].toUpperCase()}${name.slice(1)}`] || preset[name]]));
        }
    };
})();
