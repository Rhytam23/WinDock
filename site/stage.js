// The animated desktop at the top of the page: a 1600x900 Windows desktop where a cursor demonstrates WinDock
// on a 15-second loop - magnification, a bounce, a window opening, the genie, folder stacks and window previews.
// Everything is laid out in stage pixels and the whole stage is scaled to fit. The window and the genie are drawn
// row by row on a canvas (the same method WinDock uses). Pauses when off screen; a still frame for reduced motion.
(function () {
  const screen = document.getElementById("screen");
  if (!screen) return;
  const stage = screen.querySelector(".stage");
  const canvas = stage.querySelector("canvas");
  const ctx = canvas.getContext("2d");
  const reduce = matchMedia("(prefers-reduced-motion: reduce)");

  // ---------- constants (stage pixels) ----------
  const SW = 1600, SH = 900, BASE = 58, GAP = 10, PAD = 10, MAXM = 1.8, BOTTOM = SH - 22;
  const ICON_Y = BOTTOM - PAD - BASE / 2;
  const NAMES = ["files", "browser", "mail", "music", "photos", "notes", "terminal", "calendar", "settings", "|", "downloads", "trash"];
  const LABELS = { files: "Files", browser: "Browser", mail: "Mail", music: "Music", photos: "Photos", notes: "Notes",
    terminal: "Terminal", calendar: "Calendar", settings: "Settings", downloads: "Downloads", trash: "Trash" };
  const WIN = { x: 420, y: 150, w: 760, h: 500 };
  const LOOP = 15, STEP = 84;
  const FILES = ["Sunset.jpg", "Invoice.pdf", "Trip.mp4", "Project.zip", "Budget.xlsx", "Notes.txt"];

  // ---------- build the DOM ----------
  const el = (tag, cls, parent = stage) => { const e = document.createElement(tag); if (cls) e.className = cls; parent.appendChild(e); return e; };
  const bar = el("div", "st-bar");
  const icons = NAMES.map(n => {
    if (n === "|") return el("div", "st-div");
    const img = el("img", "st-icon"); img.src = "media/icons/" + n + ".png"; img.alt = ""; return img;
  });
  const dots = NAMES.map(n => n === "|" ? null : el("div", "st-dot"));
  const label = el("div", "st-label");
  const fan = FILES.map((name, i) => {
    const row = el("div", "st-file");
    const img = el("img", "", row); img.src = "media/stage/doc" + i + ".png"; img.alt = "";
    const t = el("span", "", row); t.textContent = name;
    return row;
  });
  const explorer = el("div", "st-explorer"); explorer.textContent = "Open in Explorer ›";
  const previews = el("div", "st-previews");
  ["News - Browser", "Recipes - Browser"].forEach((title, i) => {
    const f = el("figure", "", previews);
    const img = el("img", "", f); img.src = "media/stage/thumb" + i + ".png"; img.alt = "";
    const c = el("figcaption", "", f); c.textContent = title;
  });
  const caption = el("div", "st-caption");
  const cursor = el("div", "st-cursor");
  cursor.innerHTML = '<svg viewBox="0 0 40 60" width="34" height="51"><path d="M2 2v42l10-10 7 16 7-3-7-15h14z" fill="#111" /><path d="M5 9v28l7.5-7.5 7 15.5 3-1.5-7-15h10.5z" fill="#fff"/></svg>';
  const winImg = new Image(); winImg.src = "media/stage/music-window.png";

  // ---------- helpers ----------
  const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
  const prog = (t, a, b) => clamp((t - a) / (b - a));
  const easeOut = x => 1 - (1 - x) ** 3;
  const easeInOut = x => x < 0.5 ? 4 * x * x * x : 1 - (-2 * x + 2) ** 3 / 2;
  const smooth = x => x * x * (3 - 2 * x);
  const lerp = (a, b, x) => a + (b - a) * x;
  const span = (t, a, b, f = 0.3) => Math.min(easeOut(prog(t, a, a + f)), 1 - easeInOut(prog(t, b - f, b)));

  function slots(sizes) {
    const widths = NAMES.map((n, i) => n === "|" ? 2 : sizes[i]);
    const total = widths.reduce((s, w) => s + w, 0) + GAP * (NAMES.length - 1) + 8;
    let x = SW / 2 - total / 2; const out = [];
    NAMES.forEach((n, i) => { if (n === "|") x += 4; out.push(x + widths[i] / 2); x += widths[i] + GAP; if (n === "|") x += 4; });
    return out;
  }
  const BC = slots(NAMES.map(() => BASE));
  const at = n => BC[NAMES.indexOf(n)];

  function layout(cx, hover) {
    const sizes = NAMES.map((n, i) => n === "|" ? 2 : BASE * (1 + (MAXM - 1) * Math.exp(-((BC[i] - cx) ** 2) / (2 * (BASE * 1.3) ** 2)) * hover));
    const cs = slots(sizes);
    return { sizes, cs, left: cs[0] - sizes[0] / 2 - PAD, right: cs[cs.length - 1] + sizes[sizes.length - 1] / 2 + PAD };
  }

  // cursor path: [time, x, y]
  const KEYS = [
    [0, 1720, 640], [0.9, at("files"), ICON_Y], [3.0, at("trash"), ICON_Y], [3.5, at("music"), ICON_Y],
    [6.4, at("music"), ICON_Y], [7.7, at("music"), ICON_Y], [8.3, at("downloads"), ICON_Y], [10.7, at("downloads"), ICON_Y],
    [11.3, at("browser"), ICON_Y], [13.1, at("browser"), ICON_Y], [14.2, 1720, 600], [LOOP, 1720, 600]];
  function cursorAt(t) {
    for (let i = 1; i < KEYS.length; i++) {
      if (t <= KEYS[i][0]) {
        const [t0, x0, y0] = KEYS[i - 1], [t1, x1, y1] = KEYS[i];
        const e = easeInOut(prog(t, t0, t1));
        return [lerp(x0, x1, e), lerp(y0, y1, e)];
      }
    }
    return [KEYS[KEYS.length - 1][1], KEYS[KEYS.length - 1][2]];
  }
  const CLICKS = [["music", 3.62, 3.8], ["music", 6.55, 6.72], ["downloads", 8.4, 8.58]];
  const CAPTIONS = [[0.7, 3.3, "Icons grow as you glide past"], [3.45, 5.6, "Click to open: it bounces"],
    [6.2, 7.9, "Click again: genie into the dock"], [8.2, 10.8, "Folders open as stacks"], [11.2, 13.4, "Hover for window previews"]];

  // ---------- one frame ----------
  let lastCaption = "";
  function draw(t) {
    const [cx, cy] = cursorAt(t);
    const near = clamp(1 - (BOTTOM - BASE - 2 * PAD - 30 - cy) / 120);
    const hover = near * (cx < SW + 20 ? 1 : 0);
    const L = layout(cx, hover);
    const top = BOTTOM - BASE - 2 * PAD;
    bar.style.cssText = `left:${L.left}px;top:${top}px;width:${L.right - L.left}px;height:${BASE + 2 * PAD}px`;

    let hovered = null;
    NAMES.forEach((n, i) => {
      const s = L.sizes[i], x = L.cs[i];
      if (n === "|") { icons[i].style.cssText = `left:${x - 1}px;top:${top + 14}px;height:${BASE + 2 * PAD - 28}px`; return; }
      let yoff = 0;
      if (n === "music" && t > 3.8 && t < 5.0) { const u = (t - 3.8) / 1.2; yoff = -Math.abs(Math.sin(u * 2 * Math.PI)) * 62 * (1 - 0.45 * u); }
      const pressed = CLICKS.some(([name, a, b]) => name === n && t >= a && t <= b);
      icons[i].style.cssText = `left:${x - s / 2}px;top:${BOTTOM - PAD - s + yoff}px;width:${s}px;height:${s}px;filter:${pressed ? "brightness(.72)" : "none"}`;
      const running = n === "files" || n === "browser" || (n === "music" && t > 4.7);
      dots[i].style.cssText = `left:${x - 3}px;top:${BOTTOM - 5}px;opacity:${running ? 0.9 : 0}`;
      if (hover > 0.6 && Math.abs(x - cx) < s / 2 + GAP / 2) hovered = { n, x, y: BOTTOM - PAD - s + yoff };
    });
    const busy = (t > 8.45 && t < 10.6) || (t > 11.4 && t < 13.0);
    if (hovered && !busy) {
      label.textContent = LABELS[hovered.n];
      label.style.cssText = `left:${hovered.x}px;top:${hovered.y - 54}px;opacity:1`;
    } else label.style.opacity = 0;

    // window: opens after the bounce, genies into the icon on the second click
    ctx.clearRect(0, 0, SW, SH);
    const mi = NAMES.indexOf("music"), ms = L.sizes[mi], mx = L.cs[mi], iconTop = BOTTOM - PAD - ms;
    if (winImg.complete && winImg.naturalWidth) {
      if (t >= 4.85 && t < 6.75) {
        const a = easeOut(prog(t, 4.85, 5.3)), sc = lerp(0.86, 1, a);
        const w = WIN.w * sc, h = WIN.h * sc, ox = WIN.x + (WIN.w - w) / 2, oy = lerp(WIN.y + 80, WIN.y, a) + (WIN.h - h) / 2;
        ctx.save(); ctx.globalAlpha = a; ctx.shadowColor = "rgba(5,10,40,.45)"; ctx.shadowBlur = 50; ctx.shadowOffsetY = 18;
        ctx.drawImage(winImg, ox, oy, w, h); ctx.restore();
      } else if (t >= 6.75 && t < 7.6) {
        genie(easeInOut(prog(t, 6.75, 7.6)), mx, iconTop, ms);
      }
    }

    // folder fan above Downloads
    const di = NAMES.indexOf("downloads"), fx = L.cs[di];
    const fo = easeOut(prog(t, 8.55, 9.2)) * (1 - easeInOut(prog(t, 10.35, 10.65)));
    fan.forEach((row, k) => {
      const kk = clamp((fo * (FILES.length + 2) - k) / 3), e = easeOut(kk);
      const tx = fx - 6 * k ** 1.6, ty = top - 30 - STEP * (k + 1);
      row.style.cssText = `left:${lerp(fx, tx, e)}px;top:${lerp(top - 20, ty, e)}px;opacity:${e};transform:translate(-100%,-50%) translateX(36px) rotate(${-1.6 * k * e}deg) scale(${lerp(0.4, 1, e)})`;
    });
    const ea = clamp(fo * 1.4 - 0.4);
    explorer.style.cssText = `left:${fx - 6 * 6 ** 1.6}px;top:${top - 30 - STEP * 7}px;opacity:${ea}`;

    // window previews above Browser
    const pa = easeOut(prog(t, 11.45, 11.8)) * (1 - easeInOut(prog(t, 12.8, 13.1)));
    const bi = NAMES.indexOf("browser");
    previews.style.cssText = `left:${Math.max(20, L.cs[bi] - 277)}px;top:${BOTTOM - PAD - L.sizes[bi] - 260}px;opacity:${pa};transform:translateY(${(1 - pa) * 16}px) scale(${lerp(0.94, 1, pa)})`;

    // caption
    let cap = "", capA = 0;
    CAPTIONS.forEach(([a, b, text]) => { const v = span(t, a, b); if (v > capA) { capA = v; cap = text; } });
    if (cap !== lastCaption && cap) { caption.textContent = cap; lastCaption = cap; }
    caption.style.cssText = `opacity:${capA};transform:translate(-50%, ${(1 - capA) * 12}px)`;

    cursor.style.cssText = `left:${cx - 5}px;top:${cy - 5}px`;
  }

  // genie: the window pours into the icon, bottom rows first (same maths as WinDock's own effect)
  function genie(p, iconCx, iconTop, iconS) {
    const { x: MX, y: MY, w, h } = WIN;
    const ys = new Float32Array(h), ws = new Float32Array(h), cxs = new Float32Array(h);
    for (let r = 0; r < h; r++) {
      const v = r / (h - 1);
      const e = smooth(clamp(p * 1.5 - (1 - v) * 0.5)), f = smooth(clamp(p * 2 - (1 - v)));
      ys[r] = (MY + r) * (1 - e) + (iconTop + v * iconS) * e;
      ws[r] = w * (1 - f) + iconS * f;
      cxs[r] = (MX + w / 2) * (1 - f) + iconCx * f;
    }
    ctx.save();
    ctx.globalAlpha = p > 0.85 ? 1 - (p - 0.85) / 0.15 : 1;
    let r = 0;
    for (let Y = Math.floor(ys[0]); Y <= Math.ceil(ys[h - 1]); Y++) {
      while (r < h - 1 && ys[r + 1] <= Y) r++;
      const wi = ws[r];
      if (wi >= 1) ctx.drawImage(winImg, 0, r, w, 1, cxs[r] - wi / 2, Y, wi, 1.5);
    }
    ctx.restore();
  }

  // ---------- scale to fit, run while visible ----------
  const fit = () => {
    const s = screen.clientWidth / SW;
    stage.style.setProperty("--s", s);
    stage.classList.toggle("small", s < 0.45);   // phones: bigger caption and labels so they stay readable
  };
  fit(); addEventListener("resize", fit);

  if (reduce.matches) { winImg.onload = () => draw(5.6); draw(5.6); return; }
  // ?t=7.1 freezes the desktop at that second (handy for checking a scene)
  const fixed = parseFloat(new URLSearchParams(location.search).get("t"));
  if (!isNaN(fixed)) { const go = () => draw(fixed % LOOP); winImg.complete ? go() : (winImg.onload = go); return; }

  let running = false, start = 0, raf = 0, offset = 0;
  const frame = now => {
    const t = ((now - start) / 1000 + offset) % LOOP;
    draw(t);
    raf = requestAnimationFrame(frame);
  };
  const play = () => { if (running) return; running = true; start = performance.now(); raf = requestAnimationFrame(frame); };
  const pause = () => { if (!running) return; running = false; offset = ((performance.now() - start) / 1000 + offset) % LOOP; cancelAnimationFrame(raf); };
  draw(0);
  new IntersectionObserver(es => es.forEach(e => (e.isIntersecting && !document.hidden ? play() : pause())), { threshold: 0.1 }).observe(screen);
  document.addEventListener("visibilitychange", () => (document.hidden ? pause() : screen.getBoundingClientRect().top < innerHeight && play()));
})();
