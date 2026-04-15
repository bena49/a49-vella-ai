<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40 
            backdrop-blur-2xl border border-white/20 
            text-white rounded-3xl w-full max-w-lg shadow-2xl flex flex-col 
            min-h-[500px] max-h-[90vh] animate-fade-in-up">
      
      <!-- HEADER -->
      <div class="p-5 border-b border-white/10 flex justify-between items-center flex-shrink-0">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full bg-[#FF9800]/20 flex items-center justify-center text-[#FF9800]">
            <Icon name="lucide:tags" class="text-xl" />
          </div>
          <h2 class="text-sm font-bold tracking-wide">AUTO-TAG DOORS</h2>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <!-- BODY -->
      <div class="p-6 pb-10 space-y-5 overflow-y-auto custom-scrollbar flex-1">
        
        <!-- DOOR TAG FAMILY SELECTOR -->
        <div>
          <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Door Tag Family</label>
          <div class="relative">
            <div @click="isTagFamilyOpen = !isTagFamilyOpen" 
                 class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                 text-white outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                 :class="isTagFamilyOpen ? 'border-[#FF9800]' : ''">
              <span>{{ selectedTagDisplay || 'Select a door tag...' }}</span>
              <div class="text-white/50 transform transition-transform duration-200" 
                   :class="isTagFamilyOpen ? 'rotate-180' : ''">▼</div>
            </div>
            
            <div v-if="isTagFamilyOpen" 
                 class="absolute z-[40] w-full mt-1 bg-[#0A1D4A]/70 backdrop-blur-xl border border-white/25 rounded-xl 
                 overflow-hidden shadow-2xl animate-fade-in max-h-48 overflow-y-auto custom-scrollbar">
              <div v-for="tag in displayDoorTags" 
                   :key="tag.family + ':' + tag.type"
                   @click="selectTag(tag)"
                   class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                   :class="selectedTagFamily === tag.family && selectedTagType === tag.type ? 'bg-white/20 font-medium' : ''">
                {{ tag.family }} : {{ tag.type }}
              </div>
              <div v-if="displayDoorTags.length === 0" class="px-3 py-2 text-xs text-white/40 italic">
                No door tag families found in project
              </div>
            </div>
          </div>
        </div>

        <!-- VIEW SELECTOR -->
        <div>
          <div class="flex justify-between items-end mb-2">
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold block">Select Views to Tag</label>
            <button @click="toggleSelectAll" class="text-[10px] text-[#FF9800] hover:text-white transition">
              {{ isAllSelected ? 'Deselect All' : 'Select All' }}
            </button>
          </div>
          
          <div class="bg-black/20 border border-white/10 rounded-xl p-2 max-h-40 overflow-y-auto custom-scrollbar">
            <div v-if="displayViews.length > 0" class="space-y-1">
              <button 
                v-for="view in displayViews" :key="view.id"
                @click="toggleView(view.id)"
                class="w-full px-3 py-2 rounded-lg text-[11px] text-left transition-all duration-100 flex items-center gap-2"
                :class="selectedViewIds.includes(view.id) 
                  ? 'bg-[#FF9800] text-[#0A1D4A] font-bold' 
                  : 'bg-white/5 text-white/60 hover:bg-white/10 hover:text-white'"
              >
                <Icon :name="view.type === 'CeilingPlan' ? 'lucide:flip-vertical' : 'lucide:grid-2x2'" class="text-sm flex-shrink-0" />
                <span class="truncate">{{ view.name }}</span>
              </button>
            </div>
            <div v-else class="text-xs text-white/30 text-center py-4">
              No plan views found. Open a plan view or sheet first.
            </div>
          </div>
          <div class="text-[10px] text-white/30 mt-1 text-right">
            {{ selectedViewIds.length }} view(s) selected
          </div>
        </div>

        <!-- SKIP ALREADY TAGGED TOGGLE -->
        <div class="flex items-center justify-between bg-white/5 border border-white/10 rounded-xl px-4 py-3">
          <div>
            <div class="text-xs font-medium">Skip already tagged doors</div>
            <div class="text-[10px] text-white/40 mt-0.5">Doors with existing tags will be left unchanged</div>
          </div>
          <button 
            @click="skipTagged = !skipTagged"
            class="w-10 h-6 rounded-full transition-all duration-200 flex items-center px-0.5"
            :class="skipTagged ? 'bg-[#FF9800]' : 'bg-white/20'"
          >
            <div 
              class="w-5 h-5 rounded-full bg-white shadow-sm transition-transform duration-200"
              :class="skipTagged ? 'translate-x-4' : 'translate-x-0'"
            ></div>
          </button>
        </div>

      </div>

      <!-- FOOTER -->
      <div class="p-5 border-t border-white/10 flex-shrink-0">
        <button 
          @click="submitAutoTag"
          :disabled="!canSubmit"
          class="w-full py-3 rounded-xl font-bold text-sm transition-all duration-200 flex items-center justify-center gap-2"
          :class="canSubmit
            ? 'bg-[#FF9800] hover:bg-[#FFB74D] text-[#0A1D4A] shadow-lg shadow-orange-900/20' 
            : 'bg-white/10 text-white/30 cursor-not-allowed'"
        >
          <Icon name="lucide:tags" class="text-base" />
          <span>Tag {{ selectedViewIds.length }} View{{ selectedViewIds.length !== 1 ? 's' : '' }}</span>
        </button>
      </div>

    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue';

const props = defineProps({
  doorTags: { type: Array, default: () => [] },       // [{ family: "Door Tag", type: "Standard" }, ...]
  planViews: { type: Array, default: () => [] },       // [{ id: 12345, name: "L1 - Floor Plan", type: "FloorPlan" }, ...]
});

const emit = defineEmits(['close', 'submit']);

// 💥 DEBUG: Watch for prop changes
watch(() => props.doorTags, (newVal) => {
  console.log('🏷️ AutoTagWizard doorTags updated:', JSON.stringify(newVal));
}, { immediate: true });

watch(() => props.planViews, (newVal) => {
  console.log('🏷️ AutoTagWizard planViews updated:', JSON.stringify(newVal));
}, { immediate: true });

// --- STATE ---
const selectedTagFamily = ref('');
const selectedTagType = ref('');
const selectedViewIds = ref([]);
const skipTagged = ref(true);
const isTagFamilyOpen = ref(false);

// --- COMPUTED ---
const displayDoorTags = computed(() => {
  return props.doorTags && props.doorTags.length > 0 ? props.doorTags : [];
});

const displayViews = computed(() => {
  return props.planViews && props.planViews.length > 0 ? props.planViews : [];
});

const selectedTagDisplay = computed(() => {
  if (!selectedTagFamily.value) return '';
  return `${selectedTagFamily.value} : ${selectedTagType.value}`;
});

const canSubmit = computed(() => {
  return selectedTagFamily.value && selectedViewIds.value.length > 0;
});

const isAllSelected = computed(() => {
  return displayViews.value.length > 0 && 
         displayViews.value.every(v => selectedViewIds.value.includes(v.id));
});

// --- ACTIONS ---
function selectTag(tag) {
  selectedTagFamily.value = tag.family;
  selectedTagType.value = tag.type;
  isTagFamilyOpen.value = false;
}

function toggleView(viewId) {
  if (selectedViewIds.value.includes(viewId)) {
    selectedViewIds.value = selectedViewIds.value.filter(id => id !== viewId);
  } else {
    selectedViewIds.value.push(viewId);
  }
}

function toggleSelectAll() {
  if (isAllSelected.value) {
    selectedViewIds.value = [];
  } else {
    selectedViewIds.value = displayViews.value.map(v => v.id);
  }
}

function submitAutoTag() {
  if (!canSubmit.value) return;

  emit('submit', {
    tag_family: selectedTagFamily.value,
    tag_type: selectedTagType.value,
    view_ids: selectedViewIds.value,
    skip_tagged: skipTagged.value
  });
}

// --- KEYBOARD ---
const handleKeydown = (event) => {
  if (event.key === 'Escape') isTagFamilyOpen.value = false;
};
onMounted(() => { 
  document.addEventListener('keydown', handleKeydown);
  console.log('🏷️ AutoTagWizard mounted. doorTags:', JSON.stringify(props.doorTags), 'planViews:', JSON.stringify(props.planViews));
});
onUnmounted(() => { document.removeEventListener('keydown', handleKeydown); });
</script>

<style scoped>
.custom-scrollbar::-webkit-scrollbar { width: 6px; }
.custom-scrollbar::-webkit-scrollbar-track { background: transparent; margin: 2px 0; }
.custom-scrollbar::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.15); border-radius: 10px; }
.custom-scrollbar::-webkit-scrollbar-thumb:hover { background: rgba(255,255,255,0.25); }

@keyframes fade-in-up { from { opacity: 0; transform: translateY(20px) scale(0.95); } to { opacity: 1; transform: translateY(0) scale(1); } }
.animate-fade-in-up { animation: fade-in-up 0.2s ease-out forwards; }
@keyframes fade-in { from { opacity: 0; transform: translateY(-5px); } to { opacity: 1; transform: translateY(0); } }
.animate-fade-in { animation: fade-in 0.15s ease-out forwards; }
</style>
