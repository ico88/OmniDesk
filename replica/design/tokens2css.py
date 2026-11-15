# Genera tokens.css e tailwind.theme.css da tokens.json. Uso: python3 tokens2css.py tokens.json tokens.css tailwind.theme.css
import json, sys
src, out_css, out_tw = sys.argv[1:4]
t = json.load(open(src, encoding="utf-8"))
def block(sel, cols):
    return sel + " {\n" + "".join(f"  --od-color-{k}: {v};\n" for k, v in cols.items()) + "}\n"
L, D = t["color"]["light"], t["color"]["dark"]
base = [":root {", f"  color-scheme: light dark;", f"  --od-font-sans: {t['font']['sans']};", f"  --od-font-mono: {t['font']['mono']};"]
for k, v in t["type"].items():
    base += [f"  --od-text-{k}: {v['size']}px;", f"  --od-leading-{k}: {v['line']}px;", f"  --od-weight-{k}: {v['weight']};"]
for i, v in enumerate(t["space"]):
    base.append(f"  --od-space-{i}: {v}px;")
for k, v in t["radius"].items():
    base.append(f"  --od-radius-{k}: {v}px;")
for k, v in t["shadow"].items():
    base.append(f"  --od-shadow-{k}: {v};")
for k in ("fast", "base", "ease"):
    base.append(f"  --od-motion-{k}: {t['motion'][k]};")
for k, v in t["layout"].items():
    if isinstance(v, int): base.append(f"  --od-layout-{k}: {v}px;")
for k, v in t["z"].items():
    base.append(f"  --od-z-{k}: {v};")
base.append("}\n")
css = ["/* Generato da tokens.json: non modificare a mano. */\n", "\n".join(base),
       block(":root, [data-theme=\"light\"]", L),
       "@media (prefers-color-scheme: dark) {\n" + block("  :root:not([data-theme=\"light\"])", D).replace("\n  --", "\n    --").replace("\n}", "\n  }") + "}\n",
       block("[data-theme=\"dark\"]", D),
       "@media (prefers-reduced-motion: reduce) {\n  :root { --od-motion-fast: 0ms; --od-motion-base: 0ms; }\n}\n"]
open(out_css, "w", encoding="utf-8").write("\n".join(css))
tw = ["/* Mappatura Tailwind v4: importare dopo tokens.css. Generato da tokens.json. */", "@theme inline {"]
tw += [f"  --color-{k}: var(--od-color-{k});" for k in L]
tw += ["  --font-sans: var(--od-font-sans);", "  --font-mono: var(--od-font-mono);"]
for k in t["type"]:
    tw += [f"  --text-{k}: var(--od-text-{k});", f"  --text-{k}--line-height: var(--od-leading-{k});", f"  --text-{k}--font-weight: var(--od-weight-{k});"]
tw += [f"  --radius-{k}: var(--od-radius-{k});" for k in t["radius"]]
tw += [f"  --shadow-{k}: var(--od-shadow-{k});" for k in t["shadow"]]
tw += ["  --spacing: 4px;", "}"]
open(out_tw, "w", encoding="utf-8").write("\n".join(tw) + "\n")
