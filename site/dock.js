// WinDock site behaviour:
// - the opening moment (taskbar slides away, dock rises, icons pop in), started by adding "go" to <html>
// - the dock menu: magnification near the pointer (WinDock's bell curve), bounce on click, dot under the section in view
// - windows "open" as you scroll to them; feature clips play only while on screen
(function () {
  const root = document.documentElement;
  const reduce = matchMedia("(prefers-reduced-motion: reduce)");
  const dock = document.getElementById("dock");
  const apps = dock ? Array.from(dock.querySelectorAll(".app")) : [];
  apps.forEach((a, i) => a.style.setProperty("--i", i));

  // taskbar clock shows the visitor's own time
  const clock = document.getElementById("clock");
  if (clock) {
    const now = new Date();
    clock.innerHTML = now.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" }) + "<br>" + now.toLocaleDateString();
  }

  // opening moment: start once the page has painted
  requestAnimationFrame(() => requestAnimationFrame(() => root.classList.add("go")));

  // ---------- dock magnification ----------
  if (dock) {
    const MAX = 1.75;
    let raf = 0, px = null;
    const base = () => parseFloat(getComputedStyle(dock).getPropertyValue("--base")) || 52;
    const apply = () => {
      raf = 0;
      if (px === null) { apps.forEach(a => { a.style.width = a.style.height = ""; }); return; }
      const b = base(), gap = parseFloat(getComputedStyle(dock).columnGap) || 9, sigma = b * 1.3;
      const div = dock.querySelector(".divider");
      const divW = div ? div.getBoundingClientRect().width + 4 + gap : 0;
      const natural = apps.length * b + (apps.length - 1) * gap + divW;
      const r = dock.getBoundingClientRect();
      let x = r.left + r.width / 2 - natural / 2;
      apps.forEach(a => {
        if (a.previousElementSibling === div) x += divW;
        const c = x + b / 2;
        const s = b * (1 + (MAX - 1) * Math.exp(-((c - px) ** 2) / (2 * sigma * sigma)));
        a.style.width = a.style.height = s.toFixed(1) + "px";
        x += b + gap;
      });
    };
    const schedule = () => { if (!raf) raf = requestAnimationFrame(apply); };
    dock.addEventListener("pointermove", e => {
      if (e.pointerType === "touch") return;
      dock.classList.add("live"); px = e.clientX; schedule();
    });
    dock.addEventListener("pointerleave", () => { dock.classList.remove("live"); px = null; schedule(); });
    apps.forEach(a => {
      a.setAttribute("aria-label", a.dataset.name);
      a.addEventListener("focus", () => {
        if (!a.matches(":focus-visible")) return;
        const r = a.getBoundingClientRect(); px = r.left + r.width / 2; schedule();
      });
      a.addEventListener("blur", () => { px = null; schedule(); });
      a.addEventListener("click", () => {
        if (reduce.matches) return;
        a.classList.remove("bounce"); void a.offsetWidth; a.classList.add("bounce");
        setTimeout(() => a.classList.remove("bounce"), 900);
      });
    });
    addEventListener("resize", schedule);

    // running dot follows the section in view
    const bySection = new Map(apps.filter(a => a.dataset.section).map(a => [a.dataset.section, a]));
    const seen = new Map();
    const io = new IntersectionObserver(entries => {
      entries.forEach(e => seen.set(e.target.id, e.isIntersecting ? e.intersectionRatio : 0));
      let best = "top", ratio = 0;
      seen.forEach((v, id) => { if (v > ratio) { best = id; ratio = v; } });
      bySection.forEach((a, id) => a.classList.toggle("here", id === best));
    }, { threshold: [0, 0.15, 0.35, 0.6, 0.85] });
    ["top", "watch", "features", "install", "privacy"].forEach(id => { const el = document.getElementById(id); if (el) io.observe(el); });
  }

  // ---------- windows open as you reach them ----------
  const wins = document.querySelectorAll(".window");
  if ("IntersectionObserver" in window) {
    const opener = new IntersectionObserver(entries => entries.forEach(e => {
      if (e.isIntersecting) { e.target.classList.add("open"); opener.unobserve(e.target); }
    }), { threshold: 0.12, rootMargin: "0px 0px -8% 0px" });
    wins.forEach(w => opener.observe(w));
  } else {
    wins.forEach(w => w.classList.add("open"));
  }

  // ---------- the film: a clean poster with one play button; the browser's controls appear once it plays ----------
  const film = document.querySelector(".film");
  const play = document.querySelector(".player .play");
  if (film && play) {
    film.removeAttribute("controls");
    play.hidden = false;
    play.addEventListener("click", () => {
      play.hidden = true;
      film.setAttribute("controls", "");
      const p = film.play(); if (p && p.catch) p.catch(() => {});
      film.focus();
    });
  }

  // ---------- auto-hide: the page dock steps aside while it would sit on top of the film's own dock ----------
  if (dock && film) {
    let peek = false, queued = false;
    const update = () => {
      queued = false;
      const r = film.getBoundingClientRect(), d = dock.offsetHeight + 40, line = innerHeight - d;
      const over = r.top < line && r.bottom > line - 10 && r.bottom < innerHeight + d * 2;
      dock.classList.toggle("away", over);
      dock.classList.toggle("peek", over && peek);
    };
    const later = () => { if (!queued) { queued = true; requestAnimationFrame(update); } };
    addEventListener("scroll", later, { passive: true });
    addEventListener("resize", later);
    addEventListener("pointermove", e => {
      if (e.pointerType !== "mouse") return;
      const near = e.clientY > innerHeight - (peek ? dock.offsetHeight + 40 : 24);
      if (near !== peek) { peek = near; later(); }
    }, { passive: true });
    document.documentElement.addEventListener("mouseleave", () => { if (peek) { peek = false; later(); } });
    update();
  }

  // ---------- feature clips: play while visible; with reduced motion, show controls instead ----------
  const clips = document.querySelectorAll(".clip");
  if (reduce.matches) {
    clips.forEach(v => v.setAttribute("controls", ""));
  } else {
    const player = new IntersectionObserver(entries => entries.forEach(e => {
      const v = e.target;
      if (e.isIntersecting) { v.preload = "auto"; const p = v.play(); if (p && p.catch) p.catch(() => {}); }
      else v.pause();
    }), { threshold: 0.35 });
    clips.forEach(v => player.observe(v));
  }
})();
