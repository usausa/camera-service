// MJPEG images by key, kept here so that a stream can be stopped after Blazor removed the element
const images = new Map();

export function start(container, key, url) {
    stop(key);
    const image = document.createElement('img');
    image.className = 'live-image';
    image.alt = 'Live';
    image.src = url;
    container.appendChild(image);
    images.set(key, image);
}

// Removing the source stops the request (an image only removed from the page may keep loading)
export function stop(key) {
    const image = images.get(key);
    if (image !== undefined) {
        images.delete(key);
        image.removeAttribute('src');
        image.remove();
    }
}
