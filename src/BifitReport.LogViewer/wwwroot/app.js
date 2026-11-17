"use strict";

const state = {
  sourceId: null,
  fileKey: null,
  fileName: null,
  cursor: null,
  revision: null,
  canFollow: false,
  pollTimer: null,
  loading: false,
  maxLines: 2000,
  filePage: 1,
  filePageCount: 1,
  fileListGeneration: 0,
  contentGeneration: 0,
  contentAbortController: null,
  csrfToken: null
};

const elements = Object.fromEntries([
  "source", "file-filter", "files", "files-prev", "files-next", "files-page", "catalog-note", "current-file", "query", "level", "tail",
  "raw-control", "raw", "follow", "refresh", "copy", "notice", "log", "line-count", "mode",
  "connection", "user", "logout"
].map(id => [id, document.getElementById(id)]));

async function api(url, signal) {
  const response = await fetch(url, { credentials: "same-origin", headers: { "Accept": "application/json" }, signal });
  if (!response.ok) {
    if (response.status === 401) {
      window.location.assign(`/login?returnUrl=${encodeURIComponent(window.location.pathname)}`);
      throw new Error("Сессия завершена.");
    }
    let message = `HTTP ${response.status}`;
    try { message = (await response.json()).detail || message; } catch { /* response is not JSON */ }
    throw new Error(message);
  }
  return response.json();
}

function setConnection(kind, text) {
  elements.connection.textContent = text;
  elements.connection.className = `status status-${kind}`;
}

function showNotice(message) {
  elements.notice.textContent = message;
  elements.notice.classList.toggle("hidden", !message);
}

function formatSize(bytes) {
  if (bytes < 1024) return `${bytes} Б`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} КБ`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} МБ`;
}

function classify(line) {
  if (line.includes(" ERROR ")) return "error";
  if (line.includes(" WARN ")) return "warn";
  if (line.includes(" INFO ")) return "info";
  if (line.includes(" DEBUG ")) return "debug";
  if (line.includes(" TRACE ")) return "trace";
  return "continuation";
}

function appendLines(lines, replace) {
  const stickToBottom = replace || elements.log.scrollHeight - elements.log.scrollTop - elements.log.clientHeight < 80;
  if (replace) elements.log.replaceChildren();
  const fragment = document.createDocumentFragment();
  for (const line of lines) {
    const row = document.createElement("div");
    row.className = `log-line ${classify(line)}`;
    row.textContent = line;
    fragment.appendChild(row);
  }
  elements.log.appendChild(fragment);
  while (elements.log.childElementCount > state.maxLines) elements.log.firstElementChild.remove();
  elements["line-count"].textContent = `${elements.log.childElementCount} строк`;
  if (stickToBottom) elements.log.scrollTop = elements.log.scrollHeight;
}

async function loadSettings() {
  const settings = await api("/api/v1/settings");
  elements.user.textContent = settings.user;
  state.csrfToken = settings.csrfToken;
  elements["raw-control"].classList.toggle("hidden", !settings.allowRawLogs);
  elements.tail.value = String(settings.defaultTailLines);
  state.maxLines = settings.maximumTailLines;
}

async function loadSources() {
  const sources = await api("/api/v1/sources");
  elements.source.replaceChildren();
  for (const source of sources) {
    const option = document.createElement("option");
    option.value = source.id;
    option.textContent = source.available ? source.displayName : `${source.displayName} — недоступен`;
    option.disabled = !source.available;
    elements.source.appendChild(option);
  }
  const first = sources.find(source => source.available);
  if (!first) throw new Error("Нет доступных источников журналов.");
  state.sourceId = first.id;
  elements.source.value = first.id;
  await loadFiles();
}

async function loadFiles() {
  stopPolling();
  cancelContentLoad();
  const generation = ++state.fileListGeneration;
  const sourceId = state.sourceId;
  const filterValue = elements["file-filter"].value.trim();
  state.fileKey = null;
  elements.files.replaceChildren();
  elements["current-file"].textContent = "Выберите журнал";
  appendLines([], true);
  const filter = encodeURIComponent(filterValue);
  const page = await api(`/api/v1/sources/${encodeURIComponent(sourceId)}/files?page=${state.filePage}&pageSize=200&filter=${filter}`);
  if (generation !== state.fileListGeneration || sourceId !== state.sourceId || filterValue !== elements["file-filter"].value.trim()) return;
  state.filePage = page.page;
  state.filePageCount = Math.max(1, Math.ceil(page.total / page.pageSize));
  elements["files-page"].textContent = `${state.filePage} / ${state.filePageCount}`;
  elements["files-prev"].disabled = state.filePage <= 1;
  elements["files-next"].disabled = state.filePage >= state.filePageCount;
  for (const file of page.items) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "file";
    button.dataset.key = file.key;
    button.setAttribute("role", "option");

    const name = document.createElement("div");
    name.className = "file-name";
    name.textContent = file.relativePath;
    const meta = document.createElement("div");
    meta.className = "file-meta";
    const size = document.createElement("span");
    size.textContent = formatSize(file.size);
    const date = document.createElement("span");
    date.textContent = new Date(file.lastWriteTime).toLocaleString("ru-RU");
    meta.append(size, date);
    button.append(name, meta);
    button.addEventListener("click", () => selectFile(file, button));
    elements.files.appendChild(button);
  }
  elements["catalog-note"].textContent = page.catalogTruncated ? "Список ограничен настройкой MaximumCatalogFiles." : `${page.total} файлов`;
}

async function selectFile(file, button) {
  for (const item of elements.files.querySelectorAll(".file")) item.classList.remove("active");
  button.classList.add("active");
  state.fileKey = file.key;
  state.fileName = file.relativePath;
  elements["current-file"].textContent = file.relativePath;
  await loadContent();
}

function contentParameters() {
  const params = new URLSearchParams({
    tail: elements.tail.value,
    query: elements.query.value.trim(),
    level: elements.level.value,
    raw: String(elements.raw.checked)
  });
  return params.toString();
}

async function loadContent() {
  if (!state.fileKey) return;
  cancelContentLoad();
  const generation = ++state.contentGeneration;
  const sourceId = state.sourceId;
  const fileKey = state.fileKey;
  const controller = new AbortController();
  state.contentAbortController = controller;
  state.loading = true;
  stopPolling();
  showNotice("");
  try {
    const content = await api(`/api/v1/sources/${encodeURIComponent(sourceId)}/files/${encodeURIComponent(fileKey)}/content?${contentParameters()}`, controller.signal);
    if (generation !== state.contentGeneration || sourceId !== state.sourceId || fileKey !== state.fileKey) return;
    appendLines(content.lines, true);
    state.cursor = content.cursor;
    state.revision = content.revision;
    state.canFollow = content.canFollow;
    elements.follow.disabled = !content.canFollow;
    elements.mode.textContent = content.redacted ? "чувствительные данные маскируются" : "RAW: данные не маскируются";
    if (content.scanTruncated) showNotice("Поиск выполнен только в последних байтах файла: увеличьте MaximumScanBytes для более глубокой истории.");
    setConnection(content.canFollow && elements.follow.checked ? "live" : "idle", content.canFollow && elements.follow.checked ? "live" : "пауза");
  } catch (error) {
    if (error.name === "AbortError") return;
    setConnection("error", "ошибка");
    showNotice(error.message);
  } finally {
    if (generation !== state.contentGeneration) return;
    state.contentAbortController = null;
    state.loading = false;
    schedulePolling();
  }
}

function cancelContentLoad() {
  state.contentGeneration++;
  if (state.contentAbortController !== null) state.contentAbortController.abort();
  state.contentAbortController = null;
  state.loading = false;
}

function schedulePolling(delay = 2000) {
  stopPolling();
  if (!state.fileKey || !state.canFollow || !elements.follow.checked || state.cursor === null || !state.revision) return;
  state.pollTimer = window.setTimeout(pollUpdates, delay);
}

function stopPolling() {
  if (state.pollTimer !== null) window.clearTimeout(state.pollTimer);
  state.pollTimer = null;
}

async function pollUpdates() {
  if (state.loading || !state.fileKey) return schedulePolling();
  const sourceId = state.sourceId;
  const fileKey = state.fileKey;
  try {
    const params = new URLSearchParams({
      cursor: String(state.cursor),
      revision: state.revision,
      raw: String(elements.raw.checked),
      query: elements.query.value.trim(),
      level: elements.level.value
    });
    const update = await api(`/api/v1/sources/${encodeURIComponent(sourceId)}/files/${encodeURIComponent(fileKey)}/updates?${params}`);
    if (sourceId !== state.sourceId || fileKey !== state.fileKey) return;
    if (update.reset) return loadContent();
    appendLines(update.lines, false);
    state.cursor = update.cursor;
    state.revision = update.revision;
    setConnection("live", "live");
    schedulePolling(update.hasMore ? 25 : 2000);
  } catch (error) {
    setConnection("error", "повтор");
    showNotice(error.message);
    schedulePolling(5000);
  }
}

let fileFilterTimer;
elements.source.addEventListener("change", async () => { state.sourceId = elements.source.value; state.filePage = 1; await loadFiles(); });
elements["file-filter"].addEventListener("input", () => {
  window.clearTimeout(fileFilterTimer);
  state.filePage = 1;
  fileFilterTimer = window.setTimeout(loadFiles, 300);
});
elements["files-prev"].addEventListener("click", async () => { if (state.filePage > 1) { state.filePage--; await loadFiles(); } });
elements["files-next"].addEventListener("click", async () => { if (state.filePage < state.filePageCount) { state.filePage++; await loadFiles(); } });
elements.refresh.addEventListener("click", loadContent);
elements.query.addEventListener("keydown", event => { if (event.key === "Enter") loadContent(); });
elements.level.addEventListener("change", loadContent);
elements.tail.addEventListener("change", loadContent);
elements.raw.addEventListener("change", loadContent);
elements.follow.addEventListener("change", () => {
  setConnection(elements.follow.checked ? "live" : "idle", elements.follow.checked ? "live" : "пауза");
  schedulePolling(0);
});
elements.copy.addEventListener("click", async () => {
  const text = [...elements.log.children].map(line => line.textContent).join("\n");
  await navigator.clipboard.writeText(text);
  elements.copy.textContent = "Скопировано";
  window.setTimeout(() => { elements.copy.textContent = "Копировать"; }, 1200);
});
elements.logout.addEventListener("click", async () => {
  const response = await fetch("/auth/logout", {
    method: "POST",
    credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": state.csrfToken }
  });
  if (!response.ok) throw new Error(`Не удалось выйти: HTTP ${response.status}`);
  window.location.assign("/login");
});

(async () => {
  try {
    await loadSettings();
    await loadSources();
    setConnection("idle", "готово");
  } catch (error) {
    setConnection("error", "ошибка");
    showNotice(error.message);
  }
})();
