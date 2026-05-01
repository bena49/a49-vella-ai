<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40 
            backdrop-blur-2xl border border-white/20 
            text-white rounded-3xl w-full max-w-lg shadow-2xl flex flex-col 
            min-h-[600px] max-h-[90vh] animate-fade-in-up">
      
      <!-- HEADER -->
      <div class="p-5 border-b border-white/10 flex justify-between items-center flex-shrink-0">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full bg-[#FF9800]/20 flex items-center justify-center text-[#FF9800]">
            <Icon name="lucide:tags" class="text-xl" />
          </div>
          <h2 class="text-sm font-bold tracking-wide">AUTOMATE TAGGING</h2>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <!-- BODY -->
      <div class="p-6 pb-10 space-y-4 overflow-y-auto custom-scrollbar flex-1">
        
        <!-- TAG TYPE DROPDOWN -->
        <div>
          <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Tag Type</label>
          <div class="relative" @click.stop>
            <div @click="toggleDropdown('tagType')" 
                 class="w-full bg-white/10 border border-white/20 rounded-xl px-3 py-2 text-xs 
                 text-white outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                 :class="isTagTypeOpen ? 'border-[#FF9800]' : ''">
              <span>{{ selectedTagType ? TAG_TYPE_LABELS[selectedTagType] : 'Select tag type...' }}</span>
              <div class="text-white/50 transform transition-transform duration-200" 
                   :class="isTagTypeOpen ? 'rotate-180' : ''">▼</div>
            </div>
            <div v-if="isTagTypeOpen" 
                 class="absolute z-[40] w-full mt-1 bg-[#0A1D4A]/80 backdrop-blur-xl border border-white/25 rounded-xl 
                 overflow-hidden shadow-2xl animate-fade-in">
              <div v-for="tt in TAG_TYPE_OPTIONS" :key="tt.key"
                   @click="selectTagType(tt.key)"
                   class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                   :class="selectedTagType === tt.key ? 'bg-white/20 font-medium' : ''">
                {{ tt.label }}
              </div>
            </div>
          </div>
        </div>

        <!-- TAG FAMILY — standard element tags -->
        <div v-if="selectedTagType && !isSpotElevation">
          <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">
            {{ TAG_TYPE_LABELS[selectedTagType] }} Family
          </label>
          <div class="relative" @click.stop>
            <div @click="toggleDropdown('tagFamily')"
                 class="w-full bg-white/10 border border-white/20 rounded-xl px-3 py-2 text-xs
                 text-white outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                 :class="isTagFamilyOpen ? 'border-[#FF9800]' : ''">
              <span>{{ selectedTagFamilyDisplay || 'Select a tag family...' }}</span>
              <div class="text-white/50 transform transition-transform duration-200"
                   :class="isTagFamilyOpen ? 'rotate-180' : ''">▼</div>
            </div>
            <div v-if="isTagFamilyOpen"
                 class="absolute z-[40] w-full mt-1 bg-[#0A1D4A]/80 backdrop-blur-xl border border-white/25 rounded-xl
                 overflow-hidden shadow-2xl animate-fade-in max-h-48 overflow-y-auto custom-scrollbar">
              <div v-for="tag in availableTagFamilies"
                   :key="tag.family + ':' + tag.type"
                   @click="selectTagFamily(tag)"
                   class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                   :class="selectedTagFamily === tag.family && selectedTagFamilyType === tag.type ? 'bg-white/20 font-medium' : ''">
                {{ tag.family }} : {{ tag.type }}
              </div>
              <div v-if="availableTagFamilies.length === 0" class="px-3 py-2 text-xs text-white/40 italic">
                No {{ TAG_TYPE_LABELS[selectedTagType].toLowerCase() }} families found in project
              </div>
            </div>
          </div>
        </div>

        <!-- TAG FAMILY — Spot Elevation: view-type selector then single type picker -->
        <template v-if="isSpotElevation">
          <!-- Step 1: View Type -->
          <div>
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">View Type</label>
            <div class="flex gap-2">
              <button @click="selectSpotViewType('FloorPlan')"
                class="flex-1 py-2 rounded-xl text-xs font-medium transition-all border"
                :class="selectedSpotViewType === 'FloorPlan'
                  ? 'bg-[#FF9800] text-[#0A1D4A] border-transparent'
                  : 'bg-white/5 text-white/60 hover:bg-white/10 hover:text-white border-white/15'">
                Floor Plan
              </button>
              <button @click="selectSpotViewType('Section')"
                class="flex-1 py-2 rounded-xl text-xs font-medium transition-all border"
                :class="selectedSpotViewType === 'Section'
                  ? 'bg-[#FF9800] text-[#0A1D4A] border-transparent'
                  : 'bg-white/5 text-white/60 hover:bg-white/10 hover:text-white border-white/15'">
                Building/Wall Sections
              </button>
            </div>
          </div>

          <!-- Step 2: Spot Elevation Type (only after view type chosen) -->
          <div v-if="selectedSpotViewType">
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Spot Elevation Type</label>
            <div class="relative" @click.stop>
              <div @click="toggleDropdown('spotType')"
                   class="w-full bg-white/10 border border-white/20 rounded-xl px-3 py-2 text-xs
                   text-white outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                   :class="isSpotTypeOpen ? 'border-[#FF9800]' : ''">
                <span :class="selectedSpotType ? '' : 'text-white/40'">
                  {{ selectedSpotType || 'Select spot elevation type...' }}
                </span>
                <div class="text-white/50 transform transition-transform duration-200"
                     :class="isSpotTypeOpen ? 'rotate-180' : ''">▼</div>
              </div>
              <div v-if="isSpotTypeOpen"
                   class="absolute z-[40] w-full mt-1 bg-[#0A1D4A]/80 backdrop-blur-xl border border-white/25 rounded-xl
                   overflow-hidden shadow-2xl animate-fade-in max-h-48 overflow-y-auto custom-scrollbar">
                <div v-for="tag in availableTagFamilies" :key="tag.type"
                     @click="selectedSpotType = tag.type; isSpotTypeOpen = false"
                     class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                     :class="selectedSpotType === tag.type ? 'bg-white/20 font-medium' : ''">
                  {{ tag.type }}
                </div>
                <div v-if="availableTagFamilies.length === 0" class="px-3 py-2 text-xs text-white/40 italic">
                  No Spot Elevation types found in project
                </div>
              </div>
            </div>
          </div>
        </template>

        <!-- FILTERS SECTION -->
        <div v-if="selectedTagType" class="space-y-3 bg-black/10 border border-white/10 rounded-xl p-3">
          <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold">Filters</div>

          <!-- VIEW TYPE FILTER (chips) — hidden for spot elevation, view type selected above -->
          <div v-if="!isSpotElevation">
            <label class="text-[10px] text-white/40 block mb-1">View Type</label>
            <div class="flex flex-wrap gap-1.5">
              <button v-for="vt in compatibleViewTypes" :key="vt.key"
                @click="toggleViewTypeFilter(vt.key)"
                class="px-2.5 py-1 rounded-lg text-[11px] transition-all"
                :class="activeViewTypes.includes(vt.key)
                  ? 'bg-[#FF9800] text-[#0A1D4A] font-bold'
                  : 'bg-white/5 text-white/60 hover:bg-white/10 hover:text-white'">
                {{ vt.label }}
              </button>
            </div>
          </div>

          <!-- STAGE FILTER -->
          <div>
            <label class="text-[10px] text-white/40 block mb-1">Stage</label>
            <div class="flex bg-white/5 rounded-lg p-1 border border-white/10">
              <button v-for="stg in ['WV', 'PD', 'DD', 'CD', 'Other']" :key="stg"
                @click="toggleStageFilter(stg)"
                class="flex-1 rounded-md text-[11px] py-1 transition-all"
                :class="activeStages.includes(stg) 
                  ? 'bg-white/20 text-white font-bold' 
                  : 'text-white/40 hover:text-white/70'">
                {{ stg }}
              </button>
            </div>
          </div>

          <!-- LEVEL FILTER (only for Floor/Ceiling Plan) -->
          <div v-if="isLevelFilterActive">
            <label class="text-[10px] text-white/40 block mb-1">Level</label>
            <div class="flex flex-wrap gap-1.5 max-h-20 overflow-y-auto custom-scrollbar">
              <button v-for="lvl in availableLevels" :key="lvl"
                @click="toggleLevelFilter(lvl)"
                class="px-2 py-1 rounded-md text-[10px] transition-all"
                :class="activeLevels.includes(lvl) 
                  ? 'bg-[#4EE29B] text-[#0A1D4A] font-bold' 
                  : 'bg-white/5 text-white/50 hover:bg-white/10 hover:text-white'">
                {{ lvl || '(no level)' }}
              </button>
              <div v-if="availableLevels.length === 0" class="text-[10px] text-white/30 italic">
                No levels detected
              </div>
            </div>
          </div>
        </div>

        <!-- VIEW SELECTOR -->
        <div v-if="selectedTagType">
          <div class="flex justify-between items-end mb-1.5">
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold block">Select Views to Tag</label>
            <button @click="toggleSelectAll" class="text-[10px] text-[#FF9800] hover:text-white transition">
              {{ isAllSelected ? 'Deselect All' : 'Select All' }}
            </button>
          </div>
          <div class="bg-black/20 border border-white/10 rounded-xl p-2 max-h-40 overflow-y-auto custom-scrollbar">
            <div v-if="filteredViews.length > 0" class="space-y-1">
              <button v-for="view in filteredViews" :key="view.id"
                @click="toggleView(view.id)"
                class="w-full px-3 py-1.5 rounded-lg text-[11px] text-left transition-all flex items-center gap-2"
                :class="selectedViewIds.includes(view.id) 
                  ? 'bg-[#FF9800] text-[#0A1D4A] font-bold' 
                  : 'bg-white/5 text-white/60 hover:bg-white/10 hover:text-white'">
                <Icon :name="viewIcon(view.view_type)" class="text-sm flex-shrink-0" />
                <span class="truncate flex-1">{{ view.name }}</span>
                <span class="text-[9px] opacity-60">{{ viewTypeShort(view.view_type) }}</span>
              </button>
            </div>
            <div v-else class="text-xs text-white/30 text-center py-3">
              No views match the current filters.
            </div>
          </div>
          <div class="text-[10px] text-white/30 mt-1 text-right">
            {{ selectedViewIds.length }} of {{ filteredViews.length }} selected
          </div>
        </div>

        <!-- SKIP ALREADY TAGGED TOGGLE -->
        <div v-if="selectedTagType" class="flex items-center justify-between bg-white/5 border border-white/10 rounded-xl px-4 py-3">
          <div>
            <div class="text-xs font-medium">Skip already tagged {{ elementPlural }}</div>
            <div class="text-[10px] text-white/40 mt-0.5">{{ elementCap }} with existing tags will be left unchanged</div>
          </div>
          <button @click="skipTagged = !skipTagged"
            class="w-10 h-6 rounded-full transition-all flex items-center px-0.5"
            :class="skipTagged ? 'bg-[#FF9800]' : 'bg-white/20'">
            <div class="w-5 h-5 rounded-full bg-white shadow-sm transition-transform"
              :class="skipTagged ? 'translate-x-4' : 'translate-x-0'"></div>
          </button>
        </div>

      </div>

      <!-- FOOTER -->
      <div class="p-5 border-t border-white/10 flex-shrink-0">
        <button @click="submit"
          :disabled="!canSubmit"
          class="w-full py-3 rounded-xl font-bold text-sm transition-all flex items-center justify-center gap-2"
          :class="canSubmit
            ? 'bg-[#FF9800] hover:bg-[#FFB74D] text-[#0A1D4A] shadow-lg shadow-orange-900/20' 
            : 'bg-white/10 text-white/30 cursor-not-allowed'">
          <Icon name="lucide:tags" class="text-base" />
          <span>Tag {{ selectedViewIds.length }} View{{ selectedViewIds.length !== 1 ? 's' : '' }}</span>
        </button>
      </div>

    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue';

// =====================================================================
// PROPS
// =====================================================================
const props = defineProps({
  doorTags:          { type: Array, default: () => [] },
  windowTags:        { type: Array, default: () => [] },
  wallTags:          { type: Array, default: () => [] },
  roomTags:          { type: Array, default: () => [] },
  ceilingTags:       { type: Array, default: () => [] },
  spotElevationTags: { type: Array, default: () => [] },
  // taggableViews: [{id, name, view_type, stage, level, view_abbrev, scale}]
  taggableViews: { type: Array, default: () => [] },
});

const emit = defineEmits(['close', 'submit']);

// =====================================================================
// CONSTANTS — Tag Type → UI mapping
// =====================================================================
const TAG_TYPE_OPTIONS = [
  { key: 'door',           label: 'Door Tag' },
  { key: 'window',         label: 'Window Tag' },
  { key: 'wall',           label: 'Wall Tag' },
  { key: 'room',           label: 'Room Tag' },
  { key: 'ceiling',        label: 'Ceiling Tag' },
  { key: 'spot_elevation', label: 'Spot Elevation' },
];

const TAG_TYPE_LABELS = Object.fromEntries(TAG_TYPE_OPTIONS.map(o => [o.key, o.label]));

const ELEMENT_PLURAL = {
  door: 'doors', window: 'windows', wall: 'walls', room: 'rooms', ceiling: 'ceilings',
  spot_elevation: 'spot elevations',
};
const ELEMENT_CAP = {
  door: 'Doors', window: 'Windows', wall: 'Walls', room: 'Rooms', ceiling: 'Ceilings',
  spot_elevation: 'Spot Elevations',
};

// Per-Tag-Type compatibility:
//   key = Revit view_type string, label = UI label
const VIEW_TYPE_COMPAT = {
  door:    [{ key: 'FloorPlan',   label: 'Floor Plan' },
            { key: 'Elevation',   label: 'Elevation' },
            { key: 'Section',     label: 'Section' }],
  window:  [{ key: 'FloorPlan',   label: 'Floor Plan' },
            { key: 'Elevation',   label: 'Elevation' },
            { key: 'Section',     label: 'Section' }],
  wall:    [{ key: 'FloorPlan',   label: 'Floor Plan' }],
  room:    [{ key: 'FloorPlan',   label: 'Floor Plan' },
            { key: 'CeilingPlan', label: 'Ceiling Plan' },
            { key: 'Elevation',   label: 'Elevation' },
            { key: 'Section',     label: 'Section' }],
  ceiling:        [{ key: 'CeilingPlan', label: 'Ceiling Plan' }],
  spot_elevation: [{ key: 'FloorPlan',   label: 'Floor Plan' },
                   { key: 'Section',     label: 'Section' }],
};

// =====================================================================
// STATE
// =====================================================================
const selectedTagType = ref(null);
const selectedTagFamily = ref('');
const selectedTagFamilyType = ref('');
// Spot Elevation — dedicated view-type selector + single type picker
const selectedSpotViewType = ref('');  // 'FloorPlan' | 'Section'
const selectedSpotType = ref('');      // SpotDimensionType name
const activeViewTypes = ref([]);
const activeStages = ref([]);
const activeLevels = ref([]);
const selectedViewIds = ref([]);
const skipTagged = ref(true);

const isTagTypeOpen = ref(false);
const isTagFamilyOpen = ref(false);
const isSpotTypeOpen = ref(false);

// =====================================================================
// COMPUTED
// =====================================================================
const isSpotElevation = computed(() => selectedTagType.value === 'spot_elevation');

const SPOT_EXCLUDE_TYPES = ['Horizontal', 'Sloped-Percent'];

const availableTagFamilies = computed(() => {
  if (!selectedTagType.value) return [];
  const map = {
    door:           props.doorTags,
    window:         props.windowTags,
    wall:           props.wallTags,
    room:           props.roomTags,
    ceiling:        props.ceilingTags,
    spot_elevation: props.spotElevationTags.filter(
      (t) => !SPOT_EXCLUDE_TYPES.includes(t.type)
    ),
  };
  return map[selectedTagType.value] || [];
});

const selectedTagFamilyDisplay = computed(() => {
  if (!selectedTagFamily.value) return '';
  return `${selectedTagFamily.value} : ${selectedTagFamilyType.value}`;
});

const compatibleViewTypes = computed(() => {
  if (!selectedTagType.value) return [];
  return VIEW_TYPE_COMPAT[selectedTagType.value] || [];
});

// Level filter is only relevant when Floor/Ceiling Plan is among the active view types
const isLevelFilterActive = computed(() => {
  return activeViewTypes.value.includes('FloorPlan') || activeViewTypes.value.includes('CeilingPlan');
});

const availableLevels = computed(() => {
  // Distinct levels from views that pass the view-type + stage filters
  const levels = new Set();
  props.taggableViews.forEach(v => {
    if (!isLevelFilterActive.value) return;
    if (activeViewTypes.value.length > 0 && !activeViewTypes.value.includes(v.view_type)) return;
    if (activeStages.value.length > 0 && !activeStages.value.includes(v.stage)) return;
    if (v.view_type === 'FloorPlan' || v.view_type === 'CeilingPlan') {
      if (v.level) levels.add(v.level);
    }
  });
  return Array.from(levels).sort();
});

const filteredViews = computed(() => {
  if (!selectedTagType.value) return [];

  // Spot elevation: view type is driven by the dedicated selector, not the chip filter
  if (isSpotElevation.value) {
    if (!selectedSpotViewType.value) return [];
    return props.taggableViews.filter(v => {
      if (v.view_type !== selectedSpotViewType.value) return false;
      if (activeStages.value.length > 0) {
        const hasOther = activeStages.value.includes('Other');
        const standardStages = activeStages.value.filter(s => s !== 'Other');
        const viewStage = v.stage || '';
        const isStandardMatch = standardStages.includes(viewStage);
        const isOtherMatch = hasOther && !['WV', 'PD', 'DD', 'CD'].includes(viewStage);
        if (!isStandardMatch && !isOtherMatch) return false;
      }
      return true;
    });
  }

  const compatibleKeys = compatibleViewTypes.value.map(v => v.key);
  return props.taggableViews.filter(v => {
    if (!compatibleKeys.includes(v.view_type)) return false;
    if (activeViewTypes.value.length > 0 && !activeViewTypes.value.includes(v.view_type)) return false;
    if (activeStages.value.length > 0) {
      const hasOther = activeStages.value.includes('Other');
      const standardStages = activeStages.value.filter(s => s !== 'Other');
      const viewStage = v.stage || '';
      const isStandardMatch = standardStages.includes(viewStage);
      const isOtherMatch = hasOther && !['WV', 'PD', 'DD', 'CD'].includes(viewStage);
      if (!isStandardMatch && !isOtherMatch) return false;
    }
    if (isLevelFilterActive.value && activeLevels.value.length > 0) {
      if (v.view_type === 'FloorPlan' || v.view_type === 'CeilingPlan') {
        if (!activeLevels.value.includes(v.level)) return false;
      }
    }
    return true;
  });
});

const isAllSelected = computed(() => {
  return filteredViews.value.length > 0 &&
    filteredViews.value.every(v => selectedViewIds.value.includes(v.id));
});

const canSubmit = computed(() => {
  if (!selectedTagType.value || selectedViewIds.value.length === 0) return false;
  if (isSpotElevation.value)
    return !!selectedSpotViewType.value && !!selectedSpotType.value;
  return !!selectedTagFamily.value;
});

const elementPlural = computed(() => ELEMENT_PLURAL[selectedTagType.value] || 'elements');
const elementCap = computed(() => ELEMENT_CAP[selectedTagType.value] || 'Elements');

// =====================================================================
// ACTIONS
// =====================================================================
function toggleDropdown(name) {
  const all = [isTagTypeOpen, isTagFamilyOpen, isSpotTypeOpen];
  all.forEach(r => r.value = false);
  if (name === 'tagType')   isTagTypeOpen.value  = true;
  if (name === 'tagFamily') isTagFamilyOpen.value = true;
  if (name === 'spotType')  isSpotTypeOpen.value  = true;
}

function selectTagType(key) {
  selectedTagType.value = key;
  isTagTypeOpen.value = false;
  selectedTagFamily.value = '';
  selectedTagFamilyType.value = '';
  selectedSpotViewType.value = '';
  selectedSpotType.value = '';
  activeViewTypes.value = [];
  activeStages.value = [];
  activeLevels.value = [];
  selectedViewIds.value = [];
}

function selectSpotViewType(key) {
  selectedSpotViewType.value = key;
  selectedSpotType.value = '';
  selectedViewIds.value = [];
}

function selectTagFamily(tag) {
  selectedTagFamily.value = tag.family;
  selectedTagFamilyType.value = tag.type;
  isTagFamilyOpen.value = false;
}

function toggleViewTypeFilter(key) {
  const idx = activeViewTypes.value.indexOf(key);
  if (idx >= 0) activeViewTypes.value.splice(idx, 1);
  else activeViewTypes.value.push(key);
  // Clear selection that no longer applies
  pruneSelectedViews();
}

function toggleStageFilter(stage) {
  const idx = activeStages.value.indexOf(stage);
  if (idx >= 0) activeStages.value.splice(idx, 1);
  else activeStages.value.push(stage);
  pruneSelectedViews();
}

function toggleLevelFilter(lvl) {
  const idx = activeLevels.value.indexOf(lvl);
  if (idx >= 0) activeLevels.value.splice(idx, 1);
  else activeLevels.value.push(lvl);
  pruneSelectedViews();
}

function pruneSelectedViews() {
  // Drop any selected view IDs that no longer appear in filteredViews
  const valid = new Set(filteredViews.value.map(v => v.id));
  selectedViewIds.value = selectedViewIds.value.filter(id => valid.has(id));
}

function toggleView(id) {
  const idx = selectedViewIds.value.indexOf(id);
  if (idx >= 0) selectedViewIds.value.splice(idx, 1);
  else selectedViewIds.value.push(id);
}

function toggleSelectAll() {
  if (isAllSelected.value) {
    selectedViewIds.value = [];
  } else {
    selectedViewIds.value = filteredViews.value.map(v => v.id);
  }
}

function viewIcon(viewType) {
  const map = {
    FloorPlan: 'lucide:grid-2x2',
    CeilingPlan: 'lucide:flip-vertical',
    Elevation: 'lucide:rectangle-horizontal',
    Section: 'lucide:scissors',
    AreaPlan: 'lucide:square-stack',
  };
  return map[viewType] || 'lucide:file';
}

function viewTypeShort(viewType) {
  const map = {
    FloorPlan: 'Plan',
    CeilingPlan: 'RCP',
    Elevation: 'Elev',
    Section: 'Sect',
    AreaPlan: 'Area',
  };
  return map[viewType] || viewType;
}

function submit() {
  if (!canSubmit.value) return;
  const payload = {
    tag_category: selectedTagType.value,
    tag_family:   selectedTagFamily.value,
    tag_type:     selectedTagFamilyType.value,
    view_ids:     selectedViewIds.value,
    skip_tagged:  skipTagged.value,
  };
  if (isSpotElevation.value) {
    payload.spot_plan_type    = selectedSpotViewType.value === 'FloorPlan' ? selectedSpotType.value : '';
    payload.spot_section_type = selectedSpotViewType.value === 'Section'   ? selectedSpotType.value : '';
  }
  emit('submit', payload);
}

// --- CLOSE ALL DROPDOWNS ---
function closeAllDropdowns() {
  isTagTypeOpen.value  = false;
  isTagFamilyOpen.value = false;
  isSpotTypeOpen.value  = false;
}

const handleKeydown = (e) => { if (e.key === 'Escape') closeAllDropdowns(); };
onMounted(() => {
  document.addEventListener('keydown', handleKeydown);
  document.addEventListener('click', closeAllDropdowns);
});
onUnmounted(() => {
  document.removeEventListener('keydown', handleKeydown);
  document.removeEventListener('click', closeAllDropdowns);
});
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
