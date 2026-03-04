<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue';
import { RefreshCw, PlayCircle, Star, History, Menu, MonitorPlay } from 'lucide-vue-next';
import VideoPlayer from './components/VideoPlayer.vue';
import request from 'umi-request';

interface Platform {
  address: string;
  xinimg: string;
  title: string;
  Number: string | number;
}

interface Streamer {
  address: string;
  img: string;
  title: string;
  platformAddress?: string; // added to track from which platform it came
  displayName?: string;
  timestamp?: number;
}

interface HistoryItem {
  streamer: Streamer;
  platform: Platform;
  timestamp: number;
}

const platforms = ref<Platform[]>([]);
const streamers = ref<Streamer[]>([]);

const selectedPlatform = ref<Platform | { address: string, title: string, isVirtual: boolean } | null>(null);
const selectedStreamer = ref<Streamer | null>(null);

const loadingPlatforms = ref(false);
const loadingStreamers = ref(false);
const error = ref<string | null>(null);

// UI State
const isSidebarCollapsed = ref(false);

// LocalStorage States
const favoritePlatforms = ref<string[]>(JSON.parse(localStorage.getItem('livehub_fav_platforms') || '[]'));
const favoriteStreamers = ref<Streamer[]>(JSON.parse(localStorage.getItem('livehub_fav_streamers') || '[]'));
const watchHistory = ref<HistoryItem[]>(JSON.parse(localStorage.getItem('livehub_history') || '[]'));

// Watchers for LocalStorage
watch(favoritePlatforms, (newVal) => {
  localStorage.setItem('livehub_fav_platforms', JSON.stringify(newVal));
}, { deep: true });

watch(favoriteStreamers, (newVal) => {
  localStorage.setItem('livehub_fav_streamers', JSON.stringify(newVal));
}, { deep: true });

watch(watchHistory, (newVal) => {
  localStorage.setItem('livehub_history', JSON.stringify(newVal));
}, { deep: true });

const togglePlatformFavorite = (platformAddress: string, e: Event) => {
  e.stopPropagation();
  if (favoritePlatforms.value.includes(platformAddress)) {
    favoritePlatforms.value = favoritePlatforms.value.filter(a => a !== platformAddress);
  } else {
    favoritePlatforms.value.push(platformAddress);
  }
};

const toggleStreamerFavorite = (streamer: Streamer, e: Event) => {
  e.stopPropagation();
  const exists = favoriteStreamers.value.find(s => s.address === streamer.address);
  if (exists) {
    favoriteStreamers.value = favoriteStreamers.value.filter(s => s.address !== streamer.address);
  } else {
    // Save along with currently selected platform address if possible
    const sToSave = { ...streamer, platformAddress: streamer.platformAddress || (selectedPlatform.value && !('isVirtual' in selectedPlatform.value) ? selectedPlatform.value.address : undefined) };
    favoriteStreamers.value.push(sToSave);
  }
};

const isPlatformFav = (address: string) => favoritePlatforms.value.includes(address);
const isStreamerFav = (address: string) => !!favoriteStreamers.value.find(s => s.address === address);

// Computed platforms (Favorites on top)
const sortedPlatforms = computed(() => {
  const favs = platforms.value.filter(p => isPlatformFav(p.address));
  const others = platforms.value.filter(p => !isPlatformFav(p.address));
  return [...favs, ...others];
});

const currentIndex = computed(() => {
  if (!selectedStreamer.value || streamers.value.length === 0) return -1;
  return streamers.value.findIndex(s => s.address === selectedStreamer.value?.address);
});

const hasNext = computed(() => currentIndex.value >= 0 && currentIndex.value < streamers.value.length - 1);
const hasPrev = computed(() => currentIndex.value > 0);

const playNext = () => {
  if (hasNext.value) {
    const nextS = streamers.value[currentIndex.value + 1];
    if (nextS) selectStreamer(nextS);
  }
};

const playPrev = () => {
  if (hasPrev.value) {
    const prevS = streamers.value[currentIndex.value - 1];
    if (prevS) selectStreamer(prevS);
  }
};

const selectStreamer = (streamer: Streamer) => {
  selectedStreamer.value = streamer;

  // Add to history
  if (selectedPlatform.value) {
    // If we are currently in Favorites or History view, we need the original platform
    let p: Platform | undefined;
    if ('isVirtual' in selectedPlatform.value) {
      // Try to find the platform from the streamer's saved platformAddress
      p = platforms.value.find(pl => pl.address === streamer.platformAddress);
    } else {
      p = selectedPlatform.value as Platform;
    }

    if (p) {
      addToHistory(streamer, p);
    }
  }
};

const addToHistory = (streamer: Streamer, platform: Platform) => {
  let history = [...watchHistory.value];
  // Remove if already exists to move to top
  history = history.filter(h => h.streamer.address !== streamer.address);
  history.unshift({
    streamer: { ...streamer, platformAddress: platform.address },
    platform,
    timestamp: Date.now()
  });
  // Keep only last 80
  if (history.length > 80) history = history.slice(0, 80);
  watchHistory.value = history;
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
    const res = await request.get(`/api/channels/${platform.address}`);
    if (res && res.zhubo) {
      // tag streamers with platform info
      streamers.value = res.zhubo.map((z: any) => ({ ...z, platformAddress: platform.address }));
    }
  } catch (err) {
    console.error(err);
    error.value = 'Failed to load streamers for this platform.';
  } finally {
    loadingStreamers.value = false;
  }
};

const showFavorites = () => {
  selectedPlatform.value = { address: 'favs', title: 'My Favorites', isVirtual: true };
  streamers.value = favoriteStreamers.value;
};

const showHistory = () => {
  selectedPlatform.value = { address: 'history', title: 'Watch History', isVirtual: true };
  // Map history to match Streamer list view
  streamers.value = watchHistory.value.map(h => ({
    ...h.streamer,
    displayName: `[${h.platform.title}] ${h.streamer.title}`,
    timestamp: h.timestamp
  }));
};

onMounted(() => {
  fetchPlatforms();
});

const handleRefresh = () => {
  if (selectedPlatform.value && !('isVirtual' in selectedPlatform.value)) {
    fetchStreamers(selectedPlatform.value as Platform);
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

// Helper for display
const formatTime = (ts: number) => {
  const d = new Date(ts);
  return `${d.getMonth() + 1}-${d.getDate()} ${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
};

</script>

<template>
  <div class="flex h-screen w-full bg-slate-900 text-slate-100 overflow-hidden font-sans">

    <!-- Sidebar: Platforms -->
    <div
      :class="['bg-slate-950 border-r border-slate-800 flex flex-col shrink-0 shadow-xl overflow-hidden z-20 transition-all duration-300', isSidebarCollapsed ? 'w-[72px]' : 'w-72']">
      <div class="p-4 border-b border-slate-800 flex items-center justify-between bg-slate-950 h-[68px]">
        <h1 v-if="!isSidebarCollapsed"
          class="text-xl font-bold bg-gradient-to-r from-blue-400 to-emerald-400 bg-clip-text text-transparent whitespace-nowrap">
          LiveHub
        </h1>
        <div class="flex items-center gap-1" :class="{ 'w-full justify-center': isSidebarCollapsed }">
          <button v-if="!isSidebarCollapsed" @click="handleRefresh"
            class="p-2 rounded-full hover:bg-slate-800 text-slate-400 hover:text-white transition-colors focus:outline-none shrink-0"
            title="Refresh current view">
            <RefreshCw :size="18" :class="{ 'animate-spin': loadingPlatforms || loadingStreamers }" />
          </button>
          <button @click="isSidebarCollapsed = !isSidebarCollapsed"
            class="p-2 rounded-full hover:bg-slate-800 text-slate-400 hover:text-white transition-colors focus:outline-none shrink-0"
            title="Toggle Sidebar">
            <Menu :size="18" />
          </button>
        </div>
      </div>

      <div class="flex-1 overflow-y-auto w-full custom-scrollbar p-3 space-y-1.5">

        <!-- Virtual Tabs -->
        <button @click="showFavorites" :class="[
          'w-full flex items-center p-2 rounded-xl transition-all duration-200 border group',
          selectedPlatform?.address === 'favs'
            ? 'bg-blue-600/20 border-blue-500 shadow-[0_0_15px_rgba(59,130,246,0.2)] text-blue-400'
            : 'bg-slate-900/50 border-transparent hover:bg-slate-800 text-slate-300 hover:text-white'
        ]" title="My Favorites">
          <div class="w-9 h-9 rounded-[10px] flex items-center justify-center shrink-0"
            :class="selectedPlatform?.address === 'favs' ? 'bg-blue-500/20 text-blue-400' : 'bg-slate-800 group-hover:bg-slate-700'">
            <Star :size="18" :class="{ 'fill-current': selectedPlatform?.address === 'favs' }" />
          </div>
          <span v-if="!isSidebarCollapsed" class="ml-3 text-sm font-semibold truncate">Favorites</span>
        </button>

        <button @click="showHistory" :class="[
          'w-full flex items-center p-2 rounded-xl transition-all duration-200 border group mb-5',
          selectedPlatform?.address === 'history'
            ? 'bg-blue-600/20 border-blue-500 shadow-[0_0_15px_rgba(59,130,246,0.2)] text-emerald-400'
            : 'bg-slate-900/50 border-transparent hover:bg-slate-800 text-slate-300 hover:text-white'
        ]" title="Watch History">
          <div class="w-9 h-9 rounded-[10px] flex items-center justify-center shrink-0"
            :class="selectedPlatform?.address === 'history' ? 'bg-emerald-500/20 text-emerald-400' : 'bg-slate-800 group-hover:bg-slate-700'">
            <History :size="18" />
          </div>
          <span v-if="!isSidebarCollapsed" class="ml-3 text-sm font-semibold truncate">History</span>
        </button>

        <div v-if="loadingPlatforms" class="text-center p-4 text-slate-500 animate-pulse text-sm">...</div>

        <template v-else-if="sortedPlatforms.length > 0">
          <div v-if="!isSidebarCollapsed"
            class="text-xs font-semibold text-slate-500 uppercase tracking-wider px-2 pt-2 pb-1">Platforms</div>
          <div v-else class="w-full h-px bg-slate-800 my-3"></div>

          <button v-for="(p, idx) in sortedPlatforms" :key="`${p.address}-${idx}`" @click="fetchStreamers(p)" :class="[
            'w-full flex items-center p-2 rounded-xl transition-all duration-200 border relative group',
            selectedPlatform?.address === p.address
              ? 'bg-indigo-600/20 border-indigo-500 shadow-[0_0_15px_rgba(99,102,241,0.2)]'
              : 'bg-transparent border-transparent hover:bg-slate-800'
          ]" :title="p.title">
            <div class="relative shrink-0">
              <img :src="p.xinimg" :alt="p.title"
                class="w-9 h-9 rounded-full object-cover bg-slate-800 border-2 border-slate-700"
                @error="(e) => handleImgError(e, 'platform')" />
              <div v-if="isPlatformFav(p.address)" class="absolute -bottom-1 -right-1 bg-slate-900 rounded-full p-0.5"
                title="Favorited">
                <Star class="text-yellow-400 fill-yellow-400" :size="10" />
              </div>
            </div>

            <div v-if="!isSidebarCollapsed" class="flex-1 min-w-0 ml-3 text-left">
              <h3 class="text-sm font-semibold truncate text-slate-200">{{ p.title }}</h3>
              <p class="text-xs text-slate-500 truncate">{{ p.Number }} Online</p>
            </div>

            <!-- Star Button -->
            <div v-if="!isSidebarCollapsed" @click="(e) => togglePlatformFavorite(p.address, e)"
              class="opacity-0 group-hover:opacity-100 p-1.5 rounded-full hover:bg-slate-700 transition-all ml-1 shrink-0"
              :class="{ 'opacity-100': isPlatformFav(p.address) }"
              :title="isPlatformFav(p.address) ? 'Remove Favorite' : 'Mark as Favorite'">
              <Star :size="14"
                :class="isPlatformFav(p.address) ? 'text-yellow-400 fill-yellow-400' : 'text-slate-400'" />
            </div>
          </button>
        </template>

        <div v-else class="text-center p-6 text-slate-500 text-sm">
          {{ error || 'No platforms' }}
        </div>
      </div>
    </div>

    <!-- Main Content Areas -->
    <div class="flex-1 flex flex-col h-full relative min-w-0">
      <div class="absolute inset-0 bg-gradient-to-br from-blue-900/10 to-emerald-900/10 pointer-events-none" />

      <!-- Top: Video Player -->
      <div
        :class="[selectedStreamer ? 'flex-[3]' : 'flex-1', 'p-4 sm:p-6 pb-2 transition-all duration-300 flex flex-col min-h-0']">
        <VideoPlayer :url="selectedStreamer?.address || ''" :title="selectedStreamer?.title || ''" @next="playNext"
          @prev="playPrev" :has-next="hasNext" :has-prev="hasPrev" />
      </div>

      <!-- Bottom: Streamers Grid -->
      <div
        :class="[selectedStreamer ? 'flex-[1.5]' : 'flex-[3]', 'p-4 sm:p-6 pt-2 flex flex-col overflow-hidden transition-all duration-300 min-h-0']">
        <div v-if="loadingStreamers" class="flex-1 flex items-center justify-center">
          <div class="flex flex-col items-center gap-4 text-slate-400 animate-pulse">
            <div
              class="w-8 h-8 rounded-full border-4 border-t-blue-500 border-r-blue-500 border-b-transparent border-l-transparent animate-spin">
            </div>
            <p>Loading...</p>
          </div>
        </div>

        <div v-else
          class="flex-1 overflow-y-auto custom-scrollbar bg-slate-900/50 backdrop-blur-md rounded-2xl border border-slate-800/80 shadow-inner p-4 sm:p-5">

          <!-- Header for special views -->
          <div v-if="selectedPlatform && ('isVirtual' in selectedPlatform)"
            class="mb-5 pb-3 border-b border-slate-800 flex items-center gap-3">
            <Star v-if="selectedPlatform.address === 'favs'" class="text-yellow-400 fill-yellow-400" :size="24" />
            <History v-else class="text-emerald-400" :size="24" />
            <h2 class="text-xl font-bold text-slate-200">{{ selectedPlatform.title }}</h2>
          </div>

          <div v-if="!selectedPlatform"
            class="h-full flex items-center justify-center text-slate-500 text-lg flex-col gap-4">
            <MonitorPlay :size="48" class="opacity-20" />
            <p>Select a platform to discover creators</p>
          </div>

          <div v-else-if="streamers.length > 0"
            class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-8 gap-4 sm:gap-5">
            <div v-for="(s, idx) in streamers" :key="`${s.address}-${idx}`" @click="selectStreamer(s)" :class="[
              'group relative cursor-pointer rounded-xl overflow-hidden bg-slate-800 transition-all hover:-translate-y-1 hover:shadow-xl hover:shadow-blue-500/20 border-2 flex flex-col',
              selectedStreamer?.address === s.address
                ? 'border-emerald-500 ring-4 ring-emerald-500/20'
                : 'border-slate-700 hover:border-slate-500'
            ]">
              <div class="aspect-[3/4] overflow-hidden relative">
                <img :src="s.img" :alt="s.title"
                  class="w-full h-full object-cover transition-transform duration-500 group-hover:scale-110"
                  @error="(e) => handleImgError(e, 'streamer')" />
                <div
                  class="absolute inset-0 bg-gradient-to-t from-black/90 via-black/20 to-transparent opacity-60 group-hover:opacity-80 transition-opacity" />

                <!-- Play Overlay -->
                <div
                  class="absolute inset-0 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity duration-300">
                  <div
                    class="bg-white/20 backdrop-blur-sm p-3 rounded-full text-white shadow-lg transform translate-y-4 group-hover:translate-y-0 transition-transform">
                    <PlayCircle :size="32" />
                  </div>
                </div>

                <!-- Favorite Button -->
                <button @click.stop="(e) => toggleStreamerFavorite(s, e)"
                  class="absolute top-2 right-2 p-1.5 rounded-full bg-black/40 backdrop-blur-sm hover:bg-black/60 transition-colors z-10"
                  :title="isStreamerFav(s.address) ? 'Remove from favorites' : 'Add to favorites'">
                  <Star :size="16"
                    :class="isStreamerFav(s.address) ? 'text-yellow-400 fill-yellow-400' : 'text-white'" />
                </button>
              </div>
              <div
                class="p-2.5 text-xs sm:text-sm truncate text-center text-slate-300 font-medium group-hover:text-white flex-1 flex flex-col justify-center">
                <span class="truncate block w-full">{{ (s as any).displayName || s.title }}</span>
                <span v-if="selectedPlatform.address === 'history' && (s as any).timestamp"
                  class="text-[10px] text-slate-500 block mt-0.5 font-normal tracking-wide text-left">
                  {{ formatTime((s as any).timestamp) }}
                </span>
              </div>
            </div>
          </div>

          <div v-else class="h-full flex items-center justify-center text-slate-500">
            <div v-if="selectedPlatform.address === 'favs'" class="flex flex-col items-center gap-3">
              <Star :size="48" class="opacity-20 text-yellow-400" />
              <p>No favorite streamers yet.</p>
              <p class="text-sm">Click the star icon on a streamer to add them!</p>
            </div>
            <div v-else-if="selectedPlatform.address === 'history'" class="flex flex-col items-center gap-3">
              <History :size="48" class="opacity-20 text-emerald-400" />
              <p>Your watch history is empty.</p>
              <p class="text-sm">Start watching some streams!</p>
            </div>
            <div v-else>No active streams found.</div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
