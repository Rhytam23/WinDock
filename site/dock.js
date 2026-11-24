// The playable dock in the hero: icons grow near the pointer (the same bell curve WinDock uses),
// clicking an icon bounces it and marks the app as running. Works with mouse, pen and keyboard.
(function () {
  const dock = document.getElementById("dock");
  const hint = document.getElementById("hint");
  if (!dock) return;
  const apps = Array.from(dock.querySelectorAll(".app"));
  const MAX = 1.8;            // largest icon = 1.8 x normal
  const reduce = matchMedia("(prefers-reduced-motion: reduce)");
  let base = 56, raf = 0, px = null;

  function measure() {
    base = parseFloat(getComputedStyle(dock).getPropertyValue("--base")) || 56;
  }

  function apply() {
    raf = 0;
    const visible = apps.filter(a => a.offsetParent !== null);
    if (px === null) { visible.forEach(a => { a.style.width = a.style.height = ""; }); return; }
    // centres at normal size, so the curve doesn't chase itself as icons grow
    const first = visible[0].getBoundingClientRect();
    const gap = parseFloat(getComputedStyle(dock).columnGap) || 10;
    const sigma = base * 1.3;
    let x = first.left;
    const dockRect = dock.getBoundingClientRect();
    // normal-size layout starts where the dock would start if no icon were magnified
    const natural = visible.length * base + (visible.length - 1) * gap + (dock.querySelector(".divider") ? 5 + gap : 0);
    x = dockRect.left + dockRect.width / 2 - natural / 2;
    visible.forEach((a, i) => {
      if (a.previousElementSibling && a.previousElementSibling.classList.contains("divider")) x += 5 + gap;
      const c = x + base / 2;
      const s = base * (1 + (MAX - 1) * Math.exp(-((c - px) ** 2) / (2 * sigma * sigma)));
      a.style.width = a.style.height = s.toFixed(1) + "px";
      x += base + gap;
    });
  }

  function schedule() { if (!raf) raf = requestAnimationFrame(apply); }

  dock.addEventListener("pointermove", e => {
    if (e.pointerType === "touch") return;
    dock.classList.add("live");
    px = e.clientX; schedule();
    if (hint) hint.classList.add("gone");
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
      if (hint) hint.classList.add("gone");
      if (a.dataset.name === "Trash" || a.dataset.name === "Downloads") return;
      if (!reduce.matches) {
        a.classList.remove("bounce"); void a.offsetWidth; a.classList.add("bounce");
        setTimeout(() => { a.classList.remove("bounce"); a.classList.add("running"); }, 1100);
      } else {
        a.classList.add("running");
      }
    });
  });

  measure();
  addEventListener("resize", () => { measure(); schedule(); });
})();
