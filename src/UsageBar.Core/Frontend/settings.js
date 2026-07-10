(function () {
  var WINDOW_KEYS = [];

  var _dirty = false;
  var _envApiKeys = {};
  var _originalEnvSourced = {};
  var _lastSettings = null;

  var _drag = false;
  var _dragScreenX = 0;
  var _dragScreenY = 0;

  (function setupDrag() {
    var header = document.getElementById("settingsHeader");
    header.addEventListener("pointerdown", function (e) {
      if (e.target.tagName === "BUTTON") return;
      _drag = true;
      _dragScreenX = e.screenX;
      _dragScreenY = e.screenY;
      header.setPointerCapture(e.pointerId);
      e.preventDefault();
    });
    header.addEventListener("pointermove", function (e) {
      if (!_drag) return;
      var dx = Math.round(e.screenX - _dragScreenX);
      var dy = Math.round(e.screenY - _dragScreenY);
      _dragScreenX = e.screenX;
      _dragScreenY = e.screenY;
      if ((dx !== 0 || dy !== 0) && window.ipc) {
        window.ipc.postMessage(JSON.stringify({ type: "drag", dx: dx, dy: dy }));
      }
    });
    header.addEventListener("pointerup", function () { _drag = false; });
    header.addEventListener("pointercancel", function () { _drag = false; });
    header.addEventListener("lostpointercapture", function () { _drag = false; });
  })();

  function markDirty() { if (!_dirty) { _dirty = true; updateSaveHint(); } }
  function clearDirty() { _dirty = false; updateSaveHint(); }
  function updateSaveHint() {
    document.getElementById("saveHint").textContent = _dirty ? "Unsaved changes" : "";
  }

  function buildProviderGrid(settings) {
    var grid = document.getElementById("providerGrid");
    grid.innerHTML = "";
    var providers = settings.providers || [];

    providers.forEach(function (pr) {
      var isOAuth = pr.type === "oauth";
      var row = document.createElement("div");
      row.className = "provider-row";

      var nameEl = document.createElement("span");
      nameEl.className = isOAuth ? "provider-row__name provider-row__name--oauth" : "provider-row__name";
      nameEl.textContent = pr.name + (isOAuth ? " (OAuth)" : "");
      nameEl.title = pr.name;
      row.appendChild(nameEl);

      if (isOAuth) {
        // Enabled toggle
        var toggleGroup = document.createElement("span");
        toggleGroup.className = "provider-row__toggle-group";
        var toggleLabel = document.createElement("label");
        toggleLabel.className = "toggle";
        var cb = document.createElement("input");
        cb.type = "checkbox";
        cb.className = "provider-toggle";
        cb.dataset.providerName = pr.name;
        cb.checked = pr.enabled === true;
        cb.addEventListener("change", function () { markDirty(); });
        toggleLabel.appendChild(cb);
        var slider = document.createElement("span");
        slider.className = "toggle__slider";
        toggleLabel.appendChild(slider);
        var toggleText = document.createElement("span");
        toggleText.className = "toggle-inline-label";
        toggleText.textContent = "Enabled";
        toggleGroup.appendChild(toggleText);
        toggleGroup.appendChild(toggleLabel);
        row.appendChild(toggleGroup);

        // Divider between toggle groups
        var divider = document.createElement("span");
        divider.className = "provider-row__divider";
        row.appendChild(divider);

        // Refresh-token toggle
        var refreshGroup = document.createElement("span");
        refreshGroup.className = "provider-row__toggle-group";
        var refreshLabel = document.createElement("label");
        refreshLabel.className = "toggle";
        refreshLabel.title = "Auto-refresh token when expired";
        var refreshCb = document.createElement("input");
        refreshCb.type = "checkbox";
        refreshCb.className = "provider-refresh-toggle";
        refreshCb.dataset.providerName = pr.name;
        refreshCb.checked = pr.refreshToken !== false;
        refreshCb.addEventListener("change", function () { markDirty(); });
        refreshLabel.appendChild(refreshCb);
        var refreshSlider = document.createElement("span");
        refreshSlider.className = "toggle__slider";
        refreshLabel.appendChild(refreshSlider);
        var refreshText = document.createElement("span");
        refreshText.className = "toggle-inline-label";
        refreshText.textContent = "Refresh Token";
        refreshGroup.appendChild(refreshText);
        refreshGroup.appendChild(refreshLabel);
        row.appendChild(refreshGroup);
      } else {
        // Enabled toggle for non-OAuth providers
        var toggleLabel = document.createElement("label");
        toggleLabel.className = "toggle";
        var cb = document.createElement("input");
        cb.type = "checkbox";
        cb.className = "provider-toggle";
        cb.dataset.providerName = pr.name;
        cb.checked = pr.enabled === true;
        cb.addEventListener("change", function () { markDirty(); });
        toggleLabel.appendChild(cb);
        var slider = document.createElement("span");
        slider.className = "toggle__slider";
        toggleLabel.appendChild(slider);
        row.appendChild(toggleLabel);
      }

      if (!isOAuth && pr.credential) {
        var envVal = _envApiKeys[pr.credential] || "";
        var settingsVal = pr.apiKey || "";
        var fromEnv = !!envVal && !settingsVal;
        var displayVal = fromEnv ? envVal : settingsVal;

        var keyInput = document.createElement("input");
        keyInput.type = "text";
        keyInput.placeholder = "API key";
        keyInput.value = displayVal;
        keyInput.dataset.credential = pr.credential;
        if (fromEnv) {
          keyInput.dataset.fromEnv = "1";
          _originalEnvSourced[pr.credential] = true;
        }
        keyInput.addEventListener("input", function () { markDirty(); });
        row.appendChild(keyInput);

        if (fromEnv) {
          var tag = document.createElement("span");
          tag.className = "provider-row__env-tag";
          tag.textContent = "env";
          row.appendChild(tag);
        }
      }

      grid.appendChild(row);
    });
  }

  function buildIconLayoutBars(settings) {
    var layout = (settings.visual && settings.visual.iconLayout) || { mode: "auto", bars: {} };
    var mode = layout.mode || "auto";
    var bars = layout.bars || {};

    document.getElementById("iconLayoutMode").value = mode;
    var barsContainer = document.getElementById("iconLayoutBars");
    var addBtn = document.getElementById("iconLayoutAdd");

    if (mode === "manual") {
      barsContainer.style.display = "flex";
      addBtn.style.display = "inline-block";
    } else {
      barsContainer.style.display = "none";
      addBtn.style.display = "none";
    }

    barsContainer.innerHTML = "";
    var keys = Object.keys(bars);
    if (keys.length === 0) {
      // Add one empty row as starter
      barsContainer.appendChild(createBarRow("", ""));
    } else {
      keys.forEach(function (key) {
        barsContainer.appendChild(createBarRow(key, String(bars[key])));
      });
    }
  }

  function createBarRow(key, value) {
    var row = document.createElement("div");
    row.className = "icon-layout__bar-row";

    var sel = document.createElement("select");
    var availableKeys = WINDOW_KEYS.length ? WINDOW_KEYS : (key ? [key] : []);
    availableKeys.forEach(function (k) {
      var opt = document.createElement("option");
      opt.value = k;
      opt.textContent = k;
      sel.appendChild(opt);
    });
    sel.value = availableKeys.indexOf(key) >= 0 ? key : (availableKeys[0] || "");
    sel.addEventListener("change", function () { markDirty(); });

    var valInput = document.createElement("input");
    valInput.type = "number";
    valInput.placeholder = "pct";
    valInput.min = "1";
    valInput.max = "100";
    valInput.value = value || "25";
    valInput.addEventListener("input", function () { markDirty(); });

    var delBtn = document.createElement("button");
    delBtn.className = "btn btn--danger btn--sm";
    delBtn.textContent = "X";
    delBtn.addEventListener("click", function () {
      row.remove();
      markDirty();
    });

    row.appendChild(sel);
    row.appendChild(valInput);
    row.appendChild(delBtn);
    return row;
  }

  function populateForm(data) {
    var settings = data.settings || {};
    _envApiKeys = data.envApiKeys || {};
    _lastSettings = settings;

    var visual = settings.visual || {};
    document.getElementById("uiScale").value = String(visual.scale || 100);

    var refresh = settings.refresh || {};
    document.getElementById("refreshPeriod").value = String(refresh.minute || 5);

    var notif = settings.notification || {};
    document.getElementById("highPct").value = String(notif.high || 70);
    document.getElementById("criticalPct").value = String(notif.critical || 95);
    document.getElementById("notificationsEnabled").checked = notif.enabled !== false;

    var update = settings.update || {};
    document.getElementById("checkUpdates").checked = update.onStartup !== false;
    document.getElementById("startWithSystem").checked = settings.startWithSystem !== false;

    var tg = notif.telegram || {};
    document.getElementById("tgEnabled").checked = tg.enabled === true;
    document.getElementById("tgToken").value = tg.token || "";
    document.getElementById("tgChatId").value = tg.chatId ? String(tg.chatId) : "";

    var dc = notif.discord || {};
    document.getElementById("dcEnabled").checked = dc.enabled === true;
    document.getElementById("dcWebhook").value = dc.webhookUrl || "";
    document.getElementById("dcUsername").value = dc.username || "";

    buildProviderGrid(settings);
    buildIconLayoutBars(settings);

    var ver = data.version;
    if (ver) {
      document.getElementById("settingsVersion").textContent = ver;
    }

    clearDirty();
  }

  function collectSettings() {
    var settings = {};

    var tgToken = document.getElementById("tgToken").value.trim();
    var tgChatIdStr = document.getElementById("tgChatId").value.trim();
    var tgChatId = tgChatIdStr ? parseInt(tgChatIdStr) : 0;
    var dcWebhook = document.getElementById("dcWebhook").value.trim();
    var dcUsername = document.getElementById("dcUsername").value.trim();
    var mode = document.getElementById("iconLayoutMode").value;

    settings.refresh = {
      minute: parseInt(document.getElementById("refreshPeriod").value) || 5
    };

    settings.notification = {
      high: parseFloat(document.getElementById("highPct").value) || 70,
      critical: parseFloat(document.getElementById("criticalPct").value) || 95,
      enabled: document.getElementById("notificationsEnabled").checked,
      telegram: {
        token: tgToken || null,
        chatId: isNaN(tgChatId) ? 0 : tgChatId,
        enabled: document.getElementById("tgEnabled").checked
      },
      discord: {
        webhookUrl: dcWebhook || null,
        username: dcUsername || null,
        enabled: document.getElementById("dcEnabled").checked
      }
    };

    settings.update = {
      onStartup: document.getElementById("checkUpdates").checked
    };

    settings.visual = {
      scale: parseInt(document.getElementById("uiScale").value) || 100,
      iconLayout: { mode: mode, bars: makeBars() }
    };

    // Build providers array from grid
    var providers = [];
    var rows = document.querySelectorAll("#providerGrid .provider-row");
    rows.forEach(function (row) {
      var nameEl = row.querySelector(".provider-row__name");
      var toggleCb = row.querySelector(".provider-toggle");
      var keyInput = row.querySelector("input[type=text]");
      var name = nameEl ? nameEl.textContent.replace(" (OAuth)", "").trim() : "";
      var isOAuth = nameEl ? nameEl.className.indexOf("provider-row__name--oauth") >= 0 : false;
      var enabled = toggleCb ? toggleCb.checked : false;
      var apiKey = (keyInput && !isOAuth) ? (keyInput.value || null) : null;
      var credential = keyInput ? keyInput.dataset.credential || null : null;

      // Find matching provider from original settings to preserve credential
      var origProviders = _lastSettings ? (_lastSettings.providers || []) : [];
      var orig = null;
      for (var j = 0; j < origProviders.length; j++) {
        if (origProviders[j].name === name) { orig = origProviders[j]; break; }
      }
      if (orig && !credential) credential = orig.credential || null;

      var refreshCb = row.querySelector(".provider-refresh-toggle");
      var refreshToken = refreshCb ? refreshCb.checked : true;

      providers.push(refreshCb
        ? { name: name, type: isOAuth ? "oauth" : "apiKey", credential: credential, apiKey: apiKey, enabled: enabled, refreshToken: refreshToken }
        : { name: name, type: isOAuth ? "oauth" : "apiKey", credential: credential, apiKey: apiKey, enabled: enabled });
    });
    settings.providers = providers;
    settings.startWithSystem = document.getElementById("startWithSystem").checked;

    return settings;
  }

  function makeBars() {
    var bars = {};
    var barRows = document.querySelectorAll("#iconLayoutBars .icon-layout__bar-row");
    barRows.forEach(function (row) {
      var sel = row.querySelector("select");
      var valInp = row.querySelector("input[type=number]");
      var key = (sel.value || "").trim();
      var val = parseFloat(valInp.value);
      if (key && isFinite(val) && val > 0) {
        bars[key] = val;
      }
    });
    return bars;
  }

  function doSave() {
    var saveBtn = document.getElementById("saveBtn");
    saveBtn.textContent = "Saving...";
    saveBtn.disabled = true;

    var settings = collectSettings();

    // Collect which keys are env-sourced (still have fromEnv=1 on the input)
    var envSourced = [];
    var keyInputs = document.querySelectorAll("#providerGrid input[data-credential]");
    keyInputs.forEach(function (inp) {
      if (inp.dataset.fromEnv === "1") {
        envSourced.push(inp.dataset.credential);
      }
    });

    if (window.ipc) {
      window.ipc.postMessage(JSON.stringify({
        type: "settings-save",
        settings: settings,
        envSourcedKeys: envSourced
      }));
    }
  }

  function doClose() { if (window.ipc) window.ipc.postMessage(JSON.stringify({ type: "close" })); }

  function doTestNotification() {
    if (window.ipc) window.ipc.postMessage(JSON.stringify({ type: "test-notification" }));
  }

  document.getElementById("saveBtn").addEventListener("click", doSave);
  document.getElementById("closeBtn").addEventListener("click", doClose);
  document.getElementById("testNotify").addEventListener("click", doTestNotification);

  document.getElementById("iconLayoutMode").addEventListener("change", function () {
    var settings = collectSettings();
    buildIconLayoutBars(settings);
    markDirty();
  });

  document.getElementById("iconLayoutAdd").addEventListener("click", function () {
    document.getElementById("iconLayoutBars").appendChild(createBarRow("", ""));
    markDirty();
  });

  document.getElementById("checkNow").addEventListener("click", function () {
    if (window.ipc) window.ipc.postMessage(JSON.stringify({ type: "check-update" }));
  });

  var formElements = document.querySelectorAll("select, input");
  formElements.forEach(function (el) {
    el.addEventListener("change", markDirty);
    el.addEventListener("input", markDirty);
  });

  window.__loadSettings = function (payload) {
    if (Array.isArray(payload.iconLayoutKeys) && payload.iconLayoutKeys.length) {
      WINDOW_KEYS = payload.iconLayoutKeys.slice();
    }
    populateForm(payload);
  };

  window.__settingsSaved = function () {
    clearDirty();
    var saveBtn = document.getElementById("saveBtn");
    saveBtn.textContent = "Save";
    saveBtn.disabled = false;
    document.getElementById("saveHint").textContent = "Saved";
    setTimeout(function () { document.getElementById("saveHint").textContent = ""; }, 2000);
  };

  window.__updateResult = function (data) {
    var hint = document.getElementById("saveHint");
    hint.textContent = data.text || "";
    setTimeout(function () { hint.textContent = ""; }, 4000);
  };

  window.chrome.webview.addEventListener("message", function (event) {
    var message = event.data || {};
    switch (message.type) {
      case "settings-state": window.__loadSettings(message); break;
      case "settings-saved": window.__settingsSaved(); break;
      case "update-result": window.__updateResult(message); break;
    }
  });

  function signalReady() {
    if (window.ipc) window.ipc.postMessage(JSON.stringify({ type: "ready" }));
  }
  if (document.readyState === "complete" || document.readyState === "interactive") {
    signalReady();
  } else {
    document.addEventListener("DOMContentLoaded", signalReady);
  }
})();