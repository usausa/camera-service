// Live views by key, kept here so that a stream can be stopped after Blazor removed the element.
// H.264 (WebSocket + WebCodecs) where the browser can decode it (WebCodecs needs localhost or HTTPS), otherwise MJPEG.

// Header of an H.264 message (little endian), see H264Header.cs
const HEADER_VERSION = 1;

const KEY_FRAME_FLAG = 0x8;

// Full range with the BT.601 matrix (otherwise video range)
const FULL_RANGE_FLAG = 0x10;

// The decoder is slower than the stream when more frames wait, the frames are dropped until a key frame
const MAX_DECODE_QUEUE = 3;

// The server ignores the requests within 500 ms
const KEY_FRAME_REQUEST_INTERVAL = 500;

const RECONNECT_DELAY = 1000;

const views = new Map();

// h264Url: null when H.264 is disabled
export function start(container, key, mjpegUrl, h264Url) {
    stop(key);
    const view = {
        container,
        mjpegUrl,
        h264Url,
        image: null,
        canvas: null,
        context: null,
        label: document.createElement('span'),
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
        stats: { mode: '', received: 0, bytes: 0, keyFrames: 0, drawn: 0, dropped: 0, codec: null }
    };
    view.label.className = 'live-mode';
    container.appendChild(view.label);
    views.set(key, view);

    if ((h264Url !== null) && window.isSecureContext && (typeof VideoDecoder !== 'undefined')) {
        startH264(view);
    } else {
        startMjpeg(view);
    }
}

// Removing the source stops the request (an image only removed from the page may keep loading)
export function stop(key) {
    const view = views.get(key);
    if (view === undefined) {
        return;
    }

    views.delete(key);
    view.stopped = true;
    stopH264(view);
    if (view.image !== null) {
        view.image.removeAttribute('src');
        view.image.remove();
        view.image = null;
    }
    view.label.remove();
}

// Counters of the views (for checking the views)
export function getStats() {
    return [...views.values()].map((x) => ({ ...x.stats, width: x.width, height: x.height }));
}

//--------------------------------------------------------------------------------
// MJPEG
//--------------------------------------------------------------------------------

function startMjpeg(view) {
    const image = document.createElement('img');
    image.className = 'live-image';
    image.alt = 'Live';
    image.src = view.mjpegUrl;
    view.container.appendChild(image);
    view.image = image;
    setMode(view, 'MJPEG');
}

function setMode(view, mode) {
    view.label.textContent = mode;
    view.stats.mode = mode;
}

//--------------------------------------------------------------------------------
// H.264
//--------------------------------------------------------------------------------

function startH264(view) {
    view.canvas = document.createElement('canvas');
    view.canvas.className = 'live-image';
    view.context = view.canvas.getContext('2d');
    view.container.appendChild(view.canvas);
    setMode(view, 'H.264');
    openSocket(view);
}

function stopH264(view) {
    closeSocket(view);
    resetDecoder(view);
    if (view.canvas !== null) {
        view.canvas.remove();
        view.canvas = null;
        view.context = null;
    }
}

// The browser cannot decode the stream: MJPEG instead (the connection is closed, so the server stops encoding)
function fallBack(view, e) {
    console.warn('H.264 is not supported, MJPEG is used.', e);
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
            receive(view, e.data);
        }
    };
    socket.onclose = () => {
        if (view.socket !== socket) {
            return;
        }

        // The server sends the frames from a key frame after the reconnection
        view.socket = null;
        resetDecoder(view);
        setTimeout(() => {
            if (!view.stopped && (view.canvas !== null) && (view.socket === null)) {
                openSocket(view);
            }
        }, RECONNECT_DELAY);
    };
    view.socket = socket;
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
                if ((view.canvas.width !== frame.displayWidth) || (view.canvas.height !== frame.displayHeight)) {
                    view.canvas.width = frame.displayWidth;
                    view.canvas.height = frame.displayHeight;
                }
                view.context.drawImage(frame, 0, 0);
                view.stats.drawn++;
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
