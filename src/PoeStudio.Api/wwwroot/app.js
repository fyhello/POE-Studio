const state = {
  profiles: [],
  detected: null,
  selectedProfile: null,
  selectedResource: null,
  lastExportRoot: null,
  tableEditBase: null,
  tableSchemas: [],
  structuredEditBase: null,
  previewUseOverlay: true,
  batchTemplates: [],
  migrationPlans: [],
  migrationReview: null,
  migrationFilter: "all",
  migrationLastRequest: null,
  workbenchLastOutput: null,
  lastResourceItems: [],
  recentResources: [],
  indexCoverage: null,
  resourceSearchTotal: 0,
  resourceExpandedPaths: new Set(),
  resourceCollapsedPaths: new Set(),
  translationResourceMode: false,
  tableReference: null,
  manualReferenceResource: null,
  datc64Tsv: null,
  datc64AgGrid: null,
  tableCsvImporting: false,
  overlayDraftPaths: new Set(),
  overlayTotal: 0,
  jobTimer: null,
  largeText: {
    active: false,
    chunk: null,
    targetEditor: null,
    referenceEditor: null,
    referenceResource: null,
    referenceChunk: null,
    module: null
  },
  csdTagIssues: [],
  csdTagIssueCursor: -1,
  chat: {
    visible: false,
    messages: [],
    abortController: null,
    pendingRepair: null,
    repairPollTimer: null
  }
};

let currentTheme = localStorage.getItem("poeStudioTheme") || "dark";

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

const apiForm = async (url, form) => {
  const response = await fetch(url, {
    method: "POST",
    body: form
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

function applyTheme(theme) {
  currentTheme = theme === "light" ? "light" : "dark";
  document.documentElement.dataset.theme = currentTheme;
  localStorage.setItem("poeStudioTheme", currentTheme);
  if ($("themeToggleBtn")) {
    $("themeToggleBtn").textContent = currentTheme === "dark" ? "浅色" : "深色";
    $("themeToggleBtn").title = currentTheme === "dark" ? "切换到浅色" : "切换到深色";
  }
}

function toggleTheme() {
  applyTheme(currentTheme === "dark" ? "light" : "dark");
}

const selectedProfileId = () => $("profileSelect").value || state.selectedProfile?.id;
const targetProfileId = () => $("targetProfileSelect").value || selectedProfileId();
const sourceProfileId = () => selectedProfileId();
const workspaceProfileId = () => targetProfileId();
const currentOodlePath = () => $("oodlePathInput")?.value.trim() || null;

function normalizeVirtualPath(value) {
  return String(value || "").replaceAll("\\", "/").replace(/^\/+/, "").toLowerCase();
}

const formatLocalTime = (value) => new Date(value).toLocaleString("zh-CN", {
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit"
});

const auditActionText = (action) => action === "revert" ? "回滚" : "保存";

const tableExtensions = new Set([".datc64", ".dat", ".ot", ".otc", ".tdt", ".fmt"]);
const textExtensions = new Set([".ui", ".xml", ".json", ".txt", ".filter", ".atlas", ".csd"]);
const imageExtensions = new Set([".dds", ".png", ".jpg", ".jpeg", ".bmp"]);
const audioExtensions = new Set([".ogg", ".wav"]);
const languageDirectoryNames = new Set([
  "traditional chinese",
  "traditionalchinese",
  "繁体中文",
  "繁體中文",
  "simplified chinese",
  "simplifiedchinese",
  "simple chinese",
  "简体中文",
  "簡體中文"
]);
const simplifiedReferenceDirectories = [
  "simplified chinese",
  "simplifiedchinese",
  "simple chinese",
  "简体中文",
  "簡體中文"
];
const targetLanguageDirectories = [
  "traditional chinese",
  "traditionalchinese",
  "繁体中文",
  "繁體中文"
];
const resourceTreeTake = 5000;
const defaultPreviewLimit = 65536;
const largeTextThreshold = 2 * 1024 * 1024;
const largeCsdAutoAnalysisThreshold = 8 * 1024 * 1024;
const maxTextPreviewLimit = 64 * 1024 * 1024;
const datc64VirtualRowHeight = 28;
const datc64VirtualOverscan = 12;

const workflowStatusText = {
  config: "检查客户端路径",
  index: "建立真实索引",
  search: "打开目标资源",
  match: "匹配国服文本",
  draft: "写入草稿层",
  patch: "生成补丁包"
};

const migrationStatusText = (status) => ({
  0: "直接",
  1: "改名",
  2: "候选",
  3: "缺失"
}[status] || "未知");

const riskText = (risk) => ({
  0: "低",
  1: "中",
  2: "高"
}[risk] || "未知");

function selectedPlan() {
  const id = $("migrationPlanSelect")?.value;
  return id ? state.migrationPlans.find((plan) => plan.id === id) || null : null;
}

function resolveCurrentPair() {
  const sourceId = sourceProfileId();
  const targetId = targetProfileId();
  const source = state.profiles.find((profile) => profile.id === sourceId) || null;
  const target = state.profiles.find((profile) => profile.id === targetId) || null;
  return { sourceId, targetId, source, target };
}

function isSelectedTargetResource() {
  return Boolean(state.selectedResource && state.selectedResource.profileId === targetProfileId());
}

function isSelectedSourceResource() {
  return Boolean(state.selectedResource && state.selectedResource.profileId === sourceProfileId());
}

function isSelectedResourceItem(item) {
  return Boolean(item
    && state.selectedResource
    && item.profileId === state.selectedResource.profileId
    && normalizeVirtualPath(item.virtualPath) === normalizeVirtualPath(state.selectedResource.virtualPath));
}

function isTableResource(resource) {
  return Boolean(resource && (resource.kind === 2 || tableExtensions.has((resource.extension || "").toLowerCase())));
}

function isCsdResource(resource) {
  return Boolean(resource && (resource.extension || "").toLowerCase() === ".csd");
}

function splitCsdDescriptionBlocks(text) {
  const source = String(text || "");
  const matches = Array.from(source.matchAll(/^description[ \t\r]*$/gm));
  if (matches.length === 0) return [{ text: source, start: 0 }];
  const chunks = [];
  if (matches[0].index > 0) {
    chunks.push({ text: source.slice(0, matches[0].index), start: 0 });
  }
  for (let index = 0; index < matches.length; index++) {
    const start = matches[index].index;
    const end = index + 1 < matches.length ? matches[index + 1].index : source.length;
    chunks.push({ text: source.slice(start, end), start });
  }
  return chunks;
}

function analyzeCsdLanguageTags(text) {
  const blocks = splitCsdDescriptionBlocks(text).filter((block) => block.text.trimStart().startsWith("description"));
  const source = String(text || "");
  let traditional = 0;
  let simplified = 0;
  let both = 0;
  let missingSimplified = 0;
  let missingTraditional = 0;
  const issues = [];
  for (const block of blocks) {
    const traditionalInBlock = (block.text.match(/lang "Traditional Chinese"/g) || []).length;
    const simplifiedInBlock = (block.text.match(/lang "Simplified Chinese"/g) || []).length;
    traditional += traditionalInBlock;
    simplified += simplifiedInBlock;
    if (traditionalInBlock > 0 && simplifiedInBlock > 0) both++;
    if (traditionalInBlock > 0 && simplifiedInBlock === 0) {
      missingSimplified++;
      issues.push(buildCsdTagIssue(source, block, "缺简"));
    }
    if (simplifiedInBlock > 0 && traditionalInBlock === 0) {
      missingTraditional++;
      issues.push(buildCsdTagIssue(source, block, "缺繁"));
    }
  }
  return { traditional, simplified, both, missingSimplified, missingTraditional, blocks: blocks.length, issues };
}

function buildCsdTagIssue(source, block, kind) {
  const statLine = (block.text.split(/\r?\n/).find((line) => /^\s+\d+\s+\S+/.test(line)) || "").trim();
  const stat = statLine.replace(/^\d+\s+/, "") || "unknown";
  const position = block.start;
  const line = source.slice(0, position).split(/\r?\n/).length;
  return { kind, stat, position, line };
}

function shouldDeferCsdAnalysis(text, options = {}) {
  if (options.force) return false;
  const size = Number(state.selectedResource?.size || 0);
  return size > largeCsdAutoAnalysisThreshold || String(text || "").length > largeCsdAutoAnalysisThreshold;
}

function renderCsdTagStatus(text, options = {}) {
  const badge = $("csdTagStatus");
  if (!badge) return;
  if (!state.selectedResource || !isCsdResource(state.selectedResource)) {
    badge.classList.add("hidden");
    badge.classList.remove("warning");
    return;
  }

  if (shouldDeferCsdAnalysis(text, options)) {
    state.csdTagIssues = [];
    state.csdTagIssueCursor = -1;
    badge.textContent = "CSD 标签：按需检查";
    badge.title = "大 CSD 文件打开时不自动全量扫描，点击后再检查缺简/缺繁并跳转。";
    badge.classList.remove("warning");
    badge.disabled = false;
    badge.classList.remove("hidden");
    return;
  }

  const stats = analyzeCsdLanguageTags(text);
  state.csdTagIssues = stats.issues;
  state.csdTagIssueCursor = -1;
  badge.textContent = `简 ${stats.simplified} / 繁 ${stats.traditional} / 缺简 ${stats.missingSimplified} / 缺繁 ${stats.missingTraditional}`;
  badge.title = stats.issues.length > 0
    ? `点击跳转问题块。CSD description 块：${stats.blocks}，简繁都有：${stats.both}`
    : `CSD description 块：${stats.blocks}，简繁都有：${stats.both}`;
  badge.classList.toggle("warning", stats.missingSimplified > 0 || stats.missingTraditional > 0 || stats.simplified !== stats.traditional);
  badge.disabled = stats.issues.length === 0;
  badge.classList.remove("hidden");
}

function isTextPreviewResource(resource) {
  const extension = (resource?.extension || "").toLowerCase();
  return Boolean(resource && (resource.kind === 1 || resource.kind === 6 || textExtensions.has(extension)));
}

function isComparableTextResource(resource) {
  return Boolean(resource && !isTableResource(resource) && isTextPreviewResource(resource));
}

function previewLimitForResource(resource) {
  if (!isTextPreviewResource(resource)) return defaultPreviewLimit;
  const size = Number(resource.size || 0);
  return Math.min(maxTextPreviewLimit, Math.max(defaultPreviewLimit, size + 16));
}

function activeTextEditor() {
  if (state.largeText.active && state.largeText.targetEditor) {
    return {
      get value() {
        return state.largeText.targetEditor.getValue();
      },
      set value(next) {
        state.largeText.targetEditor.setValue(next || "");
      }
    };
  }

  return $("compareWorkspace").classList.contains("hidden") ? $("previewText") : $("targetPreviewText");
}

function jumpTextareaToPosition(textarea, position) {
  textarea.focus();
  const safePosition = Math.max(0, Math.min(textarea.value.length, position));
  textarea.setSelectionRange(safePosition, safePosition);
  const line = textarea.value.slice(0, safePosition).split(/\r?\n/).length;
  const lineHeight = Number.parseFloat(getComputedStyle(textarea).lineHeight) || 18;
  textarea.scrollTop = Math.max(0, (line - 4) * lineHeight);
}

async function jumpToNextCsdTagIssue() {
  if (!state.selectedResource || !isCsdResource(state.selectedResource)) return;
  const editor = activeTextEditor();
  setStatus("正在检查 CSD 标签...");
  await new Promise((resolve) => requestAnimationFrame(resolve));
  const stats = analyzeCsdLanguageTags(editor.value);
  state.csdTagIssues = stats.issues;
  if (stats.issues.length === 0) {
    renderCsdTagStatus(editor.value, { force: true });
    setStatus("当前 CSD 没有缺简/缺繁问题。");
    return;
  }

  state.csdTagIssueCursor = (state.csdTagIssueCursor + 1) % stats.issues.length;
  const issue = stats.issues[state.csdTagIssueCursor];
  if (state.largeText.active) {
    if (state.largeText.targetEditor?.view) {
      const view = state.largeText.targetEditor.view;
      view.dispatch({
        selection: { anchor: issue.position },
        scrollIntoView: true
      });
      view.focus();
    }
    setStatus(`CSD 标签问题 ${state.csdTagIssueCursor + 1}/${stats.issues.length}：${issue.kind}，第 ${issue.line} 行，${issue.stat}`);
    return;
  }

  jumpTextareaToPosition(editor, issue.position);
  setStatus(`CSD 标签问题 ${state.csdTagIssueCursor + 1}/${stats.issues.length}：${issue.kind}，第 ${issue.line} 行，${issue.stat}`);
}

function isLargeTextResource(resource) {
  return isTextPreviewResource(resource) && Number(resource?.size || 0) > largeTextThreshold;
}

function usesCodeMirrorEditor(resource) {
  return isCsdResource(resource) || isLargeTextResource(resource);
}

function isDatc64ComparisonInspection(inspection) {
  return inspection?.delimiter === "datc64-schema" || inspection?.delimiter === "datc64-auto";
}

function isLegacyDatComparisonInspection(inspection) {
  return inspection?.delimiter === "legacy-dat-schema";
}

function isStringCandidateComparisonInspection(inspection) {
  return Boolean(!inspection?.structured && inspection?.strings?.length && inspection?.rows?.length);
}

function isTableComparisonInspection(inspection) {
  return isDatc64ComparisonInspection(inspection) || isLegacyDatComparisonInspection(inspection) || isStringCandidateComparisonInspection(inspection);
}

function profileLooksLikeSource(profile) {
  const name = `${profile?.displayName || ""} ${profile?.rootPath || ""} ${profile?.platform || ""}`.toLowerCase();
  return name.includes("国服") || name.includes("简体") || name.includes("wegame") || name.includes("2002052");
}

function profileLooksLikeTarget(profile) {
  const name = `${profile?.displayName || ""} ${profile?.rootPath || ""} ${profile?.platform || ""}`.toLowerCase();
  return name.includes("国际") || name.includes("目标") || name.includes("official") || name.includes("epic") || name.includes("steam") || name.includes("content.ggpk");
}

function selectDefaultProfilePair() {
  const source = state.profiles.find(profileLooksLikeSource) || state.profiles[0] || null;
  const target = state.profiles.find((profile) => profile.id !== source?.id && profileLooksLikeTarget(profile))
    || state.profiles.find((profile) => profile.id !== source?.id)
    || source;
  if (source) {
    $("profileSelect").value = source.id;
    state.selectedProfile = source;
  }
  if (target) {
    $("targetProfileSelect").value = target.id;
  }
}

function updateWorkbench() {
  if (!$("workbenchStatus")) return;
  const { source, target } = resolveCurrentPair();
  const plan = selectedPlan();
  $("workbenchProfiles").textContent = source && target ? `${source.displayName} → ${target.displayName}` : "未选择";
  $("workbenchPlan").textContent = plan ? `${plan.name} (${plan.planned})` : "未选择";
  $("workbenchValidation").textContent = state.migrationReview
    ? `计划 ${state.migrationReview.planned ?? 0}`
    : "未校验";
  $("workbenchOverlay").textContent = state.overlayTotal > 0
    ? `已写入 ${state.overlayTotal}`
    : state.migrationReview?.drafted !== undefined
      ? `写入 ${state.migrationReview.drafted}`
    : "未写入";
  $("workbenchBuild").textContent = state.workbenchLastOutput?.build?.zipPath ? "已生成" : "未生成";
  $("workbenchSandbox").textContent = state.workbenchLastOutput?.sandbox
    ? (state.workbenchLastOutput.sandbox.ok ? "通过" : "警告")
    : "未验证";
  const readyProfiles = Boolean(source && target);
  const readyPlan = Boolean(plan);
  const readyDraft = state.overlayTotal > 0;
  $("workbenchStatus").textContent = readyPlan ? "方案就绪" : readyProfiles ? "等待方案" : "等待配置";
  updateWorkflowStatus();
  $("workbenchScanBtn").disabled = !source;
  $("workbenchPlanBtn").disabled = !readyProfiles;
  $("workbenchValidateBtn").disabled = !readyPlan;
  $("syncExternalOverlayQuickBtn").disabled = !target;
  $("runWorkbenchPipelineBtn").disabled = !(readyPlan || readyDraft);
  $("openWorkbenchOutputBtn").disabled = !state.workbenchLastOutput?.build?.outputDirectory;
}

function updateWorkflowStatus(step) {
  const label = $("workflowStatus");
  if (!label) return;
  if (step) {
    label.textContent = workflowStatusText[step] || step;
    return;
  }
  const { source, target } = resolveCurrentPair();
  if (!source || !target) {
    label.textContent = workflowStatusText.config;
  } else if (!state.lastResourceItems.length && !state.selectedResource) {
    label.textContent = workflowStatusText.search;
  } else if (state.selectedResource) {
    label.textContent = workflowStatusText.match;
  } else if (selectedPlan()) {
    label.textContent = workflowStatusText.patch;
  } else {
    label.textContent = workflowStatusText.draft;
  }
}

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

async function loadWorkspaceSettings() {
  const result = await api("/api/workspace");
  if ($("workspaceRootInput")) {
    $("workspaceRootInput").value = result.workspaceRoot || "";
  }
  return result;
}

async function saveWorkspaceSettings() {
  const workspaceRoot = $("workspaceRootInput")?.value.trim();
  if (!workspaceRoot) {
    setStatus("工作区目录不能为空");
    return;
  }

  setStatus("正在保存工作区...");
  const result = await api("/api/workspace", { workspaceRoot });
  $("workspaceRootInput").value = result.workspaceRoot || workspaceRoot;
  writeLog($("detectOutput"), result);
  state.selectedProfile = null;
  state.selectedResource = null;
  clearManualReferenceSelection();
  await refreshProfiles();
  setStatus(result.workspaceWritable ? "工作区已切换" : "工作区已保存，但不可写");
}

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
  selectDefaultProfilePair();
  $("buildNativeIndexBtn").disabled = !state.selectedProfile;
  $("deleteProfileBtn").disabled = !state.selectedProfile;
  $("patchDryRunBtn").disabled = !state.selectedProfile;
  $("patchReadinessBtn").disabled = !state.selectedProfile;
  $("nativePlanBtn").disabled = !state.selectedProfile;
  $("nativeDryBundleBtn").disabled = !state.selectedProfile;
  $("nativeIndexPlanBtn").disabled = !state.selectedProfile;
  $("patchBuildBtn").disabled = !state.selectedProfile;
  $("refreshBuildsBtn").disabled = !state.selectedProfile;
  $("refreshOverlayBtn").disabled = !state.selectedProfile;
  $("reviewOverlayBtn").disabled = !state.selectedProfile;
  $("reviewHighRiskBtn").disabled = !state.selectedProfile;
  $("reviewTextBtn").disabled = !state.selectedProfile;
  $("reviewUiBtn").disabled = !state.selectedProfile;
  $("revertHighRiskBtn").disabled = !state.selectedProfile;
  $("syncExternalOverlayBtn").disabled = !state.selectedProfile;
  $("syncExternalOverlayQuickBtn").disabled = !state.selectedProfile;
  $("exportTranslationBtn").disabled = !state.selectedProfile;
  $("applyGlossaryBtn").disabled = !state.selectedProfile;
  $("importTranslationBtn").disabled = !state.selectedProfile;
  $("previewScriptBtn").disabled = !state.selectedProfile;
  $("applyScriptBtn").disabled = !state.selectedProfile;
  $("saveBatchTemplateBtn").disabled = !state.selectedProfile;
  $("previewTemplateBtn").disabled = true;
  $("applyTemplateBtn").disabled = true;
  $("deleteBatchTemplateBtn").disabled = true;
  $("bulkExportBtn").disabled = !state.selectedProfile;
  $("bulkSignatureBtn").disabled = !state.selectedProfile;
  $("formatScanBtn").disabled = !state.selectedProfile;
  $("matchResourcesBtn").disabled = !state.selectedProfile;
  $("migrationPlanBtn").disabled = !state.selectedProfile;
  $("migrationDraftBtn").disabled = !state.selectedProfile;
  $("bulkImportBtn").disabled = !state.selectedProfile;
  resetTableSchemaPicker();
  setStatus(state.selectedProfile ? "已加载客户端配置" : "没有客户端配置");
  if (state.selectedProfile) refreshBuildHistory();
  if (state.selectedProfile) refreshOverlayList();
  if (state.selectedProfile) refreshBatchTemplates();
  if (state.selectedProfile) refreshMigrationPlans();
  updateWorkbench();
}

async function detectClient() {
  setStatus("正在检测客户端...");
  state.detected = await api("/api/profiles/detect", {
    rootPath: $("rootPathInput").value.trim(),
    oodleSearchPath: $("oodlePathInput").value.trim() || null
  });
  writeLog($("detectOutput"), state.detected);
  $("saveProfileBtn").disabled = !state.detected.detected;
  if (state.detected.detected && !$("profileNameInput").value.trim()) {
    $("profileNameInput").value = defaultProfileName(state.detected);
  }
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
  const displayName = $("profileNameInput").value.trim() || defaultProfileName(state.detected);
  const profile = await api("/api/profiles", {
    displayName,
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

function defaultProfileName(detected) {
  return `${detected.platform} POE2`;
}

async function deleteSelectedProfile() {
  const profile = state.profiles.find((item) => item.id === $("profileSelect").value) || state.selectedProfile;
  if (!profile) return;
  const confirmed = window.confirm(`删除配置“${profile.displayName}”？只删除 POE Studio 本地配置和缓存，不会删除游戏客户端文件。`);
  if (!confirmed) return;

  setStatus("正在删除配置...");
  const result = await api("/api/profiles/delete", { profileId: profile.id });
  writeLog($("detectOutput"), result);
  state.selectedProfile = null;
  state.selectedResource = null;
  await refreshProfiles();
  setStatus(result.removed ? "配置已删除" : "配置不存在");
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
  const isGgpk = state.selectedProfile?.entryKind === 1;
  const job = isGgpk
    ? await api("/api/jobs/native/ggpk/build-resource-index", {
      profileId,
      oodlePath: $("oodlePathInput").value.trim() || null
    })
    : await api("/api/jobs/native/bundles2/build-resource-index", {
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
          const logTarget = job.kind?.startsWith("native-") ? $("detectOutput") : $("actionOutput");
          writeLog(logTarget, job);
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

  if (job.kind === "patch-analyze-zip") {
    writeLog($("actionOutput"), {
      ok: result.ok,
      template: result.template,
      hasIndex: result.hasIndex,
      hasPatchBundle: result.hasPatchBundle,
      bundlesRoot: result.bundlesRoot,
      entryCount: result.entryCount,
      totalSize: result.totalSize,
      verification: result.verification,
      warnings: result.warnings,
      entries: result.entries.slice(0, 20)
    });
    setStatus(result.ok ? `补丁分析通过：${result.entryCount} 个文件` : `补丁分析警告：${result.warnings.length}`);
    return;
  }

  if (job.kind === "patch-import-zip") {
    writeLog($("actionOutput"), {
      buildId: result.buildId,
      outputDirectory: result.outputDirectory,
      zipPath: result.zipPath,
      importManifestPath: result.importManifestPath,
      analysis: result.analysis,
      warnings: result.warnings
    });
    setStatus(`外部补丁已导入：${result.buildId}`);
    refreshBuildHistory();
    return;
  }

  if (job.kind === "patch-preview-zip-install") {
    writeLog($("actionOutput"), {
      ok: result.ok,
      fileCount: result.fileCount,
      newFiles: result.newFiles,
      replacedFiles: result.replacedFiles,
      sameFiles: result.sameFiles,
      highRiskFiles: result.highRiskFiles,
      warnings: result.warnings,
      files: result.files.slice(0, 30)
    });
    setStatus(`影响预检：覆盖 ${result.replacedFiles} / 新增 ${result.newFiles} / 高风险 ${result.highRiskFiles}`);
    return;
  }

  if (job.kind === "patch-import-overlay-draft") {
    writeLog($("actionOutput"), {
      matchedRecords: result.matchedRecords,
      imported: result.imported,
      kindCounts: result.kindCounts,
      riskCounts: result.riskCounts,
      draftReportPath: result.draftReportPath,
      warnings: result.warnings,
      items: result.items.slice(0, 30)
    });
    setStatus(`已转草稿：${result.imported}/${result.matchedRecords}`);
    refreshOverlayList();
    return;
  }

  if (job.kind === "resources-migration-draft" || job.kind === "resources-migration-plan-apply") {
    showMigrationReview(result);
    state.migrationLastRequest = {
      sourceProfileId: result.sourceProfileId,
      targetProfileId: result.targetProfileId,
      sourceOodlePath: $("oodlePathInput").value.trim() || null,
      useOverlay: state.previewUseOverlay
    };
    setStatus(`迁移草稿完成：写入 ${result.drafted}，跳过 ${result.skipped}`);
    refreshOverlayList();
    return;
  }

  if (job.kind === "patch-sandbox-prepare") {
    writeLog($("actionOutput"), {
      sandboxRootPath: result.sandboxRootPath,
      sandboxBundlesPath: result.sandboxBundlesPath,
      seededFiles: result.seededFiles,
      ok: result.ok,
      validation: {
        checkedFiles: result.validation.checkedFiles,
        missingFiles: result.validation.missingFiles,
        sizeMismatches: result.validation.sizeMismatches,
        files: result.validation.files
      },
      warnings: result.warnings
    });
    setStatus(result.ok ? `沙盒已准备：${result.seededFiles} 个基础文件` : "沙盒准备有警告");
    return;
  }

  if (job.kind === "patch-pipeline-run") {
    state.workbenchLastOutput = result;
    writeLog($("actionOutput"), {
      ok: result.ok,
      validation: {
        ready: result.validation.ready,
        changed: result.validation.changed,
        missing: result.validation.missing,
        blocked: result.validation.blocked
      },
      migration: {
        drafted: result.migration.drafted,
        skipped: result.migration.skipped
      },
      build: {
        totalChanges: result.build.totalChanges,
        zipPath: result.build.zipPath,
        outputDirectory: result.build.outputDirectory
      },
      sandbox: result.sandbox ? {
        ok: result.sandbox.ok,
        sandboxRootPath: result.sandbox.sandboxRootPath,
        checkedFiles: result.sandbox.validation.checkedFiles,
        missingFiles: result.sandbox.validation.missingFiles
      } : null,
      warnings: result.warnings
    });
    showMigrationReview(result.migration);
    updateWorkbench();
    setStatus(result.ok ? `流水线完成：补丁 ${result.build.totalChanges} 项` : "流水线完成但有警告");
    refreshOverlayList();
    refreshBuildHistory();
    return;
  }

  if (job.kind === "native-bundles2-resource-index" || job.kind === "native-ggpk-resource-index") {
    writeLog($("detectOutput"), result);
    renderIndexCoverage(result);
    setStatus(formatIndexBuildStatus(result));
    searchResources();
    updateWorkbench();
    return;
  }

  writeLog($("detectOutput"), result);
  searchResources();
}

function formatIndexBuildStatus(result) {
  if (!result.ok) {
    return `资源索引失败：${result.warnings?.[0] || "未知错误"}`;
  }

  const coverage = result.bundles2Coverage;
  if (coverage && coverage.resourcesInMissingBundles > 0) {
    return `资源索引完成：${result.resolvedResources}/${coverage.indexFileCount}，缺失 bundle 对应 ${coverage.resourcesInMissingBundles} 条`;
  }

  return `资源索引完成：${result.resolvedResources}/${result.totalFiles}`;
}

function renderIndexCoverage(result) {
  state.indexCoverage = result;
  const panel = $("indexCoveragePanel");
  if (!panel) return;
  const coverage = result.bundles2Coverage;
  const resolved = result.resolvedResources ?? 0;
  const total = coverage?.indexFileCount ?? result.totalFiles ?? 0;
  const missing = coverage?.resourcesInMissingBundles ?? 0;
  $("coverageResolved").textContent = resolved.toLocaleString("zh-CN");
  $("coverageTotal").textContent = total.toLocaleString("zh-CN");
  $("coverageMissing").textContent = missing.toLocaleString("zh-CN");
  $("coverageStatus").textContent = result.ok ? "已完成" : "有问题";
  panel.classList.toggle("coverage-warning", missing > 0);
  if (coverage) {
    const missingBundleText = missing > 0
      ? `缺失 ${coverage.missingBundleCount} 个 bundle，对应多为 shader cache，不影响文本/UI 路径。`
      : "Bundles2 路径全部落到现有 bundle。";
    $("coverageNote").textContent = `Index ${coverage.indexFileCount.toLocaleString("zh-CN")} 条，现有 bundle ${coverage.existingBundleCount.toLocaleString("zh-CN")} 个。${missingBundleText}`;
  } else {
    $("coverageNote").textContent = `已索引 ${resolved.toLocaleString("zh-CN")} 个可读取资源。`;
  }
}

async function searchResources() {
  const profileId = workspaceProfileId();
  if (!profileId) return;
  const target = state.profiles.find((profile) => profile.id === profileId);
  const query = $("searchInput").value.trim();
  const extension = $("extensionFilter").value || null;
  if (!query && !extension && !state.translationResourceMode) {
    $("resourceTotal").textContent = "0";
    renderResourceHint("输入路径关键词，或先选 UI / DDS / DAT 等格式。");
    setStatus("请选择搜索条件，避免全量资源树卡顿");
    return;
  }

  state.resourceExpandedPaths.clear();
  state.resourceCollapsedPaths.clear();
  updateWorkflowStatus("search");
  setStatus(`正在搜索${target?.displayName || "目标"}资源...`);
  const result = await api("/api/resources/search", {
    profileId,
    query: query || null,
    extension,
    translationOnly: state.translationResourceMode,
    skip: 0,
    take: resourceTreeTake
  });
  $("resourceTotal").textContent = String(result.total);
  renderResources(result.items, result.total);
  const loadedText = result.items.length < result.total ? `，已加载 ${result.items.length}` : "";
  const modeText = state.translationResourceMode ? "翻译文件" : "目标资源";
  setStatus(`${modeText}：${result.total} 个${loadedText}`);
}

function setSearchPreset(query, extension) {
  state.translationResourceMode = false;
  updateSearchPresetState();
  $("searchInput").value = query || "";
  $("extensionFilter").value = extension || "";
  searchResources();
}

function showTranslationFiles() {
  state.translationResourceMode = true;
  $("searchInput").value = "";
  $("extensionFilter").value = "";
  updateSearchPresetState();
  searchResources();
}

function updateSearchPresetState() {
  for (const button of document.querySelectorAll(".search-presets button")) {
    button.classList.toggle("selected", Boolean(button.dataset.translation) && state.translationResourceMode);
  }
}

function renderResourceHint(message) {
  const list = $("resourceList");
  list.innerHTML = "";
  const hint = document.createElement("div");
  hint.className = "resource-empty";
  hint.textContent = message;
  list.appendChild(hint);
}

async function bulkExportResources() {
  const profileId = selectedProfileId();
  const query = $("searchInput").value.trim();
  if (!profileId || !query) {
    setStatus("批量导出需要先输入搜索条件");
    return;
  }

  const layerText = state.previewUseOverlay ? "草稿层" : "原始层";
  setStatus(`正在批量导出${layerText}...`);
  const result = await api("/api/resources/bulk-export", {
    profileId,
    query,
    take: 200,
    oodlePath: $("oodlePathInput").value.trim() || null,
    useOverlay: state.previewUseOverlay
  });
  state.lastExportRoot = result.exportRoot;
  writeLog($("actionOutput"), {
    matched: result.matched,
    exported: result.exported,
    exportRoot: result.exportRoot,
    warnings: result.warnings
  });
  setStatus(`${layerText}批量导出完成：${result.exported}/${result.matched}`);
}

async function bulkSignatureResources() {
  const profileId = selectedProfileId();
  const query = $("searchInput").value.trim();
  if (!profileId || !query) {
    setStatus("批量特征需要先输入搜索条件");
    return;
  }

  const layerText = state.previewUseOverlay ? "草稿层" : "原始层";
  setStatus(`正在批量提取${layerText}特征...`);
  const result = await api("/api/resources/bulk-signature", {
    profileId,
    query,
    take: 200,
    oodlePath: $("oodlePathInput").value.trim() || null,
    useOverlay: state.previewUseOverlay
  });
  writeLog($("actionOutput"), result);
  setStatus(`${layerText}批量特征完成：${result.signed}/${result.matched}`);
}

async function formatScan() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  setStatus("正在扫描格式能力...");
  const result = await api("/api/resources/format-scan", {
    profileId,
    take: 20000
  });
  renderFormatScan(result);
  setStatus(`格式扫描完成：${result.extensionCount} 类 / ${result.scanned} 个资源`);
}

function renderFormatScan(result) {
  const panel = $("formatScanPanel");
  const list = $("formatScanList");
  panel.classList.remove("hidden");
  $("formatScanSummary").textContent = `${result.extensionCount} 类 · ${result.scanned}/${result.total}`;
  list.innerHTML = "";
  for (const item of result.items) {
    const row = document.createElement("button");
    row.className = "format-scan-item";
    row.type = "button";
    row.innerHTML = `
      <span class="format-ext">${item.extension}</span>
      <span>${item.total}</span>
      <span>预览 ${item.previewable}</span>
      <span>编辑 ${item.editable}</span>
      <span>导出 ${item.exportOnly}</span>
    `;
    row.addEventListener("click", () => {
      $("extensionFilter").value = item.extension === "(none)" ? "" : item.extension;
      searchResources();
    });
    list.appendChild(row);
  }
  writeLog($("actionOutput"), {
    total: result.total,
    scanned: result.scanned,
    extensionCount: result.extensionCount,
    warnings: result.warnings,
    items: result.items.slice(0, 80)
  });
}

async function matchResources() {
  const sourceProfileId = selectedProfileId();
  const targetId = targetProfileId();
  const query = $("searchInput").value.trim();
  if (!sourceProfileId || !targetId || !query) {
    setStatus("匹配资源需要当前配置、目标配置和搜索条件");
    return;
  }

  const layerText = state.previewUseOverlay ? "草稿层" : "原始层";
  setStatus(`正在匹配${layerText}资源...`);
  const result = await api("/api/resources/match", {
    sourceProfileId,
    targetProfileId: targetId,
    query,
    take: 200,
    sourceOodlePath: $("oodlePathInput").value.trim() || null,
    targetOodlePath: $("oodlePathInput").value.trim() || null,
    useOverlay: state.previewUseOverlay
  });
  writeLog($("actionOutput"), result);
  setStatus(`${layerText}匹配完成：${result.matched}/${result.sourceMatched}`);
}

async function migrationPlan() {
  const sourceProfileId = selectedProfileId();
  const targetId = targetProfileId();
  const query = $("searchInput").value.trim();
  if (!sourceProfileId || !targetId) {
    setStatus("迁移建议需要当前配置和目标配置");
    return;
  }

  const layerText = state.previewUseOverlay ? "草稿层" : "原始层";
  updateWorkflowStatus("draft");
  state.migrationLastRequest = {
    sourceProfileId,
    targetProfileId: targetId,
    query,
    take: 200,
    targetOodlePath: $("oodlePathInput").value.trim() || null,
    sourceOodlePath: $("oodlePathInput").value.trim() || null,
    useOverlay: state.previewUseOverlay
  };
  setStatus(`正在生成${layerText}迁移建议...`);
  const result = await api("/api/resources/migration-plan", {
    sourceProfileId,
    targetProfileId: targetId,
    query,
    take: 200,
    sourceOodlePath: $("oodlePathInput").value.trim() || null,
    targetOodlePath: $("oodlePathInput").value.trim() || null,
    useOverlay: state.previewUseOverlay
  });
  writeLog($("actionOutput"), {
    sourceMatched: result.sourceMatched,
    targetMatched: result.targetMatched,
    planned: result.planned,
    statusCounts: result.statusCounts,
    riskCounts: result.riskCounts,
    warnings: result.warnings,
    items: result.items.slice(0, 80)
  });
  showMigrationReview(result);
  await refreshMigrationPlans();
  updateWorkbench();
  setStatus(`${layerText}迁移建议：${result.planned}/${result.sourceMatched}`);
}

async function migrationDraft() {
  const sourceProfileId = selectedProfileId();
  const targetId = targetProfileId();
  const query = $("searchInput").value.trim();
  if (!sourceProfileId || !targetId) {
    setStatus("生成草稿需要当前配置和目标配置");
    return;
  }

  const layerText = state.previewUseOverlay ? "草稿层" : "原始层";
  state.migrationLastRequest = {
    sourceProfileId,
    targetProfileId: targetId,
    query,
    take: 200,
    targetOodlePath: $("oodlePathInput").value.trim() || null,
    sourceOodlePath: $("oodlePathInput").value.trim() || null,
    useOverlay: state.previewUseOverlay
  };
  setStatus(`正在启动${layerText}迁移草稿任务...`);
  const job = await api("/api/jobs/resources/migration-draft", {
    sourceProfileId,
    targetProfileId: targetId,
    query,
    take: 200,
    sourceOodlePath: $("oodlePathInput").value.trim() || null,
    targetOodlePath: $("oodlePathInput").value.trim() || null,
    useOverlay: state.previewUseOverlay,
    includeHashMatches: true,
    includeCandidates: false,
    maxRiskLevel: 0
  });
  trackJob(job.id);
}

function showMigrationReview(result) {
  state.migrationReview = result;
  state.migrationLastRequest = {
    sourceProfileId: result.sourceProfileId,
    targetProfileId: result.targetProfileId,
    query: state.migrationLastRequest?.query ?? $("searchInput").value.trim(),
    take: state.migrationLastRequest?.take ?? 200,
    sourceOodlePath: state.migrationLastRequest?.sourceOodlePath ?? ($("oodlePathInput").value.trim() || null),
    targetOodlePath: state.migrationLastRequest?.targetOodlePath ?? ($("oodlePathInput").value.trim() || null),
    useOverlay: state.migrationLastRequest?.useOverlay ?? state.previewUseOverlay
  };
  state.migrationFilter = "all";
  renderMigrationReview();
}

function renderMigrationReview() {
  const result = state.migrationReview;
  const panel = $("migrationReview");
  const list = $("migrationList");
  if (!result || !panel || !list) return;

  panel.classList.remove("hidden");
  const drafted = result.items || [];
  const skipped = result.skippedItems || [];
  const planned = result.planned ?? drafted.length + skipped.length;
  $("migrationSummary").textContent = `计划 ${planned} · 写入 ${result.drafted ?? 0} · 跳过 ${result.skipped ?? 0}`;
  $("saveMigrationPlanBtn").disabled = !planned;
  updateMigrationPlanPicker();
  const rows = [
    ...drafted.map((item) => ({ ...item, drafted: true, reason: "已写入草稿" })),
    ...skipped.map((item) => ({ ...item, drafted: false }))
  ].filter((item) => {
    if (state.migrationFilter === "drafted") return item.drafted;
    if (state.migrationFilter === "skipped") return !item.drafted;
    if (state.migrationFilter === "risk") return item.riskLevel === 2;
    if (state.migrationFilter === "candidate") return item.status === 2;
    return true;
  });

  for (const button of document.querySelectorAll(".migration-tabs button")) {
    button.classList.toggle("selected", button.dataset.filter === state.migrationFilter);
  }

  list.innerHTML = "";
  for (const item of rows.slice(0, 120)) {
    const row = document.createElement("div");
    row.className = `migration-item ${item.drafted ? "drafted" : "skipped"}`;
    row.innerHTML = `
      <div class="migration-line">
        <span class="migration-path">${item.sourcePath}</span>
        <div class="migration-actions">
          ${!item.drafted && item.targetPath && item.status !== 3 ? '<button type="button" data-action="apply">写入</button>' : ""}
          <button type="button" data-action="find-source">源</button>
          ${item.targetPath ? '<button type="button" data-action="find-target">目标</button>' : ""}
          <span class="migration-badge">${item.drafted ? "已写入" : "已跳过"}</span>
        </div>
      </div>
      <div class="migration-meta">
        <span>${migrationStatusText(item.status)}</span>
        <span>风险 ${riskText(item.riskLevel)}</span>
        <span>${item.targetPath || "无目标"}</span>
      </div>
      <div class="migration-reason">${item.reason || (item.hints || []).join(" / ") || "无说明"}</div>
    `;
    const applyButton = row.querySelector('[data-action="apply"]');
    if (applyButton) {
      applyButton.addEventListener("click", () => applyMigrationItem(item));
    }
    row.querySelector('[data-action="find-source"]')?.addEventListener("click", () => jumpToMigrationPath(item.sourcePath, "source"));
    row.querySelector('[data-action="find-target"]')?.addEventListener("click", () => jumpToMigrationPath(item.targetPath, "target"));
    list.appendChild(row);
  }

  if (rows.length === 0) {
    list.innerHTML = '<div class="migration-empty">当前筛选没有结果</div>';
  }

  writeLog($("actionOutput"), {
    planned,
    drafted: result.drafted ?? 0,
    skipped: result.skipped ?? 0,
    warnings: result.warnings || [],
    showing: rows.length
  });
}

function buildMigrationCriteria() {
  const last = state.migrationLastRequest;
  if (!last) return null;
  return {
    sourceProfileId: last.sourceProfileId,
    targetProfileId: last.targetProfileId,
    query: last.query || "",
    kind: null,
    extension: $("extensionFilter").value || null,
    take: last.take || 200,
    sourceOodlePath: last.sourceOodlePath || null,
    targetOodlePath: last.targetOodlePath || null,
    useOverlay: last.useOverlay ?? state.previewUseOverlay
  };
}

function currentMigrationPlanItems() {
  const result = state.migrationReview;
  if (!result) return [];
  if (result.items?.some((item) => item.sourceSha256 !== undefined || item.hints !== undefined)) {
    return result.items;
  }

  return [
    ...(result.items || []).map((item) => ({
      sourcePath: item.sourcePath,
      targetPath: item.targetPath,
      status: item.status,
      riskLevel: item.riskLevel,
      kind: item.kind ?? 0,
      extension: item.extension ?? "",
      score: item.score ?? 0,
      pathMatched: item.pathMatched ?? false,
      hashMatched: item.hashMatched ?? false,
      sizeMatched: item.sizeMatched ?? false,
      sourceSha256: item.sha256 ?? "",
      targetSha256: item.targetSha256 ?? null,
      sourceSize: item.size ?? 0,
      targetSize: item.targetSize ?? null,
      hints: item.hints || [item.reason || ""]
    })),
    ...(result.skippedItems || []).map((item) => ({
      sourcePath: item.sourcePath,
      targetPath: item.targetPath,
      status: item.status,
      riskLevel: item.riskLevel,
      kind: item.kind ?? 0,
      extension: item.extension ?? "",
      score: 0,
      pathMatched: false,
      hashMatched: false,
      sizeMatched: false,
      sourceSha256: item.sha256 ?? "",
      targetSha256: null,
      sourceSize: item.size ?? 0,
      targetSize: null,
      hints: [item.reason || ""]
    }))
  ];
}

function updateMigrationPlanPicker() {
  const select = $("migrationPlanSelect");
  if (!select) return;
  const selected = select.value;
  select.innerHTML = '<option value="">未保存方案</option>';
  for (const plan of state.migrationPlans) {
    const option = document.createElement("option");
    option.value = plan.id;
    option.textContent = `${plan.name} (${plan.planned})`;
    select.appendChild(option);
  }
  if (selected && state.migrationPlans.some((plan) => plan.id === selected)) {
    select.value = selected;
  }
  const hasPlan = Boolean(select.value);
  $("loadMigrationPlanBtn").disabled = !hasPlan;
  $("validateMigrationPlanBtn").disabled = !hasPlan;
  $("applyMigrationPlanBtn").disabled = !hasPlan;
  $("applyCandidatesPlanBtn").disabled = !hasPlan;
  $("runPatchPipelineBtn").disabled = !hasPlan;
  $("deleteMigrationPlanBtn").disabled = !hasPlan;
}

async function refreshMigrationPlans() {
  const sourceProfileId = selectedProfileId();
  const targetId = targetProfileId();
  if (!sourceProfileId) {
    state.migrationPlans = [];
    updateMigrationPlanPicker();
    return;
  }

  const result = await api("/api/resources/migration-plans/list", {
    sourceProfileId,
    targetProfileId: targetId || null
  });
  state.migrationPlans = result.items || [];
  updateMigrationPlanPicker();
  updateWorkbench();
}

async function saveMigrationPlan() {
  const criteria = buildMigrationCriteria();
  const items = currentMigrationPlanItems();
  if (!criteria || items.length === 0) {
    setStatus("请先生成迁移建议");
    return;
  }

  const name = `${$("searchInput").value.trim() || "全部"} → ${state.profiles.find((profile) => profile.id === criteria.targetProfileId)?.displayName || "目标"}`;
  setStatus("正在保存迁移方案...");
  const saved = await api("/api/resources/migration-plans/save", {
    id: $("migrationPlanSelect").value || null,
    name,
    criteria,
    items
  });
  await refreshMigrationPlans();
  $("migrationPlanSelect").value = saved.id;
  updateMigrationPlanPicker();
  writeLog($("actionOutput"), saved);
  updateWorkbench();
  setStatus(`已保存方案：${saved.name}`);
}

async function loadMigrationPlan() {
  const sourceProfileId = selectedProfileId();
  const planId = $("migrationPlanSelect").value;
  if (!sourceProfileId || !planId) return;

  setStatus("正在加载迁移方案...");
  const plan = await api("/api/resources/migration-plans/load", { sourceProfileId, planId });
  state.migrationLastRequest = plan.criteria;
  showMigrationReview({
    sourceProfileId: plan.criteria.sourceProfileId,
    targetProfileId: plan.criteria.targetProfileId,
    sourceMatched: plan.planned,
    targetMatched: 0,
    planned: plan.planned,
    statusCounts: plan.statusCounts,
    riskCounts: plan.riskCounts,
    items: plan.items,
    warnings: []
  });
  $("migrationPlanSelect").value = plan.id;
  updateMigrationPlanPicker();
  updateWorkbench();
  setStatus(`已加载方案：${plan.name}`);
}

async function applyMigrationPlan() {
  await applyMigrationPlanWithOptions(false);
}

async function applyCandidatesMigrationPlan() {
  await applyMigrationPlanWithOptions(true);
}

async function applyMigrationPlanWithOptions(includeCandidates) {
  const sourceProfileId = selectedProfileId();
  const planId = $("migrationPlanSelect").value;
  if (!sourceProfileId || !planId) return;

  setStatus(includeCandidates ? "正在启动候选应用任务..." : "正在启动方案应用任务...");
  const job = await api("/api/jobs/resources/migration-plan-apply", {
    sourceProfileId,
    planId,
    includeHashMatches: true,
    includeCandidates,
    maxRiskLevel: includeCandidates ? 1 : 0,
    useOverlay: state.previewUseOverlay,
    sourceOodlePath: $("oodlePathInput").value.trim() || null
  });
  trackJob(job.id);
}

async function validateMigrationPlan() {
  const sourceProfileId = selectedProfileId();
  const planId = $("migrationPlanSelect").value;
  if (!sourceProfileId || !planId) return;

  setStatus("正在校验迁移方案...");
  const result = await api("/api/resources/migration-plans/validate", {
    sourceProfileId,
    planId,
    useOverlay: state.previewUseOverlay,
    sourceOodlePath: $("oodlePathInput").value.trim() || null,
    targetOodlePath: $("oodlePathInput").value.trim() || null
  });
  showMigrationValidation(result);
  writeLog($("actionOutput"), result);
  setStatus(`校验完成：可用 ${result.ready} · 变化 ${result.changed} · 缺失 ${result.missing}`);
}

async function runPatchPipeline() {
  const sourceProfileId = selectedProfileId();
  const targetId = targetProfileId();
  const planId = $("migrationPlanSelect").value;
  if (!planId) {
    await patchBuild();
    return;
  }
  if (!sourceProfileId || !targetId) return;

  setStatus("正在启动一键补丁流水线...");
  updateWorkflowStatus("patch");
  const job = await api("/api/jobs/patch/pipeline-run", {
    sourceProfileId,
    targetProfileId: targetId,
    migrationPlanId: planId,
    template: 0,
    bundleName: "Tiny.V0.1.bundle.bin",
    writerKind: 0,
    oodlePath: $("oodlePathInput").value.trim() || null,
    includeCandidates: false,
    maxRiskLevel: 0,
    sandboxRootPath: $("sandboxPathInput").value.trim() || null
  });
  trackJob(job.id);
}

async function openWorkbenchOutput() {
  const outputDirectory = state.workbenchLastOutput?.build?.outputDirectory;
  if (!outputDirectory) {
    setStatus("还没有补丁输出目录");
    return;
  }

  writeLog($("actionOutput"), {
    outputDirectory,
    zipPath: state.workbenchLastOutput.build.zipPath,
    sandboxRootPath: state.workbenchLastOutput.sandbox?.sandboxRootPath || null
  });
  setStatus(`输出目录：${outputDirectory}`);
}

function showMigrationValidation(result) {
  state.migrationReview = {
    sourceProfileId: result.sourceProfileId,
    targetProfileId: result.targetProfileId,
    planned: result.checked,
    drafted: result.ready,
    skipped: result.changed + result.missing + result.blocked,
    items: result.items.map((item) => ({
      sourcePath: item.sourcePath,
      targetPath: item.targetPath,
      status: item.state === 2 ? 3 : 2,
      riskLevel: item.riskLevel,
      drafted: item.state === 0,
      reason: item.reason
    })),
    skippedItems: []
  };
  state.migrationFilter = "all";
  renderMigrationReview();
  updateWorkbench();
}

async function deleteMigrationPlan() {
  const sourceProfileId = selectedProfileId();
  const planId = $("migrationPlanSelect").value;
  if (!sourceProfileId || !planId) return;

  setStatus("正在删除迁移方案...");
  const result = await api("/api/resources/migration-plans/delete", { sourceProfileId, planId });
  await refreshMigrationPlans();
  writeLog($("actionOutput"), result);
  setStatus(result.removed ? "方案已删除" : "方案不存在");
}

function selectMigrationPlan() {
  updateMigrationPlanPicker();
  updateWorkbench();
}

async function jumpToMigrationPath(path, side) {
  if (!path) return;
  const last = state.migrationLastRequest;
  const profileId = side === "target" ? last?.targetProfileId : last?.sourceProfileId;
  if (!profileId) {
    setStatus("缺少迁移上下文，请重新生成迁移建议");
    return;
  }

  if (side === "target") {
    $("targetProfileSelect").value = profileId;
  } else {
    $("profileSelect").value = profileId;
    state.selectedProfile = state.profiles.find((profile) => profile.id === profileId) || state.selectedProfile;
  }
  $("searchInput").value = path;
  await searchResources();
  setStatus(`${side === "target" ? "目标" : "源"}路径已定位`);
}

async function applyMigrationItem(item) {
  const last = state.migrationLastRequest;
  if (!last || !item.targetPath) {
    setStatus("缺少迁移上下文，请重新生成迁移建议");
    return;
  }

  setStatus("正在写入确认项...");
  const result = await api("/api/resources/migration-apply-item", {
    sourceProfileId: last.sourceProfileId,
    targetProfileId: last.targetProfileId,
    sourcePath: item.sourcePath,
    targetPath: item.targetPath,
    sourceOodlePath: last.sourceOodlePath,
    useOverlay: last.useOverlay,
    maxRiskLevel: 1
  });
  item.drafted = true;
  item.reason = "已手动确认写入草稿";
  renderMigrationReview();
  writeLog($("actionOutput"), result);
  setStatus(`已写入：${result.targetPath}`);
  refreshOverlayList();
}

function setMigrationFilter(filter) {
  state.migrationFilter = filter;
  renderMigrationReview();
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

function renderResources(items, total = items.length) {
  state.lastResourceItems = items;
  state.resourceSearchTotal = total;
  const list = $("resourceList");
  list.innerHTML = "";
  if (items.length < total) {
    const notice = document.createElement("div");
    notice.className = "resource-tree-notice";
    notice.textContent = `当前只显示 ${items.length}/${total} 个结果，请输入更具体路径筛选。`;
    list.appendChild(notice);
  }

  const tree = buildResourceTree(items);
  renderResourceTreeNode(list, tree, 0);
}

function buildResourceTree(items) {
  const root = { name: "", path: "", directories: new Map(), files: [], draftCount: 0 };
  for (const item of items) {
    item.hasDraft = state.overlayDraftPaths.has(normalizeVirtualPath(item.virtualPath));
    const parts = item.virtualPath.split(/[\\/]+/).filter(Boolean);
    const fileName = parts.pop() || item.virtualPath;
    let node = root;
    const pathNodes = [root];
    for (const part of parts) {
      const path = node.path ? `${node.path}/${part}` : part;
      if (!node.directories.has(part)) {
        node.directories.set(part, { name: part, path, directories: new Map(), files: [], draftCount: 0 });
      }
      node = node.directories.get(part);
      pathNodes.push(node);
    }
    node.files.push({ name: fileName, resource: item });
    if (item.hasDraft) {
      for (const pathNode of pathNodes) {
        pathNode.draftCount += 1;
      }
    }
  }
  return root;
}

function renderResourceTreeNode(container, node, depth) {
  const directories = Array.from(node.directories.values())
    .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" }));
  for (const directory of directories) {
    const defaultExpanded = depth < 2 || node.directories.size === 1;
    const expanded = state.resourceExpandedPaths.has(directory.path)
      || (defaultExpanded && !state.resourceCollapsedPaths.has(directory.path));
    const row = document.createElement("button");
    row.type = "button";
    row.className = `resource-tree-row resource-tree-dir${directory.draftCount > 0 ? " resource-tree-has-draft" : ""}`;
    row.title = directory.draftCount > 0 ? `此目录下有 ${directory.draftCount} 个草稿文件` : directory.path;
    row.style.setProperty("--depth", depth);
    row.innerHTML = `
      <span class="resource-tree-toggle">${expanded ? "v" : ">"}</span>
      <span class="resource-tree-name">${directory.name}</span>
      <span class="resource-tree-draft-dot" aria-hidden="true"></span>
      <span class="resource-tree-count">${countResourceTreeFiles(directory)}</span>
    `;
    row.addEventListener("click", () => {
      if (expanded) {
        state.resourceExpandedPaths.delete(directory.path);
        state.resourceCollapsedPaths.add(directory.path);
      } else {
        state.resourceExpandedPaths.add(directory.path);
        state.resourceCollapsedPaths.delete(directory.path);
      }
      renderResources(state.lastResourceItems, state.resourceSearchTotal);
    });
    container.appendChild(row);
    if (expanded) {
      renderResourceTreeNode(container, directory, depth + 1);
    }
  }

  const files = node.files.sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" }));
  for (const file of files) {
    const item = file.resource;
    const button = document.createElement("button");
    button.type = "button";
    button.className = `resource-item resource-tree-row resource-tree-file${item.hasDraft ? " resource-tree-has-draft" : ""}${isSelectedResourceItem(item) ? " selected" : ""}`;
    button.style.setProperty("--depth", depth);
    button.dataset.profileId = item.profileId;
    button.dataset.virtualPath = item.virtualPath;
    button.title = item.hasDraft ? `该文件已有草稿：${item.virtualPath}` : item.virtualPath;
    button.innerHTML = `
      <span class="resource-tree-leaf"></span>
      <span class="resource-path" title="${escapeHtml(item.virtualPath)}">${escapeHtml(file.name)}</span>
      <span class="resource-tree-draft-dot" aria-hidden="true"></span>
      <span class="resource-meta">${escapeHtml(item.extension || "file")} · ${formatBytes(item.size)}</span>
    `;
    button.addEventListener("click", () => previewResource(item, button, true));
    container.appendChild(button);
  }
}

function countResourceTreeFiles(node) {
  let count = node.files.length;
  for (const child of node.directories.values()) {
    count += countResourceTreeFiles(child);
  }
  return count;
}

async function previewResource(resource, button, useOverlay = state.previewUseOverlay) {
  await closeLargeTextEditor();
  state.selectedResource = resource;
  clearManualReferenceSelection();
  state.previewUseOverlay = useOverlay;
  addRecentResource(resource);
  for (const item of document.querySelectorAll(".resource-item")) {
    item.classList.remove("selected");
  }
  if (button) button.classList.add("selected");
  for (const item of document.querySelectorAll(".resource-item")) {
    item.classList.toggle("selected", item.dataset.profileId === resource.profileId
      && normalizeVirtualPath(item.dataset.virtualPath) === normalizeVirtualPath(resource.virtualPath));
  }
  $("selectedPath").textContent = resource.virtualPath;
  configureResourceWorkspace(resource);
  $("previewKind").textContent = "加载中";
  updatePreviewLayerTabs();
  setStatus("正在读取预览...");
  const preview = await api("/api/preview", {
    profileId: resource.profileId,
    virtualPath: resource.virtualPath,
    limit: previewLimitForResource(resource),
    oodlePath: currentOodlePath(),
    useOverlay
  });
  renderPreview(preview, { deferText: isComparableTextResource(resource) });
  renderResourceWorkspacePreview(resource, preview);
  if (usesCodeMirrorEditor(resource)) {
    await openLargeTextEditor(resource, preview);
  }
  $("saveOverlayBtn").disabled = usesCodeMirrorEditor(resource) ? false : preview.kind !== 1 || preview.truncated;
  $("exportResourceBtn").disabled = false;
  $("signatureBtn").disabled = false;
  $("replaceResourceBtn").disabled = false;
  const isTable = isTableResource(resource);
  $("inspectTableBtn").disabled = !isTable;
  $("exportTableCsvBtn").disabled = !isTable;
  $("importTableCsvBtn").disabled = !isTable;
  $("scanTableRefsBtn").disabled = !isTable;
  const canStructured = resource.kind === 1 || resource.kind === 6 || textExtensions.has((resource.extension || "").toLowerCase());
  $("inspectStructuredBtn").disabled = !canStructured;
  $("saveStructuredBtn").disabled = !canStructured;
  $("inferSchemaBtn").disabled = !isTable;
  $("saveSchemaBtn").disabled = !isTable;
  $("batchOverlayBtn").disabled = preview.kind !== 1 || preview.truncated;
  $("batchReplaceBtn").disabled = preview.kind !== 1;
  if (isTable) {
    await refreshTableSchemas();
    await inspectTable({ auto: true });
  } else {
    resetTableSchemaPicker();
  }
  setStatus("预览已加载");
  updateWorkflowStatus("match");
}

function addRecentResource(resource) {
  const key = `${resource.profileId}:${resource.virtualPath}`;
  state.recentResources = [
    resource,
    ...state.recentResources.filter((item) => `${item.profileId}:${item.virtualPath}` !== key)
  ].slice(0, 8);
  renderRecentResources();
}

function renderRecentResources() {
  const list = $("recentResourceList");
  if (!list) return;
  $("recentCount").textContent = String(state.recentResources.length);
  list.innerHTML = "";
  if (state.recentResources.length === 0) {
    list.innerHTML = '<div class="recent-empty">还没有打开资源</div>';
    return;
  }
  for (const resource of state.recentResources) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "recent-item";
    const fileName = resource.virtualPath.split(/[\\/]/).pop() || resource.virtualPath;
    button.innerHTML = `
      <span title="${escapeHtml(resource.virtualPath)}">${escapeHtml(fileName)}</span>
      <small>${escapeHtml(resource.extension || "file")} · ${formatBytes(resource.size)}</small>
    `;
    button.addEventListener("click", () => previewResource(resource, null, true));
    list.appendChild(button);
  }
}

function configureResourceWorkspace(resource) {
  const extension = (resource.extension || "").toLowerCase();
  const fileName = resource.virtualPath.split(/[\\/]/).pop() || resource.virtualPath;
  const { source, target } = resolveCurrentPair();
  const isTarget = resource.profileId === targetProfileId();
  const isSource = resource.profileId === sourceProfileId();
  $("editorTitle").textContent = fileName;
  $("selectedPath").textContent = resource.virtualPath;
  $("tableTargetPathBlock").classList.add("hidden");
  $("tableTargetTitle").textContent = "目标文件";
  $("tableTargetPath").textContent = "";
  $("resourceInfo").innerHTML = `
    <strong>${escapeHtml(fileName)}</strong>
    <span>${escapeHtml(resource.virtualPath)}</span>
    <span>${escapeHtml(extension || "无扩展名")} · ${formatBytes(resource.size)} · ${resource.sourceLayer === 1 ? "草稿" : "基础"} · ${isTarget ? "目标" : isSource ? "参考" : "资源"}</span>
  `;
  const activeFlow = isTableResource(resource) || resource.kind === 1 || textExtensions.has(extension)
    ? "match"
    : "search";
  updateWorkflowStatus(activeFlow);

  const isTable = isTableResource(resource);
  const isText = resource.kind === 1 || resource.kind === 6 || textExtensions.has(extension);
  const isImage = resource.kind === 3 || imageExtensions.has(extension);
  const isAudio = resource.kind === 4 || audioExtensions.has(extension);
  const isCsd = isCsdResource(resource);
  const isCodeMirrorText = usesCodeMirrorEditor(resource);
  const groups = {
    table: ["quickMatchBtn", "quickApplyReferenceBtn", "quickRestoreDefaultBtn", "exportTableCsvBtn", "importTableCsvBtn", "saveTableBtn", "scanTableRefsBtn"],
    csd: ["quickMatchBtn", "csdAdoptTraditionalBtn", "saveOverlayBtn", "exportResourceBtn", "replaceResourceBtn", "patchDryRunBtn"],
    text: ["quickMatchBtn", "quickApplyReferenceBtn", "quickRestoreDefaultBtn", "saveOverlayBtn", "exportResourceBtn", "replaceResourceBtn", "patchDryRunBtn"],
    media: ["exportResourceBtn", "replaceResourceBtn", "quickRestoreDefaultBtn"],
    binary: ["exportResourceBtn", "replaceResourceBtn", "signatureBtn", "quickRestoreDefaultBtn"]
  };
  const visible = isTable ? groups.table : isCsd ? groups.csd : isCodeMirrorText
    ? ["quickMatchBtn", "saveOverlayBtn", "exportResourceBtn", "replaceResourceBtn", "patchDryRunBtn"]
    : isText ? groups.text : (isImage || isAudio) ? groups.media : groups.binary;
  for (const button of document.querySelectorAll("#resourceQuickActions button")) {
    button.classList.toggle("hidden", !visible.includes(button.id));
  }
  $("csdTagStatus")?.classList.toggle("hidden", !isCsd);
  updateDatc64DiffFilterControls(false);
  $("compareWorkspace").classList.toggle("hidden", !(isTable || isText));
  $("previewText").classList.toggle("solo-preview", !(isTable || isText));
  $("quickMatchBtn").disabled = !(isTable || isText) || !source || !target || !isTarget || source.id === target.id;
  $("manualReferenceBtn").disabled = !(isTable || isText) || !source || !target || !isTarget || source.id === target.id;
  $("quickApplyReferenceBtn").disabled = !(isTable || isText) || !isTarget;
  $("quickRestoreDefaultBtn").disabled = !isTarget;
  $("csdAdoptTraditionalBtn").disabled = !isCsd || !isTarget;
  $("referenceStatus").textContent = isTarget ? "未加载" : "请先打开目标资源";
  $("referencePreviewText").value = "";
}

function renderResourceWorkspacePreview(resource, preview) {
  const extension = (resource.extension || "").toLowerCase();
  const comparable = isTableResource(resource) || resource.kind === 1 || resource.kind === 6 || textExtensions.has(extension);
  if (!comparable) {
    $("compareWorkspace").classList.add("hidden");
    return;
  }

  if (isTableResource(resource) && preview.kind !== 1) {
    $("compareWorkspace").classList.add("hidden");
    $("targetPreviewText").value = "";
    $("targetStatus").textContent = "表格检查";
    return;
  }

  if (usesCodeMirrorEditor(resource)) {
    $("targetPreviewText").value = "";
    $("targetPreviewText").readOnly = true;
    const layer = preview.fromOverlay ? "草稿层" : "原始层";
    $("targetStatus").textContent = `${layer} · CodeMirror`;
    return;
  }

  $("targetPreviewText").value = preview.text || preview.hex || preview.message || "";
  $("targetPreviewText").readOnly = preview.kind !== 1;
  const layer = preview.fromOverlay ? "草稿层" : "原始层";
  $("targetStatus").textContent = preview.truncated ? `${layer} · 快速预览，已截断` : layer;
  if (isCsdResource(resource)) {
    renderCsdTagStatus($("targetPreviewText").value);
  }
}

async function loadReferenceForSelectedResource() {
  if (!state.selectedResource) return null;
  if (isTableResource(state.selectedResource) && isTableComparisonInspection(state.tableEditBase)) {
    const reference = await loadTableReferenceInspection(state.tableEditBase);
    renderComparisonTable(state.tableEditBase, reference);
    setStatus(reference ? "已匹配国服表格参考" : "未匹配到国服表格参考");
    return reference ? { reference: state.tableReference?.resource || null, preview: reference } : null;
  }

  const { sourceId, targetId } = resolveCurrentPair();
  if (!sourceId || !targetId || sourceId === targetId) {
    $("referenceStatus").textContent = "未选择国服参考";
    return null;
  }
  if (!isSelectedTargetResource()) {
    $("referenceStatus").textContent = "请先打开目标资源";
    return null;
  }

  $("referenceStatus").textContent = "正在匹配";
  updateWorkflowStatus("match");
  const matched = state.manualReferenceResource
    ? { resource: state.manualReferenceResource, matchMode: "手动选择" }
    : await findReferenceResourceByLanguageAwarePath(sourceId, state.selectedResource, 10);
  const reference = matched.resource;
  if (!reference) {
    $("referenceStatus").textContent = "未匹配";
    $("referencePreviewText").value = "";
    $("largeTextReferenceStatus").textContent = "未匹配";
    return null;
  }

  if (state.largeText.active && usesCodeMirrorEditor(state.selectedResource)) {
    state.largeText.referenceResource = reference;
    const preview = await loadLargeTextReference(reference);
    $("referenceStatus").textContent = matched.matchMode;
    $("largeTextReferenceStatus").textContent = preview ? `${matched.matchMode} · 整文件` : matched.matchMode;
    return { reference, preview };
  }

  const preview = await api("/api/preview", {
    profileId: reference.profileId,
    virtualPath: reference.virtualPath,
    limit: previewLimitForResource(reference),
    oodlePath: currentOodlePath(),
    useOverlay: false
  });
  $("referencePreviewText").value = preview.text || preview.hex || preview.message || "";
  $("referenceStatus").textContent = matched.matchMode;
  return { reference, preview };
}

function splitVirtualPath(value) {
  return String(value || "").replaceAll("\\", "/").split("/").filter(Boolean);
}

function joinVirtualPath(parts) {
  return parts.join("/");
}

function findLanguageDirectoryIndex(parts) {
  return parts.findIndex((part) => languageDirectoryNames.has(part.toLowerCase()));
}

function isSimplifiedReferenceDirectory(value) {
  return simplifiedReferenceDirectories.includes(String(value || "").toLowerCase());
}

function buildReferencePathCandidates(targetPath) {
  const parts = splitVirtualPath(targetPath);
  const languageIndex = findLanguageDirectoryIndex(parts);
  if (languageIndex < 0) {
    return [joinVirtualPath(parts)];
  }

  const candidates = [];
  for (const languageDirectory of simplifiedReferenceDirectories) {
    const next = [...parts];
    next[languageIndex] = languageDirectory;
    candidates.push(joinVirtualPath(next));
  }
  return candidates;
}

function buildTargetPathCandidates(sourcePath) {
  const parts = splitVirtualPath(sourcePath);
  const languageIndex = findLanguageDirectoryIndex(parts);
  if (languageIndex < 0) {
    return [joinVirtualPath(parts)];
  }

  const candidates = [];
  for (const languageDirectory of targetLanguageDirectories) {
    const next = [...parts];
    next[languageIndex] = languageDirectory;
    candidates.push(joinVirtualPath(next));
  }
  return candidates;
}

async function findReferenceResourceByLanguageAwarePath(sourceId, targetResource, take = 50) {
  const candidates = buildReferencePathCandidates(targetResource.virtualPath);
  for (const candidate of candidates) {
    try {
      const resource = await api("/api/resources/by-path", {
        profileId: sourceId,
        virtualPath: candidate
      });
      return {
        resource,
        matchMode: candidate === targetResource.virtualPath ? "同路径" : "简体路径"
      };
    } catch {
      // Try the next explicit language-aware candidate before fuzzy search.
    }
  }

  const targetParts = splitVirtualPath(targetResource.virtualPath);
  const targetHasLanguageDirectory = findLanguageDirectoryIndex(targetParts) >= 0;
  const fileName = targetParts[targetParts.length - 1] || targetResource.virtualPath;
  const result = await api("/api/resources/search", {
    profileId: sourceId,
    query: fileName,
    extension: targetResource.extension || null,
    skip: 0,
    take
  });
  const normalizedCandidates = new Set(candidates.map((item) => item.toLowerCase()));
  const exactCandidate = result.items.find((item) =>
    normalizedCandidates.has((item.normalizedPath || item.virtualPath).toLowerCase()));
  if (exactCandidate) {
    return { resource: exactCandidate, matchMode: "简体候选" };
  }

  if (targetHasLanguageDirectory) {
    const languageCandidate = result.items.find((item) => {
      const parts = splitVirtualPath(item.virtualPath);
      const languageIndex = findLanguageDirectoryIndex(parts);
      return languageIndex >= 0
        && isSimplifiedReferenceDirectory(parts[languageIndex])
        && parts[parts.length - 1]?.toLowerCase() === fileName.toLowerCase();
    });
    return languageCandidate
      ? { resource: languageCandidate, matchMode: "语言候选" }
      : { resource: null, matchMode: "未匹配" };
  }

  const normalized = (targetResource.normalizedPath || targetResource.virtualPath).toLowerCase();
  const fallback = result.items.find((item) => (item.normalizedPath || item.virtualPath).toLowerCase() === normalized)
    || result.items.find((item) => item.virtualPath.toLowerCase().endsWith(`/${fileName.toLowerCase()}`))
    || result.items[0] || null;
  return { resource: fallback, matchMode: fallback ? "候选" : "未匹配" };
}

async function openManualReferenceDialog() {
  const { sourceId, targetId } = resolveCurrentPair();
  if (!state.selectedResource || !sourceId || !targetId || sourceId === targetId) {
    setStatus("请先选择目标资源和国服参考配置");
    return;
  }

  const fileName = splitVirtualPath(state.selectedResource.virtualPath).pop() || state.selectedResource.virtualPath;
  $("manualReferenceSearchInput").value = fileName;
  $("manualReferenceResults").innerHTML = "";
  $("manualReferenceStatus").textContent = "未搜索";
  $("manualReferenceDialog").showModal();
  await searchManualReferenceResources();
}

async function searchManualReferenceResources() {
  const sourceId = sourceProfileId();
  const query = $("manualReferenceSearchInput").value.trim();
  if (!sourceId || !query) return;

  $("manualReferenceStatus").textContent = "正在搜索";
  const result = await api("/api/resources/search", {
    profileId: sourceId,
    query,
    extension: state.selectedResource?.extension || null,
    skip: 0,
    take: 80
  });
  renderManualReferenceResults(result.items || []);
  $("manualReferenceStatus").textContent = `${result.items.length}/${result.total}`;
}

function renderManualReferenceResults(items) {
  const host = $("manualReferenceResults");
  if (!items.length) {
    host.innerHTML = `<div class="empty-state">没有找到参考资源</div>`;
    return;
  }

  host.innerHTML = "";
  for (const resource of items) {
    const row = document.createElement("div");
    row.className = "manual-reference-item";
    const text = document.createElement("div");
    const title = document.createElement("strong");
    title.textContent = resource.virtualPath;
    const meta = document.createElement("span");
    meta.textContent = `${resource.extension || "无扩展"} · ${resource.size} bytes`;
    text.appendChild(title);
    text.appendChild(meta);
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = "选择";
    button.addEventListener("click", () => chooseManualReferenceResource(resource));
    row.appendChild(text);
    row.appendChild(button);
    host.appendChild(row);
  }
}

async function chooseManualReferenceResource(resource) {
  state.manualReferenceResource = resource;
  if ($("manualReferenceDialog")?.open) {
    $("manualReferenceDialog").close();
  }
  $("referenceStatus").textContent = "手动选择";
  setStatus("已选择国服参考路径");
  await loadReferenceForSelectedResource();
}

function clearManualReferenceSelection() {
  state.manualReferenceResource = null;
  state.tableReference = null;
  if ($("referenceStatus")) $("referenceStatus").textContent = "未加载";
  if ($("referencePreviewText")) $("referencePreviewText").value = "";
  if ($("largeTextReferenceStatus")) $("largeTextReferenceStatus").textContent = "未加载";
}

function formatBytes(size) {
  if (!Number.isFinite(size)) return "0 B";
  if (size >= 1024 * 1024) return `${(size / 1024 / 1024).toFixed(1)} MB`;
  if (size >= 1024) return `${Math.max(1, Math.round(size / 1024))} KB`;
  return `${size} B`;
}

function updatePreviewLayerTabs() {
  $("previewOverlayBtn").disabled = !state.selectedResource;
  $("previewBaseBtn").disabled = !state.selectedResource;
  $("previewOverlayBtn").classList.toggle("selected", state.previewUseOverlay);
  $("previewBaseBtn").classList.toggle("selected", !state.previewUseOverlay);
}

async function switchPreviewLayer(useOverlay) {
  if (!state.selectedResource) return;
  const selectedButton = Array.from(document.querySelectorAll(".resource-item.selected"))[0] || null;
  await previewResource(state.selectedResource, selectedButton, useOverlay);
}

async function loadCodeMirrorModule() {
  if (!state.largeText.module) {
    state.largeText.module = await import("./vendor/codemirror/poe-codemirror.js?v=20260514-csd-codemirror");
  }

  return state.largeText.module;
}

async function closeLargeTextEditor() {
  state.largeText.active = false;
  state.largeText.referenceResource = null;
  if (state.largeText.targetEditor) {
    state.largeText.targetEditor.destroy();
    state.largeText.targetEditor = null;
  }

  if (state.largeText.referenceEditor) {
    state.largeText.referenceEditor.destroy();
    state.largeText.referenceEditor = null;
  }

  if ($("largeTextTargetEditor")) $("largeTextTargetEditor").innerHTML = "";
  if ($("largeTextReferenceEditor")) $("largeTextReferenceEditor").innerHTML = "";
  if ($("largeTextEditor")) $("largeTextEditor").classList.add("hidden");
}

function languageForResource(resource) {
  const extension = (resource?.extension || "").toLowerCase();
  if (extension === ".json") return "json";
  if (extension === ".xml" || extension === ".ui") return "xml";
  return "text";
}

async function createOrUpdateLargeTextEditor(slot, hostId, text, resource, readOnly) {
  const module = await loadCodeMirrorModule();
  if (state.largeText[slot]) {
    state.largeText[slot].setValue(text || "");
    return;
  }

  state.largeText[slot] = module.createPoeEditor($(hostId), {
    doc: text || "",
    language: languageForResource(resource),
    readOnly
  });
}

function showLargeTextError(message) {
  const text = message || "大文件编辑器加载失败";
  $("largeTextStatus").textContent = `大文件编辑器加载失败：${text}`;
  $("largeTextTargetStatus").textContent = "加载失败";
  setStatus(`大文件编辑器加载失败：${text}`);
}

function renderLargeTextStatus(preview) {
  const layer = preview.fromOverlay ? "草稿层" : "原始层";
  $("largeTextTitle").textContent = `${state.selectedResource?.virtualPath || "整文件编辑"}`;
  const text = preview.text || "";
  if (preview.truncated) {
    $("largeTextStatus").textContent = `${layer} · 已截断`;
  } else if (shouldUseLargeTextFastOpen(text)) {
    $("largeTextStatus").textContent = `${layer} · 整文件 · 大文件模式`;
  } else {
    $("largeTextStatus").textContent = `${layer} · 整文件 · ${countTextLines(text)} 行`;
  }
  $("largeTextTargetStatus").textContent = preview.truncated ? "目标草稿 · 已截断" : "目标草稿 · 整文件";
}

function countTextLines(text) {
  if (!text) return 0;
  const normalized = String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n");
  return normalized.endsWith("\n") ? normalized.split("\n").length - 1 : normalized.split("\n").length;
}

function shouldUseLargeTextFastOpen(text) {
  const size = Number(state.selectedResource?.size || 0);
  return size > largeCsdAutoAnalysisThreshold || String(text || "").length > largeCsdAutoAnalysisThreshold;
}

async function openLargeTextEditor(resource, preview) {
  state.largeText.active = true;
  $("compareWorkspace").classList.add("hidden");
  $("previewText").classList.add("hidden");
  $("largeTextEditor").classList.remove("hidden");
  $("largeTextReferenceStatus").textContent = "未加载";
  $("largeTextTargetStatus").textContent = "正在加载";
  $("largeTextStatus").textContent = "正在打开整文件";
  try {
    await createOrUpdateLargeTextEditor("targetEditor", "largeTextTargetEditor", preview.text || preview.hex || preview.message || "", resource, preview.kind !== 1);
    renderLargeTextStatus(preview);
    if (isCsdResource(resource)) {
      renderCsdTagStatus(preview.text || "");
    }
    if (state.largeText.referenceResource) {
      await loadLargeTextReference(state.largeText.referenceResource);
    }
  } catch (error) {
    showLargeTextError(error.message);
  }
}

async function loadLargeTextReference(reference) {
  if (!reference) return null;
  $("largeTextReferenceStatus").textContent = "正在读取";
  try {
    const preview = await api("/api/preview", {
      profileId: reference.profileId,
      virtualPath: reference.virtualPath,
      limit: previewLimitForResource(reference),
      oodlePath: currentOodlePath(),
      useOverlay: false
    });
    await createOrUpdateLargeTextEditor("referenceEditor", "largeTextReferenceEditor", preview.text || preview.hex || preview.message || "", reference, true);
    $("largeTextReferenceStatus").textContent = preview.truncated ? "已截断" : `整文件 · ${countTextLines(preview.text || "")} 行`;
    return preview;
  } catch (error) {
    $("largeTextReferenceStatus").textContent = `加载失败：${error.message}`;
    return null;
  }
}

function renderPreview(preview, options = {}) {
  const media = $("mediaPreview");
  const text = $("previewText");
  const inspection = $("inspectionPanel");
  $("previewKind").textContent = preview.fromOverlay ? `${previewKindText(preview.kind)} · 草稿层` : previewKindText(preview.kind);
  $("tableTargetPathBlock").classList.add("hidden");
  clearTableInfoMenu();
  media.classList.add("hidden");
  media.innerHTML = "";
  inspection.classList.add("hidden");
  inspection.innerHTML = "";
  $("tableEditor").classList.add("hidden");
  $("tableEditor").classList.remove("datc64-table-editor");
  $("tableEditor").innerHTML = "";
  $("largeTextEditor").classList.add("hidden");
  state.tableEditBase = null;
  state.tableReference = null;
  state.structuredEditBase = null;
  $("saveTableBtn").disabled = true;
  text.classList.remove("hidden");
  text.readOnly = preview.kind !== 1;
  text.value = options.deferText ? "" : (preview.text || preview.hex || preview.message || "");
  renderInspection(preview.inspection);

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

function renderInspection(inspection) {
  const panel = $("inspectionPanel");
  if (!inspection) return;
  const props = Object.entries(inspection.properties || {})
    .map(([key, value]) => `<span>${escapeHtml(key)}: ${escapeHtml(value)}</span>`)
    .join("");
  const warnings = (inspection.warnings || [])
    .map((item) => `<div class="inspection-warning">${escapeHtml(item)}</div>`)
    .join("");
  panel.innerHTML = `
    <div class="inspection-head">
      <strong>${escapeHtml(inspection.format)}</strong>
      <span>${escapeHtml(inspection.summary)}</span>
    </div>
    <div class="inspection-props">${props}</div>
    ${warnings}
  `;
  panel.classList.remove("hidden");
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

async function saveOverlay() {
  if (!state.selectedResource) return;

  setStatus("正在保存覆盖...");
  const result = await api("/api/overlay/save-text", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    text: activeTextEditor().value,
    oodlePath: currentOodlePath()
  });
  writeLog($("actionOutput"), result);
  setStatus("覆盖已保存");
  refreshOverlayList();
}

async function applyReferenceText() {
  if (!state.selectedResource) return;
  if (!isSelectedTargetResource()) {
    setStatus("请先打开国际服目标资源，再应用国服参考。");
    return;
  }
  const extension = (state.selectedResource.extension || "").toLowerCase();
  const isTable = state.selectedResource.kind === 2 || tableExtensions.has(extension);
  if (isTable) {
    if (isDatc64ComparisonInspection(state.tableEditBase) || isLegacyDatComparisonInspection(state.tableEditBase)) {
      await applyDatc64ReferenceCells();
      return;
    }

    if (isStringCandidateComparisonInspection(state.tableEditBase)) {
      setStatus("当前 .dat 文本候选为只读对比，不能直接应用参考；请先确认结构定义。");
      return;
    }

    setStatus("当前表格还没有可应用的并排参考，请先检查表格。");
    return;
  }
  if (!$("referencePreviewText").value) {
    await loadReferenceForSelectedResource();
  }
  const referenceText = $("referencePreviewText").value;
  if (!referenceText) {
    setStatus("没有可应用的参考内容");
    return;
  }
  activeTextEditor().value = referenceText;
  if (state.largeText.active) {
    setStatus("参考块已应用到目标块，点击保存草稿写入。");
    return;
  }

  setStatus("参考内容已应用到目标草稿，点击保存草稿写入。");
}

function adoptCsdSimplifiedChineseSlot() {
  if (!state.selectedResource || !isCsdResource(state.selectedResource)) {
    setStatus("请先打开 .csd 文件。");
    return;
  }
  if (!isSelectedTargetResource()) {
    setStatus("请先打开国际服目标 .csd 文件。");
    return;
  }

  const target = activeTextEditor();
  const original = target.value || "";
  const simplifiedTag = 'lang "Simplified Chinese"';
  if (!original.includes(simplifiedTag)) {
    setStatus("当前 CSD 没有 Simplified Chinese 标签，可能已经处理过。");
    renderCsdTagStatus(original);
    return;
  }

  let protectedTraditionalCount = 0;
  let changedTraditionalCount = 0;
  let changedSimplifiedCount = 0;
  const processCsdDescriptionBlock = (block) => {
    const hasTraditional = block.text.includes('lang "Traditional Chinese"');
    const hasSimplified = block.text.includes('lang "Simplified Chinese"');
    if (hasTraditional && !hasSimplified) {
      protectedTraditionalCount += (block.text.match(/lang "Traditional Chinese"/g) || []).length;
      return block;
    }
    if (!hasSimplified) return block;
    changedTraditionalCount += (block.text.match(/lang "Traditional Chinese"/g) || []).length;
    changedSimplifiedCount += (block.text.match(/lang "Simplified Chinese"/g) || []).length;
    return {
      ...block,
      text: block.text
      .replaceAll('lang "Traditional Chinese"', 'lang "#Traditional Chinese"')
      .replaceAll('lang "Simplified Chinese"', 'lang "Traditional Chinese"')
    };
  };
  const next = splitCsdDescriptionBlocks(original)
    .map((block) => block.text.trimStart().startsWith("description") ? processCsdDescriptionBlock(block).text : block.text)
    .join("");

  target.value = next;
  renderCsdTagStatus(next);
  $("saveOverlayBtn").disabled = false;
  setStatus(`CSD 已接管语言槽：${changedSimplifiedCount} 个简中标签改为繁中，${changedTraditionalCount} 个原繁中标签已避让，保留 ${protectedTraditionalCount} 个缺简繁中块。点击保存草稿写入。`);
}

async function applyDatc64ReferenceCells() {
  if (!isDatc64ComparisonInspection(state.tableEditBase) && !isLegacyDatComparisonInspection(state.tableEditBase)) {
    setStatus("请先打开表格对比。");
    return;
  }

  if (!state.tableReference?.inspection) {
    const reference = await loadTableReferenceInspection(state.tableEditBase);
    renderDatc64ComparisonTable(state.tableEditBase, reference);
  }

  if (state.datc64AgGrid?.api) {
    const editableIndexes = state.datc64AgGrid.editableIndexes || new Set();
    let applied = 0;
    state.datc64AgGrid.api.forEachNode((node) => {
      const data = node.data;
      if (!data) return;
      for (const columnIndex of editableIndexes) {
        const reference = data.__referenceCells?.[columnIndex] ?? "";
        if (!reference) continue;
        if ((data[`c${columnIndex}`] ?? "") !== reference) {
          data[`c${columnIndex}`] = reference;
          applied++;
        }
      }
    });
    state.datc64AgGrid.api.refreshCells({ force: true });
    $("saveTableBtn").disabled = false;
    setStatus(applied > 0 ? `已应用国服参考：${applied} 个表格单元格，点击保存草稿写入。` : "没有可应用的参考差异");
    return;
  }

  if ($("datc64TsvTarget") && state.datc64Tsv?.referenceRows) {
    const editableIndexes = state.datc64Tsv.editableIndexes || new Set();
    persistVisibleDatc64TsvEdits();
    let applied = 0;
    for (const row of state.datc64Tsv.visibleRows || []) {
      const rowNumber = row.rowNumber;
      const referenceRow = state.datc64Tsv.referenceRows.get(rowNumber);
      if (!referenceRow) continue;
      for (const columnIndex of editableIndexes) {
        const reference = referenceRow.cells?.[columnIndex] ?? "";
        if (!reference) continue;
        const current = state.datc64Tsv.edits.get(rowNumber)?.get(columnIndex) ?? row.cells?.[columnIndex] ?? "";
        if (current !== reference) {
          let rowEdits = state.datc64Tsv.edits.get(rowNumber);
          if (!rowEdits) {
            rowEdits = new Map();
            state.datc64Tsv.edits.set(rowNumber, rowEdits);
          }
          rowEdits.set(columnIndex, reference);
          applied++;
        }
      }
    }
    renderDatc64TsvVirtualRows();
    $("saveTableBtn").disabled = false;
    setStatus(applied > 0 ? `已应用国服参考：${applied} 个表格单元格，点击保存草稿写入。` : "没有可应用的参考差异");
    return;
  }

  const inputs = Array.from($("tableEditor").querySelectorAll(".datc64-target-cell"));
  let applied = 0;
  for (const input of inputs) {
    const reference = input.dataset.reference || "";
    if (!reference) continue;
    if (getDatc64CellValue(input) !== reference) {
      setDatc64CellValue(input, reference);
      applied++;
    }
  }

  $("saveTableBtn").disabled = false;
  setStatus(applied > 0 ? `已应用国服参考：${applied} 个单元格，点击保存草稿写入。` : "没有可应用的参考差异");
}

async function exportResource() {
  if (!state.selectedResource) return;
  const layerText = state.previewUseOverlay ? "草稿层" : "原始层";
  setStatus(`正在导出${layerText}...`);
  const result = await api("/api/resources/export", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    oodlePath: $("oodlePathInput").value.trim() || null,
    useOverlay: state.previewUseOverlay
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
  setStatus(`${layerText}已导出：${result.fileName}`);
}

async function extractSignature() {
  if (!state.selectedResource) return;
  const layerText = state.previewUseOverlay ? "草稿层" : "原始层";
  setStatus(`正在提取${layerText}特征...`);
  const result = await api("/api/resources/signature", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    oodlePath: $("oodlePathInput").value.trim() || null,
    useOverlay: state.previewUseOverlay
  });
  writeLog($("actionOutput"), result);
  setStatus(`${layerText}特征已提取`);
}

function chooseReplacementFile() {
  if (!state.selectedResource) return;
  $("replaceResourceInput").value = "";
  $("replaceResourceInput").click();
}

async function replaceResourceWithFile(file) {
  if (!state.selectedResource || !file) return;
  setStatus("正在保存替换资源...");
  const form = new FormData();
  form.append("profileId", state.selectedResource.profileId);
  form.append("virtualPath", state.selectedResource.virtualPath);
  form.append("file", file, file.name || "replacement.bin");
  const result = await apiForm("/api/overlay/save-file", form);
  writeLog($("actionOutput"), result);
  setStatus(`替换已保存：${file.name}`);
  refreshOverlayList();
}

async function inspectTable(options = {}) {
  if (!state.selectedResource) return;
  const schemaId = $("tableSchemaSelect").value || null;
  const schema = schemaId ? null : parseTableSchema();
  if (schema === false) return;
  setStatus("正在检查表格...");
  const result = await api("/api/tables/inspect", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    limit: tableInspectLimitForResource(state.selectedResource),
    oodlePath: $("oodlePathInput").value.trim() || null,
    schema,
    schemaId
  });
  writeLog($("actionOutput"), result);
  let referenceInspection = null;
  if (isTableComparisonInspection(result)) {
    referenceInspection = await loadTableReferenceInspection(result);
  }
  renderTableEditor(result, referenceInspection);
  if (result.structured || isStringCandidateComparisonInspection(result)) {
    if (isTableComparisonInspection(result)) {
      setStatus(referenceInspection ? `表格对比：${result.previewRowCount} 行` : `表格预览：${result.previewRowCount} 行，未匹配国服参考`);
    } else {
      setStatus(`表格预览：${result.previewRowCount} 行`);
    }
  } else {
    setStatus(options.auto ? "已打开表格检查：需要结构定义才能行列编辑" : "表格检查已生成：需要结构定义才能行列编辑");
  }
}

async function loadTableReferenceInspection(targetInspection) {
  const { sourceId, targetId } = resolveCurrentPair();
  if (!state.selectedResource || !sourceId || !targetId || sourceId === targetId || !isSelectedTargetResource()) {
    $("referenceStatus").textContent = "未选择国服参考";
    state.tableReference = null;
    return null;
  }

  $("referenceStatus").textContent = "正在匹配";
  const matched = state.manualReferenceResource
    ? { resource: state.manualReferenceResource, matchMode: "手动选择" }
    : await findReferenceResourceByLanguageAwarePath(sourceId, state.selectedResource, 50);
  const reference = matched.resource;
  const matchMode = matched.matchMode;

  if (!reference) {
    $("referenceStatus").textContent = "未匹配";
    state.tableReference = null;
    return null;
  }

  const referenceInspection = await api("/api/tables/inspect", {
    profileId: reference.profileId,
    virtualPath: reference.virtualPath,
    oodlePath: $("oodlePathInput").value.trim() || null,
    limit: tableInspectLimitForResource(reference)
  });
  const sameComparisonMode = (isStringCandidateComparisonInspection(referenceInspection) && isStringCandidateComparisonInspection(targetInspection))
    || referenceInspection.delimiter === targetInspection.delimiter;
  if (!sameComparisonMode) {
    $("referenceStatus").textContent = "结构不同";
    state.tableReference = { resource: reference, inspection: referenceInspection, matchMode: "结构不同" };
    return null;
  }

  $("referenceStatus").textContent = matchMode;
  state.tableReference = { resource: reference, inspection: referenceInspection, matchMode };
  return referenceInspection;
}

function tableInspectLimitForResource(resource) {
  const size = Number(resource?.size || 0);
  return Math.min(maxTextPreviewLimit, Math.max(defaultPreviewLimit, size + 16));
}

const agentCurrentViewRowLimit = 200;

function summarizeAgentTableRows(rows, limit = agentCurrentViewRowLimit, nullWhenMissing = false) {
  if (!rows && nullWhenMissing) return null;
  return (rows || []).slice(0, limit).map(function (row) {
    return {
      rowNumber: row.rowNumber,
      cells: (row.cells || []).map(function (cell) { return String(cell ?? ""); })
    };
  });
}

function buildAgentCurrentView() {
  if (!state.selectedResource || !state.tableEditBase) {
    return { kind: "none" };
  }

  const table = state.tableEditBase;
  const reference = state.tableReference;
  const sourceId = reference?.resource?.profileId ?? sourceProfileId() ?? null;
  const sourcePath = reference?.resource?.virtualPath ?? null;
  const targetId = state.selectedResource.profileId;
  const targetPath = state.selectedResource.virtualPath;
  const kind = reference?.inspection ? "tableComparison" : "table";

  return {
    kind,
    table: {
      profileId: targetId,
      resourcePath: targetPath,
      sourceProfileId: sourceId,
      sourceResourcePath: sourcePath,
      targetProfileId: targetId,
      targetResourcePath: targetPath,
      delimiter: table.delimiter || "",
      rowCount: table.rowCount || table.previewRowCount || 0,
      previewRowCount: table.previewRowCount || 0,
      columns: table.columns || [],
      editableColumnIndexes: table.editableColumnIndexes || [],
      targetRows: summarizeAgentTableRows(state.tableEditBase?.rows),
      sourceRows: summarizeAgentTableRows(state.tableReference?.inspection?.rows, agentCurrentViewRowLimit, true),
      referenceMatchMode: reference?.matchMode || null
    }
  };
}

async function exportTableCsv() {
  if (!state.selectedResource) return;
  const schemaId = $("tableSchemaSelect").value || null;
  const schema = schemaId ? null : parseTableSchema();
  if (schema === false) return;
  setStatus("正在导出表格 CSV...");
  const result = await api("/api/tables/export-csv", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    oodlePath: $("oodlePathInput").value.trim() || null,
    schema,
    schemaId
  });
  downloadText(`${state.selectedResource.virtualPath.split(/[\\/]/).pop() || "table"}.csv`, result.csv, "text/csv;charset=utf-8", { utf8Bom: true });
  writeLog($("actionOutput"), {
    virtualPath: result.virtualPath,
    rows: result.rows,
    columns: result.columns,
    warnings: result.warnings
  });
  setStatus(`CSV 已导出：${result.rows} 行`);
}

function chooseTableCsvFile() {
  if (!state.selectedResource) return;
  $("importTableCsvInput").value = "";
  $("importTableCsvInput").click();
}

async function importTableCsv(file) {
  if (!state.selectedResource || !file || state.tableCsvImporting) return;
  const schemaId = $("tableSchemaSelect").value || null;
  const schema = schemaId ? null : parseTableSchema();
  if (schema === false) return;
  state.tableCsvImporting = true;
  const button = $("importTableCsvBtn");
  const previousDisabled = button.disabled;
  button.disabled = true;
  try {
    setStatus(`正在上传 CSV：${file.name || "未命名文件"}`);
    const selectedResource = state.selectedResource;
    const form = new FormData();
    form.append("profileId", selectedResource.profileId);
    form.append("virtualPath", selectedResource.virtualPath);
    form.append("csvFile", file, file.name || "table.csv");
    const oodlePath = $("oodlePathInput").value.trim();
    if (oodlePath) form.append("oodlePath", oodlePath);
    if (schemaId) form.append("schemaId", schemaId);
    if (schema) form.append("schema", JSON.stringify(schema));
    const result = await apiForm("/api/tables/import-csv-file", form);
    writeLog($("actionOutput"), result);
    setStatus(`CSV 已写入草稿：${result.editedCells} 处`);
    refreshOverlayList();
    setStatus("CSV 已写入草稿，正在后台刷新当前表格...");
    setTimeout(() => inspectTable({ auto: true }).catch((error) => {
      console.error(error);
      setStatus(`CSV 已写入草稿，但刷新表格失败：${error.message || error}`);
    }), 50);
  } catch (error) {
    console.error(error);
    setStatus(`CSV 导入失败：${error.message || error}`);
  } finally {
    state.tableCsvImporting = false;
    button.disabled = previousDisabled;
  }
}

async function scanTableReferences() {
  if (!state.selectedResource) return;
  const targetPath = window.prompt("目标表路径", state.selectedResource.virtualPath);
  if (!targetPath) return;
  const sourceColumn = Number(window.prompt("当前表列号，从 0 开始", "0"));
  const targetColumn = Number(window.prompt("目标表列号，从 0 开始", "0"));
  if (!Number.isInteger(sourceColumn) || !Number.isInteger(targetColumn)) {
    setStatus("列号必须是整数");
    return;
  }

  const schemaId = $("tableSchemaSelect").value || null;
  const schema = schemaId ? null : parseTableSchema();
  if (schema === false) return;
  setStatus("正在扫描表格引用...");
  const result = await api("/api/tables/reference-scan", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    columnIndex: sourceColumn,
    targetVirtualPath: targetPath,
    targetColumnIndex: targetColumn,
    oodlePath: $("oodlePathInput").value.trim() || null,
    schema,
    schemaId,
    targetSchema: schema,
    targetSchemaId: schemaId
  });
  writeLog($("actionOutput"), result);
  setStatus(`引用扫描：命中 ${result.matched}，缺失 ${result.missing}`);
}

async function inspectStructuredResource() {
  if (!state.selectedResource) return;
  setStatus("正在检查结构...");
  const result = await api("/api/resources/structured-inspect", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    oodlePath: $("oodlePathInput").value.trim() || null,
    useOverlay: state.previewUseOverlay
  });
  state.structuredEditBase = result;
  renderStructuredEditor(result);
  writeLog($("actionOutput"), {
    virtualPath: result.virtualPath,
    format: result.format,
    nodeCount: result.nodeCount,
    warnings: result.warnings
  });
  setStatus(`结构节点：${result.nodeCount}`);
}

function renderStructuredEditor(result) {
  const editor = $("tableEditor");
  editor.innerHTML = "";
  editor.classList.add("hidden");
  if (!result.nodes?.length) return;

  const table = document.createElement("table");
  const header = document.createElement("tr");
  for (const title of ["Key", "Value", "Line"]) {
    const th = document.createElement("th");
    th.textContent = title;
    header.appendChild(th);
  }
  table.appendChild(header);
  for (const node of result.nodes) {
    const tr = document.createElement("tr");
    const keyCell = document.createElement("td");
    keyCell.textContent = node.key;
    const valueCell = document.createElement("td");
    const input = document.createElement("input");
    input.value = node.value;
    input.dataset.key = node.key;
    input.dataset.lineNumber = String(node.lineNumber);
    input.dataset.original = node.value;
    valueCell.appendChild(input);
    const lineCell = document.createElement("td");
    lineCell.textContent = String(node.lineNumber);
    tr.append(keyCell, valueCell, lineCell);
    table.appendChild(tr);
  }
  editor.appendChild(table);
  editor.classList.remove("hidden");
  $("previewText").classList.add("hidden");
}

async function saveStructuredResource() {
  if (!state.selectedResource) return;
  if (!state.structuredEditBase) {
    await inspectStructuredResource();
  }

  const edits = Array.from($("tableEditor").querySelectorAll("input"))
    .filter(input => input.value !== input.dataset.original)
    .map(input => ({
      key: input.dataset.key,
      value: input.value,
      lineNumber: Number(input.dataset.lineNumber)
    }));
  if (edits.length === 0) {
    setStatus("结构没有改动");
    return;
  }

  setStatus("正在保存结构...");
  const result = await api("/api/resources/structured-save", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    edits,
    oodlePath: $("oodlePathInput").value.trim() || null,
    useOverlay: state.previewUseOverlay
  });
  writeLog($("actionOutput"), result);
  setStatus(`结构已保存：${result.edited} 处`);
  refreshOverlayList();
  await previewResource(state.selectedResource, Array.from(document.querySelectorAll(".resource-item.selected"))[0] || null, true);
}

function renderTableEditor(result, referenceResult = null) {
  const editor = $("tableEditor");
  editor.innerHTML = "";
  editor.classList.add("hidden");
  editor.classList.remove("datc64-table-editor");
  clearTableInfoMenu();
  $("saveTableBtn").disabled = true;
  state.tableEditBase = null;
  state.datc64Tsv = null;
  state.datc64AgGrid = null;
  if (isTableComparisonInspection(result)) {
    renderComparisonTable(result, referenceResult);
    return;
  }

  if (!result.structured || !result.rows?.length) {
    renderBinaryTableInspection(result);
    return;
  }

  $("compareWorkspace").classList.add("hidden");
  const table = document.createElement("table");
  if (result.columns?.length) {
    const tr = document.createElement("tr");
    for (const column of result.columns) {
      const th = document.createElement("th");
      th.textContent = column;
      tr.appendChild(th);
    }
    table.appendChild(tr);
  }

  for (const row of result.rows) {
    const tr = document.createElement("tr");
    for (let columnIndex = 0; columnIndex < row.cells.length; columnIndex++) {
      const cell = document.createElement(!result.columns?.length && row.rowNumber === 1 ? "th" : "td");
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

function renderComparisonTable(result, referenceResult = null) {
  const editor = $("tableEditor");
  editor.classList.add("datc64-table-editor");
  $("compareWorkspace").classList.add("hidden");
  const editableIndexes = new Set(result.editableColumnIndexes || []);
  const columns = result.columns || [];
  const editableColumns = columns
    .map((column, index) => editableIndexes.has(index) ? column : null)
    .filter(Boolean);
  const tableSummary = [
    `目标 ${result.previewRowCount || 0} 行`,
    `${columns.length} 列`,
    `可编辑 ${editableIndexes.size} 列${editableColumns.length ? `：${editableColumns.slice(0, 4).join(" / ")}${editableColumns.length > 4 ? " ..." : ""}` : ""}`
  ].join(" · ");
  const hints = (result.layoutHints || [])
    .map((item) => `<li>${escapeHtml(item)}</li>`)
    .join("");
  const warningsList = result.warnings || [];
  const warnings = warningsList
    .map((item) => `<div class="inspection-warning">${escapeHtml(item)}</div>`)
    .join("");
  const referencePath = state.tableReference?.resource?.virtualPath || "未匹配";
  renderDatc64PathHeader(referencePath, state.selectedResource?.virtualPath || "");
  const compareTable = renderDatc64AgGridComparison(referenceResult, result, editableIndexes, tableSummary);
  const details = warningsList.length
    ? `
      <details class="datc64-diagnostics">
        <summary>诊断信息</summary>
        ${warnings}
        <ul>${hints || '<li class="binary-empty">暂无解析信息</li>'}</ul>
      </details>`
    : "";
  renderTableInfoMenu(referencePath, tableSummary, details);
  editor.innerHTML = `
    <div class="binary-inspector datc64-schema-inspector">
      ${compareTable}
    </div>
  `;
  editor.classList.remove("hidden");
  $("previewText").classList.add("hidden");
  $("saveTableBtn").disabled = editableIndexes.size === 0;
  state.tableEditBase = result;
  wireDatc64DiffTable();
}

function renderDatc64ComparisonTable(result, referenceResult = null) {
  renderComparisonTable(result, referenceResult);
}

function buildDatc64AgGridRows(targetResult, referenceResult) {
  const columns = targetResult.columns || [];
  const referenceRows = new Map((referenceResult?.rows || []).map((row) => [row.rowNumber, row]));
  const sourceRows = referenceResult?.rows?.length ? referenceResult.rows : (targetResult.rows || []);
  const targetRows = new Map((targetResult.rows || []).map((row) => [row.rowNumber, row]));
  return sourceRows.map((sourceRow) => {
    const row = targetRows.get(sourceRow.rowNumber) || { rowNumber: sourceRow.rowNumber, cells: [] };
    const referenceRow = referenceRows.get(row.rowNumber);
    const item = {
      __rowNumber: row.rowNumber,
      __cells: row.cells || [],
      __referenceCells: referenceRow?.cells || [],
      __originalCells: [...(row.cells || [])],
      __hasDiff: false
    };
    for (let columnIndex = 0; columnIndex < columns.length; columnIndex++) {
      item[`c${columnIndex}`] = row.cells?.[columnIndex] ?? "";
      if ((referenceRow?.cells?.[columnIndex] ?? "") !== (row.cells?.[columnIndex] ?? "")) {
        item.__hasDiff = true;
      }
    }
    return item;
  });
}

function buildDatc64AgGridReferenceRows(targetResult, referenceResult) {
  const columns = targetResult.columns || [];
  const targetRows = new Map((targetResult.rows || []).map((row) => [row.rowNumber, row]));
  const sourceRows = referenceResult?.rows?.length ? referenceResult.rows : (targetResult.rows || []);
  return sourceRows.map((row) => {
    const targetRow = targetRows.get(row.rowNumber);
    const item = {
      __rowNumber: row.rowNumber,
      __cells: row.cells || [],
      __referenceCells: row.cells || [],
      __originalCells: targetRow?.cells || [],
      __hasDiff: false
    };
    for (let columnIndex = 0; columnIndex < columns.length; columnIndex++) {
      item[`c${columnIndex}`] = row.cells?.[columnIndex] ?? "";
      if ((row.cells?.[columnIndex] ?? "") !== (targetRow?.cells?.[columnIndex] ?? "")) {
        item.__hasDiff = true;
      }
    }
    return item;
  });
}

function buildDatc64AgGridColumnDefs(columns, editableIndexes, side = "target") {
  const editableSet = editableIndexes || new Set();
  return [
    {
      headerName: "#",
      field: "__rowNumber",
      width: 72,
      pinned: "left",
      editable: false,
      lockPosition: true,
      cellClass: "datc64-ag-row-number"
    },
    ...columns.map((column, columnIndex) => ({
      headerName: datc64ColumnLabel(column, columnIndex),
      field: `c${columnIndex}`,
      minWidth: 110,
      width: Math.min(360, Math.max(120, 48 + String(column || "").length * 7)),
      editable: side === "target" && editableSet.has(columnIndex),
      cellClassRules: {
        "datc64-ag-diff-cell": (params) => (params.data?.__referenceCells?.[columnIndex] ?? "") !== (params.data?.__originalCells?.[columnIndex] ?? ""),
        "datc64-ag-editable-cell": () => side === "target" && editableSet.has(columnIndex),
        "datc64-ag-selected-cell": (params) => state.datc64AgGrid?.selectionSide === side
          && state.datc64AgGrid?.selectedCellKeys?.has(datc64CellKey(params.data?.__rowNumber, columnIndex))
      },
      valueSetter: (params) => {
        params.data[`c${columnIndex}`] = params.newValue ?? "";
        return true;
      }
    }))
  ];
}

function createDatc64AgGrid(host, rows, columns, editableIndexes, side) {
  if (!host || !window.agGrid?.createGrid) return null;
  const gridOptions = {
    rowData: rows,
    columnDefs: buildDatc64AgGridColumnDefs(columns, editableIndexes, side),
    defaultColDef: {
      sortable: false,
      resizable: true,
      filter: true,
      editable: false,
      suppressMovable: false
    },
    rowHeight: 18,
    headerHeight: 20,
    animateRows: false,
    suppressColumnVirtualisation: false,
    suppressRowVirtualisation: false,
    rowBuffer: 12,
    enableCellTextSelection: true,
    stopEditingWhenCellsLoseFocus: true,
    onCellValueChanged: () => {
      $("saveTableBtn").disabled = false;
    },
    onCellClicked: (event) => showDatc64AgGridActionCard(event, side),
    onGridReady: () => bindDatc64AgGridHeaderActions(host),
    isExternalFilterPresent: () => Boolean(state.datc64AgGrid?.diffRowsOnly),
    doesExternalFilterPass: (node) => {
      if (!state.datc64AgGrid?.diffRowsOnly) return true;
      return Boolean(node.data?.__hasDiff);
    }
  };
  return agGrid.createGrid(host, gridOptions);
}

function collectDatc64AgGridEdits() {
  const gridApi = state.datc64AgGrid?.targetApi || state.datc64AgGrid?.api;
  const editableIndexes = state.datc64AgGrid?.editableIndexes || new Set();
  if (!gridApi || !state.tableEditBase) return null;
  const edits = [];
  gridApi.forEachNode((node) => {
    const data = node.data;
    if (!data) return;
    for (const columnIndex of editableIndexes) {
      const value = data[`c${columnIndex}`] ?? "";
      const original = data.__originalCells?.[columnIndex] ?? "";
      if (value !== original) {
        edits.push({ rowNumber: data.__rowNumber, columnIndex, value });
      }
    }
  });
  return edits;
}

function datc64CellKey(rowNumber, columnIndex) {
  const row = Number(rowNumber);
  const column = Number(columnIndex);
  return Number.isInteger(row) && Number.isInteger(column) ? `${row}:${column}` : "";
}

function parseDatc64CellKey(key) {
  const match = String(key || "").match(/^(-?\d+):(\d+)$/);
  if (!match) return null;
  return { rowNumber: Number(match[1]), columnIndex: Number(match[2]) };
}

function positionDatc64ActionCardFromRect(rect, mode, rowNumber, columnIndex) {
  const actions = $("datc64CellActions");
  if (!actions || !rect) return;
  const host = $("tableEditor").getBoundingClientRect();
  const actionWidth = actions.offsetWidth || 108;
  const actionHeight = actions.offsetHeight || 58;
  const gap = 8;
  actions.dataset.mode = mode;
  actions.dataset.rowNumber = String(rowNumber || 0);
  actions.dataset.columnIndex = String(columnIndex);
  actions.style.left = `${Math.min(host.width - actionWidth - gap, Math.max(gap, rect.right - host.left + gap))}px`;
  actions.style.top = `${Math.min(host.height - actionHeight - gap, Math.max(gap, rect.top - host.top))}px`;
  actions.classList.remove("hidden");
  document.removeEventListener("click", hideDatc64ActionCard);
  setTimeout(() => document.addEventListener("click", hideDatc64ActionCard, { once: true }), 0);
}

function datc64AgColumnIndex(field) {
  const match = String(field || "").match(/^c(\d+)$/);
  return match ? Number(match[1]) : -1;
}

function showDatc64AgGridActionCard(event, side = "target") {
  const columnIndex = datc64AgColumnIndex(event.column?.getColId?.() || event.colDef?.field);
  event.event?.stopPropagation?.();
  if (columnIndex < 0) return;
  if (side === "target" && !event.colDef?.editable) return;
  const rowNumber = Number(event.data?.__rowNumber || 0);
  const focus = { rowNumber, columnIndex };
  if (event.event?.shiftKey) {
    event.event?.preventDefault?.();
    window.getSelection?.()?.removeAllRanges?.();
  }
  const anchor = event.event?.shiftKey
    && state.datc64AgGrid?.selectionAnchor
    && state.datc64AgGrid?.selectionSide === side
    ? state.datc64AgGrid.selectionAnchor
    : focus;
  setDatc64AgGridSelectedCellRange(anchor, focus, side);
  if (side !== "target") {
    hideDatc64ActionCard({ preserveAgSelection: true });
    return;
  }
  const rect = event.event?.target?.getBoundingClientRect?.();
  positionDatc64ActionCardFromRect(rect, "cell", rowNumber, columnIndex);
}

function showDatc64AgGridColumnActionCard(event) {
  const columnIndex = datc64AgColumnIndex(event.column?.getColId?.() || event.column?.getColDef?.()?.field);
  if (columnIndex < 0) return;
  event.event?.stopPropagation?.();
  const rect = event.event?.target?.getBoundingClientRect?.();
  positionDatc64ActionCardFromRect(rect, "column", 0, columnIndex);
}

function bindDatc64AgGridHeaderActions(host) {
  if (!host || host.dataset.datc64HeaderActionsBound === "true") return;
  const headerRoot = host.querySelector(".ag-header");
  if (!headerRoot) return;
  host.dataset.datc64HeaderActionsBound = "true";
  headerRoot.addEventListener("click", (event) => {
    const headerCell = event.target?.closest?.(".ag-header-cell");
    if (!headerCell) return;
    const columnIndex = datc64AgColumnIndex(
      headerCell.getAttribute("col-id") ||
      headerCell.dataset.colId ||
      headerCell.dataset.colid ||
      headerCell.querySelector(".ag-header-cell-comp-wrapper")?.getAttribute?.("col-id")
    );
    if (columnIndex < 0) return;
    const rect = headerCell.getBoundingClientRect?.();
    if (!rect) return;
    event.stopPropagation?.();
    positionDatc64ActionCardFromRect(rect, "column", 0, columnIndex);
  });
}

function setDatc64AgGridSelectedCellRange(anchor, focus, side = "target") {
  if (!state.datc64AgGrid) return;
  const selected = new Set();
  if (anchor && focus) {
    const rowStart = Math.min(anchor.rowNumber, focus.rowNumber);
    const rowEnd = Math.max(anchor.rowNumber, focus.rowNumber);
    const columnStart = Math.min(anchor.columnIndex, focus.columnIndex);
    const columnEnd = Math.max(anchor.columnIndex, focus.columnIndex);
    for (let rowNumber = rowStart; rowNumber <= rowEnd; rowNumber++) {
      for (let columnIndex = columnStart; columnIndex <= columnEnd; columnIndex++) {
        if (side === "reference" || state.datc64AgGrid.editableIndexes?.has(columnIndex)) {
          selected.add(datc64CellKey(rowNumber, columnIndex));
        }
      }
    }
  }
  state.datc64AgGrid.selectionSide = side;
  state.datc64AgGrid.selectionAnchor = anchor || null;
  state.datc64AgGrid.pasteAnchor = side === "target" ? focus : state.datc64AgGrid.pasteAnchor;
  state.datc64AgGrid.selectedCellKeys = selected;
  state.datc64AgGrid.referenceApi?.refreshCells({ force: true });
  state.datc64AgGrid.targetApi?.refreshCells({ force: true });
}

function datc64SelectedAgGridCells() {
  const selected = state.datc64AgGrid?.selectedCellKeys;
  if (!selected?.size) return [];
  return Array.from(selected)
    .map(parseDatc64CellKey)
    .filter(Boolean)
    .sort((left, right) => left.rowNumber - right.rowNumber || left.columnIndex - right.columnIndex);
}

function datc64AgGridApiForSide(side) {
  return side === "reference"
    ? state.datc64AgGrid?.referenceApi
    : state.datc64AgGrid?.targetApi;
}

function datc64AgGridRowsByNumber(api) {
  const rows = new Map();
  api?.forEachNode((node) => {
    if (node.data) rows.set(node.data.__rowNumber, node.data);
  });
  return rows;
}

function buildDatc64AgGridClipboardMatrix(cells, rowByNumber) {
  if (!cells.length) return [];
  const rowNumbers = Array.from(new Set(cells.map((cell) => cell.rowNumber))).sort((left, right) => left - right);
  const columnIndexes = Array.from(new Set(cells.map((cell) => cell.columnIndex))).sort((left, right) => left - right);
  const selectedKeys = new Set(cells.map((cell) => datc64CellKey(cell.rowNumber, cell.columnIndex)));
  return rowNumbers.map((rowNumber) => {
    const row = rowByNumber.get(rowNumber);
    return columnIndexes.map((columnIndex) => {
      if (!selectedKeys.has(datc64CellKey(rowNumber, columnIndex))) return "";
      return row?.[`c${columnIndex}`] ?? "";
    });
  });
}

function datc64ClipboardCellToTsv(value) {
  const cell = String(value ?? "");
  if (cell.includes("\t") || cell.includes("\n") || cell.includes("\r") || cell.includes("\"")) {
    return `"${cell.replaceAll("\"", "\"\"")}"`;
  }
  return cell;
}

function serializeDatc64AgGridSelection(cells, rowByNumber) {
  const matrix = buildDatc64AgGridClipboardMatrix(cells, rowByNumber);
  return matrix.map((row) => row.map(datc64ClipboardCellToTsv).join("\t")).join("\n");
}

function parseDatc64ClipboardTsv(text) {
  const rows = [];
  let row = [];
  let cell = "";
  let quote = false;
  const source = String(text || "").replace(/\r\n/g, "\n").replace(/\r/g, "\n");
  for (let index = 0; index < source.length; index++) {
    const char = source[index];
    if (char === "\"") {
      if (quote && source[index + 1] === "\"") {
        cell += "\"";
        index++;
      } else {
        quote = !quote;
      }
      continue;
    }
    if (char === "\t" && !quote) {
      row.push(cell);
      cell = "";
      continue;
    }
    if (char === "\n" && !quote) {
      row.push(cell);
      rows.push(row);
      row = [];
      cell = "";
      continue;
    }
    cell += char;
  }
  row.push(cell);
  rows.push(row);
  if (rows.length > 1 && rows[rows.length - 1].length === 1 && rows[rows.length - 1][0] === "") {
    rows.pop();
  }
  return rows;
}

function copyDatc64AgGridSelectionToClipboard(event) {
  const grid = state.datc64AgGrid;
  if (!grid?.selectionSide) return false;
  const selectedCells = datc64SelectedAgGridCells();
  if (!selectedCells.length) return false;
  const sourceApi = datc64AgGridApiForSide(grid.selectionSide);
  const matrix = buildDatc64AgGridClipboardMatrix(selectedCells, datc64AgGridRowsByNumber(sourceApi));
  const text = matrix.map((row) => row.map(datc64ClipboardCellToTsv).join("\t")).join("\n");
  if (!text) return false;
  grid.clipboardText = text;
  grid.clipboardMatrix = matrix;
  event?.clipboardData?.setData("text/plain", text);
  event?.preventDefault?.();
  setStatus(`已复制 ${selectedCells.length} 个单元格`);
  return true;
}

function pasteDatc64AgGridClipboardToTarget(event) {
  const grid = state.datc64AgGrid;
  if (!grid?.targetApi || state.datc64AgGrid.selectionSide !== "target") return false;
  const anchor = grid.pasteAnchor || grid.selectionAnchor;
  if (!anchor) return false;
  const pastedText = event?.clipboardData?.getData("text/plain") || "";
  const text = pastedText || grid.clipboardText;
  const isInternalClipboard = Boolean(grid.clipboardMatrix) && (!pastedText || pastedText === grid.clipboardText);
  const rows = isInternalClipboard ? grid.clipboardMatrix : parseDatc64ClipboardTsv(text);
  if (!rows.length) return false;
  const rowNumbers = [];
  grid.targetApi.forEachNode((node) => {
    if (node.data) rowNumbers.push(node.data.__rowNumber);
  });
  rowNumbers.sort((left, right) => left - right);
  const startRowOffset = rowNumbers.indexOf(anchor.rowNumber);
  if (startRowOffset < 0) return false;
  const rowByNumber = datc64AgGridRowsByNumber(grid.targetApi);
  let changed = 0;
  for (let rowOffset = 0; rowOffset < rows.length; rowOffset++) {
    const targetRowNumber = rowNumbers[startRowOffset + rowOffset];
    if (!rowByNumber.has(targetRowNumber)) continue;
    const data = rowByNumber.get(targetRowNumber);
    for (let columnOffset = 0; columnOffset < rows[rowOffset].length; columnOffset++) {
      const targetColumnIndex = anchor.columnIndex + columnOffset;
      if (!grid.editableIndexes?.has(targetColumnIndex)) continue;
      const value = rows[rowOffset][columnOffset] ?? "";
      if ((data[`c${targetColumnIndex}`] ?? "") !== value) {
        data[`c${targetColumnIndex}`] = value;
        changed++;
      }
    }
  }
  if (changed === 0) {
    setStatus("粘贴完成：没有可写入的变化");
    event?.preventDefault?.();
    return true;
  }
  grid.targetApi.refreshCells({ force: true });
  $("saveTableBtn").disabled = false;
  event?.preventDefault?.();
  setStatus(`已粘贴 ${changed} 个单元格，请确认后保存草稿`);
  return true;
}

function applyDatc64AgGridDiffFilters() {
  const grid = state.datc64AgGrid;
  if (!grid?.targetApi) return false;
  grid.diffRowsOnly = Boolean($("diffRowsOnlyInput")?.checked);
  grid.diffColumnsOnly = Boolean($("diffColumnsOnlyInput")?.checked);
  const columns = state.tableEditBase?.columns || [];
  const diffColumns = new Set();
  grid.targetApi.forEachNode((node) => {
    const data = node.data;
    if (!data) return;
    for (let columnIndex = 0; columnIndex < columns.length; columnIndex++) {
      if ((data.__referenceCells?.[columnIndex] ?? "") !== (data.__originalCells?.[columnIndex] ?? "")) {
        diffColumns.add(columnIndex);
      }
    }
  });
  for (let columnIndex = 0; columnIndex < columns.length; columnIndex++) {
    const field = `c${columnIndex}`;
    const hide = state.datc64AgGrid?.diffColumnsOnly && !diffColumns.has(columnIndex);
    grid.referenceApi?.setColumnsVisible([field], !hide);
    grid.targetApi?.setColumnsVisible([field], !hide);
  }
  grid.referenceApi?.onFilterChanged();
  grid.targetApi?.onFilterChanged();
  return true;
}

function syncDatc64AgGridScroll(sourceHost, targetHost) {
  if (!sourceHost || !targetHost) return;
  const sourceBody = sourceHost.querySelector(".ag-body-viewport");
  const targetBody = targetHost.querySelector(".ag-body-viewport");
  const sourceHorizontal = sourceHost.querySelector(".ag-body-horizontal-scroll-viewport");
  const targetHorizontal = targetHost.querySelector(".ag-body-horizontal-scroll-viewport");
  bindDatc64ScrollPair(sourceBody, targetBody, "vertical", syncDatc64AgGridVerticalScrollByRatio);
  bindDatc64ScrollPair(sourceHorizontal, targetHorizontal, "horizontal", (source, target) => {
    target.scrollLeft = source.scrollLeft;
  });
}

function syncDatc64AgGridVerticalScrollByRatio(source, target) {
  const sourceMax = Math.max(0, source.scrollHeight - source.clientHeight);
  const targetMax = Math.max(0, target.scrollHeight - target.clientHeight);
  if (sourceMax <= 0 || targetMax <= 0) {
    target.scrollTop = Math.min(source.scrollTop, targetMax);
    return;
  }
  const ratio = source.scrollTop / sourceMax;
  target.scrollTop = ratio * targetMax;
}

function bindDatc64ScrollPair(sourceElement, targetElement, axis, sync) {
  if (!sourceElement || !targetElement) return;
  const key = `datc64Sync${axis}`;
  if (sourceElement.dataset[key] === "true") return;
  sourceElement.dataset[key] = "true";
  let syncing = false;
  sourceElement.addEventListener("scroll", () => {
    if (syncing) return;
    syncing = true;
    sync(sourceElement, targetElement);
    requestAnimationFrame(() => { syncing = false; });
  });
}

function bindDatc64AgGridClipboardShortcuts(host) {
  if (!host || host.dataset.datc64ClipboardBound === "true") return;
  host.dataset.datc64ClipboardBound = "true";
  host.addEventListener("copy", (event) => {
    copyDatc64AgGridSelectionToClipboard(event);
  });
  host.addEventListener("paste", (event) => {
    pasteDatc64AgGridClipboardToTarget(event);
  });
  host.addEventListener("keydown", (event) => {
    const shortcut = event.ctrlKey || event.metaKey;
    if (!shortcut) return;
    const key = String(event.key || "").toLowerCase();
    if (key === "c") {
      copyDatc64AgGridSelectionToClipboard(event);
    } else if (key === "v") {
      pasteDatc64AgGridClipboardToTarget(event);
    }
  });
}

function renderDatc64AgGridComparison(referenceResult, targetResult, editableIndexes, tableSummary = "") {
  const columns = targetResult.columns || [];
  const rows = buildDatc64AgGridRows(targetResult, referenceResult);
  const referenceRows = buildDatc64AgGridReferenceRows(targetResult, referenceResult);
  const referenceStatus = referenceResult
    ? `${referenceResult.previewRowCount || 0} 行`
    : "未匹配";
  state.datc64AgGrid = {
    api: null,
    referenceApi: null,
    targetApi: null,
    editableIndexes: new Set(editableIndexes),
    selectedCellKeys: new Set(),
    selectionAnchor: null,
    selectionSide: null,
    pasteAnchor: null,
    clipboardText: "",
    clipboardMatrix: null,
    diffRowsOnly: false,
    diffColumnsOnly: false
  };

  requestAnimationFrame(() => {
    const referenceHost = $("datc64AgReferenceGridHost");
    const targetHost = $("datc64AgTargetGridHost");
    bindDatc64AgGridClipboardShortcuts(referenceHost);
    bindDatc64AgGridClipboardShortcuts(targetHost);
    state.datc64AgGrid.referenceApi = createDatc64AgGrid(referenceHost, referenceRows, columns, new Set(), "reference");
    state.datc64AgGrid.targetApi = createDatc64AgGrid(targetHost, rows, columns, editableIndexes, "target");
    state.datc64AgGrid.api = state.datc64AgGrid.targetApi;
    const bindScrollSync = () => {
      syncDatc64AgGridScroll(referenceHost, targetHost);
      syncDatc64AgGridScroll(targetHost, referenceHost);
    };
    requestAnimationFrame(bindScrollSync);
    setTimeout(bindScrollSync, 100);
  });

  return `
    <div class="datc64-diff-workspace datc64-ag-workspace">
      <div class="inspection-head">
        <strong>AG Grid 表格对比</strong>
        <span>国服参考 ${escapeHtml(referenceStatus)} · 专业表格引擎 · 虚拟滚动</span>
        <span class="datc64-target-summary">${escapeHtml(tableSummary)}</span>
      </div>
      <div class="datc64-ag-grid-pair">
        <section>
          <div class="datc64-pane-title">国服参考表格</div>
          <div id="datc64AgReferenceGridHost" class="datc64-ag-grid ag-theme-quartz"></div>
        </section>
        <section>
          <div class="datc64-pane-title">国际服目标表格</div>
          <div id="datc64AgTargetGridHost" class="datc64-ag-grid ag-theme-quartz"></div>
        </section>
      </div>
      <div id="datc64CellActions" class="datc64-cell-actions hidden">
        <button type="button" data-action="apply">应用参考</button>
        <button type="button" data-action="restore">恢复默认</button>
      </div>
    </div>
  `;
}

function renderTableInfoMenu(referencePath, tableSummary, detailsHtml) {
  const menu = $("tableInfoMenu");
  const body = $("tableInfoMenuBody");
  if (!menu || !body) return;
  body.innerHTML = detailsHtml || '<div class="table-info-empty">没有诊断警告</div>';
  menu.classList.remove("hidden");
}

function renderDatc64PathHeader(referencePath, targetPath) {
  const targetFileName = targetPath.split(/[\\/]/).pop() || targetPath || "目标文件";
  const referenceFileName = referencePath && referencePath !== "未匹配"
    ? (referencePath.split(/[\\/]/).pop() || referencePath)
    : "未匹配参考";
  $("editorTitle").textContent = referenceFileName;
  $("selectedPath").textContent = referencePath || "未匹配";
  $("tableTargetTitle").textContent = targetFileName;
  $("tableTargetPath").textContent = targetPath || "未选择目标";
  $("tableTargetPathBlock").classList.remove("hidden");
}

function clearTableInfoMenu() {
  const menu = $("tableInfoMenu");
  const body = $("tableInfoMenuBody");
  if (!menu || !body) return;
  menu.classList.add("hidden");
  menu.removeAttribute("open");
  body.innerHTML = "";
}

function escapeTsvCell(value) {
  return String(value ?? "")
    .replaceAll("\\", "\\\\")
    .replaceAll("\t", "\\t")
    .replaceAll("\r", "\\r")
    .replaceAll("\n", "\\n");
}

function unescapeTsvCell(value) {
  let result = "";
  const text = String(value ?? "");
  for (let index = 0; index < text.length; index++) {
    const current = text[index];
    if (current !== "\\" || index + 1 >= text.length) {
      result += current;
      continue;
    }

    const next = text[++index];
    if (next === "t") result += "\t";
    else if (next === "r") result += "\r";
    else if (next === "n") result += "\n";
    else if (next === "\\") result += "\\";
    else result += `\\${next}`;
  }
  return result;
}

function datc64ColumnLabel(column, index) {
  const clean = String(column || "").trim();
  return clean || `Column ${index}`;
}

function buildDatc64Tsv(inspection, rows, columns) {
  const sourceRows = rows || inspection?.rows || [];
  const sourceColumns = columns || inspection?.columns || [];
  const header = ["#", ...sourceColumns.map(datc64ColumnLabel)].map(escapeTsvCell).join("\t");
  const lines = [header];
  for (const row of sourceRows) {
    const cells = sourceColumns.map((_, columnIndex) => row.cells?.[columnIndex] ?? "");
    lines.push([row.rowNumber, ...cells].map(escapeTsvCell).join("\t"));
  }
  return lines.join("\n");
}

function parseDatc64Tsv(text, columns) {
  const result = new Map();
  const expectedColumns = columns || [];
  const lines = String(text || "").replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
  for (let lineIndex = 1; lineIndex < lines.length; lineIndex++) {
    const line = lines[lineIndex];
    if (!line.trim()) continue;
    const parts = line.split("\t");
    const rowNumber = Number(unescapeTsvCell(parts[0] || ""));
    if (!Number.isFinite(rowNumber) || rowNumber <= 0) continue;
    const cells = [];
    for (let columnIndex = 0; columnIndex < expectedColumns.length; columnIndex++) {
      cells[columnIndex] = unescapeTsvCell(parts[columnIndex + 1] ?? "");
    }
    result.set(rowNumber, cells);
  }
  return result;
}

function calculateDatc64TsvColumnWidths(columns, rows, referenceRows) {
  return calculateDatc64ColumnWidths(columns, rows, referenceRows);
}

function datc64TsvGridColumnStyle(columns, columnWidths) {
  const widths = columns.map((_, index) => `${columnWidths[index] || 160}px`).join(" ");
  return `52px ${widths || "120px"}`;
}

function renderDatc64TsvGrid(side, tsvText, columns, editableIndexes, columnWidths, referenceRows = new Map(), targetRows = new Map()) {
  const rows = parseDatc64Tsv(tsvText, columns);
  const editableSet = editableIndexes || new Set();
  const columnHeader = columns
    .map((column, columnIndex) => `<div class="datc64-tsv-head ${editableSet.has(columnIndex) ? "editable-column" : ""}" data-side="${side}" data-column-index="${columnIndex}">${escapeHtml(datc64ColumnLabel(column, columnIndex))}</div>`)
    .join("");
  const body = Array.from(rows.entries()).map(([rowNumber, cells]) => {
    const line = columns.map((_, columnIndex) => {
      const value = cells[columnIndex] ?? "";
      const editable = side === "target" && editableSet.has(columnIndex);
      const referenceValue = referenceRows.get(rowNumber)?.cells?.[columnIndex] ?? "";
      const targetValue = targetRows.get(rowNumber)?.cells?.[columnIndex] ?? "";
      const changed = referenceValue !== targetValue;
      const diffClass = changed ? "datc64-tsv-diff-cell" : "";
      const common = `data-row-number="${rowNumber}" data-column-index="${columnIndex}" data-has-diff="${changed ? "true" : "false"}" data-reference="${escapeHtml(referenceValue)}" data-original="${escapeHtml(targetValue)}"`;
      if (editable) {
        return `<div class="datc64-tsv-cell datc64-tsv-target-cell ${diffClass}" data-tsv-cell="true" ${common} contenteditable="true" spellcheck="false">${escapeHtml(value)}</div>`;
      }
      return `<div class="datc64-tsv-cell ${diffClass}" ${common}>${escapeHtml(value)}</div>`;
    }).join("");
    return `<div class="datc64-tsv-row"><div class="datc64-tsv-row-number">${rowNumber}</div>${line}</div>`;
  }).join("");
  const gridColumns = datc64TsvGridColumnStyle(columns, columnWidths || []);
  return `
    <div class="datc64-tsv-table" data-tsv-side="${side}" style="--datc64-tsv-columns:${gridColumns}">
      <div class="datc64-tsv-row datc64-tsv-header">
        <div class="datc64-tsv-row-number">#</div>
        ${columnHeader}
      </div>
      ${body || '<div class="datc64-tsv-empty">没有可显示的行</div>'}
    </div>
  `;
}

function datc64TsvVisibleRange(scrollTop, rowCount, viewportHeight) {
  const visibleCount = Math.ceil(Math.max(datc64VirtualRowHeight, viewportHeight || datc64VirtualRowHeight) / datc64VirtualRowHeight);
  const start = Math.max(0, Math.floor((scrollTop || 0) / datc64VirtualRowHeight) - datc64VirtualOverscan);
  const end = Math.min(rowCount, start + visibleCount + datc64VirtualOverscan * 2);
  return { start, end };
}

function datc64TsvRowsForSide(side) {
  const rows = state.datc64Tsv?.visibleRows || [];
  if (side === "reference") {
    const referenceRows = state.datc64Tsv?.referenceRows || new Map();
    return rows.map((row) => referenceRows.get(row.rowNumber) || { rowNumber: row.rowNumber, cells: [] });
  }
  return rows.map((row) => {
    const cells = [...(row.cells || [])];
    const rowEdits = state.datc64Tsv?.edits?.get(row.rowNumber);
    if (rowEdits) {
      for (const [columnIndex, value] of rowEdits.entries()) {
        cells[columnIndex] = value;
      }
    }
    return { rowNumber: row.rowNumber, cells };
  });
}

function renderDatc64TsvVirtualRowsForSide(side, scrollTop, viewportHeight) {
  const columns = state.datc64Tsv?.columns || [];
  const allRows = datc64TsvRowsForSide(side);
  const range = datc64TsvVisibleRange(scrollTop, allRows.length, viewportHeight);
  const visibleRows = allRows.slice(range.start, range.end);
  const tsvText = buildDatc64Tsv(null, visibleRows, columns);
  const referenceRows = state.datc64Tsv?.referenceRows || new Map();
  const targetRows = state.datc64Tsv?.targetRows || new Map();
  const editableIndexes = side === "target" ? state.datc64Tsv?.editableIndexes || new Set() : new Set();
  const grid = renderDatc64TsvGrid(side, tsvText, columns, editableIndexes, state.datc64Tsv?.columnWidths || [], referenceRows, targetRows);
  const paddingTop = range.start * datc64VirtualRowHeight;
  const paddingBottom = Math.max(0, allRows.length - range.end) * datc64VirtualRowHeight;
  return `
    <div class="datc64-tsv-virtual-spacer" style="paddingTop:${paddingTop}px;paddingBottom:${paddingBottom}px">
      ${grid}
    </div>
  `;
}

function persistVisibleDatc64TsvEdits() {
  if (!state.datc64Tsv?.edits) return;
  const editableIndexes = state.datc64Tsv.editableIndexes || new Set();
  $("tableEditor")?.querySelectorAll(".datc64-tsv-target-cell").forEach((cell) => {
    const rowNumber = Number(cell.dataset.rowNumber);
    const columnIndex = Number(cell.dataset.columnIndex);
    if (!editableIndexes.has(columnIndex)) return;
    const original = cell.dataset.original || "";
    const value = cell.textContent || "";
    let rowEdits = state.datc64Tsv.edits.get(rowNumber);
    if (value === original) {
      if (rowEdits) {
        rowEdits.delete(columnIndex);
        if (rowEdits.size === 0) state.datc64Tsv.edits.delete(rowNumber);
      }
      return;
    }
    if (!rowEdits) {
      rowEdits = new Map();
      state.datc64Tsv.edits.set(rowNumber, rowEdits);
    }
    rowEdits.set(columnIndex, value);
  });
}

function renderDatc64TsvVirtualRows() {
  if (!state.datc64Tsv) return;
  if (state.datc64Tsv.rendering) return;
  state.datc64Tsv.rendering = true;
  persistVisibleDatc64TsvEdits();
  const referenceHost = $("datc64TsvReference");
  const targetHost = $("datc64TsvTarget");
  const scrollTop = targetHost?.scrollTop || referenceHost?.scrollTop || 0;
  const viewportHeight = targetHost?.clientHeight || referenceHost?.clientHeight || 360;
  if (referenceHost) referenceHost.innerHTML = renderDatc64TsvVirtualRowsForSide("reference", scrollTop, viewportHeight);
  if (targetHost) targetHost.innerHTML = renderDatc64TsvVirtualRowsForSide("target", scrollTop, viewportHeight);
  if (referenceHost) referenceHost.scrollTop = scrollTop;
  if (targetHost) targetHost.scrollTop = scrollTop;
  wireDatc64TsvGrid();
  state.datc64Tsv.rendering = false;
}

function buildDatc64TsvFromGrid() {
  if (!state.datc64Tsv) return null;
  persistVisibleDatc64TsvEdits();
  const columns = state.datc64Tsv.columns || [];
  const lines = [["#", ...columns.map(datc64ColumnLabel)].map(escapeTsvCell).join("\t")];
  for (const row of state.datc64Tsv.visibleRows || state.tableEditBase?.rows || []) {
    const rowNumber = row.rowNumber;
    const cells = [];
    for (let columnIndex = 0; columnIndex < columns.length; columnIndex++) {
      const edited = state.datc64Tsv.edits?.get(rowNumber)?.get(columnIndex);
      cells.push(edited ?? row.cells?.[columnIndex] ?? "");
    }
    lines.push([rowNumber, ...cells].map(escapeTsvCell).join("\t"));
  }
  return lines.join("\n");
}

function getDatc64TsvRows(filterDiffRows = false) {
  const targetRows = state.tableEditBase?.rows || [];
  if (!filterDiffRows || !state.datc64Tsv?.diffRows?.size) return targetRows;
  return targetRows.filter((row) => state.datc64Tsv.diffRows.has(row.rowNumber));
}

function refreshDatc64TsvTextareas() {
  if (!state.datc64Tsv) return;
  if (!$("saveTableBtn")?.disabled) {
    setStatus("当前 TSV 有未保存编辑，请先保存草稿后再切换差异筛选。");
    if ($("diffRowsOnlyInput")) $("diffRowsOnlyInput").checked = false;
    return;
  }
  const rows = getDatc64TsvRows(Boolean($("diffRowsOnlyInput")?.checked));
  const columns = state.datc64Tsv.columns || [];
  state.datc64Tsv.visibleRows = rows;
  state.datc64Tsv.targetRows = new Map((rows || []).map((row) => [row.rowNumber, row]));
  state.datc64Tsv.edits = new Map();
  renderDatc64TsvVirtualRows();
}

function renderDatc64TsvComparison(referenceResult, targetResult, editableIndexes, tableSummary = "") {
  const columns = targetResult.columns || [];
  const rows = targetResult.rows || [];
  const referenceRows = new Map((referenceResult?.rows || []).map((row) => [row.rowNumber, row]));
  const diffRows = new Set();
  if (referenceResult) {
    for (const row of rows) {
      const referenceRow = referenceRows.get(row.rowNumber);
      for (let columnIndex = 0; columnIndex < columns.length; columnIndex++) {
        const referenceValue = referenceRow?.cells?.[columnIndex] ?? "";
        const targetValue = row.cells?.[columnIndex] ?? "";
        if (referenceValue !== targetValue) {
          diffRows.add(row.rowNumber);
          break;
        }
      }
    }
  }

  const targetRows = new Map(rows.map((row) => [row.rowNumber, row]));
  state.datc64Tsv = {
    columns,
    editableIndexes: new Set(editableIndexes),
    referenceRows,
    diffRows,
    visibleRows: rows,
    targetRows,
    edits: new Map()
  };

  const referenceStatus = referenceResult
    ? `${referenceResult.previewRowCount || 0} 行`
    : "未匹配";
  const editHint = editableIndexes.size > 0
    ? "表格模式 · 右侧可编辑列可直接修改"
    : "表格模式 · 当前自动结构为只读对比";
  const columnWidths = calculateDatc64TsvColumnWidths(columns, rows, referenceRows);
  state.datc64Tsv.targetRows = targetRows;
  state.datc64Tsv.columnWidths = columnWidths;
  requestAnimationFrame(renderDatc64TsvVirtualRows);

  return `
    <div class="datc64-diff-workspace datc64-tsv-workspace">
      <div class="inspection-head">
        <strong>表格对比</strong>
        <span>国服参考 ${escapeHtml(referenceStatus)} · ${escapeHtml(editHint)}</span>
        <span class="datc64-target-summary">${escapeHtml(tableSummary)}</span>
      </div>
      <div class="datc64-tsv-grid">
        <section>
          <div class="datc64-pane-title">国服参考表格</div>
          <div id="datc64TsvReference" class="datc64-tsv-scroll"></div>
        </section>
        <section>
          <div class="datc64-pane-title">国际服目标表格</div>
          <div id="datc64TsvTarget" class="datc64-tsv-scroll datc64-tsv-target"></div>
        </section>
      </div>
      <div id="datc64CellActions" class="datc64-cell-actions hidden">
        <button type="button" data-action="apply">应用参考</button>
        <button type="button" data-action="restore">恢复默认</button>
      </div>
    </div>
  `;
}

function renderDatc64CompareTable(referenceResult, targetResult, editableIndexes, tableSummary = "") {
  const columns = targetResult.columns || [];
  const referenceRows = new Map((referenceResult?.rows || []).map((row) => [row.rowNumber, row]));
  const rows = targetResult.rows || [];
  const diffKeys = new Set();
  const diffRows = new Set();
  const diffColumns = new Set();
  if (referenceResult) {
    for (const row of rows) {
      const referenceRow = referenceRows.get(row.rowNumber);
      for (let columnIndex = 0; columnIndex < columns.length; columnIndex++) {
        const referenceValue = referenceRow?.cells?.[columnIndex] ?? "";
        const targetValue = row.cells?.[columnIndex] ?? "";
        if (referenceValue !== targetValue) {
          diffKeys.add(`${row.rowNumber}:${columnIndex}`);
          diffRows.add(row.rowNumber);
          diffColumns.add(columnIndex);
        }
      }
    }
  }
  const referenceStatus = referenceResult
    ? `${referenceResult.previewRowCount || 0} 行`
    : "未匹配";
  const editHint = editableIndexes.size > 0
    ? "差异高亮 · 目标可编辑列可直接修改"
    : "差异高亮 · 当前自动结构为只读对比";
  const columnWidths = calculateDatc64ColumnWidths(columns, rows, referenceRows);
  const tablePixelWidth = 46 + columnWidths.reduce((sum, width) => sum + width, 0);
  const columnStyle = (index) => `style="--column-width:${columnWidths[index] || 160}px"`;
  const head = (side) => columns
    .map((column, index) => `<th class="${editableIndexes.has(index) ? "editable-column" : ""}" ${columnStyle(index)} data-side="${side}" data-column-index="${index}" data-has-diff="${diffColumns.has(index) ? "true" : "false"}" title="${escapeHtml(column)}">${escapeHtml(column)}</th>`)
    .join("");
  const headerTable = (side) => `
    <div class="datc64-table-head" data-sync-head="${side}">
      <table>
        <thead><tr><th class="row-number">#</th>${head(side)}</tr></thead>
      </table>
    </div>
  `;
  const buildBody = (side) => rows.map((row) => {
    const referenceCells = columns.map((_, columnIndex) => referenceRows.get(row.rowNumber)?.cells?.[columnIndex] ?? "");
    const targetCells = columns.map((_, columnIndex) => row.cells?.[columnIndex] ?? "");
    const cells = side === "reference" ? referenceCells : targetCells;
    const line = cells
      .map((cell, columnIndex) => {
        const referenceValue = referenceCells[columnIndex] || "";
        const targetValue = targetCells[columnIndex] || "";
        const changed = diffKeys.has(`${row.rowNumber}:${columnIndex}`);
        const editable = side === "target" && editableIndexes.has(columnIndex);
        const classes = [
          changed ? "diff-cell datc64-diff-cell" : "",
          editable ? "editable-cell" : "readonly-cell"
        ].filter(Boolean).join(" ");
        const common = `data-side="${side}" data-row-number="${row.rowNumber}" data-column-index="${columnIndex}" data-has-diff="${diffColumns.has(columnIndex) ? "true" : "false"}" data-diff-key="${row.rowNumber}:${columnIndex}" data-reference="${escapeHtml(referenceValue)}" data-original="${escapeHtml(targetValue)}"`;
        if (editable) {
          return `<td class="${classes} datc64-target-cell" ${columnStyle(columnIndex)} ${common} contenteditable="true" spellcheck="false">${escapeHtml(targetValue)}</td>`;
        }

        return `<td class="${classes}" ${columnStyle(columnIndex)} ${common}>${escapeHtml(cell)}</td>`;
      })
      .join("");
    return `
      <tr data-row-number="${row.rowNumber}" data-has-diff="${diffRows.has(row.rowNumber) ? "true" : "false"}">
        <th class="row-number">${row.rowNumber}</th>
        ${line}
      </tr>
    `;
  }).join("");
  const emptyBody = `<tr><td class="datc64-empty-row" colspan="${columns.length + 1}">没有可显示的行</td></tr>`;

  return `
    <div class="datc64-diff-workspace">
      <div class="inspection-head">
        <strong>左右对比</strong>
        <span>国服参考 ${escapeHtml(referenceStatus)} · ${escapeHtml(editHint)}</span>
        <span class="datc64-target-summary">${escapeHtml(tableSummary)}</span>
      </div>
      <div class="datc64-diff-grid">
        <section>
          <div class="datc64-pane-title">国服参考</div>
          ${headerTable("reference")}
          <div class="datc64-table-scroll" data-sync-pane="reference">
            <table>
              <tbody>${buildBody("reference") || emptyBody}</tbody>
            </table>
          </div>
        </section>
        <section>
          <div class="datc64-pane-title">国际服目标</div>
          ${headerTable("target")}
          <div class="datc64-table-scroll" data-sync-pane="target">
            <table>
              <tbody>${buildBody("target") || emptyBody}</tbody>
            </table>
          </div>
        </section>
      </div>
      <div class="datc64-horizontal-bar-grid">
        <div class="datc64-horizontal-scroll" data-sync-hbar="reference"><div style="width:${tablePixelWidth}px"></div></div>
        <div class="datc64-horizontal-scroll" data-sync-hbar="target"><div style="width:${tablePixelWidth}px"></div></div>
      </div>
      <div id="datc64CellActions" class="datc64-cell-actions hidden">
        <button type="button" data-action="apply">应用参考</button>
        <button type="button" data-action="restore">恢复默认</button>
      </div>
    </div>
  `;
}

function calculateDatc64ColumnWidths(columns, rows, referenceRows) {
  return columns.map((column, columnIndex) => {
    let maxLength = String(column || "").length;
    const sampleRows = rows.slice(0, 80);
    for (const row of sampleRows) {
      const targetValue = row.cells?.[columnIndex] ?? "";
      const referenceValue = referenceRows.get(row.rowNumber)?.cells?.[columnIndex] ?? "";
      maxLength = Math.max(maxLength, String(targetValue).length, String(referenceValue).length);
    }
    if (/^(id|#|index|count|type|classno|actnumber|character)$/i.test(String(column || "").replace(/\s*@\d+$/, ""))) {
      return Math.min(130, Math.max(70, 34 + maxLength * 7));
    }
    return Math.min(360, Math.max(110, 46 + maxLength * 7));
  });
}

function wireDatc64DiffTable() {
  const editor = $("tableEditor");
  updateDatc64DiffFilterControls(true);
  if ($("datc64TsvTarget")) {
    wireDatc64TsvGrid();
    return;
  }
  const panes = Array.from(editor.querySelectorAll(".datc64-table-scroll"));
  const heads = new Map(Array.from(editor.querySelectorAll(".datc64-table-head"))
    .map((head) => [head.dataset.syncHead, head]));
  const hbars = new Map(Array.from(editor.querySelectorAll(".datc64-horizontal-scroll"))
    .map((bar) => [bar.dataset.syncHbar, bar]));
  let syncing = false;
  const syncHead = (pane) => {
    const head = heads.get(pane.dataset.syncPane);
    if (head) head.scrollLeft = pane.scrollLeft;
  };
  const syncHbar = (pane) => {
    const hbar = hbars.get(pane.dataset.syncPane);
    if (!hbar) return;
    hbar.scrollLeft = pane.scrollLeft;
    const spacer = hbar.firstElementChild;
    const table = pane.querySelector("table");
    if (spacer && table) {
      spacer.style.width = `${table.scrollWidth}px`;
    }
  };
  for (const pane of panes) {
    syncHead(pane);
    syncHbar(pane);
    pane.addEventListener("scroll", () => {
      hideDatc64ActionCard();
      if (syncing) return;
      syncing = true;
      syncHead(pane);
      syncHbar(pane);
      for (const other of panes) {
        if (other === pane) continue;
        other.scrollTop = pane.scrollTop;
        other.scrollLeft = pane.scrollLeft;
        syncHead(other);
        syncHbar(other);
      }
      requestAnimationFrame(() => { syncing = false; });
    });
  }
  for (const hbar of hbars.values()) {
    hbar.addEventListener("scroll", () => {
      if (syncing) return;
      syncing = true;
      const pane = panes.find((item) => item.dataset.syncPane === hbar.dataset.syncHbar);
      if (pane) {
        pane.scrollLeft = hbar.scrollLeft;
        syncHead(pane);
      }
      for (const other of panes) {
        if (other === pane) continue;
        other.scrollLeft = hbar.scrollLeft;
        syncHead(other);
        syncHbar(other);
      }
      requestAnimationFrame(() => { syncing = false; });
    });
  }
  window.requestAnimationFrame(() => panes.forEach(syncHbar));
  applyDatc64DiffFilters();

  const actions = $("datc64CellActions");
  if (!actions) return;
  editor.querySelectorAll(".datc64-target-cell, .datc64-diff-workspace th[data-side='target'].editable-column").forEach((item) => {
    item.addEventListener("focus", (event) => showDatc64ActionCard(event.currentTarget));
    item.addEventListener("click", (event) => {
      event.stopPropagation();
      showDatc64ActionCard(event.currentTarget);
    });
    if (item.classList.contains("datc64-target-cell")) {
      item.addEventListener("input", () => {
        $("saveTableBtn").disabled = false;
      });
      item.addEventListener("keydown", (event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          item.blur();
        }
      });
      item.addEventListener("paste", (event) => {
        event.preventDefault();
        const text = event.clipboardData?.getData("text/plain") || "";
        document.execCommand("insertText", false, text);
      });
    }
  });
  actions.addEventListener("click", (event) => {
    event.stopPropagation();
    const button = event.target.closest("button[data-action]");
    if (!button) return;
    handleDatc64Action(button.dataset.action);
  });
}

function showDatc64ActionCard(anchor) {
  const actions = $("datc64CellActions");
  if (!actions || !anchor) return;
  const cell = anchor.closest("td, th, .datc64-tsv-cell, .datc64-tsv-head");
  if (!cell) return;
  const columnIndex = Number(anchor.dataset.columnIndex ?? cell.dataset.columnIndex);
  const rowNumber = Number(anchor.dataset.rowNumber ?? cell.dataset.rowNumber ?? 0);
  const isColumn = anchor.tagName === "TH" || anchor.classList.contains("datc64-tsv-head");
  actions.dataset.mode = isColumn ? "column" : "cell";
  actions.dataset.columnIndex = String(columnIndex);
  actions.dataset.rowNumber = String(rowNumber);
  actions.style.left = "0px";
  actions.style.top = "0px";
  actions.classList.remove("hidden");
  const rect = anchor.getBoundingClientRect();
  const host = $("tableEditor").getBoundingClientRect();
  const actionWidth = actions.offsetWidth || 108;
  const actionHeight = actions.offsetHeight || 58;
  const gap = 8;
  const left = isColumn
    ? host.width - actionWidth - gap
    : (rect.right + actionWidth + gap > host.right ? rect.left - host.left - actionWidth - gap : rect.right - host.left + gap);
  actions.style.left = `${Math.min(host.width - actionWidth - gap, Math.max(gap, left))}px`;
  actions.style.top = `${Math.min(host.height - actionHeight - gap, Math.max(gap, rect.top - host.top))}px`;
  editorHighlightDatc64Selection(actions.dataset.mode, rowNumber, columnIndex);
  document.removeEventListener("click", hideDatc64ActionCard);
  setTimeout(() => document.addEventListener("click", hideDatc64ActionCard, { once: true }), 0);
}

function editorHighlightDatc64Selection(mode, rowNumber, columnIndex) {
  const editor = $("tableEditor");
  editor.querySelectorAll(".datc64-selected, .datc64-tsv-selected").forEach((item) => {
    item.classList.remove("datc64-selected", "datc64-tsv-selected");
  });
  const selector = mode === "column"
    ? `td[data-column-index="${columnIndex}"], th[data-column-index="${columnIndex}"], .datc64-tsv-cell[data-column-index="${columnIndex}"], .datc64-tsv-head[data-column-index="${columnIndex}"]`
    : `td[data-row-number="${rowNumber}"][data-column-index="${columnIndex}"], th[data-row-number="${rowNumber}"][data-column-index="${columnIndex}"], .datc64-tsv-cell[data-row-number="${rowNumber}"][data-column-index="${columnIndex}"]`;
  editor.querySelectorAll(selector).forEach((item) => item.classList.add(item.classList.contains("datc64-tsv-cell") || item.classList.contains("datc64-tsv-head") ? "datc64-tsv-selected" : "datc64-selected"));
}

function handleDatc64Action(action) {
  const actions = $("datc64CellActions");
  if (!actions) return;
  const mode = actions.dataset.mode;
  const columnIndex = Number(actions.dataset.columnIndex);
  const rowNumber = Number(actions.dataset.rowNumber);
  const selectedCells = mode === "cell" ? datc64SelectedAgGridCells() : [];
  const agEditableColumn = state.datc64AgGrid?.editableIndexes?.has(columnIndex) ? columnIndex : -1;
  if (state.datc64AgGrid?.targetApi && (selectedCells.length > 0 || (mode === "column" && agEditableColumn >= 0))) {
    const selectedKeys = new Set(selectedCells.map((cell) => datc64CellKey(cell.rowNumber, cell.columnIndex)));
    let changed = 0;
    state.datc64AgGrid.targetApi.forEachNode((node) => {
      const data = node.data;
      if (!data) return;
      if (mode === "column") {
        const value = action === "apply"
          ? (data.__referenceCells?.[agEditableColumn] ?? "")
          : (data.__originalCells?.[agEditableColumn] ?? "");
        if ((data[`c${agEditableColumn}`] ?? "") !== value) {
          data[`c${agEditableColumn}`] = value;
          changed++;
        }
        return;
      }
      for (const selectedCell of selectedCells) {
        if (data.__rowNumber !== selectedCell.rowNumber) continue;
        const editableColumnIndex = selectedCell.columnIndex;
        if (!state.datc64AgGrid.editableIndexes?.has(editableColumnIndex)) continue;
        if (!selectedKeys.has(datc64CellKey(data.__rowNumber, editableColumnIndex))) continue;
        const value = action === "apply"
          ? (data.__referenceCells?.[editableColumnIndex] ?? "")
          : (data.__originalCells?.[editableColumnIndex] ?? "");
        if ((data[`c${editableColumnIndex}`] ?? "") !== value) {
          data[`c${editableColumnIndex}`] = value;
          changed++;
        }
      }
    });
    state.datc64AgGrid.targetApi.refreshCells({ force: true });
    $("saveTableBtn").disabled = false;
    setStatus(action === "apply" ? `已应用参考：${changed} 个单元格` : `已恢复默认：${changed} 个单元格`);
    hideDatc64ActionCard();
    return;
  }
  const columnIndexes = [columnIndex];
  const selector = mode === "column"
    ? columnIndexes.map((index) => `.datc64-target-cell[data-column-index="${index}"], .datc64-tsv-target-cell[data-column-index="${index}"]`).join(", ")
    : `.datc64-target-cell[data-row-number="${rowNumber}"][data-column-index="${columnIndex}"], .datc64-tsv-target-cell[data-row-number="${rowNumber}"][data-column-index="${columnIndex}"]`;
  const inputs = Array.from($("tableEditor").querySelectorAll(selector));
  if (inputs.length === 0) {
    setStatus("当前选择没有可编辑单元格");
    return;
  }

  for (const input of inputs) {
    setDatc64CellValue(input, action === "apply"
      ? (input.dataset.reference || "")
      : (input.dataset.original || ""));
  }
  $("saveTableBtn").disabled = false;
  setStatus(action === "apply" ? `已应用参考：${inputs.length} 个单元格` : `已恢复默认：${inputs.length} 个单元格`);
  hideDatc64ActionCard();
}

function getDatc64CellValue(cell) {
  return cell.tagName === "INPUT" ? cell.value : cell.textContent;
}

function setDatc64CellValue(cell, value) {
  if (cell.tagName === "INPUT") {
    cell.value = value;
    return;
  }
  cell.textContent = value;
}

function collectDatc64TsvEdits() {
  if (!$("datc64TsvTarget") || !state.tableEditBase || !state.datc64Tsv) return null;
  const columns = state.datc64Tsv.columns || [];
  const editableIndexes = state.datc64Tsv.editableIndexes || new Set();
  const targetText = buildDatc64TsvFromGrid();
  const parsed = parseDatc64Tsv(targetText || "", columns);
  const originalRows = new Map((state.tableEditBase.rows || []).map((row) => [row.rowNumber, row]));
  const edits = [];
  for (const [rowNumber, cells] of parsed.entries()) {
    const original = originalRows.get(rowNumber);
    if (!original) continue;
    for (const columnIndex of editableIndexes) {
      const nextValue = cells[columnIndex] ?? "";
      const originalValue = original.cells?.[columnIndex] ?? "";
      if (nextValue !== originalValue) {
        edits.push({ rowNumber, columnIndex, value: nextValue });
      }
    }
  }
  return edits;
}

function hideDatc64ActionCard(options = {}) {
  const actions = $("datc64CellActions");
  if (actions) {
    actions.classList.add("hidden");
  }
  $("tableEditor")?.querySelectorAll(".datc64-selected, .datc64-tsv-selected").forEach((item) => item.classList.remove("datc64-selected", "datc64-tsv-selected"));
  if (state.datc64AgGrid && !options.preserveAgSelection) {
    state.datc64AgGrid.selectedCellKeys = new Set();
    state.datc64AgGrid.selectionSide = null;
    state.datc64AgGrid.targetApi?.refreshCells({ force: true });
    state.datc64AgGrid.referenceApi?.refreshCells({ force: true });
  }
}

function parseTableSchema() {
  const text = $("tableSchemaText").value.trim();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch (error) {
    setStatus(`表结构 JSON 无法解析：${error.message}`);
    return false;
  }
}

function resetTableSchemaPicker() {
  state.tableSchemas = [];
  if (!$("tableSchemaSelect")) return;
  $("tableSchemaSelect").innerHTML = '<option value="">临时结构</option>';
  $("deleteSchemaBtn").disabled = true;
  $("inferSchemaBtn").disabled = !isTableResource(state.selectedResource);
  $("saveSchemaBtn").disabled = !isTableResource(state.selectedResource);
}

async function refreshTableSchemas() {
  if (!isTableResource(state.selectedResource)) {
    resetTableSchemaPicker();
    return;
  }

  const result = await api("/api/tables/schemas/list", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath
  });
  state.tableSchemas = result.items || [];
  $("tableSchemaSelect").innerHTML = '<option value="">临时结构</option>';
  for (const schema of state.tableSchemas) {
    const option = document.createElement("option");
    option.value = schema.id;
    option.textContent = schema.name;
    $("tableSchemaSelect").appendChild(option);
  }
  $("deleteSchemaBtn").disabled = true;
  $("inferSchemaBtn").disabled = false;
  $("saveSchemaBtn").disabled = false;
}

function selectTableSchema() {
  const schemaId = $("tableSchemaSelect").value;
  $("deleteSchemaBtn").disabled = !schemaId;
  if (!schemaId) return;
  const entry = state.tableSchemas.find((item) => item.id === schemaId);
  if (!entry) return;
  $("tableSchemaText").value = JSON.stringify(entry.schema, null, 2);
}

async function saveTableSchema() {
  if (!isTableResource(state.selectedResource)) return;
  const schema = parseTableSchema();
  if (schema === false || !schema) {
    setStatus("先填写表结构 JSON");
    return;
  }

  const selected = state.tableSchemas.find((item) => item.id === $("tableSchemaSelect").value);
  const fallbackName = selected?.name || state.selectedResource.virtualPath.split(/[\\/]/).pop() || "表结构";
  const name = window.prompt("结构名称", fallbackName);
  if (!name) return;

  setStatus("正在保存表结构...");
  const entry = await api("/api/tables/schemas/save", {
    id: selected?.id || null,
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    name,
    schema
  });
  await refreshTableSchemas();
  $("tableSchemaSelect").value = entry.id;
  $("deleteSchemaBtn").disabled = false;
  setStatus(`表结构已保存：${entry.name}`);
}

async function inferTableSchema() {
  if (!isTableResource(state.selectedResource)) return;
  setStatus("正在自动识别表结构...");
  const result = await api("/api/tables/schemas/infer", {
    profileId: state.selectedResource.profileId,
    virtualPath: state.selectedResource.virtualPath,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  if (!result.inferred || !result.schema) {
    writeLog($("actionOutput"), result);
    setStatus("未识别到可用结构");
    return;
  }

  $("tableSchemaSelect").value = "";
  $("deleteSchemaBtn").disabled = true;
  $("tableSchemaText").value = JSON.stringify(result.schema, null, 2);
  writeLog($("actionOutput"), result);
  setStatus(`已生成结构：${result.formatPath}`);
}

async function deleteTableSchema() {
  const schemaId = $("tableSchemaSelect").value;
  const profileId = selectedProfileId();
  if (!profileId || !schemaId) return;

  setStatus("正在删除表结构...");
  const result = await api("/api/tables/schemas/delete", {
    profileId,
    schemaId
  });
  await refreshTableSchemas();
  setStatus(result.removed ? "表结构已删除" : "表结构不存在");
}

function renderBinaryTableInspection(result) {
  const editor = $("tableEditor");
  $("compareWorkspace").classList.add("hidden");
  const hasTextCandidates = (result.strings || []).length > 0;
  const headerFields = Object.entries(result.headerFields || {})
    .slice(0, 8)
    .map(([key, value]) => `<span>${escapeHtml(key)}=${escapeHtml(value)}</span>`)
    .join("");
  const stringItems = result.strings || [];
  const strings = stringItems
    .slice(0, 20)
    .map((item) => `<li><span class="encoding-badge">${escapeHtml(item.encoding || "ascii")}</span> @${item.offset} (${item.length}) ${escapeHtml(item.value)}</li>`)
    .join("");
  const hints = (result.layoutHints || [])
    .map((item) => `<li>${escapeHtml(item)}</li>`)
    .join("");
  const warnings = (result.warnings || [])
    .map((item) => `<div class="inspection-warning">${escapeHtml(item)}</div>`)
    .join("");
  const stringEmpty = stringItems.length
    ? ""
    : '<li class="binary-empty">没有可信的可读文本候选</li>';
  const rows = result.rows || [];
  const candidateTable = rows.length
    ? renderStringCandidateTable(
        result.columns || (hasTextCandidates ? ["#", "offset", "bytes", "encoding", "text"] : ["word", "offset", "u32", "i32", "hex"]),
        rows,
        hasTextCandidates ? "可信文本表" : "二进制字段概览",
        hasTextCandidates ? "用于翻译定位，需结构定义后才能保存字段编辑" : "按 4-byte word 展示，需结构定义后才能解释字段")
    : "";
  editor.innerHTML = `
    <div class="binary-inspector">
      <div class="inspection-head">
        <strong>${escapeHtml(result.format)}</strong>
        <span>二进制数据表 · 未识别结构</span>
      </div>
      ${warnings}
      <div class="inspection-props">${headerFields || "<span>没有可显示的头字段</span>"}</div>
      ${candidateTable}
      <div class="binary-columns">
        <div><strong>布局提示</strong><ul>${hints || '<li class="binary-empty">暂无布局线索</li>'}</ul></div>
        <details class="candidate-details">
          <summary>可信文本候选</summary>
          <ul>${strings || stringEmpty}</ul>
        </details>
      </div>
      <details>
        <summary>原始十六进制</summary>
        <pre>${escapeHtml(result.hexPreview || "")}</pre>
      </details>
    </div>
  `;
  editor.classList.remove("hidden");
  $("previewText").classList.add("hidden");
}

function renderStringCandidateTable(columns, rows, title, subtitle) {
  const head = columns
    .map((column) => `<th>${escapeHtml(column)}</th>`)
    .join("");
  const body = rows
    .map((row) => `<tr>${row.cells.map((cell) => `<td>${escapeHtml(cell)}</td>`).join("")}</tr>`)
    .join("");
  return `
    <div class="string-candidate-table">
      <div class="inspection-head">
        <strong>${escapeHtml(title)}</strong>
        <span>只读 · ${escapeHtml(subtitle)}</span>
      </div>
      <table>
        <thead><tr>${head}</tr></thead>
        <tbody>${body}</tbody>
      </table>
    </div>
  `;
}

async function saveTableEdits() {
  if (!state.selectedResource || !state.tableEditBase) return;
  const edits = collectDatc64AgGridEdits() || collectDatc64TsvEdits();
  const cellEdits = edits || Array.from($("tableEditor").querySelectorAll(
    isDatc64ComparisonInspection(state.tableEditBase) || isLegacyDatComparisonInspection(state.tableEditBase)
      ? ".datc64-target-cell"
      : "input"))
    .filter(input => getDatc64CellValue(input) !== input.dataset.original)
    .map(input => ({
      rowNumber: Number(input.dataset.rowNumber),
      columnIndex: Number(input.dataset.columnIndex),
      value: getDatc64CellValue(input)
    }));
  if (cellEdits.length === 0) {
    setStatus("表格没有改动");
    return;
  }

  setStatus("正在保存表格覆盖...");
  const schemaId = $("tableSchemaSelect").value || null;
  const schema = schemaId ? null : parseTableSchema();
  if (schema === false) return;
  $("saveTableBtn").disabled = true;
  try {
    const result = await api("/api/tables/save", {
      profileId: state.selectedResource.profileId,
      virtualPath: state.selectedResource.virtualPath,
      edits: cellEdits,
      oodlePath: $("oodlePathInput").value.trim() || null,
      schema,
      schemaId
    });
    writeLog($("actionOutput"), result);
    setStatus(`表格已保存：${result.editedCells} 处`);
    await refreshOverlayList();
    await inspectTable();
  } catch (error) {
    setStatus(`保存表格失败：${error.message}`);
    writeLog($("actionOutput"), { error: error.message });
  } finally {
    $("saveTableBtn").disabled = false;
  }
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

function downloadText(fileName, text, contentType = "text/plain;charset=utf-8", options = {}) {
  const content = options.utf8Bom ? `\uFEFF${text || ""}` : text;
  const blob = new Blob([content], { type: contentType });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = fileName;
  link.click();
  setTimeout(() => URL.revokeObjectURL(link.href), 1000);
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
    writerKind: 1,
    oodlePath: $("oodlePathInput").value.trim() || null
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
  writeLog($("actionOutput"), {
    bundlePath: result.bundlePath,
    containerBundlePath: result.containerBundlePath,
    manifestPath: result.manifestPath,
    indexPlanPath: result.indexPlanPath,
    nativeIndexDryPath: result.nativeIndexDryPath,
    nativeIndexRewriteDryPath: result.nativeIndexRewriteDryPath,
    size: result.size,
    totalItems: result.plan?.totalItems,
    indexItems: result.indexPlan?.totalItems,
    warnings: result.warnings
  });
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
    text: activeTextEditor().value,
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

async function applyGlossary() {
  const profileId = selectedProfileId();
  const csv = $("translationCsvText").value;
  const glossary = $("glossaryText").value;
  if (!profileId || !csv.trim()) {
    setStatus("应用术语需要先导出或粘贴翻译 CSV");
    return;
  }
  if (!glossary.trim()) {
    setStatus("请先填写术语表");
    return;
  }

  setStatus("正在应用术语...");
  const result = await api("/api/translation/apply-glossary", {
    profileId,
    csv,
    glossary
  });
  $("translationCsvText").value = result.csv;
  writeLog($("actionOutput"), {
    entries: result.entries,
    terms: result.terms,
    changed: result.changed,
    warnings: result.warnings
  });
  setStatus(`术语已应用：${result.changed}/${result.entries}`);
}

async function runBatchScript(apply) {
  const profileId = selectedProfileId();
  if (!profileId) return;
  const operations = parseBatchOperations();
  if (!operations) return;

  const layerText = state.previewUseOverlay ? "草稿层" : "原始层";
  setStatus(apply ? `正在基于${layerText}应用脚本...` : `正在基于${layerText}预检脚本...`);
  const result = await api("/api/batch/run-script", {
    profileId,
    operations,
    apply,
    useOverlay: state.previewUseOverlay
  });
  writeLog($("actionOutput"), result);
  setStatus(apply ? `${layerText}脚本已应用：${result.changed}` : `${layerText}脚本预检完成：${result.changed}`);
  if (apply) refreshOverlayList();
}

function parseBatchOperations() {
  let operations;
  try {
    operations = JSON.parse($("batchScriptText").value || "[]");
  } catch (error) {
    setStatus("脚本 JSON 格式不正确");
    return null;
  }

  if (!Array.isArray(operations) || operations.length === 0) {
    setStatus("脚本至少需要一条规则");
    return null;
  }

  return operations;
}

async function refreshBatchTemplates() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  const result = await api("/api/batch/templates/list", { profileId });
  state.batchTemplates = result.items || [];
  $("batchTemplateSelect").innerHTML = '<option value="">临时脚本</option>';
  for (const item of state.batchTemplates) {
    const option = document.createElement("option");
    option.value = item.id;
    option.textContent = item.name;
    $("batchTemplateSelect").appendChild(option);
  }
  updateBatchTemplateButtons();
}

function selectBatchTemplate() {
  const id = $("batchTemplateSelect").value;
  const item = state.batchTemplates.find((template) => template.id === id);
  if (item) {
    $("batchScriptText").value = JSON.stringify(item.operations, null, 2);
  }
  updateBatchTemplateButtons();
}

function updateBatchTemplateButtons() {
  const hasProfile = Boolean(selectedProfileId());
  const hasTemplate = Boolean($("batchTemplateSelect").value);
  $("saveBatchTemplateBtn").disabled = !hasProfile;
  $("deleteBatchTemplateBtn").disabled = !hasTemplate;
  $("previewTemplateBtn").disabled = !hasTemplate;
  $("applyTemplateBtn").disabled = !hasTemplate;
}

async function saveBatchTemplate() {
  const profileId = selectedProfileId();
  if (!profileId) return;
  const operations = parseBatchOperations();
  if (!operations) return;
  const selected = state.batchTemplates.find((item) => item.id === $("batchTemplateSelect").value);
  const name = window.prompt("模板名称", selected?.name || "批处理模板");
  if (!name) return;

  setStatus("正在保存批处理模板...");
  const item = await api("/api/batch/templates/save", {
    id: selected?.id || null,
    profileId,
    name,
    operations
  });
  await refreshBatchTemplates();
  $("batchTemplateSelect").value = item.id;
  updateBatchTemplateButtons();
  setStatus(`批处理模板已保存：${item.name}`);
}

async function deleteBatchTemplate() {
  const profileId = selectedProfileId();
  const templateId = $("batchTemplateSelect").value;
  if (!profileId || !templateId) return;
  setStatus("正在删除批处理模板...");
  const result = await api("/api/batch/templates/delete", { profileId, templateId });
  await refreshBatchTemplates();
  setStatus(result.removed ? "批处理模板已删除" : "模板不存在");
}

async function runBatchTemplate(apply) {
  const profileId = selectedProfileId();
  const templateId = $("batchTemplateSelect").value;
  if (!profileId || !templateId) return;
  const layerText = state.previewUseOverlay ? "草稿层" : "原始层";
  setStatus(apply ? `正在基于${layerText}应用模板...` : `正在基于${layerText}预检模板...`);
  const result = await api("/api/batch/run-template", {
    profileId,
    templateId,
    apply,
    useOverlay: state.previewUseOverlay
  });
  writeLog($("actionOutput"), result);
  setStatus(apply ? `${layerText}模板已应用：${result.changed}` : `${layerText}模板预检完成：${result.changed}`);
  if (apply) refreshOverlayList();
}

async function patchBuild() {
  const profileId = workspaceProfileId();
  if (!profileId) return;
  setStatus("正在启动补丁构建...");
  const job = await api("/api/jobs/patch/build", {
    profileId,
    template: 3,
    bundleName: "Tiny.V0.1.bundle.bin",
    writerKind: 1,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  trackJob(job.id);
}

async function refreshBuildHistory() {
  const profileId = workspaceProfileId();
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
      <div class="build-meta">${item.buildId} · ${Math.max(1, Math.round(item.zipSize / 1024))} KB${item.importManifestPath ? " · 外部导入" : ""}</div>
      <div class="build-actions">
        ${item.importManifestPath ? `<button type="button" data-action="manifest">清单</button>` : ""}
        ${item.importManifestPath ? `<button type="button" data-action="draft">转草稿</button>` : ""}
        <button type="button" data-action="verify">验证</button>
        <button type="button" data-action="install-preview">预检安装</button>
        <button type="button" data-action="sandbox-prepare">准备沙盒</button>
        <button type="button" data-action="sandbox">沙盒</button>
        <button type="button" data-action="install">安装</button>
        <button type="button" data-action="uninstall-preview">预检卸载</button>
        <button type="button" data-action="uninstall">卸载</button>
      </div>
    `;
    for (const button of row.querySelectorAll("button")) {
      button.addEventListener("click", () => {
        if (button.dataset.action === "manifest") {
          showImportManifest(item.buildId);
          return;
        }
        if (button.dataset.action === "draft") {
          importPatchOverlayDraft(item.buildId);
          return;
        }
        if (button.dataset.action === "verify") {
          runPatchVerifyAction(item.buildId);
          return;
        }
        if (button.dataset.action === "sandbox") {
          runPatchSandboxValidate(item.buildId);
          return;
        }
        if (button.dataset.action === "sandbox-prepare") {
          runPatchSandboxPrepare(item.buildId);
          return;
        }
        runPatchInstallAction(item.buildId, button.dataset.action);
      });
    }
    list.appendChild(row);
  }
  if (result.items.length === 0) {
    list.innerHTML = '<div class="build-item"><div class="build-meta">暂无补丁输出</div></div>';
  }
}

async function showImportManifest(buildId) {
  const profileId = selectedProfileId();
  if (!profileId || !buildId) return;
  setStatus("正在读取导入清单...");
  const result = await api("/api/patch/import-manifest", {
    profileId,
    buildId
  });
  writeLog($("actionOutput"), result);
  setStatus(`导入清单：${result.analysis?.entryCount || 0} 个文件`);
}

async function importPatchOverlayDraft(buildId) {
  const profileId = selectedProfileId();
  if (!profileId || !buildId) return;
  setStatus("正在转成 overlay 草稿...");
  const job = await api("/api/jobs/patch/import-overlay-draft", {
    profileId,
    buildId,
    bundleName: "Tiny.V0.1.bundle.bin",
    oodlePath: $("oodlePathInput").value.trim() || null,
    take: 500
  });
  trackJob(job.id);
}

async function runPatchVerifyAction(buildId) {
  const profileId = selectedProfileId();
  if (!profileId || !buildId) return;
  setStatus("正在验证补丁...");
  const result = await api("/api/patch/verify", {
    profileId,
    buildId,
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  writeLog($("actionOutput"), result);
  setStatus(result.ok ? `验证通过：${result.patchedFileRecords}` : `验证警告：${result.warnings.length}`);
}

async function analyzeExternalPatchZip() {
  const zipPath = $("externalPatchZipInput").value.trim();
  if (!zipPath) {
    setStatus("请先填写外部补丁 zip 路径");
    return;
  }

  setStatus("正在分析外部补丁...");
  const job = await api("/api/jobs/patch/analyze-zip", {
    zipPath,
    bundleName: "Tiny.V0.1.bundle.bin",
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  trackJob(job.id);
}

async function importExternalPatchZip() {
  const profileId = selectedProfileId();
  const zipPath = $("externalPatchZipInput").value.trim();
  if (!profileId) {
    setStatus("请先选择客户端配置");
    return;
  }
  if (!zipPath) {
    setStatus("请先填写外部补丁 zip 路径");
    return;
  }

  setStatus("正在导入外部补丁...");
  const job = await api("/api/jobs/patch/import-zip", {
    profileId,
    zipPath,
    bundleName: "Tiny.V0.1.bundle.bin",
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  trackJob(job.id);
}

async function previewExternalPatchInstall() {
  const profileId = selectedProfileId();
  const zipPath = $("externalPatchZipInput").value.trim();
  if (!profileId) {
    setStatus("请先选择客户端配置");
    return;
  }
  if (!zipPath) {
    setStatus("请先填写外部补丁 zip 路径");
    return;
  }

  setStatus("正在预检补丁影响...");
  const job = await api("/api/jobs/patch/preview-zip-install", {
    profileId,
    zipPath,
    bundleName: "Tiny.V0.1.bundle.bin",
    oodlePath: $("oodlePathInput").value.trim() || null
  });
  trackJob(job.id);
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

async function runPatchSandboxValidate(buildId) {
  const profileId = selectedProfileId();
  const sandboxRootPath = $("sandboxPathInput").value.trim();
  if (!profileId || !buildId) return;
  if (!sandboxRootPath) {
    setStatus("请先填写沙盒目录");
    return;
  }

  setStatus("正在沙盒验证...");
  const result = await api("/api/patch/sandbox-validate", {
    profileId,
    buildId,
    sandboxRootPath
  });
  writeLog($("actionOutput"), result);
  setStatus(result.ok ? `沙盒验证通过：${result.checkedFiles}` : `沙盒验证警告：${result.missingFiles + result.sizeMismatches}`);
}

async function runPatchSandboxPrepare(buildId) {
  const profileId = selectedProfileId();
  const sandboxRootPath = $("sandboxPathInput").value.trim();
  if (!profileId || !buildId) return;
  if (!sandboxRootPath) {
    setStatus("请先填写沙盒目录");
    return;
  }

  setStatus("正在准备沙盒...");
  const job = await api("/api/jobs/patch/sandbox-prepare", {
    profileId,
    buildId,
    sandboxRootPath,
    overwrite: true
  });
  trackJob(job.id);
}

async function refreshOverlayList() {
  const profileId = workspaceProfileId();
  if (!profileId) return;
  const result = await api("/api/overlay/list", { profileId });
  state.overlayTotal = result.total ?? result.items.length;
  state.overlayDraftPaths = new Set((result.items || []).map((item) => normalizeVirtualPath(item.virtualPath)));
  if (state.lastResourceItems.length > 0) {
    renderResources(state.lastResourceItems, state.resourceSearchTotal);
  }
  if ($("overlayPathHint")) {
    $("overlayPathHint").textContent = `草稿目录：${result.overlayFilesRoot || "未加载"}`;
    $("overlayPathHint").title = result.overlayFilesRoot || "";
  }
  if ($("draftPanelSummary")) {
    $("draftPanelSummary").textContent = `共 ${state.overlayTotal} 个`;
  }
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
  updateWorkbench();
  refreshOverlayAudit();
}

function switchSideTab(name) {
  for (const button of document.querySelectorAll(".side-tabs button")) {
    button.classList.toggle("selected", button.dataset.sideTab === name);
  }
  for (const panel of document.querySelectorAll(".side-tab-panel")) {
    panel.classList.toggle("active", panel.dataset.sidePanel === name);
  }
}

function updateDatc64DiffFilterControls(visible) {
  $("diffRowsOnlyControl")?.classList.toggle("hidden", !visible);
  $("diffColumnsOnlyControl")?.classList.toggle("hidden", !visible);
}

function applyDatc64DiffFilters() {
  const editor = $("tableEditor");
  const rowsOnly = Boolean($("diffRowsOnlyInput")?.checked);
  const columnsOnly = Boolean($("diffColumnsOnlyInput")?.checked);
  if (applyDatc64AgGridDiffFilters()) {
    hideDatc64ActionCard();
    return;
  }
  if ($("datc64TsvTarget")) {
    refreshDatc64TsvTextareas();
    return;
  }
  editor.classList.toggle("datc64-filter-diff-rows", rowsOnly);
  editor.classList.toggle("datc64-filter-diff-columns", columnsOnly);
  hideDatc64ActionCard();
  window.requestAnimationFrame(() => {
    editor.querySelectorAll(".datc64-table-scroll").forEach((pane) => {
      const hbar = editor.querySelector(`.datc64-horizontal-scroll[data-sync-hbar="${pane.dataset.syncPane}"]`);
      const spacer = hbar?.firstElementChild;
      const table = pane.querySelector("table");
      if (spacer && table) {
        spacer.style.width = `${table.scrollWidth}px`;
      }
    });
  });
}

function wireDatc64TsvGrid() {
  const editor = $("tableEditor");
  const panes = Array.from(editor.querySelectorAll(".datc64-tsv-scroll"));
  let syncing = false;
  for (const pane of panes) {
    pane.addEventListener("scroll", () => {
      if (syncing) return;
      syncing = true;
      for (const other of panes) {
        if (other === pane) continue;
        other.scrollTop = pane.scrollTop;
        other.scrollLeft = pane.scrollLeft;
      }
      requestAnimationFrame(() => {
        syncing = false;
        renderDatc64TsvVirtualRows();
      });
    });
  }
  const actions = $("datc64CellActions");
  editor.querySelectorAll(".datc64-tsv-target-cell, .datc64-tsv-head[data-side='target'].editable-column").forEach((cell) => {
    cell.addEventListener("focus", (event) => showDatc64ActionCard(event.currentTarget));
    cell.addEventListener("click", (event) => {
      event.stopPropagation();
      showDatc64ActionCard(event.currentTarget);
    });
    if (!cell.classList.contains("datc64-tsv-target-cell")) return;
    cell.addEventListener("input", () => {
      $("saveTableBtn").disabled = false;
    });
    cell.addEventListener("keydown", (event) => {
      if (event.key === "Enter") {
        event.preventDefault();
        cell.blur();
      }
    });
    cell.addEventListener("paste", (event) => {
      event.preventDefault();
      const text = event.clipboardData?.getData("text/plain") || "";
      document.execCommand("insertText", false, text);
    });
  });
  if (actions) {
    actions.addEventListener("click", (event) => {
      event.stopPropagation();
      const button = event.target.closest("button[data-action]");
      if (!button) return;
      handleDatc64Action(button.dataset.action);
    });
  }
}

async function syncExternalOverlay() {
  const profileId = workspaceProfileId();
  if (!profileId) return;
  const confirmed = window.confirm(
    "将按草稿目录 overlay/files 当前实际存在的文件重建草稿清单。\n\n" +
    "你已经手动删除的文件会从草稿列表移除；当前目录里保留的文件会重新登记。\n" +
    "这个操作不会删除 overlay/files 里的任何现有文件。是否继续？"
  );
  if (!confirmed) return;
  setStatus("正在重建草稿清单...");
  const result = await api("/api/overlay/sync-external", { profileId });
  writeLog($("actionOutput"), {
    mode: result.mode,
    overlayFilesRoot: result.overlayFilesRoot,
    manifestPath: result.manifestPath,
    discovered: result.discovered,
    imported: result.imported,
    skipped: result.skipped,
    warnings: result.warnings
  });
  setStatus(`草稿清单已重建：登记 ${result.imported} / 扫描 ${result.discovered}，跳过 ${result.skipped}`);
  await refreshOverlayList();
}

async function reviewOverlayChanges(options = {}) {
  const profileId = workspaceProfileId();
  if (!profileId) return;
  setStatus("正在审查修改...");
  const request = {
    profileId,
    take: 200,
    previewChars: 220
  };
  if (options.riskLevel !== undefined) request.riskLevel = options.riskLevel;
  if (options.kind !== undefined) request.kind = options.kind;
  const result = await api("/api/overlay/review", request);
  writeLog($("actionOutput"), {
    total: result.total,
    reviewed: result.reviewed,
    riskCounts: result.riskCounts,
    kindCounts: result.kindCounts,
    warnings: result.warnings,
    items: result.items.map((item) => ({
      virtualPath: item.virtualPath,
      kind: item.kind,
      riskLevel: item.riskLevel,
      changedLines: item.changedLines,
      textDiff: item.textDiff,
      overlaySize: item.overlaySize,
      baseSize: item.baseSize,
      warnings: item.warnings
    }))
  });
  setStatus(`审查完成：${result.reviewed}/${result.total}`);
}

async function revertHighRiskOverlays() {
  const profileId = workspaceProfileId();
  if (!profileId) return;
  setStatus("正在回滚高危修改...");
  const result = await api("/api/overlay/bulk-revert", {
    profileId,
    riskLevel: 2,
    take: 500
  });
  writeLog($("actionOutput"), result);
  setStatus(`高危已回滚：${result.removed}/${result.matched}`);
  refreshOverlayList();
}

async function refreshOverlayAudit() {
  const profileId = workspaceProfileId();
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
  const profileId = state.selectedResource?.profileId || selectedProfileId();
  if (!profileId) return;
  const result = await api("/api/overlay/revert", { profileId, virtualPath });
  writeLog($("actionOutput"), result);
  setStatus(result.removed ? "修改已回滚" : "没有可回滚的修改");
  refreshOverlayList();
}

async function restoreSelectedResourceDefault() {
  if (!state.selectedResource) return;
  if (!isSelectedTargetResource()) {
    setStatus("恢复默认只对目标客户端草稿生效，请先打开目标资源。");
    return;
  }
  const confirmed = window.confirm("恢复默认会删除当前资源的草稿修改，不会影响游戏客户端。是否继续？");
  if (!confirmed) return;
  await revertOverlay(state.selectedResource.virtualPath);
  const selectedButton = Array.from(document.querySelectorAll(".resource-item.selected"))[0] || null;
  await previewResource(state.selectedResource, selectedButton, false);
  state.previewUseOverlay = true;
  updatePreviewLayerTabs();
}

function openClientConfig() {
  const dialog = $("clientConfigDialog");
  if (dialog?.showModal) {
    dialog.showModal();
  }
  updateWorkflowStatus("config");
}

function closeClientConfig() {
  const dialog = $("clientConfigDialog");
  if (dialog?.open) {
    dialog.close();
  }
}

function bind() {
  for (const button of document.querySelectorAll(".side-tabs button")) {
    button.addEventListener("click", () => switchSideTab(button.dataset.sideTab || "status"));
  }
  $("openConfigBtn").addEventListener("click", openClientConfig);
  $("themeToggleBtn").addEventListener("click", toggleTheme);
  $("closeConfigBtn").addEventListener("click", closeClientConfig);
  $("saveWorkspaceBtn").addEventListener("click", saveWorkspaceSettings);
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
  $("deleteProfileBtn").addEventListener("click", deleteSelectedProfile);
  $("buildNativeIndexBtn").addEventListener("click", startNativeIndexJob);
  $("searchBtn").addEventListener("click", searchResources);
  $("extensionFilter").addEventListener("change", () => {
    state.translationResourceMode = false;
    updateSearchPresetState();
    searchResources();
  });
  for (const button of document.querySelectorAll(".search-presets button")) {
    button.addEventListener("click", () => {
      if (button.dataset.translation) {
        showTranslationFiles();
        return;
      }
      setSearchPreset(button.dataset.query || "", button.dataset.extension || "");
    });
  }
  $("flowConfigBtn").addEventListener("click", openClientConfig);
  $("flowIndexBtn").addEventListener("click", () => {
    updateWorkflowStatus("index");
    if (!$("clientConfigDialog")?.open) openClientConfig();
  });
  $("flowSearchUiBtn").addEventListener("click", () => setSearchPreset("metadata/ui", ".ui"));
  $("flowSearchTableBtn").addEventListener("click", () => setSearchPreset("", ".datc64"));
  $("flowMatchBtn").addEventListener("click", loadReferenceForSelectedResource);
  $("flowDraftBtn").addEventListener("click", migrationDraft);
  $("flowPatchBtn").addEventListener("click", runPatchPipeline);
  $("bulkExportBtn").addEventListener("click", bulkExportResources);
  $("bulkSignatureBtn").addEventListener("click", bulkSignatureResources);
  $("formatScanBtn").addEventListener("click", formatScan);
  $("matchResourcesBtn").addEventListener("click", matchResources);
  $("migrationPlanBtn").addEventListener("click", migrationPlan);
  $("migrationDraftBtn").addEventListener("click", migrationDraft);
  $("quickMatchBtn").addEventListener("click", loadReferenceForSelectedResource);
  $("manualReferenceBtn").addEventListener("click", openManualReferenceDialog);
  $("manualReferenceSearchBtn").addEventListener("click", searchManualReferenceResources);
  $("manualReferenceSearchInput").addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      searchManualReferenceResources();
    }
  });
  $("clearManualReferenceBtn").addEventListener("click", () => {
    clearManualReferenceSelection();
    if ($("manualReferenceDialog")?.open) $("manualReferenceDialog").close();
    setStatus("已清除手动参考选择");
  });
  $("closeManualReferenceBtn").addEventListener("click", () => $("manualReferenceDialog").close());
  $("quickApplyReferenceBtn").addEventListener("click", applyReferenceText);
  $("quickRestoreDefaultBtn").addEventListener("click", restoreSelectedResourceDefault);
  $("csdAdoptTraditionalBtn").addEventListener("click", adoptCsdSimplifiedChineseSlot);
  $("csdTagStatus").addEventListener("click", jumpToNextCsdTagIssue);
  $("diffRowsOnlyInput").addEventListener("change", applyDatc64DiffFilters);
  $("diffColumnsOnlyInput").addEventListener("change", applyDatc64DiffFilters);
  $("workbenchScanBtn").addEventListener("click", searchResources);
  $("workbenchPlanBtn").addEventListener("click", migrationPlan);
  $("workbenchValidateBtn").addEventListener("click", validateMigrationPlan);
  $("runWorkbenchPipelineBtn").addEventListener("click", runPatchPipeline);
  $("openWorkbenchOutputBtn").addEventListener("click", openWorkbenchOutput);
  $("migrationPlanSelect").addEventListener("change", selectMigrationPlan);
  $("saveMigrationPlanBtn").addEventListener("click", saveMigrationPlan);
  $("loadMigrationPlanBtn").addEventListener("click", loadMigrationPlan);
  $("validateMigrationPlanBtn").addEventListener("click", validateMigrationPlan);
  $("applyMigrationPlanBtn").addEventListener("click", applyMigrationPlan);
  $("applyCandidatesPlanBtn").addEventListener("click", applyCandidatesMigrationPlan);
  $("runPatchPipelineBtn").addEventListener("click", runPatchPipeline);
  $("deleteMigrationPlanBtn").addEventListener("click", deleteMigrationPlan);
  for (const button of document.querySelectorAll(".migration-tabs button")) {
    button.addEventListener("click", () => setMigrationFilter(button.dataset.filter || "all"));
  }
  $("bulkImportBtn").addEventListener("click", bulkImportOverlay);
  $("searchInput").addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      state.translationResourceMode = false;
      updateSearchPresetState();
      searchResources();
    }
  });
  $("profileSelect").addEventListener("change", () => {
    state.selectedProfile = state.profiles.find((item) => item.id === $("profileSelect").value) || null;
    state.selectedResource = null;
    state.previewUseOverlay = true;
    updatePreviewLayerTabs();
    resetTableSchemaPicker();
    searchResources();
    refreshBuildHistory();
    refreshOverlayList();
    refreshBatchTemplates();
    refreshMigrationPlans();
    updateWorkbench();
  });
  $("targetProfileSelect").addEventListener("change", () => {
    state.selectedResource = null;
    clearManualReferenceSelection();
    $("referencePreviewText").value = "";
    $("referenceStatus").textContent = "未加载";
    $("targetPreviewText").value = "";
    refreshMigrationPlans();
    updateWorkbench();
    setStatus("目标配置已切换，请从左侧重新打开目标资源。");
  });
  $("saveOverlayBtn").addEventListener("click", saveOverlay);
  $("previewOverlayBtn").addEventListener("click", () => switchPreviewLayer(true));
  $("previewBaseBtn").addEventListener("click", () => switchPreviewLayer(false));
  $("saveTableBtn").addEventListener("click", saveTableEdits);
  $("exportResourceBtn").addEventListener("click", exportResource);
  $("signatureBtn").addEventListener("click", extractSignature);
  $("replaceResourceBtn").addEventListener("click", chooseReplacementFile);
  $("inspectTableBtn").addEventListener("click", inspectTable);
  $("exportTableCsvBtn").addEventListener("click", exportTableCsv);
  $("importTableCsvBtn").addEventListener("click", chooseTableCsvFile);
  $("importTableCsvInput").addEventListener("change", (event) => importTableCsv(event.target.files?.[0]));
  $("scanTableRefsBtn").addEventListener("click", scanTableReferences);
  $("inspectStructuredBtn").addEventListener("click", inspectStructuredResource);
  $("saveStructuredBtn").addEventListener("click", saveStructuredResource);
  $("tableSchemaSelect").addEventListener("change", selectTableSchema);
  $("inferSchemaBtn").addEventListener("click", inferTableSchema);
  $("saveSchemaBtn").addEventListener("click", saveTableSchema);
  $("deleteSchemaBtn").addEventListener("click", deleteTableSchema);
  $("replaceResourceInput").addEventListener("change", (event) => replaceResourceWithFile(event.target.files[0]));
  $("batchOverlayBtn").addEventListener("click", batchOverlay);
  $("batchReplaceBtn").addEventListener("click", batchReplaceText);
  $("exportTranslationBtn").addEventListener("click", exportTranslationCsv);
  $("applyGlossaryBtn").addEventListener("click", applyGlossary);
  $("importTranslationBtn").addEventListener("click", importTranslationCsv);
  $("previewScriptBtn").addEventListener("click", () => runBatchScript(false));
  $("applyScriptBtn").addEventListener("click", () => runBatchScript(true));
  $("batchTemplateSelect").addEventListener("change", selectBatchTemplate);
  $("saveBatchTemplateBtn").addEventListener("click", saveBatchTemplate);
  $("deleteBatchTemplateBtn").addEventListener("click", deleteBatchTemplate);
  $("previewTemplateBtn").addEventListener("click", () => runBatchTemplate(false));
  $("applyTemplateBtn").addEventListener("click", () => runBatchTemplate(true));
  $("patchDryRunBtn").addEventListener("click", patchDryRun);
  $("patchReadinessBtn").addEventListener("click", patchReadiness);
  $("nativePlanBtn").addEventListener("click", nativePatchPlan);
  $("nativeDryBundleBtn").addEventListener("click", nativeDryBundle);
  $("nativeIndexPlanBtn").addEventListener("click", nativeIndexPlan);
  $("patchBuildBtn").addEventListener("click", patchBuild);
  $("refreshBuildsBtn").addEventListener("click", refreshBuildHistory);
  $("analyzePatchZipBtn").addEventListener("click", analyzeExternalPatchZip);
  $("previewPatchZipBtn").addEventListener("click", previewExternalPatchInstall);
  $("importPatchZipBtn").addEventListener("click", importExternalPatchZip);
  $("reviewOverlayBtn").addEventListener("click", () => reviewOverlayChanges());
  $("reviewHighRiskBtn").addEventListener("click", () => reviewOverlayChanges({ riskLevel: 2 }));
  $("reviewTextBtn").addEventListener("click", () => reviewOverlayChanges({ kind: 1 }));
  $("reviewUiBtn").addEventListener("click", () => reviewOverlayChanges({ kind: 6 }));
  $("revertHighRiskBtn").addEventListener("click", revertHighRiskOverlays);
  $("syncExternalOverlayBtn").addEventListener("click", syncExternalOverlay);
  $("syncExternalOverlayQuickBtn").addEventListener("click", syncExternalOverlay);
  $("refreshOverlayBtn").addEventListener("click", refreshOverlayList);

  // Chat bindings
  $("openAgentWorkspaceBtn").addEventListener("click", () => {
    state.chat.visible = !state.chat.visible;
    $("chatWorkspace").classList.toggle("hidden", !state.chat.visible);
    if (state.chat.visible) {
      $("chatInput").focus();
    }
  });
  $("chatCloseBtn").addEventListener("click", () => {
    state.chat.visible = false;
    $("chatWorkspace").classList.add("hidden");
    if (state.chat.abortController) {
      state.chat.abortController.abort();
      state.chat.abortController = null;
    }
  });
  $("approveAgentRepairBtn").addEventListener("click", approveAgentRepair);
  $("chatSendBtn").addEventListener("click", () => {
    const input = $("chatInput");
    const message = input.value.trim();
    if (message && !state.chat.abortController) {
      input.value = "";
      startChat(message);
    }
  });
  $("chatInput").addEventListener("keydown", (event) => {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      $("chatSendBtn").click();
    }
  });
}

// === Chat Functions ===

let chatMessageCount = 0;

async function startChat(message) {
  const messagesEl = $("chatMessages");
  const abortController = new AbortController();
  state.chat.abortController = abortController;
  state.chat.messages = [];

  addChatMessage("user", escapeHtml(message));
  addChatMessage("status", "AI 助手思考中...");

  try {
    // Resolve source/target resource context for the chat prompt
    const srcId = sourceProfileId() || null;
    const tgtId = targetProfileId() || null;
    const resProfileId = state.selectedResource?.profileId ?? null;
    const resPath = state.selectedResource?.virtualPath ?? null;

    let srcPath = null;
    let tgtPath = null;
    if (resPath && resProfileId) {
      if (resProfileId === tgtId) {
        // Selected resource is a target resource → find corresponding source path
        tgtPath = resPath;
        srcPath = buildReferencePathCandidates(resPath)[0] || null;
      } else if (resProfileId === srcId) {
        // Selected resource is a source resource → find corresponding target path
        srcPath = resPath;
        tgtPath = buildTargetPathCandidates(resPath)[0] || null;
      } else {
        // Not clearly source or target, just send what we have
        srcPath = resProfileId === srcId ? resPath : null;
        tgtPath = resProfileId === tgtId ? resPath : null;
      }
    }

    const response = await fetch("/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        message,
        profileId: resProfileId ?? state.selectedProfile?.id ?? null,
        resourcePath: resPath,
        sourceProfileId: srcId,
        targetProfileId: tgtId,
        sourceResourcePath: srcPath,
        targetResourcePath: tgtPath,
        currentView: buildAgentCurrentView()
      }),
      signal: abortController.signal
    });

    if (!response.ok) {
      updateLastChatMessage("error", `请求失败 (${response.status})`);
      return;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const events = buffer.split("\n\n");
      buffer = events.pop() || "";

      for (const block of events) {
        processSseBlock(block);
      }
    }

    if (buffer.trim()) {
      processSseBlock(buffer);
    }
  } catch (err) {
    if (err.name !== "AbortError") {
      updateLastChatMessage("error", `连接错误：${escapeHtml(err.message)}`);
    }
  } finally {
    releaseChatSendLock();
  }
}

function releaseChatSendLock() {
  state.chat.abortController = null;
}

function processSseBlock(block) {
  const lines = block.split("\n");
  let eventName = "";
  let dataJson = "";

  for (const line of lines) {
    if (line.startsWith("event: ")) {
      eventName = line.slice(7).trim();
    } else if (line.startsWith("data: ")) {
      dataJson = line.slice(6).trim();
    }
  }

  if (!dataJson) return;

  let data;
  try {
    data = JSON.parse(dataJson);
  } catch {
    return;
  }

  switch (eventName) {
    case "message":
      updateLastChatMessage("status", "");
      addChatMessage("assistant", escapeHtml(data.text || ""));
      break;
    case "tool_call":
      addChatToolCall(data.tool, data.arguments || "{}", data.status || "pending", data.resultText || null);
      if (data.status === "completed" &&
          (data.tool === "poe_write_overlay_text" || data.tool === "poe_write_overlay_binary")) {
        refreshOverlayList();
      }
      break;
    case "command":
      addChatCommand(data.command || "", data.exitCode);
      break;
    case "error":
      updateLastChatMessage("status", "");
      addChatMessage("error", escapeHtml(data.text || "未知错误"));
      break;
    case "diagnostic":
      renderAgentDiagnostic(data.finding || data);
      break;
    case "done":
      updateLastChatMessage("status", "");
      releaseChatSendLock();
      if (data.autoDiagnostic) {
        addChatMessage("status", "检测到本次运行未完成，正在启动自动诊断...");
      }
      break;
  }
}

function renderAgentDiagnostic(finding) {
  const panel = $("agentDiagnosticPanel");
  panel.classList.remove("hidden");
  $("agentDiagnosticSummary").textContent = `${finding.code || "diagnostic"}：${finding.summary || ""}`;
  $("approveAgentRepairBtn").disabled = finding.severity !== "high";
  state.chat.pendingRepair = finding;
  releaseChatSendLock();
}

async function approveAgentRepair() {
  if (!state.chat.pendingRepair) return;
  releaseChatSendLock();
  const result = await api("/api/agent/repair/approve", {
    runId: state.chat.pendingRepair.runId,
    code: state.chat.pendingRepair.code
  });
  if (result?.repairRunId) {
    addChatMessage("status", `Agent 修复已启动：${escapeHtml(result.repairRunId)}`);
    pollAgentRepairRun(result.repairRunId);
  }
}

function pollAgentRepairRun(repairRunId) {
  if (state.chat.repairPollTimer) {
    clearTimeout(state.chat.repairPollTimer);
  }

  const terminalRepairStatuses = new Set(["completed", "failed", "cancelled"]);
  const poll = async () => {
    try {
      const events = await api(`/api/agent/runs/${encodeURIComponent(repairRunId)}/trace`);
      renderAgentRepairTrace(repairRunId, events || []);
      const terminalRunEvent = [...(events || [])].reverse().find((event) =>
        event.eventName === "run" && terminalRepairStatuses.has(event.status));
      if (!terminalRunEvent) {
        state.chat.repairPollTimer = setTimeout(poll, 1500);
      }
    } catch (error) {
      addChatMessage("error", `Agent 修复进度读取失败：${escapeHtml(error.message)}`);
    }
  };

  poll();
}

function renderAgentRepairTrace(repairRunId, events) {
  const latest = [...events].reverse().find((event) => event.eventName === "codex_event" || event.eventName === "run");
  if (!latest) return;
  let text = `Agent 修复 ${repairRunId.slice(0, 8)}：${latest.eventName}/${latest.status}`;
  try {
    const data = JSON.parse(latest.dataJson || "{}");
    if (data.message) {
      text += `\n${data.message}`;
    } else if (data.stderrSummary) {
      text += `\n${data.stderrSummary}`;
    }
  } catch {
  }
  addChatMessage("status", escapeHtml(text));
}

function addChatMessage(role, content) {
  const messagesEl = $("chatMessages");
  const id = "chat-msg-" + (++chatMessageCount);
  const div = document.createElement("div");
  div.id = id;
  div.className = "chat-msg chat-msg-" + role;

  if (role === "user") {
    div.innerHTML = '<div class="chat-msg-label">你</div><div class="chat-msg-content">' + content + "</div>";
  } else if (role === "assistant") {
    div.innerHTML = '<div class="chat-msg-label">AI</div><div class="chat-msg-content">' + content + "</div>";
  } else if (role === "error") {
    div.innerHTML = '<div class="chat-msg-label">错误</div><div class="chat-msg-content chat-error-text">' + content + "</div>";
  } else if (role === "status") {
    div.innerHTML = '<div class="chat-msg-label">AI</div><div class="chat-msg-content chat-status-text">' + content + "</div>";
  }

  messagesEl.appendChild(div);
  messagesEl.scrollTop = messagesEl.scrollHeight;
  return div;
}

function updateLastChatMessage(role, content) {
  const messagesEl = $("chatMessages");
  const lastMsg = messagesEl.lastElementChild;
  if (!lastMsg) return;

  if (role === "status") {
    const contentEl = lastMsg.querySelector(".chat-status-text");
    if (contentEl) {
      contentEl.textContent = content || "思考中...";
    } else {
      lastMsg.className = "chat-msg chat-msg-status";
      lastMsg.innerHTML = '<div class="chat-msg-label">AI</div><div class="chat-msg-content chat-status-text">' + (content || "思考中...") + "</div>";
    }
  } else if (role === "error") {
    lastMsg.className = "chat-msg chat-msg-error";
    lastMsg.innerHTML = '<div class="chat-msg-label">错误</div><div class="chat-msg-content chat-error-text">' + content + "</div>";
  }
}

function addChatToolCall(tool, argsInput, status, resultText = null) {
  const existing = findOpenToolCall(tool, argsInput);
  if (existing) {
    existing.querySelector(".chat-tool-status").textContent = status || "pending";
    if (resultText) {
      existing.querySelector(".chat-tool-result").textContent = summarizeChatToolResult(tool, resultText);
    }
    return existing;
  }

  const messagesEl = $("chatMessages");
  const id = "chat-tool-" + (++chatMessageCount);
  const div = document.createElement("div");
  div.id = id;
  div.className = "chat-tool-call";
  div.dataset.toolCallKey = chatToolCallKey(tool, argsInput);

  let argsDisplay = "";
  if (argsInput && typeof argsInput === "object") {
    argsDisplay = Object.entries(argsInput)
      .map(function (kv) { return escapeHtml(kv[0]) + ": " + escapeHtml(String(kv[1]).slice(0, 200)); })
      .join("<br>");
  } else if (typeof argsInput === "string") {
    try {
      const args = JSON.parse(argsInput);
      argsDisplay = Object.entries(args)
        .map(function (kv) { return escapeHtml(kv[0]) + ": " + escapeHtml(String(kv[1]).slice(0, 200)); })
        .join("<br>");
    } catch {
      argsDisplay = escapeHtml(argsInput.slice(0, 300));
    }
  } else {
    argsDisplay = "";
  }

  div.innerHTML = '<div class="chat-tool-head"><span class="chat-tool-name">' + escapeHtml(tool) + '</span><span class="chat-tool-status">' + escapeHtml(status) + '</span></div><div class="chat-tool-args">' + argsDisplay + '</div><pre class="chat-tool-result"></pre>';
  if (resultText) {
    div.querySelector(".chat-tool-result").textContent = summarizeChatToolResult(tool, resultText);
  }
  messagesEl.appendChild(div);
  messagesEl.scrollTop = messagesEl.scrollHeight;
  return div;
}

function summarizeChatToolResult(tool, resultText) {
  if (tool === "poe_get_project_knowledge") {
    try {
      const data = JSON.parse(resultText);
      const sections = Array.isArray(data.sections) ? data.sections : [];
      const first = sections[0];
      return [
        `知识块：${sections.length}`,
        `缺失：${data.missingSectionIds?.length ?? 0}`,
        first ? `示例：${first.sectionId} / ${first.title}` : "未返回知识块"
      ].join("\n");
    } catch {
      return resultText.slice(0, 2000);
    }
  }

  return resultText.slice(0, 2000);
}

function findOpenToolCall(tool, argsInput) {
  const key = chatToolCallKey(tool, argsInput);
  return document.querySelector(`[data-tool-call-key="${CSS.escape(key)}"]`);
}

function chatToolCallKey(tool, argsInput) {
  return `${tool}:${JSON.stringify(argsInput || {})}`;
}

function addChatCommand(command, exitCode) {
  const messagesEl = $("chatMessages");
  const id = "chat-cmd-" + (++chatMessageCount);
  const div = document.createElement("div");
  div.id = id;
  div.className = "chat-command";
  div.innerHTML = '<div class="chat-cmd-line">$ ' + escapeHtml(command) + "</div>" + (exitCode !== null ? '<div class="chat-cmd-exit">退出码: ' + exitCode + "</div>" : "");
  messagesEl.appendChild(div);
  messagesEl.scrollTop = messagesEl.scrollHeight;
}

applyTheme(currentTheme);
bind();
loadWorkspaceSettings()
  .then(refreshProfiles)
  .catch((error) => setStatus(error.message));
