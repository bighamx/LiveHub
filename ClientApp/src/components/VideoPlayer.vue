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
            const startHls = (playUrl: string, isProxyRetry: boolean = false) => {
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
                            if (!isProxyRetry && data.type === Hls.ErrorTypes.NETWORK_ERROR) {
                                console.warn('HLS Network Error detected, attempting CORS proxy...', data);
                                hls.destroy();
                                const proxyUrl = `/api/stream/proxym3u8?url=${encodeURIComponent(props.url)}`;
                                startHls(proxyUrl, true);
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
                        if (!isProxyRetry) {
                            console.warn('Native HLS Error detected, attempting CORS proxy...');
                            videoRef.value!.removeEventListener('loadedmetadata', onLoadedMetadata);
                            videoRef.value!.removeEventListener('error', onError);
                            const proxyUrl = `/api/stream/proxym3u8?url=${encodeURIComponent(props.url)}`;
                            startHls(proxyUrl, true);
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
                const startFlv = (playUrl: string, isProxyRetry: boolean = false) => {
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
                        if (!isProxyRetry && errType === flvjs.ErrorTypes.NETWORK_ERROR) {
                            console.warn('FLV Network Error detected, attempting CORS proxy...', errDetail);
                            const proxyUrl = `/api/stream/proxyflv?flvUrl=${encodeURIComponent(props.url)}`;
                            startFlv(proxyUrl, true);
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
    <div class="flex flex-col h-full w-full bg-black rounded-xl overflow-hidden shadow-2xl relative group">
        <!-- Title overlay -->
        <div
            class="absolute top-0 left-0 w-full p-4 bg-gradient-to-b from-black/80 to-transparent z-10 flex items-center justify-between opacity-0 group-hover:opacity-100 transition-opacity duration-300">
            <h2 class="text-white text-lg font-semibold truncate drop-shadow-md">{{ props.title || 'Live Stream Viewer'
                }}</h2>

            <!-- Fullscreen Button -->
            <button @click="toggleFullScreen"
                class="p-2 rounded-full bg-black/40 hover:bg-white/20 text-white backdrop-blur-sm transition-all shadow-lg"
                title="Toggle Fullscreen">
                <Maximize v-if="!isFullScreen" :size="20" />
                <Minimize v-else :size="20" />
            </button>
        </div>

        <!-- Navigation Controls (Left/Right) -->
        <div
            class="absolute inset-y-0 left-0 flex items-center px-4 z-10 opacity-0 group-hover:opacity-100 transition-opacity duration-300 pointer-events-none">
            <button v-if="props.hasPrev" @click.stop="emit('prev')"
                class="pointer-events-auto p-3 rounded-full bg-black/40 hover:bg-white/20 text-white backdrop-blur-sm transition-transform hover:scale-110 shadow-lg"
                title="Previous Stream">
                <ChevronLeft :size="32" />
            </button>
        </div>

        <div
            class="absolute inset-y-0 right-0 flex items-center px-4 z-10 opacity-0 group-hover:opacity-100 transition-opacity duration-300 pointer-events-none">
            <button v-if="props.hasNext" @click.stop="emit('next')"
                class="pointer-events-auto p-3 rounded-full bg-black/40 hover:bg-white/20 text-white backdrop-blur-sm transition-transform hover:scale-110 shadow-lg"
                title="Next Stream">
                <ChevronRight :size="32" />
            </button>
        </div>

        <div class="flex-1 relative bg-black flex items-center justify-center min-h-0">
            <div v-if="error"
                class="absolute inset-0 flex flex-col items-center justify-center bg-zinc-900 z-20 p-6 text-center">
                <div class="text-red-500 mb-2">
                    <AlertCircle :size="48" />
                </div>
                <p class="text-red-400 font-medium">{{ error }}</p>
                <p class="text-zinc-500 text-sm w-full mt-4 break-words">URL: {{ props.url }}</p>
            </div>

            <div v-else-if="!props.url" class="text-zinc-600 flex flex-col items-center">
                <MonitorPlay :size="64" class="mb-4 opacity-50" />
                <p>Select a stream to start watching</p>
            </div>

            <!-- Use absolute inset-0 so natural video size doesn't stretch flex items out of bounds -->
            <video ref="videoRef" controls autoplay class="absolute inset-0 w-full h-full object-contain"
                v-show="props.url && !error"></video>
        </div>
    </div>
</template>
