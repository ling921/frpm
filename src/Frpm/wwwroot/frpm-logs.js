export function isNearBottom(element) {
    if (!element) return true;
    return element.scrollHeight - element.scrollTop - element.clientHeight <= 32;
}

export function scrollToBottom(element) {
    if (element) element.scrollTop = element.scrollHeight;
}
