// ============================================================================
// Ignorant Vega — Content Script
// 注入到每个页面，提供 DOM 观察和页面状态报告
// ============================================================================

(() => {
  "use strict";

  // 页面就绪状态
  let pageReady = false;
  let pageErrors = [];

  // 捕获页面错误
  window.addEventListener("error", (e) => {
    pageErrors.push({
      message: e.message,
      source: e.filename,
      line: e.lineno,
      col: e.colno,
      timestamp: Date.now()
    });
    // 最多保留 50 条
    if (pageErrors.length > 50) pageErrors.shift();
  });

  // 页面加载完成
  window.addEventListener("load", () => {
    pageReady = true;
  });

  // 监听来自 background 的消息
  chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (msg.type === "get_page_info") {
      sendResponse({
        url: location.href,
        title: document.title,
        ready: pageReady,
        errors: pageErrors,
        scrollHeight: document.documentElement.scrollHeight,
        scrollWidth: document.documentElement.scrollWidth,
        viewportHeight: window.innerHeight,
        viewportWidth: window.innerWidth,
        forms: Array.from(document.forms).map(f => ({
          id: f.id,
          name: f.name,
          action: f.action,
          method: f.method,
          fields: Array.from(f.elements).map(el => ({
            name: el.name,
            type: el.type,
            tagName: el.tagName,
            value: el.value?.slice(0, 100)
          }))
        })),
        links: Array.from(document.links).slice(0, 100).map(a => ({
          href: a.href,
          text: a.textContent?.trim().slice(0, 100)
        }))
      });
      return true;
    }

    if (msg.type === "get_element_info") {
      const el = document.querySelector(msg.selector);
      if (!el) {
        sendResponse({ error: `元素未找到: ${msg.selector}` });
        return true;
      }
      const rect = el.getBoundingClientRect();
      sendResponse({
        tagName: el.tagName,
        id: el.id,
        className: el.className,
        text: el.textContent?.trim().slice(0, 500),
        innerText: el.innerText?.trim().slice(0, 500),
        visible: rect.width > 0 && rect.height > 0,
        rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
        attributes: Object.fromEntries(
          Array.from(el.attributes).map(a => [a.name, a.value])
        ),
        children: el.children.length,
        parent: el.parentElement?.tagName
      });
      return true;
    }

    if (msg.type === "observe_element") {
      // 设置 DOM 变化观察器
      const target = document.querySelector(msg.selector);
      if (!target) {
        sendResponse({ error: `元素未找到: ${msg.selector}` });
        return true;
      }

      const changes = [];
      const observer = new MutationObserver((mutations) => {
        for (const m of mutations) {
          changes.push({
            type: m.type,
            target: m.target.tagName,
            added: m.addedNodes.length,
            removed: m.removedNodes.length,
            attributeName: m.attributeName,
            timestamp: Date.now()
          });
        }
        // 最多保留 100 条
        if (changes.length > 100) changes.splice(0, changes.length - 100);
        
        // 通知 background
        chrome.runtime.sendMessage({
          type: "dom_changed",
          selector: msg.selector,
          changes: changes.slice(-10)
        });
      });

      observer.observe(target, {
        childList: true,
        subtree: true,
        attributes: true,
        characterData: true
      });

      // 存储 observer 以便后续断开
      if (!window.__vegaObservers) window.__vegaObservers = {};
      window.__vegaObservers[msg.selector] = observer;

      sendResponse({ observing: msg.selector });
      return true;
    }

    if (msg.type === "stop_observing") {
      if (window.__vegaObservers?.[msg.selector]) {
        window.__vegaObservers[msg.selector].disconnect();
        delete window.__vegaObservers[msg.selector];
        sendResponse({ stopped: msg.selector });
      } else {
        sendResponse({ error: "未在观察此元素" });
      }
      return true;
    }
  });
})();
