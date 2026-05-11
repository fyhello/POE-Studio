const state = {
  profiles: [],
  detected: null,
  selectedProfile: null,
  selectedResource: null,
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

const presets = {
  cn: "C:\\WeGameApps\\rail_apps\\流放之路：降临(2002052)",
  global: "E:\\PSAutoRecover\\ui\\rood\\Grinding Gear Games\\Path of Exile 2"
};

async function refreshProfiles() {
  state.profiles = await api("/api/profiles");
  $("profileCount").textContent = String(state.profiles.length);
  $("profileSelect").innerHTML = "";
  for (const profile of state.profiles) {
    const option = document.createElement("option");
    option.value = profile.id;
    option.textContent = `${profile.displayName} (${profile.platform})`;
    $("profileSelect").appendChild(option);
  }
  state.selectedProfile = state.profiles[0] || null;
  $("buildNativeIndexBtn").disabled = !state.selectedProfile;
  $("patchDryRunBtn").disabled = !state.selectedProfile;
  $("patchBuildBtn").disabled = !state.selectedProfile;
  $("refreshBuildsBtn").disabled = !state.selectedProfile;
  $("refreshOverlayBtn").disabled = !state.selectedProfile;
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
  $("previewKind").textContent = preview.kind === 1 ? "文本" : preview.kind === 2 ? "十六进制" : "不可预览";
  $("previewText").value = preview.text || preview.hex || preview.message || "";
  $("saveOverlayBtn").disabled = preview.kind !== 1;
  $("batchOverlayBtn").disabled = preview.kind !== 1;
  setStatus("预览已加载");
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

async function patchDryRun() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  setStatus("正在执行补丁预检...");
  const result = await api("/api/patch/dry-run", { profileId });
  writeLog($("actionOutput"), result);
  setStatus(`补丁预检完成：${result.totalChanges} 个改动`);
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
    `;
    list.appendChild(row);
  }
  if (result.items.length === 0) {
    list.innerHTML = '<div class="build-item"><div class="build-meta">暂无补丁输出</div></div>';
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
  $("batchOverlayBtn").addEventListener("click", batchOverlay);
  $("patchDryRunBtn").addEventListener("click", patchDryRun);
  $("patchBuildBtn").addEventListener("click", patchBuild);
  $("refreshBuildsBtn").addEventListener("click", refreshBuildHistory);
  $("refreshOverlayBtn").addEventListener("click", refreshOverlayList);
}

bind();
refreshProfiles().catch((error) => setStatus(error.message));
