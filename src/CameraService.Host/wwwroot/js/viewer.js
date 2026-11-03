// Keys of the image viewer while it is open: the arrows move, Space plays, Escape closes
const keys = new Set(['ArrowLeft', 'ArrowRight', 'Home', 'End', ' ', 'Escape']);

let handler = null;

export function listen(viewer) {
    stop();
    handler = (e) => {
        // A list of the viewer keeps its own keys
        if (!keys.has(e.key) || e.altKey || e.ctrlKey || e.metaKey || (e.target instanceof HTMLSelectElement)) {
            return;
        }

        e.preventDefault();
        viewer.invokeMethodAsync('OnKeyAsync', e.key);
    };
    document.addEventListener('keydown', handler);
}

export function stop() {
    if (handler !== null) {
        document.removeEventListener('keydown', handler);
        handler = null;
    }
}
