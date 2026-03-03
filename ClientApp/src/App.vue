<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { RefreshCw, PlayCircle } from 'lucide-vue-next';
import VideoPlayer from './components/VideoPlayer.vue';
import request from 'umi-request';

interface Platform {
  address: string;
  xinimg: string;
  title: string;
  Number: string;
}

interface Streamer {
  address: string;
  img: string;
  title: string;
}

const platforms = ref<Platform[]>([]);
const streamers = ref<Streamer[]>([]);

const selectedPlatform = ref<Platform | null>(null);
const selectedStreamer = ref<Streamer | null>(null);

const loadingPlatforms = ref(false);
const loadingStreamers = ref(false);
const error = ref<string | null>(null);

const currentIndex = computed(() => {
  if (!selectedStreamer.value || streamers.value.length === 0) return -1;
  return streamers.value.findIndex(s => s.address === selectedStreamer.value?.address);
});

const hasNext = computed(() => currentIndex.value >= 0 && currentIndex.value < streamers.value.length - 1);
const hasPrev = computed(() => currentIndex.value > 0);

const playNext = () => {
  if (hasNext.value) {
    selectedStreamer.value = streamers.value[currentIndex.value + 1] || null;
  }
};

const playPrev = () => {
  if (hasPrev.value) {
    selectedStreamer.value = streamers.value[currentIndex.value - 1] || null;
  }
};

const fetchPlatforms = async () => {
  loadingPlatforms.value = true;
  error.value = null;
  try {
    const res = await request.get('/api/channels');
    if (res && res.pingtai) {
      platforms.value = res.pingtai;
    }
  } catch (err: any) {
    console.error(err);
    error.value = 'Failed to load platforms. This could be due to a backend issue.';
  } finally {
    loadingPlatforms.value = false;
  }
};

const fetchStreamers = async (platform: Platform) => {
  selectedPlatform.value = platform;
  loadingStreamers.value = true;
  streamers.value = [];

  try {
    // Fetch via the new proxy route we planned
    const res = await request.get(`/api/channels/${platform.address}`);
    if (res && res.zhubo) {
      streamers.value = res.zhubo;
    }
  } catch (err) {
    console.error(err);
    error.value = 'Failed to load streamers for this platform.';
  } finally {
    loadingStreamers.value = false;
  }
};

onMounted(() => {
  fetchPlatforms();
});

const handleRefresh = () => {
  if (selectedPlatform.value) {
    fetchStreamers(selectedPlatform.value);
  } else {
    fetchPlatforms();
  }
};

const handleImgError = (e: Event, type: 'platform' | 'streamer') => {
  const target = e.target as HTMLImageElement;
  if (type === 'platform') {
    target.src = 'https://via.placeholder.com/40?text=No+Img';
  } else {
    target.src = 'https://via.placeholder.com/300x400?text=Stream+Offline';
  }
};
</script>

<template>
  <div class="flex h-screen w-full bg-slate-900 text-slate-100 overflow-hidden font-sans">

    <!-- Sidebar: Platforms -->
    <div class="w-72 bg-slate-950 border-r border-slate-800 flex flex-col shrink-0 shadow-xl overflow-hidden z-20">
      <div class="p-5 border-b border-slate-800 flex items-center justify-between bg-slate-950">
        <h1 class="text-xl font-bold bg-gradient-to-r from-blue-400 to-emerald-400 bg-clip-text text-transparent">
          LiveHub</h1>
        <button @click="handleRefresh"
          class="p-2 rounded-full hover:bg-slate-800 text-slate-400 hover:text-white transition-colors focus:outline-none"
          title="Refresh current view">
          <RefreshCw :size="18" :class="{ 'animate-spin': loadingPlatforms || loadingStreamers }" />
        </button>
      </div>

      <div class="flex-1 overflow-y-auto w-full custom-scrollbar p-3 space-y-2">
        <div v-if="loadingPlatforms" class="text-center p-6 text-slate-500 animate-pulse">Loading platforms...</div>

        <template v-else-if="platforms.length > 0">
          <button v-for="(p, idx) in platforms" :key="`${p.address}-${idx}`" @click="fetchStreamers(p)" :class="[
            'w-full text-left flex items-center gap-3 p-3 rounded-xl transition-all duration-200 border',
            selectedPlatform?.address === p.address
              ? 'bg-blue-600/20 border-blue-500 shadow-[0_0_15px_rgba(59,130,246,0.2)]'
              : 'bg-slate-900 border-transparent hover:bg-slate-800 hover:border-slate-700'
          ]">
            <img :src="p.xinimg" :alt="p.title"
              class="w-10 h-10 rounded-full object-cover bg-slate-800 border-2 border-slate-700"
              @error="(e) => handleImgError(e, 'platform')" />
            <div class="flex-1 min-w-0">
              <h3 class="text-sm font-semibold truncate text-slate-200">{{ p.title }}</h3>
              <p class="text-xs text-slate-500 truncate">{{ p.Number }} Online</p>
            </div>
          </button>
        </template>

        <div v-else class="text-center p-6 text-slate-500 text-sm">
          {{ error || 'No platforms available' }}
        </div>
      </div>
    </div>

    <!-- Main Content Areas -->
    <div class="flex-1 flex flex-col h-full relative">
      <div class="absolute inset-0 bg-gradient-to-br from-blue-900/10 to-emerald-900/10 pointer-events-none" />

      <!-- Top: Video Player (Expand if selectedStreamer exists) -->
      <div
        :class="[selectedStreamer ? 'flex-[3] min-h-[500px]' : 'flex-1 min-h-[300px]', 'p-6 pb-3 transition-all duration-300']">
        <VideoPlayer :url="selectedStreamer?.address || ''" :title="selectedStreamer?.title || ''" @next="playNext"
          @prev="playPrev" :has-next="hasNext" :has-prev="hasPrev" />
      </div>

      <!-- Bottom: Streamers Grid (Shrink if selectedStreamer exists) -->
      <div
        :class="[selectedStreamer ? 'flex-1 min-h-[200px]' : 'flex-1', 'p-6 pt-3 flex flex-col overflow-hidden transition-all duration-300']">
        <div v-if="loadingStreamers" class="flex-1 flex items-center justify-center">
          <div class="flex flex-col items-center gap-4 text-slate-400 animate-pulse">
            <div
              class="w-8 h-8 rounded-full border-4 border-t-blue-500 border-r-blue-500 border-b-transparent border-l-transparent animate-spin">
            </div>
            <p>Loading streamers...</p>
          </div>
        </div>

        <div v-else
          class="flex-1 overflow-y-auto custom-scrollbar bg-slate-900/50 backdrop-blur-md rounded-2xl border border-slate-800/80 shadow-inner p-5">
          <div v-if="!selectedPlatform"
            class="h-full flex items-center justify-center text-slate-500 text-lg flex-col gap-4">
            <PlayCircle :size="48" class="opacity-20" />
            <p>Select a platform to discover creators</p>
          </div>

          <div v-else-if="streamers.length > 0"
            class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-5">
            <div v-for="(s, idx) in streamers" :key="`${s.address}-${idx}`" @click="selectedStreamer = s" :class="[
              'group cursor-pointer rounded-xl overflow-hidden bg-slate-800 transition-all hover:-translate-y-1 hover:shadow-xl hover:shadow-blue-500/20 border-2',
              selectedStreamer?.address === s.address
                ? 'border-emerald-500 ring-4 ring-emerald-500/20'
                : 'border-slate-700 hover:border-slate-500'
            ]">
              <div class="aspect-[3/4] overflow-hidden relative">
                <img :src="s.img" :alt="s.title"
                  class="w-full h-full object-cover transition-transform duration-500 group-hover:scale-110"
                  @error="(e) => handleImgError(e, 'streamer')" />
                <div
                  class="absolute inset-0 bg-gradient-to-t from-black/80 via-black/20 to-transparent opacity-60 group-hover:opacity-80 transition-opacity" />

                <!-- Play Overlay -->
                <div
                  class="absolute inset-0 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity duration-300">
                  <div
                    class="bg-white/20 backdrop-blur-sm p-3 rounded-full text-white shadow-lg transform translate-y-4 group-hover:translate-y-0 transition-transform">
                    <PlayCircle :size="32" />
                  </div>
                </div>
              </div>
              <div class="p-3 text-sm truncate text-center text-slate-300 font-medium group-hover:text-white">
                {{ s.title }}
              </div>
            </div>
          </div>

          <div v-else class="h-full flex items-center justify-center text-slate-500">
            No active streams found.
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
