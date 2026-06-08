using System.Text.Json;

namespace BrowserDemo.Services.Automation;

/// <summary>
/// 浏览器自动化 JS 脚本生成器。
/// 所有方法返回纯 JS 字符串，无运行时状态，无依赖。
/// 可脱离 WebView2 独立单元测试 — 用 <c>new Function(script)</c> 验证语法。
///
/// 核心 API: <see cref="InjectionScript"/> 在页面加载完成后注入一次，
/// 建立 <c>window.bermainA11y</c> 全局对象。后续的 GetSnapshot/Click/Type 等
/// 通过 <see cref="GetSnapshotCall"/> / <see cref="ClickElementCall"/> 等方法
/// 生成对该全局对象的调用代码。
/// </summary>
public static class AutomationScripts
{
    /// <summary>
    /// 元素 ID 用的 data-* 属性名。
    /// 注入 JS 时用 <c>data-bermain-id</c>；JS 内部使用 <c>dataset.bermainId</c>。
    /// </summary>
    public const string ElementIdAttr = "data-bermain-id";

    /// <summary>JS 内通过 dataset 访问的属性名（驼峰）</summary>
    public const string ElementIdDatasetKey = "bermainId";

    /// <summary>快照最大元素数量。超出则截断并标记 truncated:true。</summary>
    public const int MaxSnapshotElements = 1000;

    /// <summary>
    /// 注入到页面的完整 API。
    /// 在 NavigationCompleted 事件中调用一次，建立 <c>window.bermainA11y</c> 全局对象。
    /// 幂等：重复注入不会重复定义（用 if 守护）。
    /// </summary>
    public static string InjectionScript { get; } = $$"""
        (function() {
            'use strict';
            if (window.bermainA11y) { return; } // 幂等：重复注入跳过

            // ★ 可交互元素 CSS 选择器 ★
            var INTERACTIVE_SELECTOR = [
                'a', 'button', 'input', 'select', 'textarea', 'datalist',
                '[role="button"]', '[role="link"]', '[role="menuitem"]',
                '[role="tab"]', '[role="option"]', '[role="checkbox"]',
                '[role="radio"]', '[role="switch"]', '[role="combobox"]',
                '[role="textbox"]', '[role="searchbox"]', '[role="spinbutton"]',
                '[role="slider"]', '[role="treeitem"]', '[role="gridcell"]',
                '[tabindex]:not([tabindex="-1"])', '[contenteditable]',
                'summary', 'details', 'label'
            ].join(',');

            var ATTR = '{{ElementIdAttr}}';
            var ATTR_KEY = '{{ElementIdDatasetKey}}';
            var MAX = {{MaxSnapshotElements}};

            // ★ 递归收集可交互元素（穿透 open Shadow DOM 与同源 iframe）★
            function collectInteractive(root, out) {
                try {
                    var nodes = root.querySelectorAll(INTERACTIVE_SELECTOR);
                    for (var i = 0; i < nodes.length; i++) {
                        out.push(nodes[i]);
                        if (out.length >= MAX) return;
                    }
                    // open Shadow DOM 穿透
                    var hostCandidates = root.querySelectorAll('*');
                    for (var j = 0; j < hostCandidates.length; j++) {
                        var sr = hostCandidates[j].shadowRoot;
                        if (sr) {
                            collectInteractive(sr, out);
                            if (out.length >= MAX) return;
                        }
                    }
                    // 同源 iframe 穿透
                    var iframes = root.querySelectorAll('iframe');
                    for (var k = 0; k < iframes.length; k++) {
                        try {
                            var doc = iframes[k].contentDocument;
                            if (doc) {
                                collectInteractive(doc, out);
                                if (out.length >= MAX) return;
                            }
                        } catch (e) { /* 跨域 iframe 不可访问 */ }
                    }
                } catch (e) { /* 容错：节点可能在遍历期间被移除 */ }
            }

            // ★ 给元素分配 ID（写入 dataset）★
            function assignIds(elements) {
                for (var i = 0; i < elements.length; i++) {
                    try { elements[i].dataset[ATTR_KEY] = String(i); }
                    catch (e) { /* SVG 等无 dataset 的元素跳过 */ }
                }
            }

            // ★ 生成简化 CSS 选择器（最多 5 段；遇到 #id 截断）★
            function generateCssSelector(el) {
                var path = [];
                var node = el;
                while (node && node.nodeType === 1 && node !== document.body && path.length < 5) {
                    var sel = node.tagName.toLowerCase();
                    if (node.id) {
                        try { sel = '#' + CSS.escape(node.id); }
                        catch (e) { sel = '#' + node.id; }
                        path.unshift(sel);
                        return path.join(' > ');
                    }
                    if (node.classList && node.classList.length > 0) {
                        sel += '.' + Array.prototype.slice.call(node.classList, 0, 2)
                            .map(function(c){
                                try { return CSS.escape(c); } catch(e) { return c; }
                            }).join('.');
                    }
                    path.unshift(sel);
                    node = node.parentElement;
                }
                return path.join(' > ');
            }

            // ★ 收集元素信息 ★
            function collectElementInfo(el, index) {
                var rect = { x: 0, y: 0, w: 0, h: 0 };
                try {
                    var r = el.getBoundingClientRect();
                    rect = { x: Math.round(r.x), y: Math.round(r.y),
                             w: Math.round(r.width), h: Math.round(r.height) };
                } catch (e) {}

                var tag = (el.tagName || '').toLowerCase();
                var isInput = tag === 'input' || tag === 'textarea' || tag === 'select';
                var t = el.type || null;

                return {
                    id: index,
                    tag: tag,
                    text: ((el.textContent || '') + '').trim().substring(0, 200),
                    role: el.getAttribute ? (el.getAttribute('role') || null) : null,
                    type: t,
                    name: el.getAttribute ? (el.getAttribute('name') || null) : null,
                    href: el.getAttribute ? (el.getAttribute('href') || null) : null,
                    aria_label: el.getAttribute ? (el.getAttribute('aria-label') || null) : null,
                    placeholder: el.getAttribute ? (el.getAttribute('placeholder') || null) : null,
                    value: isInput && el.value != null ? (el.value + '').substring(0, 100) : null,
                    checked: (t === 'checkbox' || t === 'radio') ? !!el.checked : null,
                    disabled: !!el.disabled,
                    readonly: !!el.readOnly,
                    rect: rect,
                    visible: rect.w > 0 && rect.h > 0 && (el.offsetParent !== null
                              || (el.getClientRects && el.getClientRects().length > 0)),
                    css_selector: generateCssSelector(el)
                };
            }

            // ★ 通过 ID 在 document + open shadow root + 同源 iframe 中查找元素 ★
            function findById(id) {
                var attr = '[' + ATTR + '="' + id + '"]';
                function searchIn(root) {
                    try {
                        var hit = root.querySelector(attr);
                        if (hit) return hit;
                        var all = root.querySelectorAll('*');
                        for (var i = 0; i < all.length; i++) {
                            var sr = all[i].shadowRoot;
                            if (sr) {
                                var h = searchIn(sr);
                                if (h) return h;
                            }
                        }
                        var iframes = root.querySelectorAll('iframe');
                        for (var j = 0; j < iframes.length; j++) {
                            try {
                                var doc = iframes[j].contentDocument;
                                if (doc) {
                                    var h2 = searchIn(doc);
                                    if (h2) return h2;
                                }
                            } catch (e) {}
                        }
                    } catch (e) {}
                    return null;
                }
                return searchIn(document);
            }

            // ★ 暴露的公共 API ★
            window.bermainA11y = {

                // 完整快照：刷新 ID + 序列化元素
                getSnapshot: function() {
                    var elements = [];
                    collectInteractive(document, elements);
                    var truncated = elements.length >= MAX;
                    assignIds(elements);
                    var info = [];
                    for (var i = 0; i < elements.length; i++) {
                        try { info.push(collectElementInfo(elements[i], i)); }
                        catch (e) { /* 跳过损坏元素 */ }
                    }
                    return JSON.stringify({
                        url: location.href,
                        title: document.title,
                        snapshotAt: new Date().toISOString(),
                        elementCount: info.length,
                        truncated: truncated,
                        elements: info
                    });
                },

                // 点击：scrollIntoView → focus → mousedown/mouseup/click
                clickElement: function(id) {
                    var el = findById(id);
                    if (!el) return JSON.stringify({ error: 'not_found', id: id });
                    try {
                        el.scrollIntoView({ block: 'center', behavior: 'instant' });
                    } catch (e) {
                        try { el.scrollIntoView(); } catch (e2) {}
                    }
                    try { el.focus(); } catch (e) {}
                    try {
                        el.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, view: window }));
                        el.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, view: window }));
                        el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window }));
                    } catch (e) {
                        try { el.click(); }
                        catch (e2) { return JSON.stringify({ error: 'click_failed', message: e2.message }); }
                    }
                    return JSON.stringify({
                        success: true, tag: el.tagName,
                        text: ((el.textContent || '') + '').trim().substring(0, 60)
                    });
                },

                // 输入文本：原生 setter 绕过框架拦截 + dispatch input/change
                typeInElement: function(id, text, clearFirst) {
                    var el = findById(id);
                    if (!el) return JSON.stringify({ error: 'not_found', id: id });
                    try { el.focus(); } catch (e) {}
                    var tag = el.tagName;

                    if (tag === 'INPUT' || tag === 'TEXTAREA') {
                        try {
                            var proto = (tag === 'INPUT' ? window.HTMLInputElement : window.HTMLTextAreaElement).prototype;
                            var desc = Object.getOwnPropertyDescriptor(proto, 'value');
                            var nativeSetter = desc && desc.set;
                            var finalValue = clearFirst ? text : ((el.value || '') + text);
                            if (nativeSetter) {
                                nativeSetter.call(el, finalValue);
                            } else {
                                el.value = finalValue;
                            }
                            el.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
                            el.dispatchEvent(new Event('change', { bubbles: true }));
                        } catch (e) {
                            return JSON.stringify({ error: 'type_failed', message: e.message });
                        }
                    } else if (el.isContentEditable) {
                        try {
                            if (clearFirst) el.textContent = '';
                            el.textContent = (el.textContent || '') + text;
                            el.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
                            el.dispatchEvent(new Event('change', { bubbles: true }));
                        } catch (e) {
                            return JSON.stringify({ error: 'type_failed', message: e.message });
                        }
                    } else {
                        return JSON.stringify({ error: 'not_typeable', tag: tag });
                    }
                    return JSON.stringify({
                        success: true, tag: tag,
                        valuePreview: ((el.value || el.textContent || '') + '').substring(0, 60)
                    });
                },

                // 悬停
                hoverElement: function(id) {
                    var el = findById(id);
                    if (!el) return JSON.stringify({ error: 'not_found', id: id });
                    try {
                        var r = el.getBoundingClientRect();
                        var init = { bubbles: true, cancelable: true, view: window,
                                     clientX: r.x + r.width / 2, clientY: r.y + r.height / 2 };
                        el.dispatchEvent(new MouseEvent('mouseover', init));
                        el.dispatchEvent(new MouseEvent('mouseenter', init));
                        el.dispatchEvent(new MouseEvent('mousemove', init));
                    } catch (e) {
                        return JSON.stringify({ error: 'hover_failed', message: e.message });
                    }
                    return JSON.stringify({ success: true, tag: el.tagName });
                },

                // 选择下拉
                selectOption: function(id, value) {
                    var el = findById(id);
                    if (!el) return JSON.stringify({ error: 'not_found', id: id });
                    if (el.tagName !== 'SELECT') return JSON.stringify({ error: 'not_select', tag: el.tagName });
                    try {
                        var matched = false;
                        for (var i = 0; i < el.options.length; i++) {
                            var o = el.options[i];
                            if (o.value === value || (o.text || '').trim() === value) {
                                el.selectedIndex = i;
                                matched = true; break;
                            }
                        }
                        if (!matched) return JSON.stringify({ error: 'option_not_found', value: value });
                        el.dispatchEvent(new Event('input', { bubbles: true }));
                        el.dispatchEvent(new Event('change', { bubbles: true }));
                    } catch (e) {
                        return JSON.stringify({ error: 'select_failed', message: e.message });
                    }
                    return JSON.stringify({ success: true, selectedValue: el.value });
                },

                // 滚动
                scroll: function(dx, dy) {
                    try {
                        window.scrollBy({ left: dx || 0, top: dy || 0, behavior: 'instant' });
                    } catch (e) {
                        try { window.scrollBy(dx || 0, dy || 0); }
                        catch (e2) { return JSON.stringify({ error: 'scroll_failed' }); }
                    }
                    return JSON.stringify({
                        success: true, scrollX: window.scrollX, scrollY: window.scrollY
                    });
                },

                // 等待文本出现（Promise）
                waitForText: function(text, timeoutMs) {
                    timeoutMs = timeoutMs || 10000;
                    return new Promise(function(resolve) {
                        var start = Date.now();
                        function check() {
                            try {
                                if (document.body && (document.body.innerText || '').indexOf(text) !== -1) {
                                    resolve(JSON.stringify({ success: true, foundAt: Date.now() - start }));
                                    return;
                                }
                            } catch (e) {}
                            if (Date.now() - start > timeoutMs) {
                                resolve(JSON.stringify({ success: false, error: 'timeout', elapsedMs: Date.now() - start }));
                                return;
                            }
                            setTimeout(check, 100);
                        }
                        check();
                    });
                },

                // 获取指定 ID 元素的 CSS 选择器（兜底用）
                getCssSelector: function(id) {
                    var el = findById(id);
                    if (!el) return JSON.stringify({ error: 'not_found', id: id });
                    return JSON.stringify({ success: true, selector: generateCssSelector(el) });
                }
            };
        })();
        """;

    // ====================================================================
    // 调用方法 — 每次调用拼接参数后通过 ExecuteScriptAsync 执行
    // ====================================================================

    /// <summary>获取完整快照（含元素 JSON 数组）</summary>
    public const string GetSnapshotCall = "window.bermainA11y.getSnapshot()";

    /// <summary>点击元素（data-bermain-id）</summary>
    public static string ClickElementCall(int elementId)
        => $"window.bermainA11y.clickElement({elementId})";

    /// <summary>输入文本（含框架兼容事件）</summary>
    public static string TypeInElementCall(int elementId, string text, bool clearFirst = true)
        => $"window.bermainA11y.typeInElement({elementId}, {EncodeJsString(text)}, {(clearFirst ? "true" : "false")})";

    /// <summary>悬停元素</summary>
    public static string HoverCall(int elementId)
        => $"window.bermainA11y.hoverElement({elementId})";

    /// <summary>选择下拉选项（按 value 或 text）</summary>
    public static string SelectOptionCall(int elementId, string value)
        => $"window.bermainA11y.selectOption({elementId}, {EncodeJsString(value)})";

    /// <summary>滚动页面</summary>
    public static string ScrollCall(int dx, int dy)
        => $"window.bermainA11y.scroll({dx}, {dy})";

    /// <summary>等待文本出现（返回 Promise&lt;{success, error?, elapsedMs}&gt;）</summary>
    public static string WaitForTextCall(string text, int timeoutMs)
        => $"window.bermainA11y.waitForText({EncodeJsString(text)}, {timeoutMs})";

    /// <summary>获取元素的 CSS 选择器（兜底）</summary>
    public static string GetCssSelectorCall(int elementId)
        => $"window.bermainA11y.getCssSelector({elementId})";

    // ====================================================================
    // 工具方法
    // ====================================================================

    /// <summary>
    /// 将 .NET 字符串编码为 JS 字面量（含外层引号），用于嵌入到 ExecuteScriptAsync 的 JS 代码中。
    /// 用 JsonSerializer 处理转义，自然支持引号/换行/Unicode/反斜杠。
    /// </summary>
    private static string EncodeJsString(string value)
        => JsonSerializer.Serialize(value ?? string.Empty);
}
