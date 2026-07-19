function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  return node;
}

function providerIcon(iconKey, title) {
  const name = (iconKey || title || "").toLowerCase();
  if (name === "codex" || name === "openai") return iconImage("{{OPENAI_ICON}}", "Codex");
  if (name === "claude") return iconImage("{{CLAUDE_ICON}}", "Claude");
  if (name === "elevenlabs") return iconImage("{{ELEVENLABS_ICON}}", "ElevenLabs");
  if (name === "kilo") return iconImage("{{KILO_ICON}}", "Kilo");
  if (name === "copilot") return iconImage("{{COPILOT_ICON}}", "Copilot");
  if (name === "warp") return iconImage("{{WARP_ICON}}", "Warp");
  if (name === "synthetic") return iconImage("{{SYNTHETIC_ICON}}", "Synthetic");
  if (name === "chutes") return iconImage("{{CHUTES_ICON}}", "Chutes");
  if (name === "zai") return iconImage("{{ZAI_ICON}}", "Zai");
  if (name === "alibaba") return iconImage("{{ALIBABA_ICON}}", "Alibaba");
  if (name === "minimax") return iconImage("{{MINIMAX_ICON}}", "MiniMax");
  if (name === "codebuff") return iconImage("{{CODEBUFF_ICON}}", "Codebuff");
  if (name === "antigravity") return iconImage("{{ANTIGRAVITY_ICON}}", "Antigravity");
  return null;
}

function iconImage(src, label) {
  const image = el("img", "card__provider-icon");
  image.src = src;
  image.alt = label;
  image.decoding = "async";
  return image;
}

function levelOf(remainPct) {
  // Matches IconRenderer.LevelFromPercent: Low <50%, Medium <80%, High <95%, Critical >=95%.
  if (remainPct <= 5) return "critical";
  if (remainPct <= 20) return "high";
  if (remainPct <= 50) return "medium";
  return "low";
}

function resetText(detail) {
  const d = (detail || "").trim();
  if (!d) return "";
  if (d === "now") return "Resets now";
  return "Resets in " + d;
}

function metricRow(m, showLabel) {
  const pct = Number.isFinite(m.percent) ? Math.min(100, Math.max(0, m.percent)) : 0;
  const remain = 100 - pct;
  const level = levelOf(remain);
  const wrap = el("div", "metric");
  const label = (m.label || "").trim();
  var sub = (m.sub || "").trim();
  if (sub || (showLabel && label)) {
    if (showLabel && label) {
      var titleGroup = el("div", "metric__title-group");
      titleGroup.appendChild(el("span", "metric__title", label));
      if (sub) titleGroup.appendChild(el("span", "metric__sub", sub));
      wrap.appendChild(titleGroup);
    } else if (sub) {
      wrap.appendChild(el("span", "metric__sub", sub));
    }
  }
  const bar = el("div", "metric__bar");
  const fill = el("div", "metric__bar-fill");
  fill.dataset.level = level;
  fill.style.width = pct + "%";
  bar.appendChild(fill);
  wrap.appendChild(bar);
  const row = el("div", "metric__row");
  row.appendChild(el("span", "metric__pct", Math.round(pct) + "% used"));
  const reset = resetText(m.detail);
  if (reset) row.appendChild(el("span", "metric__reset", reset));
  wrap.appendChild(row);
  return wrap;
}

function renderCard(card) {
  const hasMetrics = card.metrics && card.metrics.length > 0;
  const isBalance = !hasMetrics && card.lines && card.lines.length > 0;
  const art = el(
    "article",
    [
      "card",
      hasMetrics ? "card--with-details" : "card--header-only",
      isBalance ? "card--balance" : "",
    ]
      .filter(Boolean)
      .join(" "),
  );
  const header = el("header", "card__header");
  const titleRow = el("div", "card__title-row");
  const nameGroup = el("div", "card__name-group");
  nameGroup.appendChild(el("span", "card__name", card.title));
  if (isBalance) {
    nameGroup.appendChild(el("span", "card__value", card.lines[0]));
  } else if (card.plan) {
    nameGroup.appendChild(el("span", "card__plan", card.plan));
  }
  titleRow.appendChild(nameGroup);
  if (card.notice) titleRow.appendChild(el("span", "card__plan card__notice", card.notice));
  var icon = hasMetrics ? providerIcon(card.icon, card.title) : null;
  if (icon) titleRow.appendChild(icon);
  header.appendChild(titleRow);
  art.appendChild(header);
  if (hasMetrics) {
    art.appendChild(el("div", "card__divider"));
    const content = el("div", "card__content");
    const group = el("section", "card__metrics");
    var subCounts = {};
    card.metrics.forEach(function (m) { var s = (m.sub || "").trim() || "__none__"; subCounts[s] = (subCounts[s] || 0) + 1; });
    card.metrics.forEach(function (m) {
      var s = (m.sub || "").trim() || "__none__";
      group.appendChild(metricRow(m, subCounts[s] > 1));
    });
    content.appendChild(group);
    art.appendChild(content);
  }
  return art;
}

function reportSize(desiredWidth) {
  var app = document.getElementById("app");
  if (!app) return;
  var rect = app.getBoundingClientRect();
  var w = Math.ceil(rect.width);
  var h = Math.ceil(rect.height);
  if (h > 0 && window.ipc) {
    window.ipc.postMessage(JSON.stringify({ type: "size", width: w, height: h }));
  }
}

window.__render = function (data) {
  var app = document.getElementById("app");
  app.innerHTML = "";
  var userScale = data.scale && data.scale > 0 ? data.scale / 100 : 1;
  var panel = el("div", "panel panel--tray");
  var body = el("div", "panel__body");
  var stack = el("div", "stack");
  var visible = (data.cards || []).filter(function (card) { return !card.hide; });
  var total = visible.length;
  var maxPerCol = Math.max(6, Math.floor(window.screen.availHeight / 70));
  var cols = total <= maxPerCol ? 1 : Math.ceil(total / maxPerCol);
  var colWidth = 150;
  var desiredWidth = Math.min(120 + cols * colWidth, 900);
  if (cols > 1) {
    stack.classList.add("stack--multi");
    stack.style.gridTemplateColumns = "repeat(" + cols + ", 1fr)";
  }
  visible.forEach(function (card, i) {
    if (i > 0 && cols === 1) stack.appendChild(el("div", "stack__sep"));
    var item = el("div", "stack__item");
    item.appendChild(renderCard(card));
    stack.appendChild(item);
  });
  body.appendChild(stack);
  panel.appendChild(body);
  app.appendChild(panel);

  app.style.width = desiredWidth + "px";
  if (userScale !== 1) {
    app.style.zoom = userScale;
    panel.style.setProperty("--panel-radius", (9 / userScale) + "px");
    panel.style.setProperty("--panel-stroke", (1 / userScale) + "px");
    panel.style.setProperty("--panel-stroke-inset", (1 / userScale) + "px");
  } else {
    app.style.zoom = "";
    panel.style.removeProperty("--panel-radius");
    panel.style.removeProperty("--panel-stroke");
    panel.style.removeProperty("--panel-stroke-inset");
  }

  requestAnimationFrame(function () { return requestAnimationFrame(function () { return reportSize(desiredWidth); }); });
};

function signalReady() {
  if (window.ipc) window.ipc.postMessage(JSON.stringify({ type: "ready" }));
}

if (window.ipc && window.ipc.addMessageListener) {
  window.ipc.addMessageListener(function (message) {
    if (window.__render) window.__render(message || {});
  });
}
if (document.readyState === "complete" || document.readyState === "interactive") {
  signalReady();
} else {
  document.addEventListener("DOMContentLoaded", signalReady);
}
