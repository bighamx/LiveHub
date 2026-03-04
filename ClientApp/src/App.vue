<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue';
import { RefreshCw, PlayCircle, Star, History, Menu, MonitorPlay } from 'lucide-vue-next';
import VideoPlayer from './components/VideoPlayer.vue';
import request from 'umi-request';

const workerProxyBase = 'https://proxy.xbyham.com/';
const isPagesDomain = window.location.hostname.includes('pages.dev');
const apiTargetBase = (import.meta.env.VITE_API_BASE as string | undefined)?.trim() || window.location.origin;

const buildApiUrl = (path: string) => {
  if (!isPagesDomain) {
    return path;
  }

  const pathParts = path.split('/').filter(Boolean);
  const lastSegment = pathParts.length > 0 ? pathParts[pathParts.length - 1] : '';
  const absoluteApiUrl = path == "/api/channels"
    ? `http://api.vipmisss.com:81/mf/json.txt`
    : `http://api.vipmisss.com:81/mf/${lastSegment}`;
  return `${workerProxyBase}${(absoluteApiUrl)}`;
};

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
const isSidebarCollapsed = ref(window.innerWidth < 768);

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

  if (window.innerWidth < 768) {
    isSidebarCollapsed.value = true;
  }

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
    const res = await request.get(buildApiUrl('/api/channels'));
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
    const res = await request.get(buildApiUrl(`/api/channels/${platform.address}`));
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

const handleImgError = (e: Event, _type: 'platform' | 'streamer') => {
  const target = e.target as HTMLImageElement;
  target.src = 'https://placehold.co/40?text=No+Img';
};

// Helper for display
const formatTime = (ts: number) => {
  const d = new Date(ts);
  return `${d.getMonth() + 1}-${d.getDate()} ${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`;
};

</script>

<template>
  <div class="app-container">

    <!-- Sidebar: Platforms -->
    <div class="sidebar" :class="{ 'sidebar-collapsed': isSidebarCollapsed }">
      <div class="sidebar-header">
        <h1 v-if="!isSidebarCollapsed" class="sidebar-title">LiveHub</h1>
        <div class="sidebar-actions" :class="{ 'collapsed-actions': isSidebarCollapsed }">
          <button v-if="!isSidebarCollapsed" @click="handleRefresh" class="icon-btn" title="Refresh current view">
            <RefreshCw :size="18" :class="{ 'spin': loadingPlatforms || loadingStreamers }" />
          </button>
          <button @click="isSidebarCollapsed = !isSidebarCollapsed" class="icon-btn" title="Toggle Sidebar">
            <Menu :size="18" />
          </button>
        </div>
      </div>

      <div class="sidebar-content custom-scrollbar">
        <!-- Virtual Tabs -->
        <button @click="showFavorites" class="nav-item group" :class="{
          'collapsed-item': isSidebarCollapsed,
          'active fav-active': selectedPlatform?.address === 'favs',
          'inactive': selectedPlatform?.address !== 'favs'
        }" title="My Favorites">
          <div class="nav-icon" :class="selectedPlatform?.address === 'favs' ? 'icon-fav-active' : 'icon-inactive'">
            <Star :size="18" :class="{ 'fill-current': selectedPlatform?.address === 'favs' }" />
          </div>
          <span v-if="!isSidebarCollapsed" class="nav-text">Favorites</span>
        </button>

        <button @click="showHistory" class="nav-item group mb-spacer" :class="{
          'collapsed-item': isSidebarCollapsed,
          'active hist-active': selectedPlatform?.address === 'history',
          'inactive': selectedPlatform?.address !== 'history'
        }" title="Watch History">
          <div class="nav-icon" :class="selectedPlatform?.address === 'history' ? 'icon-hist-active' : 'icon-inactive'">
            <History :size="18" />
          </div>
          <span v-if="!isSidebarCollapsed" class="nav-text">History</span>
        </button>

        <div v-if="loadingPlatforms" class="loading-text">...</div>

        <template v-else-if="sortedPlatforms.length > 0">
          <div v-if="!isSidebarCollapsed" class="section-title">Platforms</div>
          <div v-else class="divider"></div>

          <button v-for="(p, idx) in sortedPlatforms" :key="`${p.address}-${idx}`" @click="fetchStreamers(p)"
            class="nav-item platform-item group" :class="{
              'collapsed-item': isSidebarCollapsed,
              'platform-active': selectedPlatform?.address === p.address,
              'platform-inactive': selectedPlatform?.address !== p.address
            }" :title="p.title">
            <div class="platform-img-wrapper" :class="{ 'mb-small': isSidebarCollapsed }">
              <img :src="p.xinimg" :alt="p.title" class="platform-img" @error="(e) => handleImgError(e, 'platform')" />
              <div v-if="isPlatformFav(p.address)" class="fav-badge" title="Favorited">
                <Star class="fav-icon-small" :size="10" />
              </div>
            </div>

            <div v-if="!isSidebarCollapsed" class="platform-info">
              <h3 class="platform-name">{{ p.title }}</h3>
              <p class="platform-desc">{{ p.Number }} Online</p>
            </div>

            <!-- Star Button -->
            <div v-if="!isSidebarCollapsed" @click="(e) => togglePlatformFavorite(p.address, e)" class="fav-toggle-btn"
              :class="{ 'visible': isPlatformFav(p.address) }"
              :title="isPlatformFav(p.address) ? 'Remove Favorite' : 'Mark as Favorite'">
              <Star :size="14" :class="isPlatformFav(p.address) ? 'fav-icon-medium' : 'fav-icon-inactive'" />
            </div>
          </button>
        </template>

        <div v-else class="empty-text">
          {{ error || 'No platforms' }}
        </div>
      </div>
    </div>

    <!-- Main Content Areas -->
    <div class="main-area">
      <div class="main-bg-gradient"></div>

      <!-- Top: Video Player -->
      <div class="top-pane" :class="{ 'expanded-top': selectedStreamer, 'flex-1': !selectedStreamer }">
        <VideoPlayer :url="selectedStreamer?.address || ''" :title="selectedStreamer?.title || ''" @next="playNext"
          @prev="playPrev" :has-next="hasNext" :has-prev="hasPrev" />
      </div>

      <!-- Bottom: Streamers Grid -->
      <div class="bottom-pane" :class="{ 'shrunk-bottom': selectedStreamer, 'expanded-bottom': !selectedStreamer }">
        <div v-if="loadingStreamers" class="loading-container">
          <div class="spinner-wrapper">
            <div class="spinner"></div>
            <p>Loading...</p>
          </div>
        </div>

        <div v-else class="grid-container custom-scrollbar">
          <!-- Header for special views -->
          <div v-if="selectedPlatform && ('isVirtual' in selectedPlatform)" class="virtual-header">
            <Star v-if="selectedPlatform.address === 'favs'" class="fav-icon-large" :size="24" />
            <History v-else class="hist-icon-large" :size="24" />
            <h2 class="virtual-title">{{ selectedPlatform.title }}</h2>
          </div>

          <div v-if="!selectedPlatform" class="empty-state">
            <MonitorPlay :size="48" class="empty-icon" />
            <p>Select a platform to discover creators</p>
          </div>

          <div v-else-if="streamers.length > 0" class="streamer-grid">
            <div v-for="(s, idx) in streamers" :key="`${s.address}-${idx}`" @click="selectStreamer(s)"
              class="streamer-card group" :class="{
                'card-active': selectedStreamer?.address === s.address,
                'card-inactive': selectedStreamer?.address !== s.address
              }">
              <div class="card-img-wrapper">
                <img :src="s.img" :alt="s.title" class="card-img" @error="(e) => handleImgError(e, 'streamer')" />
                <div class="card-overlay" />

                <!-- Play Overlay -->
                <div class="play-overlay">
                  <div class="play-btn">
                    <PlayCircle :size="32" />
                  </div>
                </div>

                <!-- Favorite Button -->
                <button @click.stop="(e) => toggleStreamerFavorite(s, e)" class="card-fav-btn"
                  :title="isStreamerFav(s.address) ? 'Remove from favorites' : 'Add to favorites'">
                  <Star :size="16" :class="isStreamerFav(s.address) ? 'fav-icon-medium' : 'text-white'" />
                </button>
              </div>
              <div class="card-info">
                <span class="card-name">{{ (s as any).displayName || s.title }}</span>
                <span v-if="selectedPlatform.address === 'history' && (s as any).timestamp" class="card-time">
                  {{ formatTime((s as any).timestamp) }}
                </span>
              </div>
            </div>
          </div>

          <div v-else class="empty-state">
            <div v-if="selectedPlatform.address === 'favs'" class="empty-favs">
              <Star :size="48" class="empty-icon fav-color" />
              <p>No favorite streamers yet.</p>
              <p class="empty-subtext">Click the star icon on a streamer to add them!</p>
            </div>
            <div v-else-if="selectedPlatform.address === 'history'" class="empty-hist">
              <History :size="48" class="empty-icon hist-color" />
              <p>Your watch history is empty.</p>
              <p class="empty-subtext">Start watching some streams!</p>
            </div>
            <div v-else>No active streams found.</div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
/* App Layout */
.app-container {
  display: flex;
  height: 100vh;
  width: 100%;
  background-color: #0f172a;
  color: #f1f5f9;
  overflow: hidden;
  font-family: ui-sans-serif, system-ui, sans-serif;
}

/* Sidebar */
.sidebar {
  background-color: #020617;
  border-right: 1px solid #1e293b;
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 8px 10px -6px rgba(0, 0, 0, 0.1);
  overflow: hidden;
  z-index: 20;
  transition: all 0.3s;
  width: 9rem;

  &.sidebar-collapsed {
    width: 60px;
  }
}

.sidebar-header {
  padding: 1rem;
  border-bottom: 1px solid #1e293b;
  display: flex;
  align-items: center;
  justify-content: space-between;
  background-color: #020617;
  height: 68px;
  box-sizing: border-box;
}

.sidebar-title {
  font-size: 1.25rem;
  font-weight: 700;
  background-image: linear-gradient(to right, #60a5fa, #34d399);
  -webkit-background-clip: text;
  background-clip: text;
  color: transparent;
  white-space: nowrap;
  margin: 0;
}

.sidebar-actions {
  display: flex;
  align-items: center;
  gap: 0.25rem;

  &.collapsed-actions {
    width: 100%;
    justify-content: center;
  }
}

.icon-btn {
  padding: 0.5rem;
  border-radius: 9999px;
  color: #94a3b8;
  background: transparent;
  border: none;
  cursor: pointer;
  transition: background-color 0.2s, color 0.2s;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;

  &:hover {
    background-color: #1e293b;
    color: #fff;
  }

  &:focus {
    outline: none;
  }
}

.spin {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from {
    transform: rotate(0deg);
  }

  to {
    transform: rotate(360deg);
  }
}

.sidebar-content {
  flex: 1;
  overflow-y: auto;
  width: 100%;
  padding: 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  box-sizing: border-box;
}

/* Nav items */
.nav-item {
  width: 100%;
  display: flex;
  align-items: center;
  padding: 0.5rem;
  border-radius: 0.75rem;
  transition: all 0.2s;
  border: 1px solid transparent;
  cursor: pointer;
  box-sizing: border-box;
  text-align: left;
  position: relative;
  background-color: transparent;
  color: inherit;

  &.platform-item {
    padding: 0.375rem;
  }

  &.collapsed-item {
    justify-content: center;
    flex-direction: column;
  }

  &.active {
    &.fav-active {
      background-color: rgba(37, 99, 235, 0.2);
      border-color: #3b82f6;
      box-shadow: 0 0 15px rgba(59, 130, 246, 0.2);
      color: #60a5fa;
    }

    &.hist-active {
      background-color: rgba(37, 99, 235, 0.2);
      border-color: #3b82f6;
      box-shadow: 0 0 15px rgba(59, 130, 246, 0.2);
      color: #34d399;
    }
  }

  &.inactive {
    background-color: rgba(15, 23, 42, 0.5);
    color: #cbd5e1;

    &:hover {
      background-color: #1e293b;
      color: #fff;
    }
  }

  &.platform-active {
    background-color: rgba(79, 70, 229, 0.2);
    border-color: #6366f1;
    box-shadow: 0 0 15px rgba(99, 102, 241, 0.2);
  }

  &.platform-inactive {
    background-color: transparent;

    &:hover {
      background-color: #1e293b;
    }
  }

  &:hover .nav-icon.icon-inactive {
    background-color: #334155;
  }

  &:hover .fav-toggle-btn {
    opacity: 1;
  }
}

.mb-spacer {
  margin-bottom: 1.25rem;
}

.mb-small {
  margin-bottom: 0.25rem;
}

.nav-icon {
  width: 2.25rem;
  height: 2.25rem;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;

  &.icon-fav-active {
    background-color: rgba(59, 130, 246, 0.2);
    color: #60a5fa;
  }

  &.icon-hist-active {
    background-color: rgba(16, 185, 129, 0.2);
    color: #34d399;
  }

  &.icon-inactive {
    background-color: #1e293b;
  }
}

.nav-text {
  margin-left: 0.75rem;
  font-size: 0.875rem;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.loading-text {
  text-align: center;
  padding: 1rem;
  color: #64748b;
  font-size: 0.875rem;
  animation: pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
}

@keyframes pulse {

  0%,
  100% {
    opacity: 1;
  }

  50% {
    opacity: .5;
  }
}

.section-title {
  font-size: 0.75rem;
  font-weight: 600;
  color: #64748b;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 0.5rem 0.5rem 0.25rem;
}

.divider {
  width: 100%;
  height: 1px;
  background-color: #1e293b;
  margin: 0.75rem 0;
}

/* Platform Items Elements */
.platform-img-wrapper {
  position: relative;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}

.platform-img {
  width: 1.75rem;
  height: 1.75rem;
  min-width: 28px;
  min-height: 28px;
  max-width: 28px;
  max-height: 28px;
  border-radius: 9999px;
  object-fit: cover;
  background-color: #1e293b;
  border: 2px solid #334155;
  margin: 0 auto;
  aspect-ratio: 1 / 1;
}

.fav-badge {
  position: absolute;
  bottom: -0.25rem;
  right: -0.25rem;
  background-color: #0f172a;
  border-radius: 9999px;
  padding: 0.125rem;
  display: flex;
}

.fav-icon-small,
.fav-icon-medium,
.fav-icon-large {
  color: #facc15;
  fill: #facc15;
}

.fav-icon-inactive {
  color: #94a3b8;
  fill: transparent;
}

.hist-icon-large {
  color: #34d399;
}

.text-white {
  color: #fff;
}

.platform-info {
  flex: 1;
  min-width: 0;
  margin-left: 0.75rem;
  text-align: left;
}

.platform-name {
  font-size: 0.875rem;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: #e2e8f0;
  margin: 0;
}

.platform-desc {
  font-size: 0.75rem;
  color: #64748b;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  margin: 0;
}

.fav-toggle-btn {
  opacity: 0;
  padding: 0.375rem;
  border-radius: 9999px;
  transition: all 0.2s;
  margin-left: 0.25rem;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;

  &.visible {
    opacity: 1;
  }

  &:hover {
    background-color: #334155;
  }
}

/* Main Content Overlay */
.main-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  height: 100%;
  position: relative;
  min-width: 0;
}

.main-bg-gradient {
  position: absolute;
  inset: 0;
  background: linear-gradient(to bottom right, rgba(30, 58, 138, 0.1), rgba(6, 78, 59, 0.1));
  pointer-events: none;
}

/* Top & Bottom Panes */
.top-pane {
  padding: 1rem 1rem 0.5rem 1rem;
  transition: all 0.3s;
  display: flex;
  flex-direction: column;
  min-height: 0;

  &.expanded-top {
    flex: 3;
  }

  &.flex-1 {
    flex: 1;
  }
}

.bottom-pane {
  padding: 1rem 1rem 0.5rem 1rem;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  transition: all 0.3s;
  min-height: 0;

  &.shrunk-bottom {
    flex: 1.5;
  }

  &.expanded-bottom {
    flex: 3;
  }
}

/* Streamer Grid */
.loading-container {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}

.spinner-wrapper {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  color: #94a3b8;
  animation: pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
}

.spinner {
  width: 2rem;
  height: 2rem;
  border-radius: 9999px;
  border: 4px solid transparent;
  border-top-color: #3b82f6;
  border-right-color: #3b82f6;
  animation: spin 1s linear infinite;
}

.grid-container {
  flex: 1;
  overflow-y: auto;
  background-color: rgba(15, 23, 42, 0.5);
  backdrop-filter: blur(12px);
  border-radius: 1rem;
  border: 1px solid rgba(30, 41, 59, 0.8);
  box-shadow: inset 0 2px 4px 0 rgba(0, 0, 0, 0.06);
  padding: 1rem;
  box-sizing: border-box;
}

.virtual-header {
  margin-bottom: 1.25rem;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid #1e293b;
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.virtual-title {
  font-size: 1.25rem;
  font-weight: 700;
  color: #e2e8f0;
  margin: 0;
}

.empty-state {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #64748b;
  font-size: 1.125rem;
  flex-direction: column;
  gap: 1rem;
}

.empty-icon {
  opacity: 0.2;
}

.streamer-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0.75rem;
}

.streamer-card {
  position: relative;
  cursor: pointer;
  border-radius: 0.75rem;
  overflow: hidden;
  background-color: #1e293b;
  transition: all 0.3s;
  border: 2px solid transparent;
  display: flex;
  flex-direction: column;

  &:hover {
    transform: translateY(-4px);
    box-shadow: 0 20px 25px -5px rgba(59, 130, 246, 0.2), 0 8px 10px -6px rgba(59, 130, 246, 0.2);

    .card-img {
      transform: scale(1.1);
    }

    .card-overlay {
      opacity: 0.8;
    }

    .play-overlay {
      opacity: 1;
    }

    .play-btn {
      transform: translateY(0);
    }

    .card-name {
      color: #fff;
    }
  }

  &.card-active {
    border-color: #10b981;
    box-shadow: 0 0 0 4px rgba(16, 185, 129, 0.2);
  }

  &.card-inactive {
    border-color: #334155;

    &:hover {
      border-color: #64748b;
    }
  }
}

.card-img-wrapper {
  aspect-ratio: 3 / 4;
  overflow: hidden;
  position: relative;
}

.card-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  transition: transform 0.5s;
}

.card-overlay {
  position: absolute;
  inset: 0;
  background: linear-gradient(to top, rgba(0, 0, 0, 0.9), rgba(0, 0, 0, 0.2), transparent);
  opacity: 0.6;
  transition: opacity 0.3s;
}

.play-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  opacity: 0;
  transition: opacity 0.3s;
}

.play-btn {
  background-color: rgba(255, 255, 255, 0.2);
  backdrop-filter: blur(4px);
  padding: 0.75rem;
  border-radius: 9999px;
  color: #fff;
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
  transform: translateY(1rem);
  transition: transform 0.3s;
}

.card-fav-btn {
  position: absolute;
  top: 0.5rem;
  right: 0.5rem;
  padding: 0.375rem;
  border-radius: 9999px;
  background-color: rgba(0, 0, 0, 0.4);
  backdrop-filter: blur(4px);
  transition: background-color 0.2s;
  z-index: 10;
  border: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;

  &:hover {
    background-color: rgba(0, 0, 0, 0.6);
  }
}

.card-info {
  padding: 0.625rem;
  text-align: center;
  color: #cbd5e1;
  flex: 1;
  display: flex;
  flex-direction: column;
  justify-content: center;
}

.card-name {
  font-size: 0.75rem;
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: block;
  width: 100%;
}

.card-time {
  font-size: 10px;
  color: #64748b;
  display: block;
  margin-top: 0.125rem;
  font-weight: 400;
  letter-spacing: 0.025em;
  text-align: left;
}

.empty-favs,
.empty-hist {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
}

.fav-color {
  color: #facc15;
}

.hist-color {
  color: #34d399;
}

.empty-subtext {
  font-size: 0.875rem;
}

/* -------------------------------------
   MOBILE AND RESPONSIVE MEDIA QUERIES
   ------------------------------------- */

@media (min-width: 640px) {
  .sidebar {
    width: 18rem;
  }

  .sidebar.sidebar-collapsed {
    width: 72px;
  }

  .nav-item.platform-item {
    padding: 0.5rem;
  }

  .nav-item.collapsed-item {
    flex-direction: row;
  }

  .mb-small {
    margin-bottom: 0;
  }

  .platform-img {
    width: 2.25rem;
    height: 2.25rem;
    min-width: 36px;
    min-height: 36px;
    max-width: 36px;
    max-height: 36px;
  }

  .card-name {
    font-size: 0.875rem;
  }

  .top-pane {
    padding: 1.5rem 1.5rem 0.5rem 1.5rem;
  }

  .bottom-pane {
    padding: 1.5rem 1.5rem 0.5rem 1.5rem;
  }

  .grid-container {
    padding: 1.25rem;
  }

  .streamer-grid {
    grid-template-columns: repeat(4, minmax(0, 1fr));
    gap: 1.25rem;
  }
}

@media (min-width: 768px) {
  .streamer-grid {
    grid-template-columns: repeat(5, minmax(0, 1fr));
  }
}

@media (min-width: 1024px) {
  .streamer-grid {
    grid-template-columns: repeat(6, minmax(0, 1fr));
  }
}

@media (min-width: 1280px) {
  .streamer-grid {
    grid-template-columns: repeat(7, minmax(0, 1fr));
  }
}

@media (min-width: 1536px) {
  .streamer-grid {
    grid-template-columns: repeat(8, minmax(0, 1fr));
  }
}
</style>
