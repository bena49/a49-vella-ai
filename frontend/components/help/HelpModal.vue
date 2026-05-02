<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40
        backdrop-blur-2xl border border-white/20
        text-white rounded-3xl w-full max-w-4xl shadow-2xl flex flex-col
        min-h-[600px] max-h-[90vh] animate-fade-in-up overflow-hidden">

      <!-- Header -->
      <div class="p-5 md:border-b md:border-white/10 flex justify-between items-center flex-shrink-0">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full flex items-center justify-center transition-colors"
               :class="[activeMeta?.activeBgClass || 'bg-[#60A5FA]/20',
                        activeMeta?.activeIconClass || 'text-[#60A5FA]']">
            <Icon :name="activeMeta?.icon || 'lucide:circle-question-mark'" />
          </div>
          <div>
            <h2 class="text-sm font-bold tracking-wide">VELLA COMMAND GUIDE</h2>
            <p class="text-[10px] text-white/50 uppercase tracking-wider">
              <span class="md:hidden">{{ activeMeta?.label || 'Cheat Sheet' }}</span>
              <span class="hidden md:inline">Cheat Sheet &amp; Standards</span>
            </p>
          </div>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <!-- Mobile Menu Toggle — narrow only. Single button switches between
           hamburger/Open Menu and X/Close Menu based on sidebarOpen. -->
      <button @click="sidebarOpen = !sidebarOpen"
              class="md:hidden flex items-center gap-2 px-5 py-0 border-b border-white/10
                     text-white/70 hover:text-white transition"
              :aria-label="sidebarOpen ? 'Close navigation menu' : 'Open navigation menu'">
        <Icon :name="sidebarOpen ? 'lucide:x' : 'ic:baseline-menu'" class="text-xl" />
        <span class="text-xs font-medium uppercase tracking-wider">
          {{ sidebarOpen ? 'Close Menu' : 'Open Menu' }}
        </span>
      </button>

      <!-- Body: sidebar + content -->
      <div class="flex flex-1 min-h-0 relative">

        <!-- Sidebar: persistent on md+; slide-over drawer on narrow -->
        <aside
          class="bg-[#0A1D4A]/90 border-r border-white/10 flex-shrink-0
                 flex flex-col
                 md:static md:translate-x-0 md:w-48
                 absolute inset-y-0 left-0 z-20 w-56
                 transition-transform duration-200 ease-out"
          :class="sidebarOpen ? 'translate-x-0' : '-translate-x-full md:translate-x-0'">

          <nav class="flex-1 overflow-y-auto custom-scrollbar py-3">
            <div v-for="(group, gIdx) in groups" :key="gIdx" class="mb-2">
              <div class="px-4 py-1.5 text-[9px] font-bold uppercase tracking-wider text-white/40">
                {{ group.label }}
              </div>
              <button v-for="item in group.items" :key="item.id"
                      @click="selectTab(item.id)"
                      class="w-full flex items-center gap-2.5 px-4 py-2 text-xs text-left
                             transition border-l-2"
                      :class="activeTab === item.id
                        ? 'bg-white/10 text-white border-white/40 font-medium'
                        : 'text-white/70 hover:bg-white/5 hover:text-white border-transparent'">
                <Icon :name="item.icon" class="text-base flex-shrink-0" />
                <span class="truncate">{{ item.label }}</span>
              </button>
            </div>
          </nav>

          <!-- Bottom-anchored: Comment button -->
          <div class="border-t border-white/10 flex-shrink-0">
            <button @click="selectTab(commentItem.id)"
                    class="w-full flex items-center gap-2.5 px-4 py-3 text-xs text-left
                           transition border-l-2"
                    :class="activeTab === commentItem.id
                      ? 'bg-white/10 text-white border-white/40 font-medium'
                      : 'text-white/70 hover:bg-white/5 hover:text-white border-transparent'">
              <Icon :name="commentItem.icon" class="text-base flex-shrink-0" />
              <span class="truncate">{{ commentItem.label }}</span>
            </button>
          </div>
        </aside>

        <!-- Drawer backdrop on narrow when open -->
        <div v-if="sidebarOpen"
             @click="sidebarOpen = false"
             class="md:hidden absolute inset-0 bg-black/30 z-10"></div>

        <!-- Content -->
        <div class="flex-1 overflow-y-auto p-5 custom-scrollbar min-w-0">
          <component :is="activeComponent"
                     :userName="props.userName"
                     :sessionKey="props.sessionKey"
                     :submitDirect="props.submitDirect"
                     @pick="copyToClipboard" />
        </div>
      </div>

      <!-- Footer -->
      <div class="p-5 border-t border-white/10 flex-shrink-0">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-2 text-[10px] text-white/50">
            <Icon name="lucide:info" class="text-sm opacity-50" />
            <span>Click any command to copy to clipboard</span>
          </div>

          <!-- Toast Notification -->
          <transition
            enter-active-class="transition duration-200 ease-out"
            enter-from-class="opacity-0 translate-y-2"
            enter-to-class="opacity-100 translate-y-0"
            leave-active-class="transition duration-150 ease-in"
            leave-from-class="opacity-100 translate-y-0"
            leave-to-class="opacity-0 translate-y-2"
          >
            <div v-if="showToast" class="flex items-center gap-2 text-[#60A5FA] text-xs font-bold px-3 py-1 bg-[#60A5FA]/10 rounded-full border border-[#60A5FA]/20">
              <Icon name="lucide:check" class="text-sm" />
              <span>Copied!</span>
            </div>
          </transition>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue';

// Tab content components
import HelpViewsTab from './HelpViewsTab.vue';
import HelpSheetsTab from './HelpSheetsTab.vue';
import HelpCreatePlaceTab from './HelpCreatePlaceTab.vue';
import HelpRoomElevationTab from './HelpRoomElevationTab.vue';
import HelpTaggingTab from './HelpTaggingTab.vue';
import HelpDimensionsTab from './HelpDimensionsTab.vue';
import HelpInsertStandardDetailsTab from './HelpInsertStandardDetailsTab.vue';
import HelpPreflightTab from './HelpPreflightTab.vue';
import HelpStandardsTab from './HelpStandardsTab.vue';
import HelpMathCalculationTab from './HelpMathCalculationTab.vue';
import HelpCommentTab from './HelpCommentTab.vue';

defineEmits(['close']);

// Identity passed in from index.vue so the Comment form can attribute "From".
// submitDirect is the auth'd POST helper from useChat — used by the Comment
// form to send the email request without going through the chat flow.
const props = defineProps({
  userName:     { type: String,   default: '' },
  sessionKey:   { type: String,   default: '' },
  submitDirect: { type: Function, default: null },
});

const activeTab = ref('views');
const showToast = ref(false);

// Sidebar drawer state — controls narrow-viewport visibility.
// On md+ the sidebar is always visible regardless of this flag.
const sidebarOpen = ref(false);

// Grouped navigation — mirrors the ChatInput "+" menu structure.
const groups = [
  {
    label: 'Creation Wizards',
    items: [
      { id: 'views',       label: 'Views',           comp: HelpViewsTab,            icon: 'lucide:grid-2x2',       activeIconClass: 'text-[#4EE29B]', activeBgClass: 'bg-[#4EE29B]/20' },
      { id: 'sheets',      label: 'Sheets',          comp: HelpSheetsTab,           icon: 'lucide:files',          activeIconClass: 'text-[#60A5FA]', activeBgClass: 'bg-[#60A5FA]/20' },
      { id: 'cp',          label: 'Create & Place',  comp: HelpCreatePlaceTab,      icon: 'lucide:grid-2x2-plus',  activeIconClass: 'text-[#00BCD4]', activeBgClass: 'bg-[#00BCD4]/20' },
      { id: 'room',        label: 'Room Elevations', comp: HelpRoomElevationTab,    icon: 'lucide:frame',          activeIconClass: 'text-[#F43F5E]', activeBgClass: 'bg-[#F43F5E]/20' },
    ],
  },
  {
    label: 'Automation',
    items: [
      { id: 'tags',        label: 'Tags',            comp: HelpTaggingTab,          icon: 'lucide:tags',           activeIconClass: 'text-[#FF9800]', activeBgClass: 'bg-[#FF9800]/20' },
      { id: 'dim',         label: 'Dimensions',      comp: HelpDimensionsTab,       icon: 'tabler:ruler-measure',  activeIconClass: 'text-[#00BCD4]', activeBgClass: 'bg-[#00BCD4]/20' },
    ],
  },
  {
    label: 'Standards',
    items: [
      { id: 'isd',         label: 'Insert Standard Details', comp: HelpInsertStandardDetailsTab, icon: 'material-symbols:library-add-outline-rounded', activeIconClass: 'text-[#388E3C]', activeBgClass: 'bg-[#388E3C]/20' },
      { id: 'preflight',   label: 'Preflight Check', comp: HelpPreflightTab,        icon: 'lucide:shield-check',   activeIconClass: 'text-[#FBBF24]', activeBgClass: 'bg-[#FBBF24]/20' },
      { id: 'stds',        label: 'Standards Reference', comp: HelpStandardsTab,    icon: 'lucide:bookmark',       activeIconClass: 'text-[#EEFF41]', activeBgClass: 'bg-[#EEFF41]/20' },
    ],
  },
  {
    label: 'Utilities',
    items: [
      { id: 'math',        label: 'Math',            comp: HelpMathCalculationTab,  icon: 'lucide:calculator',     activeIconClass: 'text-[#FFB74D]', activeBgClass: 'bg-[#FFB74D]/20' },
    ],
  },
];

// Anchored at the bottom of the sidebar — sits outside the grouped nav.
// Header icon + bg use neutral white tones so the Comment view doesn't
// compete with the colored wizard icons.
const commentItem = {
  id:              'comment',
  label:           'Comment',
  comp:            HelpCommentTab,
  icon:            'material-symbols:add-comment-outline-rounded',
  activeIconClass: 'text-white',
  activeBgClass:   'bg-white/15',
};

// Flat list for lookup — includes the bottom-anchored Comment item.
const allItems = computed(() => [...groups.flatMap(g => g.items), commentItem]);
const activeMeta = computed(() => allItems.value.find(i => i.id === activeTab.value));
const activeComponent = computed(() => activeMeta.value?.comp);

// Detect if we're in narrow mode (viewport < md). matchMedia is reactive
// via the listener — keeps `isNarrow` in sync as the user resizes.
const isNarrow = ref(false);
let mql = null;
function syncNarrow(e) { isNarrow.value = e.matches; }

onMounted(() => {
  if (typeof window !== 'undefined' && window.matchMedia) {
    mql = window.matchMedia('(max-width: 767.98px)');
    isNarrow.value = mql.matches;
    if (mql.addEventListener) mql.addEventListener('change', syncNarrow);
    else mql.addListener(syncNarrow); // legacy Safari fallback
  }
});

onUnmounted(() => {
  if (mql) {
    if (mql.removeEventListener) mql.removeEventListener('change', syncNarrow);
    else mql.removeListener(syncNarrow);
  }
});

function selectTab(id) {
  activeTab.value = id;
  // Auto-close drawer on narrow after selection
  if (isNarrow.value) sidebarOpen.value = false;
}

// 📋 CLICK TO COPY LOGIC
async function copyToClipboard(text) {
  try {
    await navigator.clipboard.writeText(text);
    showToast.value = true;
    setTimeout(() => { showToast.value = false; }, 2000);
  } catch (err) {
    console.error('Failed to copy!', err);
  }
}
</script>

<style scoped>
.custom-scrollbar::-webkit-scrollbar { width: 6px; }
.custom-scrollbar::-webkit-scrollbar-track { background: transparent; margin: 2px 0; }
.custom-scrollbar::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.15); border-radius: 10px; }
.custom-scrollbar::-webkit-scrollbar-thumb:hover { background: rgba(255,255,255,0.25); }

@keyframes fade-in-up {
  from { opacity: 0; transform: translateY(20px) scale(0.95); }
  to   { opacity: 1; transform: translateY(0)    scale(1); }
}
.animate-fade-in-up { animation: fade-in-up 0.2s ease-out forwards; }
</style>
