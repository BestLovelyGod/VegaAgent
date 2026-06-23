chrome.runtime.sendMessage({ type: "get_status" }, (response) => {
  const el = document.getElementById("status");
  if (response?.connected) {
    el.className = "status connected";
    el.textContent = "✅ 已连接 (Native Messaging)";
  } else {
    el.className = "status polling";
    el.textContent = "⏳ 轮询模式 (等待 Agent)";
  }
});
