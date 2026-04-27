<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40 
            backdrop-blur-2xl border border-white/20 
            text-white rounded-3xl w-full max-w-lg shadow-2xl flex flex-col 
            min-h-[580px] max-h-[90vh] animate-fade-in-up">

      <!-- HEADER -->
      <div class="p-5 border-b border-white/10 flex justify-between items-center flex-shrink-0">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full bg-[#00BCD4]/20 flex items-center justify-center text-[#00BCD4]">
            <Icon name="tabler:ruler-measure" class="text-xl" />
          </div>
          <h2 class="text-sm font-bold tracking-wide">AUTOMATE DIMENSIONS</h2>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <!-- BODY -->
      <div class="p-6 pb-10 space-y-4 overflow-y-auto custom-scrollbar flex-1">

        <!-- DIMENSION STYLE DROPDOWN -->
        <div>
          <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">
            Dimension Style <span class="text-white/30 normal-case font-normal">(optional — auto-selects if blank)</span>
          </label>
          <div class="relative">
            <div @click="isDimTypeOpen = !isDimTypeOpen"
                 class="w-full bg-white/10 border border-white/20 rounded-xl px-3 py-2 text-xs 
                 text-white cursor-pointer flex justify-between items-center hover:bg-white/15"
                 :class="isDimTypeOpen ? 'border-[#00BCD4]' : ''">
              <span :class="selectedDimType ? '' : 'text-white/40'">
                {{ selectedDimType || 'Auto-select from project...' }}
              </span>
              <div class="text-white/50 transition-transform duration-200"
                   :class="isDimTypeOpen ? 'rotate-180' : ''">▼</div>
            </div>
            <div v-if="isDimTypeOpen"
                 class="absolute z-[40] w-full mt-1 bg-[#0A1D4A]/90 backdrop-blur-xl border border-white/25 rounded-xl 
                 overflow-hidden shadow-2xl animate-fade-in max-h-48 overflow-y-auto custom-scrollbar">
              <!-- Auto-select option -->
              <div @click="selectedDimType = ''; isDimTypeOpen = false"
                   class="px-3 py-2 text-xs text-white/50 italic hover:bg-white/10 transition cursor-pointer border-b border-white/10">
                Auto-select from project
              </div>
              <div v-for="dt in props.dimTypes" :key="dt"
                   @click="selectedDimType = dt; isDimTypeOpen = false"
                   class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                   :class="selectedDimType === dt ? 'bg-white/20 font-medium' : ''">
                {{ dt }}
              </div>
              <div v-if="props.dimTypes.length === 0"
                   class="px-3 py-2 text-xs text-white/40 italic">
                No dimension styles found in project
              </div>
            </div>
          </div>
        </div>

        <!-- WHAT TO REFERENCE -->
        <div class="space-y-2 bg-black/10 border border-white/10 rounded-xl p-3">
          <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1">Reference</div>

          <label class="flex items-center justify-between cursor-pointer py-1"
                 @click="includeOpenings = !includeOpenings">
            <div class="flex items-center gap-2">
              <Icon name="lucide:door-open" class="text-sm text-[#00BCD4]" />
              <span class="text-xs">Door &amp; Window Openings</span>
            </div>
            <div class="w-8 h-5 rounded-full transition-all flex items-center px-0.5 flex-shrink-0"
                 :class="includeOpenings ? 'bg-[#00BCD4]' : 'bg-white/20'">
              <div class="w-4 h-4 rounded-full bg-white shadow-sm transition-transform"
                   :class="includeOpenings ? 'translate-x-3' : 'translate-x-0'"></div>
            </div>
          </label>

          <label class="flex items-center justify-between cursor-pointer py-1"
                 @click="includeGrids = !includeGrids">
            <div class="flex items-center gap-2">
              <Icon name="lucide:hash" class="text-sm text-[#00BCD4]" />
              <span class="text-xs">Structural Grids</span>
            </div>
            <div class="w-8 h-5 rounded-full transition-all flex items-center px-0.5 flex-shrink-0"
                 :class="includeGrids ? 'bg-[#00BCD4]' : 'bg-white/20'">
              <div class="w-4 h-4 rounded-full bg-white shadow-sm transition-transform"
                   :class="includeGrids ? 'translate-x-3' : 'translate-x-0'"></div>
            </div>
          </label>
        </div>

        <!-- DIMENSION LAYER SELECTION -->
        <div class="space-y-2 bg-black/10 border border-white/10 rounded-xl p-3">
          <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1">Layers (Exterior Stack)</div>
          
          <label class="flex items-center justify-between cursor-pointer py-1" @click="includeTotal = !includeTotal">
            <div class="flex items-center gap-2">
              <Icon name="lucide:layers" class="text-sm text-[#00BCD4]" />
              <span class="text-xs">Layer 1: Overall (Total)</span>
            </div>
            <div :class="includeTotal ? 'bg-[#00BCD4]' : 'bg-white/20'" class="w-8 h-5 rounded-full transition-all flex items-center px-0.5">
              <div :class="includeTotal ? 'translate-x-3' : 'translate-x-0'" class="w-4 h-4 rounded-full bg-white shadow-sm transition-transform"></div>
            </div>
          </label>

          <label class="flex items-center justify-between cursor-pointer py-1" @click="includeGridsOnly = !includeGridsOnly">
            <div class="flex items-center gap-2">
              <Icon name="lucide:grid-3x3" class="text-sm text-[#00BCD4]" />
              <span class="text-xs">Layer 2: Grid-to-Grid</span>
            </div>
            <div :class="includeGridsOnly ? 'bg-[#00BCD4]' : 'bg-white/20'" class="w-8 h-5 rounded-full transition-all flex items-center px-0.5">
              <div :class="includeGridsOnly ? 'translate-x-3' : 'translate-x-0'" class="w-4 h-4 rounded-full bg-white shadow-sm transition-transform"></div>
            </div>
          </label>

          <label class="flex items-center justify-between cursor-pointer py-1" @click="includeDetail = !includeDetail">
            <div class="flex items-center gap-2">
              <Icon name="lucide:ruler" class="text-sm text-[#00BCD4]" />
              <span class="text-xs">Layer 3: Interior Detail</span>
            </div>
            <div :class="includeDetail ? 'bg-[#00BCD4]' : 'bg-white/20'" class="w-8 h-5 rounded-full transition-all flex items-center px-0.5">
              <div :class="includeDetail ? 'translate-x-3' : 'translate-x-0'" class="w-4 h-4 rounded-full bg-white shadow-sm transition-transform"></div>
            </div>
          </label>

          <label class="flex items-center justify-between cursor-pointer py-1" @click="includeInterior = !includeInterior">
            <div class="flex items-center gap-2">
              <Icon name="lucide:layout-panel-left" class="text-sm text-[#00BCD4]" />
              <span class="text-xs">Interior Room Strings</span>
            </div>
            <div :class="includeInterior ? 'bg-[#00BCD4]' : 'bg-white/20'" class="w-8 h-5 rounded-full transition-all flex items-center px-0.5">
              <div :class="includeInterior ? 'translate-x-3' : 'translate-x-0'" class="w-4 h-4 rounded-full bg-white shadow-sm transition-transform"></div>
            </div>
          </label>
        </div>

        <!-- OFFSET FROM WALL FACE -->
        <div class="bg-black/10 border border-white/10 rounded-xl p-3">
          <div class="flex justify-between items-center mb-2">
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold">
              Offset from Wall Face
            </label>
            <span class="text-xs font-bold text-[#00BCD4]">{{ offsetMm }} mm</span>
          </div>
          <input type="range" min="400" max="2000" step="100"
                 v-model.number="offsetMm"
                 class="w-full h-1.5 rounded-full appearance-none cursor-pointer dim-slider" />
          <div class="flex justify-between text-[9px] text-white/30 mt-1">
            <span>400</span><span>1200</span><span>2000</span>
          </div>
        </div>

        <!-- INTERIOR STRING INSET -->
        <div class="bg-black/10 border border-white/10 rounded-xl p-3">
          <div class="flex justify-between items-center mb-2">
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold">
              Interior String Position
            </label>
            <span class="text-xs font-bold text-[#00BCD4]">{{ insetMm }} mm</span>
          </div>
          <div class="text-[10px] text-white/40 mb-2">Distance from building edge where interior strings are drawn</div>
          <input type="range" min="200" max="3000" step="100"
                 v-model.number="insetMm"
                 class="w-full h-1.5 rounded-full appearance-none cursor-pointer dim-slider" />
          <div class="flex justify-between text-[9px] text-white/30 mt-1">
            <span>200</span><span>1500</span><span>3000</span>
          </div>
          <!-- Interior Search Depth -->
          <div class="mt-3">
            <div class="flex items-center justify-between mb-1">
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold">
                Interior Depth
              </label>
              <span class="text-xs font-bold text-[#00BCD4]">{{ depthMm === 0 ? 'Full' : depthMm + ' mm' }}</span>
            </div>
            <div class="text-[10px] text-white/40 mb-2">How far from each exterior wall to pick up interior strings. Increase for large buildings. 0 = full building.</div>
            <input type="range" min="0" max="20000" step="500"
                   v-model.number="depthMm"
                   class="w-full h-1.5 rounded-full appearance-none cursor-pointer dim-slider" />
            <div class="flex justify-between text-[9px] text-white/30 mt-1">
              <span>Full</span><span>10m</span><span>20m</span>
            </div>
          </div>
        </div>

        <!-- SMART EXTERIOR PLACEMENT -->
        <div class="flex items-center justify-between bg-white/5 border border-white/10 rounded-xl px-4 py-3">
          <div>
            <div class="text-xs font-medium">Smart Exterior Placement</div>
            <div class="text-[10px] text-white/40 mt-0.5">Exterior walls dimension outward from building perimeter</div>
          </div>
          <button @click="smartExterior = !smartExterior"
            class="w-10 h-6 rounded-full transition-all flex items-center px-0.5 flex-shrink-0"
            :class="smartExterior ? 'bg-[#00BCD4]' : 'bg-white/20'">
            <div class="w-5 h-5 rounded-full bg-white shadow-sm transition-transform"
              :class="smartExterior ? 'translate-x-4' : 'translate-x-0'"></div>
          </button>
        </div>

        <!-- STAGE FILTER -->
        <div class="space-y-2 bg-black/10 border border-white/10 rounded-xl p-3">
          <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold">Stage Filter</div>
          <div class="flex bg-white/5 rounded-lg p-1 border border-white/10">
            <button v-for="stg in ['WV', 'PD', 'DD', 'CD', 'Other']" :key="stg"
              @click="toggleStage(stg)"
              class="flex-1 rounded-md text-[11px] py-1 transition-all"
              :class="activeStages.includes(stg)
                ? 'bg-white/20 text-white font-bold'
                : 'text-white/40 hover:text-white/70'">
              {{ stg }}
            </button>
          </div>

          <!-- LEVEL FILTER -->
          <div v-if="availableLevels.length > 0">
            <label class="text-[10px] text-white/40 block mb-1">Level</label>
            <div class="flex flex-wrap gap-1.5 max-h-20 overflow-y-auto custom-scrollbar">
              <button v-for="lvl in availableLevels" :key="lvl"
                @click="toggleLevel(lvl)"
                class="px-2 py-1 rounded-md text-[10px] transition-all"
                :class="activeLevels.includes(lvl)
                  ? 'bg-[#00BCD4] text-[#0A1D4A] font-bold'
                  : 'bg-white/5 text-white/50 hover:bg-white/10 hover:text-white'">
                {{ lvl || '(no level)' }}
              </button>
            </div>
          </div>
        </div>

        <!-- VIEW SELECTOR -->
        <div>
          <div class="flex justify-between items-end mb-1.5">
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold">
              Floor Plan Views
            </label>
            <button @click="toggleSelectAll" class="text-[10px] text-[#00BCD4] hover:text-white transition">
              {{ isAllSelected ? 'Deselect All' : 'Select All' }}
            </button>
          </div>
          <div class="bg-black/20 border border-white/10 rounded-xl p-2 max-h-44 overflow-y-auto custom-scrollbar">
            <div v-if="filteredViews.length > 0" class="space-y-1">
              <button v-for="view in filteredViews" :key="view.id"
                @click="toggleView(view.id)"
                class="w-full px-3 py-1.5 rounded-lg text-[11px] text-left transition-all flex items-center gap-2"
                :class="selectedViewIds.includes(view.id)
                  ? 'bg-[#00BCD4] text-[#0A1D4A] font-bold'
                  : 'bg-white/5 text-white/60 hover:bg-white/10 hover:text-white'">
                <Icon name="lucide:grid-2x2" class="text-sm flex-shrink-0" />
                <span class="truncate flex-1">{{ view.name }}</span>
                <span class="text-[9px] opacity-60">{{ view.level || view.stage || '' }}</span>
              </button>
            </div>
            <div v-else class="text-xs text-white/30 text-center py-3">
              No floor plan views match the current filters.
            </div>
          </div>
          <div class="text-[10px] text-white/30 mt-1 text-right">
            {{ selectedViewIds.length }} of {{ filteredViews.length }} selected
          </div>
        </div>

      </div>

      <!-- FOOTER -->
      <div class="p-5 border-t border-white/10 flex-shrink-0">
        <button @click="submit"
          :disabled="!canSubmit"
          class="w-full py-3 rounded-xl font-bold text-sm transition-all flex items-center justify-center gap-2"
          :class="canSubmit
            ? 'bg-[#00BCD4] hover:bg-[#26C6DA] text-[#0A1D4A] shadow-lg shadow-cyan-900/20'
            : 'bg-white/10 text-white/30 cursor-not-allowed'">
          <Icon name="tabler:ruler-measure" class="text-base" />
          <span>Dimension {{ selectedViewIds.length }} View{{ selectedViewIds.length !== 1 ? 's' : '' }}</span>
        </button>
      </div>

    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue';

const props = defineProps({
  dimTypes:       { type: Array, default: () => [] },
  floorPlanViews: { type: Array, default: () => [] },
});

const emit = defineEmits(['close', 'submit']);

// ── State ────────────────────────────────────────────────────────────────────
const selectedDimType  = ref('');
const includeOpenings  = ref(true);
const includeGrids     = ref(true);
const offsetMm         = ref(1400);
const insetMm          = ref(1200);
const depthMm          = ref(5000); // Interior string search depth. 0 = full building. Increase for large plans.
const smartExterior    = ref(true);
const activeStages     = ref([]);
const activeLevels     = ref([]);
const selectedViewIds  = ref([]);
const isDimTypeOpen    = ref(false);
const includeTotal     = ref(true); // Layer 1
const includeGridsOnly = ref(true); // Layer 2
const includeDetail    = ref(true); // Layer 3 (controls Pass1 opening refs)
const includeInterior  = ref(true); // Pass 3: interior room strings (H + V through building)

// ── Computed ─────────────────────────────────────────────────────────────────
const availableLevels = computed(() => {
  const levels = new Set();
  props.floorPlanViews.forEach(v => {
    if (activeStages.value.length > 0 && !passesStageFilter(v)) return;
    if (v.level) levels.add(v.level);
  });
  return Array.from(levels).sort();
});

const filteredViews = computed(() =>
  props.floorPlanViews.filter(v => {
    if (activeStages.value.length > 0 && !passesStageFilter(v)) return false;
    if (activeLevels.value.length > 0 && !activeLevels.value.includes(v.level)) return false;
    return true;
  })
);

const isAllSelected = computed(() =>
  filteredViews.value.length > 0 &&
  filteredViews.value.every(v => selectedViewIds.value.includes(v.id))
);

const canSubmit = computed(() => selectedViewIds.value.length > 0);

// ── Helpers ───────────────────────────────────────────────────────────────────
function passesStageFilter(v) {
  const hasOther    = activeStages.value.includes('Other');
  const stdStages   = activeStages.value.filter(s => s !== 'Other');
  const viewStage   = v.stage || '';
  const isStd       = stdStages.includes(viewStage);
  const isOther     = hasOther && !['WV', 'PD', 'DD', 'CD'].includes(viewStage);
  return isStd || isOther;
}

function pruneSelection() {
  const valid = new Set(filteredViews.value.map(v => v.id));
  selectedViewIds.value = selectedViewIds.value.filter(id => valid.has(id));
}

// ── Actions ───────────────────────────────────────────────────────────────────
function toggleStage(stg) {
  const idx = activeStages.value.indexOf(stg);
  if (idx >= 0) activeStages.value.splice(idx, 1);
  else activeStages.value.push(stg);
  pruneSelection();
}

function toggleLevel(lvl) {
  const idx = activeLevels.value.indexOf(lvl);
  if (idx >= 0) activeLevels.value.splice(idx, 1);
  else activeLevels.value.push(lvl);
  pruneSelection();
}

function toggleView(id) {
  const idx = selectedViewIds.value.indexOf(id);
  if (idx >= 0) selectedViewIds.value.splice(idx, 1);
  else selectedViewIds.value.push(id);
}

function toggleSelectAll() {
  if (isAllSelected.value) selectedViewIds.value = [];
  else selectedViewIds.value = filteredViews.value.map(v => v.id);
}

function submit() {
  if (!canSubmit.value) return;
  emit('submit', {
    view_ids:           selectedViewIds.value,
    include_openings:   includeOpenings.value,
    include_grids:      includeGrids.value,
    include_total:      includeTotal.value,
    include_grids_only: includeGridsOnly.value,
    include_detail:     includeDetail.value,
    include_interior:   includeInterior.value,
    offset_mm:          offsetMm.value,
    inset_mm:           insetMm.value,
    depth_mm:           depthMm.value,
    smart_exterior:     smartExterior.value,
    dim_type_name:      selectedDimType.value,
  });
}

const handleKeydown = (e) => { if (e.key === 'Escape') isDimTypeOpen.value = false; };
onMounted(()   => document.addEventListener('keydown', handleKeydown));
onUnmounted(() => document.removeEventListener('keydown', handleKeydown));
</script>

<style scoped>
.custom-scrollbar::-webkit-scrollbar { width: 6px; }
.custom-scrollbar::-webkit-scrollbar-track { background: transparent; }
.custom-scrollbar::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.15); border-radius: 10px; }
.custom-scrollbar::-webkit-scrollbar-thumb:hover { background: rgba(255,255,255,0.25); }
.dim-slider { accent-color: #00BCD4; }
@keyframes fade-in-up { from { opacity: 0; transform: translateY(20px) scale(0.95); } to { opacity: 1; transform: translateY(0) scale(1); } }
.animate-fade-in-up { animation: fade-in-up 0.2s ease-out forwards; }
@keyframes fade-in { from { opacity: 0; transform: translateY(-5px); } to { opacity: 1; transform: translateY(0); } }
.animate-fade-in { animation: fade-in 0.15s ease-out forwards; }
</style>
