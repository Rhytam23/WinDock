// The site's menu is a dock that behaves like WinDock: icons grow near the pointer (the same bell curve WinDock
// uses), clicking bounces the icon, and the running dot sits under the section you're reading.
(function () {
  const dock = document.getElementById("dock");
  if (!dock) return;
  const apps = Array.from(dock.querySelectorAll(".app"));
  const MAX = 1.75;
  const reduce = matchMedia("(prefers-reduced-motion: reduce)");
  let raf = 0, px = null;

  const base = () => parseFloat(getComputedStyle(dock).getPropertyValue("--base")) || 52;

  function apply() {
    raf = 0;
    if (px === null) { apps.forEach(a => { a.style.width = a.style.height = ""; }); return; }
    const b = base(), gap = parseFloat(getComputedStyle(dock).columnGap) || 9, sigma = b * 1.3;
    const div = dock.querySelector(".divider");
    const divW = div ? div.getBoundingClientRect().width + 4 + gap : 0;
    // centres at normal size, so the curve doesn't chase itself as the dock widens
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
  }
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

  // running dot follows the section in view
  const bySection = new Map(apps.filter(a => a.dataset.section).map(a => [a.dataset.section, a]));
  const targets = [document.getElementById("top"), ...["watch", "features", "install", "privacy"].map(id => document.getElementById(id))].filter(Boolean);
  const seen = new Map();
  const mark = () => {
    let best = null, bestRatio = 0;
    seen.forEach((ratio, id) => { if (ratio > bestRatio) { best = id; bestRatio = ratio; } });
    bySection.forEach((a, id) => a.classList.toggle("here", id === (best || "top")));
  };
  const io = new IntersectionObserver(entries => {
    entries.forEach(e => seen.set(e.target.id, e.isIntersecting ? e.intersectionRatio : 0));
    mark();
  }, { threshold: [0, 0.15, 0.35, 0.6, 0.85] });
  targets.forEach(t => io.observe(t));

  addEventListener("resize", schedule);
})();
