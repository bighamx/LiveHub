<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from 'vue';
import { Maximize, Minimize, ChevronLeft, ChevronRight, AlertCircle, MonitorPlay } from 'lucide-vue-next';
import Hls from 'hls.js';
import flvjs from 'flv.js';

const props = defineProps<{
    url: string;
    title: string;
    hasNext?: boolean;
    hasPrev?: boolean;
}>();

const emit = defineEmits<{
    (e: 'next'): void;
    (e: 'prev'): void;
}>();

const videoRef = ref<HTMLVideoElement | null>(null);
const error = ref<string | null>(null);
const isFullScreen = ref(false);

let flvPlayer: flvjs.Player | null = null;
let hlsPlayer: Hls | null = null;

const workerProxyBase = 'https://proxy.pengcube.workers.dev/';

const buildWorkerProxyUrl = (targetUrl: string) => {
    return `${workerProxyBase}${(targetUrl)}`;
};

const buildBackendM3u8ProxyUrl = (targetUrl: string) => {
    return `/api/stream/proxym3u8?url=${encodeURIComponent(targetUrl)}`;
};

const buildBackendFlvProxyUrl = (targetUrl: string) => {
    return `/api/stream/proxyflv?flvUrl=${encodeURIComponent(targetUrl)}`;
};

const toggleFullScreen = async () => {
    if (!videoRef.value) return;

    try {
        if (!document.fullscreenElement) {
            await videoRef.value.parentElement?.requestFullscreen();
            isFullScreen.value = true;
        } else {
            await document.exitFullscreen();
            isFullScreen.value = false;
        }
    } catch (err: any) {
        console.error('Error attempting to enable fullscreen:', err);
    }
};

// Listen for native full screen exits (e.g., pressing ESC)
onMounted(() => {
    document.addEventListener('fullscreenchange', () => {
        isFullScreen.value = !!document.fullscreenElement;
    });
});

const cleanupPlayers = () => {
    if (flvPlayer) {
        flvPlayer.destroy();
        flvPlayer = null;
    }
    if (hlsPlayer) {
        hlsPlayer.destroy();
        hlsPlayer = null;
    }
};

const initPlayer = () => {
    cleanupPlayers();
    error.value = null;

    if (!props.url || !videoRef.value) return;

    try {
        const lowerUrl = props.url.toLowerCase();

        // HLS (.m3u8)
        if (lowerUrl.includes('.m3u8')) {
            const startHls = (playUrl: string, retryStage: 'origin' | 'worker' | 'backend' = 'origin') => {
                if (hlsPlayer) {
                    hlsPlayer.destroy();
                    hlsPlayer = null;
                }

                if (Hls.isSupported()) {
                    const hls = new Hls({
                        enableWorker: true,
                        lowLatencyMode: true,
                    });
                    hlsPlayer = hls;
                    hls.loadSource(playUrl);
                    hls.attachMedia(videoRef.value!);

                    hls.on(Hls.Events.MANIFEST_PARSED, () => {
                        videoRef.value?.play().catch((e: any) => console.warn('Auto-play blocked', e));
                    });

                    hls.on(Hls.Events.ERROR, (_, data) => {
                        if (data.fatal) {
                            if (data.type === Hls.ErrorTypes.NETWORK_ERROR && retryStage !== 'backend') {
                                console.warn('HLS Network Error detected, retrying with proxy...', data);
                                hls.destroy();
                                if (retryStage === 'origin') {
                                    startHls(buildWorkerProxyUrl(props.url), 'worker');
                                } else {
                                    startHls(buildBackendM3u8ProxyUrl(props.url), 'backend');
                                }
                            } else {
                                error.value = 'HLS playback error: ' + data.type;
                            }
                        }
                    });
                }
                // Native HLS (Safari)
                else if (videoRef.value!.canPlayType('application/vnd.apple.mpegurl')) {
                    videoRef.value!.src = playUrl;

                    const onLoadedMetadata = () => {
                        videoRef.value?.play().catch((e: any) => console.warn('Auto-play blocked', e));
                    };

                    const onError = () => {
                        if (retryStage !== 'backend') {
                            console.warn('Native HLS Error detected, retrying with proxy...');
                            videoRef.value!.removeEventListener('loadedmetadata', onLoadedMetadata);
                            videoRef.value!.removeEventListener('error', onError);
                            if (retryStage === 'origin') {
                                startHls(buildWorkerProxyUrl(props.url), 'worker');
                            } else {
                                startHls(buildBackendM3u8ProxyUrl(props.url), 'backend');
                            }
                        } else {
                            error.value = 'HLS playback error (Native).';
                        }
                    };

                    videoRef.value!.addEventListener('loadedmetadata', onLoadedMetadata, { once: true });
                    videoRef.value!.addEventListener('error', onError, { once: true });
                } else {
                    error.value = 'HLS is not supported in this browser.';
                }
            };

            startHls(props.url);
        }
        // RTMP to FLV via C# backend proxy
        else if (lowerUrl.startsWith('rtmp://')) {
            if (flvjs.isSupported()) {
                const proxyUrl = `/api/stream?rtmpUrl=${encodeURIComponent(props.url)}`;
                flvPlayer = flvjs.createPlayer({
                    type: 'flv',
                    url: proxyUrl,
                    isLive: true,
                    cors: true
                });
                flvPlayer.attachMediaElement(videoRef.value);
                flvPlayer.load();
                try {
                    const playResult = flvPlayer.play() as any;
                    if (playResult && playResult.catch) {
                        playResult.catch((e: any) => console.warn('Auto-play blocked', e));
                    }
                } catch (e: any) {
                    console.warn('Auto-play blocked', e);
                }
                flvPlayer.on(flvjs.Events.ERROR, (errType, errDetail) => {
                    error.value = `FLV Error: ${errType} - ${errDetail}`;
                });
            } else {
                error.value = 'FLV is not supported in this browser.';
            }
        }
        // Real FLV
        else if (lowerUrl.includes('.flv')) {
            if (flvjs.isSupported()) {
                const startFlv = (playUrl: string, retryStage: 'origin' | 'worker' | 'backend' = 'origin') => {
                    if (flvPlayer) {
                        flvPlayer.destroy();
                    }
                    flvPlayer = flvjs.createPlayer({
                        type: 'flv',
                        url: playUrl,
                        isLive: true,
                        cors: true
                    });
                    flvPlayer.attachMediaElement(videoRef.value!);
                    flvPlayer.load();
                    try {
                        const playResult = flvPlayer.play() as any;
                        if (playResult && playResult.catch) {
                            playResult.catch((e: any) => console.warn('Auto-play blocked', e));
                        }
                    } catch (e: any) {
                        console.warn('Auto-play blocked', e);
                    }

                    flvPlayer.on(flvjs.Events.ERROR, (errType, errDetail) => {
                        if (errType === flvjs.ErrorTypes.NETWORK_ERROR && retryStage !== 'backend') {
                            console.warn('FLV Network Error detected, retrying with proxy...', errDetail);
                            if (retryStage === 'origin') {
                                startFlv(buildWorkerProxyUrl(props.url), 'worker');
                            } else {
                                startFlv(buildBackendFlvProxyUrl(props.url), 'backend');
                            }
                        } else {
                            error.value = `FLV Error: ${errType} - ${errDetail}`;
                        }
                    });
                };

                // Initial attempt with original URL
                startFlv(props.url);

            } else {
                error.value = 'FLV is not supported in this browser.';
            }
        }
        // MP4 or generic fallback
        else {
            videoRef.value.src = props.url;
            videoRef.value.play().catch((e: any) => console.warn('Auto-play blocked', e));
        }
    } catch (err: any) {
        error.value = err.message || 'Failed to initialize player';
    }
};

watch(() => props.url, () => {
    initPlayer();
});

onMounted(() => {
    if (props.url) {
        initPlayer();
    }
});

onUnmounted(() => {
    cleanupPlayers();
});
</script>

<template>
    <div class="vp-container">
        <!-- Title overlay -->
        <div class="vp-title-overlay">
            <h2 class="vp-title">{{ props.title || 'Live Stream Viewer' }}</h2>
            <button @click="toggleFullScreen" class="vp-btn" title="Toggle Fullscreen">
                <Maximize v-if="!isFullScreen" :size="20" />
                <Minimize v-else :size="20" />
            </button>
        </div>

        <!-- Navigation Controls (Left/Right) -->
        <div class="vp-nav left">
            <button v-if="props.hasPrev" @click.stop="emit('prev')" class="vp-btn large nav-btn"
                title="Previous Stream">
                <ChevronLeft :size="32" />
            </button>
        </div>

        <div class="vp-nav right">
            <button v-if="props.hasNext" @click.stop="emit('next')" class="vp-btn large nav-btn" title="Next Stream">
                <ChevronRight :size="32" />
            </button>
        </div>

        <div class="vp-wrapper">
            <div v-if="error" class="vp-error-screen">
                <div class="vp-error-icon">
                    <AlertCircle :size="48" />
                </div>
                <p class="vp-error-text">{{ error }}</p>
                <p class="vp-error-url">URL: {{ props.url }}</p>
            </div>

            <div v-else-if="!props.url" class="vp-empty-screen">
                <MonitorPlay :size="64" class="vp-empty-icon" />
                <p>Select a stream to start watching</p>
            </div>

            <video ref="videoRef" controls autoplay class="vp-video" v-show="props.url && !error"></video>
        </div>
    </div>
</template>

<style scoped lang="scss">
.vp-container {
    display: flex;
    flex-direction: column;
    height: 100%;
    width: 100%;
    background-color: #000;
    border-radius: 0.75rem;
    overflow: hidden;
    box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.25);
    position: relative;

    &:hover {

        .vp-title-overlay,
        .vp-nav {
            opacity: 1;
        }
    }
}

.vp-title-overlay {
    position: absolute;
    top: 0;
    left: 0;
    width: 100%;
    padding: 1rem;
    background: linear-gradient(to bottom, rgba(0, 0, 0, 0.8), transparent);
    z-index: 10;
    display: flex;
    align-items: center;
    justify-content: space-between;
    opacity: 0;
    box-sizing: border-box;
    transition: opacity 0.3s;
}

.vp-title {
    color: #fff;
    font-size: 1.125rem;
    font-weight: 600;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    text-shadow: 0 1px 2px rgba(0, 0, 0, 0.4);
    margin: 0;
}

.vp-btn {
    padding: 0.5rem;
    border-radius: 9999px;
    background-color: rgba(0, 0, 0, 0.4);
    color: #fff;
    border: none;
    cursor: pointer;
    backdrop-filter: blur(4px);
    transition: all 0.2s;
    box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
    display: flex;
    align-items: center;
    justify-content: center;

    &:hover {
        background-color: rgba(255, 255, 255, 0.2);
    }

    &.large {
        padding: 0.75rem;
    }

    &.nav-btn {
        pointer-events: auto;

        &:hover {
            transform: scale(1.1);
        }
    }
}

.vp-nav {
    position: absolute;
    top: 0;
    bottom: 0;
    display: flex;
    align-items: center;
    padding: 0 1rem;
    z-index: 10;
    opacity: 0;
    transition: opacity 0.3s;
    pointer-events: none;

    &.left {
        left: 0;
    }

    &.right {
        right: 0;
    }
}

.vp-wrapper {
    flex: 1;
    position: relative;
    background-color: #000;
    display: flex;
    align-items: center;
    justify-content: center;
    min-height: 0;
}

.vp-error-screen {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    background-color: #18181b;
    z-index: 20;
    padding: 1.5rem;
    text-align: center;
    box-sizing: border-box;

    .vp-error-icon {
        color: #ef4444;
        margin-bottom: 0.5rem;
    }

    .vp-error-text {
        color: #f87171;
        font-weight: 500;
        margin: 0;
    }

    .vp-error-url {
        color: #71717a;
        font-size: 0.875rem;
        width: 100%;
        margin-top: 1rem;
        word-break: break-all;
    }
}

.vp-empty-screen {
    color: #52525b;
    display: flex;
    flex-direction: column;
    align-items: center;

    .vp-empty-icon {
        margin-bottom: 1rem;
        opacity: 0.5;
    }
}

.vp-video {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    width: 100%;
    height: 100%;
    object-fit: contain;
}
</style>
