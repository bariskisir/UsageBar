// Renders the CodexBar tray "MenuCard" DOM using the verbatim styles.css that
// ships with Win-CodexBar (apps/desktop-tauri/src). The class hierarchy mirrors
// MenuCard.tsx / TrayPanel.tsx so the copied CSS applies unchanged.
//
// Data is pushed from Rust via window.__render({ cards: [...] }); the rendered
// height is reported back over the wry IPC bridge so the host window can size
// itself to the content.

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
}

// Usage level mirrors MenuCard.levelOf (keyed off remaining capacity).
function levelOf(remainPct) {
  if (remainPct <= 5) return "critical";
  if (remainPct <= 25) return "high";
  return "normal";
}

function resetText(detail) {
  const d = (detail || "").trim();
  if (!d) return "";
  if (d === "now") return "Resets now";
  return "Resets in " + d;
}

// One metric row — mirrors MenuCard.MetricRow.
function metricRow(m) {
  const pct = Math.min(100, Math.max(0, m.percent));
  const remain = 100 - pct;
  const level = levelOf(remain);

  const wrap = el("div", "menu-metric");
  wrap.appendChild(el("span", "menu-metric__title", m.label));

  const bar = el("div", "menu-metric__bar");
  const fill = el("div", "menu-metric__bar-fill");
  fill.dataset.level = level;
  fill.style.width = pct + "%";
  bar.appendChild(fill);
  wrap.appendChild(bar);

  const row = el("div", "menu-metric__row");
  row.appendChild(el("span", "menu-metric__pct", Math.round(pct) + "% used"));
  const reset = resetText(m.detail);
  if (reset) row.appendChild(el("span", "menu-metric__reset", reset));
  wrap.appendChild(row);

  return wrap;
}

// One provider card — mirrors MenuCard.
function menuCard(card) {
  const hasMetrics = card.metrics && card.metrics.length > 0;
  const isBalance = !hasMetrics && card.lines && card.lines.length > 0;
  const art = el(
    "article",
    [
      "menu-card",
      hasMetrics ? "menu-card--with-details" : "menu-card--header-only",
      isBalance ? "menu-card--balance" : "",
    ]
      .filter(Boolean)
      .join(" "),
  );

  const header = el("header", "menu-card__header");
  const titleRow = el("div", "menu-card__title-row");
  const nameGroup = el("div", "menu-card__name-group");
  nameGroup.appendChild(el("span", "menu-card__name", card.title));

  if (isBalance) {
    // Balance providers: value right-aligned on the same row as the name
    // (reuses the email slot, which the tray CSS right-aligns).
    nameGroup.appendChild(el("span", "menu-card__email", card.lines[0]));
  } else if (card.plan) {
    // Metric providers: show the plan/tier (e.g. "Plus", "Max") on the right.
    nameGroup.appendChild(el("span", "menu-card__plan-inline", card.plan));
  }
  titleRow.appendChild(nameGroup);
  header.appendChild(titleRow);
  art.appendChild(header);

  if (hasMetrics) {
    art.appendChild(el("div", "menu-card__divider"));
    const content = el("div", "menu-card__content");
    const group = el("section", "menu-card__group menu-card__metrics");
    card.metrics.forEach((m) => group.appendChild(metricRow(m)));
    content.appendChild(group);
    art.appendChild(content);
  }

  return art;
}

function reportHeight() {
  const surface = document.querySelector(".menu-surface");
  if (!surface) return;
  const h = Math.ceil(surface.getBoundingClientRect().height);
  if (h > 0 && window.ipc) {
    window.ipc.postMessage(JSON.stringify({ type: "height", value: h }));
  }
}

window.__render = function (data) {
  const root = document.getElementById("root");
  root.innerHTML = "";

  const surface = el("div", "menu-surface menu-surface--tray");
  const body = el("div", "menu-surface__body");
  const stack = el("div", "menu-stack");

  (data.cards || []).forEach((card, i) => {
    if (i > 0) stack.appendChild(el("div", "menu-stack__sep"));
    const item = el("div", "menu-stack__item");
    item.appendChild(menuCard(card));
    stack.appendChild(item);
  });

  body.appendChild(stack);
  surface.appendChild(body);
  root.appendChild(surface);

  // Report height after layout + font metrics settle.
  requestAnimationFrame(() => requestAnimationFrame(reportHeight));
};

// Tell Rust we are ready so it can push the current snapshot.
function signalReady() {
  if (window.ipc) window.ipc.postMessage(JSON.stringify({ type: "ready" }));
}
if (document.readyState === "complete" || document.readyState === "interactive") {
  signalReady();
} else {
  document.addEventListener("DOMContentLoaded", signalReady);
}
