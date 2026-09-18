window.frpmOAuth = (() => {
    let popup;
    let callbackReference;
    let closeTimer;

    const stopMonitoring = () => {
        if (closeTimer) window.clearInterval(closeTimer);
        closeTimer = undefined;
    };

    const receive = event => {
        if (event.origin !== window.location.origin || event.data?.type !== "frpm:lolia-oauth") return;
        stopMonitoring();
        callbackReference?.invokeMethodAsync("OnLoliaOAuthCompleted", event.data.success === true, event.data.message ?? "OAuth 已完成。");
        popup = undefined;
    };

    return {
        initialize(reference) {
            callbackReference = reference;
            window.removeEventListener("message", receive);
            window.addEventListener("message", receive);
        },
        openPending() {
            popup = window.open("about:blank", "frpm-lolia-oauth", "popup=yes,width=620,height=760,resizable=yes,scrollbars=yes");
            if (!popup) return false;
            popup.document.title = "LoliaFrp OAuth";
            popup.document.body.textContent = "正在准备授权…";
            popup.focus();
            stopMonitoring();
            closeTimer = window.setInterval(() => {
                if (!popup || !popup.closed) return;
                stopMonitoring();
                popup = undefined;
                callbackReference?.invokeMethodAsync("OnLoliaOAuthCompleted", false, "授权窗口已关闭，未保存任何更改。");
            }, 500);
            return true;
        },
        navigate(url) {
            if (!popup || popup.closed) throw new Error("授权弹窗已关闭，请重试。");
            popup.location.replace(url);
            popup.focus();
        },
        cancel() {
            stopMonitoring();
            if (popup && !popup.closed) popup.close();
            popup = undefined;
        },
        dispose() {
            stopMonitoring();
            window.removeEventListener("message", receive);
            callbackReference = undefined;
        }
    };
})();
