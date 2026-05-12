const state = {
  profiles: [],
  detected: null,
  selectedProfile: null,
  selectedResource: null,
  lastExportRoot: null,
  tableEditBase: null,
  jobTimer: null
};

const $ = (id) => document.getElementById(id);

const api = async (url, body) => {
  const response = await fetch(url, {
    method: body === undefined ? "GET" : "POST",
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const payload = await response.json();
  if (!response.ok || !payload.ok) {
    throw new Error(payload.message || payload.errorCode || `HTTP ${response.status}`);
  }
  return payload.data;
};

const setStatus = (message) => {
  $("statusText").textContent = message;
};

const writeLog = (target, value) => {
  target.textContent = typeof value === "string" ? value : JSON.stringify(value, null, 2);
};

const selectedProfileId = () => $("profileSelect").value || state.selectedProfile?.id;
const targetProfileId = () => $("targetProfileSelect").value || selectedProfileId();

const formatLocalTime = (value) => new Date(value).toLocaleString("zh-CN", {
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit"
});

const auditActionText = (action) => action === "revert" ? "回滚" : "保存";

const previewKindText = (kind) => ({
  1: "文本",
  2: "十六进制",
  3: "图片",
  4: "音频",
  5: "字体"
}[kind] || "不可预览");

const presets = {
  cn: "C:\\WeGameApps\\rail_apps\\流放之路：降临(2002052)",
  global: "E:\\PSAutoRecover\\ui\\rood\\Grinding Gear Games\\Path of Exile 2"
};

async function refreshProfiles() {
  state.profiles = await api("/api/profiles");
  $("profileCount").textContent = String(state.profiles.length);
  $("profileSelect").innerHTML = "";
  $("targetProfileSelect").innerHTML = "";
  for (const profile of state.profiles) {
    const option = document.createElement("option");
    option.value = profile.id;
    option.textContent = `${profile.displayName} (${profile.platform})`;
    $("profileSelect").appendChild(option);
    $("targetProfileSelect").appendChild(option.cloneNode(true));
  }
  state.selectedProfile = state.profiles[0] || null;
  if (state.profiles.length > 1) {
    $("targetProfileSelect").value = state.profiles[1].id;
  }
  $("buildNativeIndexBtn").disabled = !state.selectedProfile;
  $("patchDryRunBtn").disabled = !state.selectedProfile;
  $("patchReadinessBtn").disabled = !state.selectedProfile;
  $("nativePlanBtn").disabled = !state.selectedProfile;
  $("nativeDryBundleBtn").disabled = !state.selectedProfile;
  $("nativeIndexPlanBtn").disabled = !state.selectedProfile;
  $("patchBuildBtn").disabled = !state.selectedProfile;
  $("refreshBuildsBtn").disabled = !state.selectedProfile;
  $("refreshOverlayBtn").disabled = !state.selectedProfile;
  $("exportTranslationBtn").disabled = !state.selectedProfile;
  $("importTranslationBtn").disabled = !state.selectedProfile;
  $("previewScriptBtn").disabled = !state.selectedProfile;
  $("applyScriptBtn").disabled = !state.selectedProfile;
  $("bulkExportBtn").disabled = !state.selectedProfile;
  $("bulkSignatureBtn").disabled = !state.selectedProfile;
  $("matchResourcesBtn").disabled = !state.selectedProfile;
  $("bulkImportBtn").disabled = !state.selectedProfile;
  setStatus(state.selectedProfile ? "已加载客户端配置" : "没有客户端配置");
  if (state.selectedProfile) refreshBuildHistory();
  if (state.selectedProfile) refreshOverlayList();
}

async function detectClient() {
  setStatus("正在检测客户端...");
  state.detected = await api("/api/profiles/detect", {
    rootPath: $("rootPathInput").value.trim(),
    oodleSearchPath: $("oodlePathInput").value.trim() || null
  });
  writeLog($("detectOutput"), state.detected);
  $("saveProfileBtn").disabled = !state.detected.detected;
  setStatus(state.detected.detected ? "检测完成，可以保存配置" : "未检测到支持的客户端");
}

async function runDiagnostics() {
  setStatus("正在自检...");
  const result = await api("/api/diagnostics");
  writeLog($("detectOutput"), result);
  setStatus(result.workspaceWritable ? `自检通过：${result.profileCount} 个配置` : "自检发现问题");
}

async function saveProfile() {
  if (!state.detected) return;
  setStatus("正在保存配置...");
  const profile = await api("/api/profiles", {
    displayName: `${state.detected.platform} POE2`,
    rootPath: state.detected.rootPath,
    platform: state.detected.platform,
    entryKind: state.detected.entryKind,
    contentGgpkPath: state.detected.contentGgpkPath,
    bundles2Path: state.detected.bundles2Path,
    indexPath: state.detected.indexPath,
    oodleStatus: state.detected.oodleStatus,
    clientFingerprint: state.detected.clientFingerprint
  });
  await refreshProfiles();
  $("profileSelect").value = profile.id;
  state.selectedProfile = profile;
  setStatus("配置已保存");
}

async function quickConnect() {
  setStatus("正在一键接入客户端...");
  const profile = await api("/api/profiles/detect-and-save", {
    rootPath: $("rootPathInput").value.trim(),
    oodleSearchPath: $("oodlePathInput").value.trim() || null
  });
  await refreshProfiles();
  $("profileSelect").value = profile.id;
  state.selectedProfile = profile;
  writeLog($("detectOutput"), profile);
  setStatus("客户端已接入");
}

async function startNativeIndexJob() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  setStatus("正在启动索引任务...");
  const job = await api("/api/jobs/native/bundles2/build-resource-index", {
    profileId,
    indexPath: state.selectedProfile?.indexPath || null,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  trackJob(job.id);
}

function trackJob(jobId) {
  clearInterval(state.jobTimer);
  state.jobTimer = setInterval(async () => {
    try {
      const job = await api(`/api/jobs/${jobId}`);
      $("jobProgress").style.width = `${job.progressPercent}%`;
      $("jobMessage").textContent = job.message;
      setStatus(`${job.kind}: ${job.message}`);
      if (job.status === 2 || job.status === 3) {
        clearInterval(state.jobTimer);
        if (job.status === 3) {
          writeLog($("actionOutput"), job);
          setStatus(job.errorMessage || "任务失败");
          return;
        }
        if (job.resultJson) handleJobResult(job);
      }
    } catch (error) {
      clearInterval(state.jobTimer);
      setStatus(error.message);
    }
  }, 500);
}

function handleJobResult(job) {
  const result = JSON.parse(job.resultJson);
  if (job.kind === "patch-build") {
    writeLog($("actionOutput"), result);
    setStatus(result.zipPath ? `补丁已生成：${result.zipPath}` : job.message);
    refreshBuildHistory();
    return;
  }

  writeLog($("detectOutput"), result);
  searchResources();
}

async function searchResources() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  setStatus("正在搜索资源...");
  const result = await api("/api/resources/search", {
    profileId,
    query: $("searchInput").value.trim() || null,
    skip: 0,
    take: 120
  });
  $("resourceTotal").textContent = String(result.total);
  renderResources(result.items);
  setStatus(`找到 ${result.total} 个资源`);
}

async function bulkExportResources() {
  const profileId = selectedProfileId();
  const query = $("searchInput").value.trim();
  if (!profileId || !query) {
    setStatus("批量导出需要先输入搜索条件");
    return;
  }

  setStatus("正在批量导出资源...");
  const result = await api("/api/resources/bulk-export", {
    profileId,
    query,
    take: 200,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  state.lastExportRoot = result.exportRoot;
  writeLog($("actionOutput"), {
    matched: result.matched,
    exported: result.exported,
    exportRoot: result.exportRoot,
    warnings: result.warnings
  });
  setStatus(`批量导出完成：${result.exported}/${result.matched}`);
}

async function bulkSignatureResources() {
  const profileId = selectedProfileId();
  const query = $("searchInput").value.trim();
  if (!profileId || !query) {
    setStatus("批量特征需要先输入搜索条件");
    return;
  }

  setStatus("正在批量提取特征...");
  const result = await api("/api/resources/bulk-signature", {
    profileId,
    query,
    take: 200,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  writeLog($("actionOutput"), result);
  setStatus(`批量特征完成：${result.signed}/${result.matched}`);
}

async function matchResources() {
  const sourceProfileId = selectedProfileId();
  const targetId = targetProfileId();
  const query = $("searchInput").value.trim();
  if (!sourceProfileId || !targetId || !query) {
    setStatus("匹配资源需要当前配置、目标配置和搜索条件");
    return;
  }

  setStatus("正在匹配资源...");
  const result = await api("/api/resources/match", {
    sourceProfileId,
    targetProfileId: targetId,
    query,
    take: 200,
    sourceOodlePath: $("oodlePathInput").value.trim() || null,
    targetOodlePath: $("oodlePathInput").value.trim() || null
  });
  writeLog($("actionOutput"), result);
  setStatus(`匹配完成：${result.matched}/${result.sourceMatched}`);
}

async function bulkImportOverlay() {
  const profileId = selectedProfileId();
  if (!profileId || !state.lastExportRoot) {
    setStatus("请先批量导出，再从导出目录导入覆盖");
    return;
  }

  setStatus("正在从导出目录导入覆盖...");
  const result = await api("/api/resources/bulk-import-overlay", {
    profileId,
    exportRoot: state.lastExportRoot,
    take: 500
  });
  writeLog($("actionOutput"), result);
  setStatus(`导入覆盖完成：${result.imported}`);
  refreshOverlayList();
}

function renderResources(items) {
  const list = $("resourceList");
  list.innerHTML = "";
  for (const item of items) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "resource-item";
    button.innerHTML = `
      <span class="resource-path">${item.virtualPath}</span>
      <span class="resource-meta">${item.extension || "file"} · ${item.size}</span>
    `;
    button.addEventListener("click", () => previewResource(item, button));
    list.appendChild(button);
  }
}

async function previewResource(resource, button) {
  state.selectedResource = resource;
  for (const item of document.querySelectorAll(".resource-item")) {
    item.classList.remove("selected");
  }
  button.classList.add("selected");
  $("selectedPath").textContent = resource.virtualPath;
  $("previewKind").textContent = "加载中";
  setStatus("正在读取预览...");
  const preview = await api("/api/preview", {
    profileId: resource.profileId,
    virtualPath: resource.virtualPath,
    limit: 65536,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  renderPreview(preview);
  $("saveOverlayBtn").disabled = preview.kind !== 1;
  $("exportResourceBtn").disabled = false;
  $("signatureBtn").disabled = false;
  $("replaceResourceBtn").disabled = false;
  $("inspectTableBtn").disabled = resource.kind !== 2;
  $("batchOverlayBtn").disabled = preview.kind !== 1;
  $("batchReplaceBtn").disabled = preview.kind !== 1;
  setStatus("预览已加载");
}

function renderPreview(preview) {
  const media = $("mediaPreview");
  const text = $("previewText");
  $("previewKind").textContent = previewKindText(preview.kind);
  media.classList.add("hidden");
  media.innerHTML = "";
  $("tableEditor").classList.add("hidden");
  $("tableEditor").innerHTML = "";
  state.tableEditBase = null;
  $("saveTableBtn").disabled = true;
  text.classList.remove("hidden");
  text.readOnly = preview.kind !== 1;
  text.value = preview.text || preview.hex || preview.message || "";

  if (preview.kind === 3 && preview.base64Content && preview.mediaType) {
    media.innerHTML = `<img alt="资源预览" src="data:${preview.mediaType};base64,${preview.base64Content}">`;
  } else if (preview.kind === 4 && preview.base64Content && preview.mediaType) {
    media.innerHTML = `<audio controls src="data:${preview.mediaType};base64,${preview.base64Content}"></audio>`;
  } else if (preview.kind === 5 && preview.base64Content && preview.mediaType) {
    const family = `font_${Date.now()}`;
    const style = document.createElement("style");
    style.textContent = `@font-face{font-family:${family};src:url(data:${preview.mediaType};base64,${preview.base64Content}) format("truetype");}`;
    media.appendChild(style);
    const sample = document.createElement("div");
    sample.className = "font-sample";
    sample.style.fontFamily = family;
    sample.textContent = "流放之路 Path of Exile 2 0123456789";
    media.appendChild(sample);
  } else {
    return;
  }

  media.classList.remove("hidden");
  text.classList.add("hidden");
}

async function saveOverlay() {
  if (!state.selectedResource) return;
  setStatus("正在保存覆盖...");
  const result = await api("/api/overlay/save-text", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    text: $("previewText").value
  });
  writeLog($("actionOutput"), result);
  setStatus("覆盖已保存");
  refreshOverlayList();
}

async function exportResource() {
  if (!state.selectedResource) return;
  setStatus("正在导出资源...");
  const result = await api("/api/resources/export", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  const link = document.createElement("a");
  link.href = `data:${result.contentType};base64,${result.base64Content}`;
  link.download = result.fileName || "resource.bin";
  link.click();
  writeLog($("actionOutput"), {
    virtualPath: result.virtualPath,
    size: result.size,
    contentType: result.contentType,
    warnings: result.warnings
  });
  setStatus(`资源已导出：${result.fileName}`);
}

async function extractSignature() {
  if (!state.selectedResource) return;
  setStatus("正在提取特征...");
  const result = await api("/api/resources/signature", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  writeLog($("actionOutput"), result);
  setStatus("特征已提取");
}

function chooseReplacementFile() {
  if (!state.selectedResource) return;
  $("replaceResourceInput").value = "";
  $("replaceResourceInput").click();
}

async function replaceResourceWithFile(file) {
  if (!state.selectedResource || !file) return;
  setStatus("正在保存替换资源...");
  const base64Content = await readFileAsBase64(file);
  const result = await api("/api/overlay/save-binary", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    base64Content
  });
  writeLog($("actionOutput"), result);
  setStatus(`替换已保存：${file.name}`);
  refreshOverlayList();
}

async function inspectTable() {
  if (!state.selectedResource) return;
  setStatus("正在检查表格...");
  const result = await api("/api/tables/inspect", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  writeLog($("actionOutput"), result);
  renderTableEditor(result);
  setStatus(result.structured ? `表格预览：${result.previewRowCount} 行` : "表格二进制预览已生成");
}

function renderTableEditor(result) {
  const editor = $("tableEditor");
  editor.innerHTML = "";
  editor.classList.add("hidden");
  $("saveTableBtn").disabled = true;
  state.tableEditBase = null;
  if (!result.structured || !result.rows?.length) {
    return;
  }

  const table = document.createElement("table");
  for (const row of result.rows) {
    const tr = document.createElement("tr");
    for (let columnIndex = 0; columnIndex < row.cells.length; columnIndex++) {
      const cell = document.createElement(row.rowNumber === 1 ? "th" : "td");
      const input = document.createElement("input");
      input.value = row.cells[columnIndex];
      input.dataset.rowNumber = String(row.rowNumber);
      input.dataset.columnIndex = String(columnIndex);
      input.dataset.original = row.cells[columnIndex];
      cell.appendChild(input);
      tr.appendChild(cell);
    }
    table.appendChild(tr);
  }
  editor.appendChild(table);
  editor.classList.remove("hidden");
  $("previewText").classList.add("hidden");
  $("saveTableBtn").disabled = false;
  state.tableEditBase = result;
}

async function saveTableEdits() {
  if (!state.selectedResource || !state.tableEditBase) return;
  const edits = Array.from($("tableEditor").querySelectorAll("input"))
    .filter(input => input.value !== input.dataset.original)
    .map(input => ({
      rowNumber: Number(input.dataset.rowNumber),
      columnIndex: Number(input.dataset.columnIndex),
      value: input.value
    }));
  if (edits.length === 0) {
    setStatus("表格没有改动");
    return;
  }

  setStatus("正在保存表格覆盖...");
  const result = await api("/api/tables/save", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    edits,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  writeLog($("actionOutput"), result);
  setStatus(`表格已保存：${result.editedCells} 处`);
  refreshOverlayList();
}

function readFileAsBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const value = String(reader.result || "");
      resolve(value.includes(",") ? value.split(",", 2)[1] : value);
    };
    reader.onerror = () => reject(reader.error || new Error("读取文件失败"));
    reader.readAsDataURL(file);
  });
}

async function patchDryRun() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  setStatus("正在执行补丁预检...");
  const result = await api("/api/patch/dry-run", { profileId });
  writeLog($("actionOutput"), result);
  setStatus(`补丁预检完成：${result.totalChanges} 个改动`);
}

async function patchReadiness() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  setStatus("正在检查正式写包条件...");
  const result = await api("/api/patch/readiness", {
    profileId,
    writerKind: 1
  });
  writeLog($("actionOutput"), result);
  setStatus(result.ready ? "正式写包条件已满足" : `正式写包阻塞：${result.blockers.length}`);
}

async function nativePatchPlan() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  setStatus("正在生成写包计划...");
  const result = await api("/api/patch/native-plan", { profileId });
  writeLog($("actionOutput"), result);
  setStatus(result.ready ? `写包计划：${result.totalItems} 项` : `写包计划阻塞：${result.blockers.length}`);
}

async function nativeDryBundle() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  setStatus("正在生成 dry bundle...");
  const result = await api("/api/patch/native-dry-bundle", {
    profileId,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  writeLog($("actionOutput"), result);
  setStatus(`Dry bundle 已生成：${Math.max(1, Math.round(result.size / 1024))} KB`);
}

async function nativeIndexPlan() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  setStatus("正在生成 index 计划...");
  const result = await api("/api/patch/native-index-plan", { profileId });
  writeLog($("actionOutput"), result);
  setStatus(result.ready ? `Index 计划：${result.totalItems} 项` : `Index 计划阻塞：${result.blockers.length}`);
}

async function batchOverlay() {
  const profileId = selectedProfileId();
  const query = $("searchInput").value.trim();
  if (!profileId || !query) {
    setStatus("批量覆盖需要先输入搜索条件");
    return;
  }

  setStatus("正在批量保存覆盖...");
  const result = await api("/api/overlay/batch-save-text", {
    profileId,
    query,
    text: $("previewText").value,
    take: 50
  });
  writeLog($("actionOutput"), result);
  setStatus(`批量覆盖完成：${result.saved}/${result.matched}`);
  refreshOverlayList();
}

async function batchReplaceText() {
  const profileId = selectedProfileId();
  const query = $("searchInput").value.trim();
  const find = $("batchFindInput").value;
  if (!profileId || !query) {
    setStatus("批量替换需要先输入搜索条件");
    return;
  }

  if (!find) {
    setStatus("批量替换需要填写查找内容");
    $("batchFindInput").focus();
    return;
  }

  setStatus("正在批量替换文本...");
  const result = await api("/api/overlay/batch-replace-text", {
    profileId,
    query,
    find,
    replace: $("batchReplaceInput").value,
    take: 80
  });
  writeLog($("actionOutput"), result);
  setStatus(`批量替换完成：${result.changed}/${result.matched}`);
  refreshOverlayList();
}

async function exportTranslationCsv() {
  const profileId = selectedProfileId();
  const query = $("searchInput").value.trim();
  if (!profileId || !query) {
    setStatus("导出翻译需要先输入搜索条件");
    return;
  }

  setStatus("正在导出翻译 CSV...");
  const result = await api("/api/translation/export-csv", {
    profileId,
    query,
    take: 200
  });
  $("translationCsvText").value = result.csv;
  writeLog($("actionOutput"), {
    matched: result.matched,
    exported: result.exported,
    warnings: result.warnings
  });
  setStatus(`翻译 CSV 已导出：${result.exported}/${result.matched}`);
}

async function importTranslationCsv() {
  const profileId = selectedProfileId();
  const csv = $("translationCsvText").value;
  if (!profileId || !csv.trim()) {
    setStatus("导入翻译需要先粘贴 CSV");
    return;
  }

  setStatus("正在导入翻译并生成覆盖...");
  const result = await api("/api/translation/import-csv", {
    profileId,
    csv
  });
  writeLog($("actionOutput"), result);
  setStatus(`翻译已应用：${result.applied}/${result.imported}`);
  refreshOverlayList();
}

async function runBatchScript(apply) {
  const profileId = selectedProfileId();
  if (!profileId) return;
  let operations;
  try {
    operations = JSON.parse($("batchScriptText").value || "[]");
  } catch (error) {
    setStatus("脚本 JSON 格式不正确");
    return;
  }

  if (!Array.isArray(operations) || operations.length === 0) {
    setStatus("脚本至少需要一条规则");
    return;
  }

  setStatus(apply ? "正在应用批处理脚本..." : "正在预检批处理脚本...");
  const result = await api("/api/batch/run-script", {
    profileId,
    operations,
    apply
  });
  writeLog($("actionOutput"), result);
  setStatus(apply ? `脚本已应用：${result.changed}` : `脚本预检完成：${result.changed}`);
  if (apply) refreshOverlayList();
}

async function patchBuild() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  setStatus("正在启动补丁构建...");
  const job = await api("/api/jobs/patch/build", {
    profileId,
    template: 3,
    writerKind: 0
  });
  trackJob(job.id);
}

async function refreshBuildHistory() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  const result = await api("/api/patch/build-history", { profileId });
  const list = $("buildList");
  list.innerHTML = "";
  for (const item of result.items) {
    const row = document.createElement("div");
    row.className = "build-item";
    row.innerHTML = `
      <div class="build-line">
        <span class="build-path">${item.zipPath || item.outputDirectory}</span>
        ${item.downloadUrl ? `<a class="download-link" href="${item.downloadUrl}">下载</a>` : ""}
      </div>
      <div class="build-meta">${item.buildId} · ${Math.max(1, Math.round(item.zipSize / 1024))} KB</div>
      <div class="build-actions">
        <button type="button" data-action="install-preview">预检安装</button>
        <button type="button" data-action="install">安装</button>
        <button type="button" data-action="uninstall-preview">预检卸载</button>
        <button type="button" data-action="uninstall">卸载</button>
      </div>
    `;
    for (const button of row.querySelectorAll("button")) {
      button.addEventListener("click", () => runPatchInstallAction(item.buildId, button.dataset.action));
    }
    list.appendChild(row);
  }
  if (result.items.length === 0) {
    list.innerHTML = '<div class="build-item"><div class="build-meta">暂无补丁输出</div></div>';
  }
}

async function runPatchInstallAction(buildId, action) {
  const profileId = selectedProfileId();
  if (!profileId || !buildId) return;
  const uninstall = action.includes("uninstall");
  const apply = action === "install" || action === "uninstall";
  setStatus(uninstall ? (apply ? "正在卸载补丁..." : "正在预检卸载...") : (apply ? "正在安装补丁..." : "正在预检安装..."));
  const result = await api(uninstall ? "/api/patch/uninstall" : "/api/patch/install", {
    profileId,
    buildId,
    apply
  });
  writeLog($("actionOutput"), result);
  if (uninstall) {
    setStatus(apply ? `补丁已卸载：${result.removed}` : `卸载预检：${result.removed}`);
  } else {
    setStatus(apply ? `补丁已安装：${result.fileCount}` : `安装预检：${result.fileCount}`);
  }
}

async function refreshOverlayList() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  const result = await api("/api/overlay/list", { profileId });
  const list = $("overlayList");
  list.innerHTML = "";
  for (const item of result.items) {
    const row = document.createElement("div");
    row.className = "overlay-item";
    row.innerHTML = `
      <div class="overlay-line">
        <span class="overlay-path">${item.virtualPath}</span>
        <button class="revert-link" type="button">回滚</button>
      </div>
      <div class="build-meta">${Math.max(1, Math.round(item.overlaySize / 1024))} KB</div>
    `;
    row.querySelector("button").addEventListener("click", () => revertOverlay(item.virtualPath));
    list.appendChild(row);
  }
  if (result.items.length === 0) {
    list.innerHTML = '<div class="overlay-item"><div class="build-meta">暂无修改</div></div>';
  }
  refreshOverlayAudit();
}

async function refreshOverlayAudit() {
  const profileId = selectedProfileId();
  const list = $("overlayAuditList");
  if (!profileId || !list) return;
  const result = await api("/api/overlay/audit", { profileId, take: 8 });
  list.innerHTML = "";
  for (const item of result.items) {
    const row = document.createElement("div");
    row.className = "audit-item";
    row.innerHTML = `
      <span class="audit-action">${auditActionText(item.action)}</span>
      <span class="audit-path">${item.virtualPath}</span>
      <span class="audit-time">${formatLocalTime(item.at)}</span>
    `;
    list.appendChild(row);
  }
  if (result.items.length === 0) {
    list.innerHTML = '<div class="audit-item empty"><span class="build-meta">暂无操作</span></div>';
  }
}

async function revertOverlay(virtualPath) {
  const profileId = selectedProfileId();
  if (!profileId) return;
  const result = await api("/api/overlay/revert", { profileId, virtualPath });
  writeLog($("actionOutput"), result);
  setStatus(result.removed ? "修改已回滚" : "没有可回滚的修改");
  refreshOverlayList();
}

function bind() {
  $("refreshProfilesBtn").addEventListener("click", refreshProfiles);
  $("diagnosticsBtn").addEventListener("click", runDiagnostics);
  $("cnPresetBtn").addEventListener("click", () => {
    $("rootPathInput").value = presets.cn;
  });
  $("globalPresetBtn").addEventListener("click", () => {
    $("rootPathInput").value = presets.global;
  });
  $("detectBtn").addEventListener("click", detectClient);
  $("saveProfileBtn").addEventListener("click", saveProfile);
  $("quickConnectBtn").addEventListener("click", quickConnect);
  $("buildNativeIndexBtn").addEventListener("click", startNativeIndexJob);
  $("searchBtn").addEventListener("click", searchResources);
  $("bulkExportBtn").addEventListener("click", bulkExportResources);
  $("bulkSignatureBtn").addEventListener("click", bulkSignatureResources);
  $("matchResourcesBtn").addEventListener("click", matchResources);
  $("bulkImportBtn").addEventListener("click", bulkImportOverlay);
  $("searchInput").addEventListener("keydown", (event) => {
    if (event.key === "Enter") searchResources();
  });
  $("profileSelect").addEventListener("change", () => {
    state.selectedProfile = state.profiles.find((item) => item.id === $("profileSelect").value) || null;
    searchResources();
    refreshBuildHistory();
    refreshOverlayList();
  });
  $("saveOverlayBtn").addEventListener("click", saveOverlay);
  $("saveTableBtn").addEventListener("click", saveTableEdits);
  $("exportResourceBtn").addEventListener("click", exportResource);
  $("signatureBtn").addEventListener("click", extractSignature);
  $("replaceResourceBtn").addEventListener("click", chooseReplacementFile);
  $("inspectTableBtn").addEventListener("click", inspectTable);
  $("replaceResourceInput").addEventListener("change", (event) => replaceResourceWithFile(event.target.files[0]));
  $("batchOverlayBtn").addEventListener("click", batchOverlay);
  $("batchReplaceBtn").addEventListener("click", batchReplaceText);
  $("exportTranslationBtn").addEventListener("click", exportTranslationCsv);
  $("importTranslationBtn").addEventListener("click", importTranslationCsv);
  $("previewScriptBtn").addEventListener("click", () => runBatchScript(false));
  $("applyScriptBtn").addEventListener("click", () => runBatchScript(true));
  $("patchDryRunBtn").addEventListener("click", patchDryRun);
  $("patchReadinessBtn").addEventListener("click", patchReadiness);
  $("nativePlanBtn").addEventListener("click", nativePatchPlan);
  $("nativeDryBundleBtn").addEventListener("click", nativeDryBundle);
  $("nativeIndexPlanBtn").addEventListener("click", nativeIndexPlan);
  $("patchBuildBtn").addEventListener("click", patchBuild);
  $("refreshBuildsBtn").addEventListener("click", refreshBuildHistory);
  $("refreshOverlayBtn").addEventListener("click", refreshOverlayList);
}

bind();
refreshProfiles().catch((error) => setStatus(error.message));
