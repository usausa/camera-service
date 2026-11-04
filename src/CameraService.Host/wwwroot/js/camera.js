// Live views by key, kept here so that a stream can be stopped after Blazor removed the element.
// H.264 (WebSocket + WebCodecs) where the browser can decode it (WebCodecs needs localhost or HTTPS), otherwise MJPEG (fetch).
// Both are drawn on a canvas and connect again when the connection is lost, the label shows that the picture is old meanwhile.

// Header of an H.264 message (little endian), see H264Header.cs
const HEADER_VERSION = 1;

const KEY_FRAME_FLAG = 0x8;

// Full range with the BT.601 matrix (otherwise video range)
const FULL_RANGE_FLAG = 0x10;

// The decoder is slower than the stream when more frames wait, the frames are dropped until a key frame
const MAX_DECODE_QUEUE = 3;

// The server ignores the requests within 500 ms
const KEY_FRAME_REQUEST_INTERVAL = 500;

// WebSocketCloseStatus.InternalServerError: the server has no encoder
const CLOSE_NO_ENCODER = 1011;

// Parts of the MJPEG stream, see StreamEndpoints.cs (the first header has no line break before it)
const MJPEG_DELIMITER = new TextEncoder().encode('\r\n--frame\r\nContent-Type: image/jpeg\r\n\r\n');

const MJPEG_FIRST_HEADER_LENGTH = MJPEG_DELIMITER.length - 2;

const MJPEG_BUFFER_SIZE = 256 * 1024;

const RECONNECT_DELAY = 1000;

// No data for this time: the label tells that the picture is old (the first frame takes about 3 seconds when the camera is closed)
const STALE_TIMEOUT = 5000;

// No data for this time: the connection is lost without a close (for example the network went down) or the camera sends nothing
const STALL_TIMEOUT = 15000;

const STALL_CHECK_INTERVAL = 1000;

const OFFLINE_TEXT = 'Reconnecting';

const views = new Map();

// h264Url: null when H.264 is disabled
export function start(container, key, mjpegUrl, h264Url) {
    stop(key);

    // The element is gone (an error here would end the Blazor circuit)
    if (container === null) {
        return;
    }

    const view = {
        container,
        mjpegUrl,
        h264Url,
        canvas: null,
        context: null,
        label: document.createElement('span'),
        mode: '',
        offline: false,
        // Time of the last data from the server
        lastData: performance.now(),
        watchdog: 0,
        // MJPEG
        abort: null,
        nextImage: null,
        drawing: false,
        // H.264
        socket: null,
        decoder: null,
        // Configuration of the decoder
        codec: null,
        width: 0,
        height: 0,
        fullRange: false,
        // Delta frames are not decoded until a key frame
        waitingKeyFrame: true,
        lastKeyFrameRequest: 0,
        nextTimestamp: 0,
        stopped: false,
        stats: { mode: '', received: 0, bytes: 0, keyFrames: 0, drawn: 0, dropped: 0, reconnects: 0, codec: null }
    };
    view.label.className = 'live-mode';
    container.appendChild(view.label);
    views.set(key, view);
    view.watchdog = setInterval(() => checkStall(view), STALL_CHECK_INTERVAL);

    if ((h264Url !== null) && window.isSecureContext && (typeof VideoDecoder !== 'undefined')) {
        startH264(view);
    } else {
        startMjpeg(view);
    }
}

// Stopping the request and the socket here: the server keeps the camera open while they are connected
export function stop(key) {
    const view = views.get(key);
    if (view === undefined) {
        return;
    }

    views.delete(key);
    view.stopped = true;
    clearInterval(view.watchdog);
    stopMjpeg(view);
    stopH264(view);
    view.label.remove();
}

// Counters of the views (for checking the views)
export function getStats() {
    return [...views.values()].map((x) => ({ ...x.stats, width: x.width, height: x.height, offline: x.offline, url: x.mode === 'H.264' ? x.h264Url : x.mjpegUrl }));
}

//--------------------------------------------------------------------------------
// View
//--------------------------------------------------------------------------------

function createCanvas(view) {
    view.canvas = document.createElement('canvas');
    view.canvas.className = 'live-image';
    view.canvas.setAttribute('aria-label', 'Live');
    view.context = view.canvas.getContext('2d');
    view.container.appendChild(view.canvas);
}

function removeCanvas(view) {
    if (view.canvas !== null) {
        view.canvas.remove();
        view.canvas = null;
        view.context = null;
    }
}

function draw(view, image, width, height) {
    if ((view.canvas.width !== width) || (view.canvas.height !== height)) {
        view.canvas.width = width;
        view.canvas.height = height;
    }
    view.context.drawImage(image, 0, 0);
    view.stats.drawn++;
}

function setMode(view, mode) {
    view.mode = mode;
    view.stats.mode = mode;
    view.offline = false;
    view.label.textContent = mode;
    view.label.classList.remove('offline');
    view.lastData = performance.now();
}

// The picture stays on the canvas, the label tells that it is old
function setOffline(view) {
    if (!view.offline) {
        view.offline = true;
        view.label.textContent = OFFLINE_TEXT;
        view.label.classList.add('offline');
    }
}

function onData(view) {
    view.lastData = performance.now();
    if (view.offline) {
        view.offline = false;
        view.label.textContent = view.mode;
        view.label.classList.remove('offline');
    }
}

// The connection is closed after the stall timeout, the stream connects again
function checkStall(view) {
    const elapsed = performance.now() - view.lastData;
    if (elapsed < STALE_TIMEOUT) {
        return;
    }

    setOffline(view);
    if (elapsed < STALL_TIMEOUT) {
        return;
    }

    view.lastData = performance.now();
    if (view.abort !== null) {
        view.abort.abort();
    } else if (view.socket !== null) {
        closeSocket(view);
        resetDecoder(view);
        reconnectSocket(view);
    }
}

function delay(milliseconds) {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

//--------------------------------------------------------------------------------
// MJPEG
//--------------------------------------------------------------------------------

function startMjpeg(view) {
    createCanvas(view);
    setMode(view, 'MJPEG');
    runMjpeg(view);
}

function stopMjpeg(view) {
    if (view.abort !== null) {
        view.abort.abort();
        view.abort = null;
    }
    view.nextImage = null;
    removeCanvas(view);
}

async function runMjpeg(view) {
    while (!view.stopped) {
        view.abort = new AbortController();
        try {
            await readMjpeg(view, view.abort.signal);
        } catch (e) {
            if (e.name !== 'AbortError') {
                console.warn('MJPEG connection lost.', e);
            }
        }

        if (!view.stopped) {
            setOffline(view);
            view.stats.reconnects++;
            await delay(RECONNECT_DELAY);
        }
    }
}

// Until the stream ends. The parts are split by the delimiter, which follows each JPEG (a part is complete when the next one starts).
async function readMjpeg(view, signal) {
    const response = await fetch(`${view.mjpegUrl}?raw`, { signal, cache: 'no-store' });
    if (!response.ok) {
        throw new Error(`Status ${response.status}`);
    }

    const reader = response.body.getReader();
    let buffer = new Uint8Array(MJPEG_BUFFER_SIZE);
    let length = 0;
    // Start of the current JPEG, -1 until the first header has arrived
    let start = -1;
    // Position to look for the next delimiter from
    let search = 0;
    while (true) {
        const { done, value } = await reader.read();
        if (done) {
            return;
        }

        view.stats.bytes += value.length;
        onData(view);

        // A part larger than the buffer makes it grow
        if (length + value.length > buffer.length) {
            const larger = new Uint8Array(Math.max(buffer.length * 2, length + value.length));
            larger.set(buffer.subarray(0, length));
            buffer = larger;
        }
        buffer.set(value, length);
        length += value.length;

        if (start < 0) {
            if (length < MJPEG_FIRST_HEADER_LENGTH) {
                continue;
            }
            start = MJPEG_FIRST_HEADER_LENGTH;
            search = start;
        }

        while (true) {
            const end = findDelimiter(buffer, search, length);
            if (end < 0) {
                search = Math.max(start, length - MJPEG_DELIMITER.length + 1);
                break;
            }

            view.stats.received++;
            showJpeg(view, buffer.slice(start, end));
            start = end + MJPEG_DELIMITER.length;
            search = start;
        }

        // The rest is the current part
        if (start > 0) {
            buffer.copyWithin(0, start, length);
            length -= start;
            search -= start;
            start = 0;
        }
    }
}

function findDelimiter(buffer, from, length) {
    const last = length - MJPEG_DELIMITER.length;
    let index = buffer.indexOf(MJPEG_DELIMITER[0], from);
    while ((index >= 0) && (index <= last)) {
        let matched = 1;
        while ((matched < MJPEG_DELIMITER.length) && (buffer[index + matched] === MJPEG_DELIMITER[matched])) {
            matched++;
        }
        if (matched === MJPEG_DELIMITER.length) {
            return index;
        }
        index = buffer.indexOf(MJPEG_DELIMITER[0], index + 1);
    }
    return -1;
}

// Only the latest image is decoded when the decoder is slower than the stream
function showJpeg(view, data) {
    view.nextImage = new Blob([data], { type: 'image/jpeg' });
    if (!view.drawing) {
        drawImages(view);
    }
}

async function drawImages(view) {
    view.drawing = true;
    while ((view.nextImage !== null) && !view.stopped) {
        const blob = view.nextImage;
        view.nextImage = null;
        try {
            const image = await createImageBitmap(blob);
            if (view.canvas !== null) {
                view.width = image.width;
                view.height = image.height;
                draw(view, image, image.width, image.height);
            }
            image.close();
        } catch (e) {
            console.warn('JPEG decode failed.', e);
        }
    }
    view.drawing = false;
}

//--------------------------------------------------------------------------------
// H.264
//--------------------------------------------------------------------------------

function startH264(view) {
    createCanvas(view);
    setMode(view, 'H.264');
    openSocket(view);
}

function stopH264(view) {
    closeSocket(view);
    resetDecoder(view);
    removeCanvas(view);
}

// The stream cannot be shown: MJPEG instead (the connection is closed, so the server stops encoding)
function fallBack(view, e) {
    console.warn('H.264 is not available, MJPEG is used.', e);
    stopH264(view);
    if (!view.stopped) {
        startMjpeg(view);
    }
}

function openSocket(view) {
    const url = new URL(view.h264Url, document.baseURI);
    url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const socket = new WebSocket(url);
    socket.binaryType = 'arraybuffer';
    socket.onmessage = (e) => {
        if (view.socket === socket) {
            onData(view);
            receive(view, e.data);
        }
    };
    socket.onclose = (e) => {
        if (view.socket !== socket) {
            return;
        }

        view.socket = null;
        resetDecoder(view);
        if (e.code === CLOSE_NO_ENCODER) {
            fallBack(view, 'The server has no encoder.');
            return;
        }

        // The server sends the frames from a key frame after the reconnection
        setOffline(view);
        reconnectSocket(view);
    };
    view.socket = socket;
}

function reconnectSocket(view) {
    view.stats.reconnects++;
    setTimeout(() => {
        if (!view.stopped && (view.canvas !== null) && (view.socket === null)) {
            openSocket(view);
        }
    }, RECONNECT_DELAY);
}

// Clears the socket first, so that it is not opened again
function closeSocket(view) {
    if (view.socket !== null) {
        const socket = view.socket;
        view.socket = null;
        socket.close();
    }
}

function resetDecoder(view) {
    if ((view.decoder !== null) && (view.decoder.state !== 'closed')) {
        view.decoder.close();
    }
    view.decoder = null;
    view.codec = null;
    view.waitingKeyFrame = true;
}

// Any text message asks the server for a key frame
function requestKeyFrame(view) {
    view.waitingKeyFrame = true;
    const time = performance.now();
    if ((view.socket !== null) && (view.socket.readyState === WebSocket.OPEN) && (time - view.lastKeyFrameRequest > KEY_FRAME_REQUEST_INTERVAL)) {
        view.lastKeyFrameRequest = time;
        view.socket.send('key');
    }
}

// Codec string for WebCodecs (avc1.PPCCLL: profile_idc, constraint flags, level_idc) from the SPS, null without an SPS
function getCodecString(data) {
    const hex = (value) => value.toString(16).toUpperCase().padStart(2, '0');
    for (let i = 0; i + 6 < data.length; i++) {
        if ((data[i] === 0) && (data[i + 1] === 0) && (data[i + 2] === 1)) {
            if ((data[i + 3] & 0x1f) === 7) {
                return `avc1.${hex(data[i + 4])}${hex(data[i + 5])}${hex(data[i + 6])}`;
            }
            i += 2;
        }
    }
    return null;
}

function createDecoder(view) {
    return new VideoDecoder({
        output: (frame) => {
            if (view.canvas !== null) {
                draw(view, frame, frame.displayWidth, frame.displayHeight);
            }
            frame.close();
        },
        error: (e) => {
            // A configuration the browser cannot decode does not change with a key frame
            if (e.name === 'NotSupportedError') {
                fallBack(view, e);
                return;
            }

            // The decoder is closed, created again with the next key frame
            console.warn('H.264 decode failed.', e);
            view.decoder = null;
            view.codec = null;
            requestKeyFrame(view);
        }
    });
}

function receive(view, data) {
    const header = new DataView(data);
    if (header.getUint16(0, true) !== HEADER_VERSION) {
        return;
    }

    const flags = header.getUint32(12, true);
    const frame = {
        width: header.getUint16(4, true),
        height: header.getUint16(6, true),
        keyFrame: (flags & KEY_FRAME_FLAG) !== 0,
        fullRange: (flags & FULL_RANGE_FLAG) !== 0,
        data: new Uint8Array(data, header.getUint16(2, true))
    };
    view.stats.received++;
    view.stats.bytes += data.byteLength;
    if (frame.keyFrame) {
        view.stats.keyFrames++;
    }

    decode(view, frame);
}

function decode(view, frame) {
    if (frame.keyFrame) {
        // The key frame has the SPS, the decoder is configured again when the stream changes
        const codec = getCodecString(frame.data);
        if ((codec !== null) &&
            ((view.decoder === null) || (codec !== view.codec) || (frame.width !== view.width) || (frame.height !== view.height) || (frame.fullRange !== view.fullRange))) {
            try {
                view.decoder ??= createDecoder(view);
                // Annex B (no description), the frames are output in the decode order.
                // The colors are given because the software encoder writes the range without the colour description,
                // which a decoder ignores (the YUV is BT.601 as the JPEG, the primaries and the transfer are BT.709 / sRGB).
                // The browser decides on the hardware decoding ('prefer-hardware' fails where it is not available).
                view.decoder.configure({
                    codec,
                    codedWidth: frame.width,
                    codedHeight: frame.height,
                    optimizeForLatency: true,
                    colorSpace: { primaries: 'bt709', transfer: 'bt709', matrix: 'bt470bg', fullRange: frame.fullRange }
                });
                view.codec = codec;
                view.width = frame.width;
                view.height = frame.height;
                view.fullRange = frame.fullRange;
                view.stats.codec = codec;
            } catch (e) {
                // An invalid configuration does not change with a key frame either
                fallBack(view, e);
                return;
            }
        }
        if (view.decoder !== null) {
            view.waitingKeyFrame = false;
        }
    } else if (!view.waitingKeyFrame && (view.decoder !== null) && (view.decoder.decodeQueueSize >= MAX_DECODE_QUEUE)) {
        // The decoder is slower than the stream
        requestKeyFrame(view);
    }

    if ((view.decoder === null) || view.waitingKeyFrame) {
        view.stats.dropped++;
        requestKeyFrame(view);
        return;
    }

    try {
        view.decoder.decode(new EncodedVideoChunk({ type: frame.keyFrame ? 'key' : 'delta', timestamp: view.nextTimestamp++, data: frame.data }));
    } catch (e) {
        console.warn('H.264 decode failed.', e);
        resetDecoder(view);
        requestKeyFrame(view);
    }
}
