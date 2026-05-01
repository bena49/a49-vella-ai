<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40 
        backdrop-blur-2xl border border-white/20 
        text-white rounded-3xl w-full max-w-lg shadow-2xl flex flex-col 
        min-h-[600px] max-h-[90vh] animate-fade-in-up">
      
      <div class="p-5 border-b border-white/10 flex justify-between items-center">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full bg-[#60A5FA]/20 flex items-center justify-center text-[#60A5FA]">
            <Icon name="lucide:files" class="text-xl" />
          </div>
          <h2 class="text-sm font-bold tracking-wide">SHEET CREATOR</h2>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <div class="p-6 pb-10 space-y-3 overflow-y-auto custom-scrollbar flex-1">
        
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Sheet Series</label>
            <div class="relative">
              <div @click="toggleDropdown('series')" 
                   class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                   text-white focus:border-[#60A5FA] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                   :class="isSeriesOpen ? 'border-[#60A5FA]' : ''">
                <span>{{ selectedSeries }}</span>
                <div class="text-white/50 transform transition-transform duration-200" 
                     :class="isSeriesOpen ? 'rotate-180' : ''">▼</div>
              </div>
              
              <div v-if="isSeriesOpen" 
                   class="absolute z-50 w-full mt-1 bg-[#0A1D4A]/10 backdrop-blur-xl border border-white/25 rounded-xl overflow-hidden shadow-2xl animate-fade-in">
                <div v-for="option in sheetSeriesOptions" 
                     :key="option"
                     @click="selectSeries(option)"
                     class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                     :class="selectedSeries === option ? 'bg-white/20 font-medium' : ''">
                  {{ option }}
                </div>
              </div>
            </div>
          </div>

          <div>
             <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Stage</label>
             <div class="flex bg-white/5 rounded-xl p-1 border border-white/10">
               <button 
                  v-for="stg in ['WV', 'PD', 'DD', 'CD']" :key="stg"
                  @click="!isCustomSheet && (selectedStage = stg); closeAllDropdowns()"
                  :disabled="isCustomSheet"
                  class="flex-1 rounded-lg text-xs py-1 transition-all"
                  :class="[
                    selectedStage === stg && !isCustomSheet ? 'bg-white/20 text-white font-bold shadow-sm' : '',
                    !selectedStage === stg && !isCustomSheet ? 'text-white/40 hover:text-white/70' : '',
                    isCustomSheet ? 'text-white/10 cursor-not-allowed' : '' 
                  ]"
                >
                 {{ stg }}
               </button>
             </div>
          </div>
        </div>

        <div v-if="isLevelBased">
          <div class="flex justify-between items-end mb-2">
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold block">Select Levels</label>
            <button @click="toggleSelectAll(); closeAllDropdowns()" class="text-[10px] text-[#60A5FA] hover:text-white transition">
              {{ isAllSelected ? 'Deselect All' : 'Select All' }}
            </button>
          </div>
          <div class="bg-black/20 border border-white/10 rounded-xl p-2 max-h-32 overflow-y-auto custom-scrollbar">
            <div class="flex flex-wrap gap-1.5">
              <button
                v-for="lvl in displayLevels" :key="lvl"
                @click="!isLevelDisabled(lvl) && toggleLevel(lvl)"
                :disabled="isLevelDisabled(lvl)"
                class="px-2.5 py-1.5 rounded-lg text-[11px] transition-all duration-100 whitespace-nowrap"
                :class="[
                  selectedLevels.includes(lvl) 
                    ? 'bg-[#60A5FA] text-[#0A1D4A] font-bold' 
                    : isLevelDisabled(lvl) 
                      ? 'bg-white/5 text-white/10 cursor-not-allowed border border-white/5'  /* 💥 Disabled Style */
                      : 'bg-white/5 text-white/60 hover:bg-white/10 hover:text-white'
                ]"
              >
                {{ lvl }}
              </button>
            </div>
          </div>
          <div class="text-[10px] text-white/30 mt-1 text-right">{{ selectedLevels.length }} sheets to be created</div>
        </div>

        <div v-else>
          <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Number of Sheets</label>
          <div class="flex items-center gap-4 bg-white/5 border border-white/10 rounded-xl p-3">
             <button @click="batchCount > 0 ? batchCount-- : null" 
                     class="w-8 h-8 rounded-lg bg-white/10 hover:bg-white/20 flex items-center justify-center transition">-</button>
             <div class="flex-1 text-center font-bold text-lg">{{ batchCount }}</div>
             <button @click="batchCount++" 
                     class="w-8 h-8 rounded-lg bg-white/10 hover:bg-white/20 flex items-center justify-center transition">+</button>
          </div>
        </div>

        <div>
          <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Titleblock</label>
          <div class="relative">
            <div @click="toggleDropdown('titleblock')" 
                 class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                 text-white focus:border-[#60A5FA] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                 :class="isTitleblockOpen ? 'border-[#60A5FA]' : ''">
              <span>{{ selectedTitleblock || 'Default (Auto-Detect)' }}</span>
              <div class="text-white/50 transform transition-transform duration-200" 
                   :class="isTitleblockOpen ? 'rotate-180' : ''">▼</div>
            </div>
            
            <div v-if="isTitleblockOpen" 
                  class="absolute z-50 w-full top-full mt-1 bg-[#0A1D4A]/70 backdrop-blur-xl border border-white/25 rounded-xl 
                  overflow-hidden shadow-2xl animate-fade-in max-h-48 overflow-y-auto custom-scrollbar">
              <div @click="selectTitleblock(null)"
                   class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10"
                   :class="selectedTitleblock === null ? 'bg-white/20 font-medium' : ''">
                Default (Auto-Detect)
              </div>
              <div v-for="option in filteredTitleblocks" 
                   :key="option"
                   @click="selectTitleblock(option)"
                   class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                   :class="selectedTitleblock === option ? 'bg-white/20 font-medium' : ''">
                {{ option }}
              </div>
            </div>
          </div>
        </div>

      </div>

      <div class="p-5 border-t border-white/10">
        <button 
          @click="generateCommand"
          :disabled="(isLevelBased && selectedLevels.length === 0) || (!isLevelBased && batchCount === 0)"
          class="w-full py-3 rounded-xl font-bold text-sm transition-all duration-200 flex items-center justify-center gap-2"
          :class="(isLevelBased && selectedLevels.length > 0) || (!isLevelBased && batchCount > 0)
            ? 'bg-[#60A5FA] hover:bg-[#6faefa] text-[#0A1D4A] shadow-lg shadow-blue-900/20 translate-y-0' 
            : 'bg-white/10 text-white/30 cursor-not-allowed'"
        >
          <span>Generate Command</span>
          <Icon name="lucide:arrow-up" class="rotate-90" />
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue';

const props = defineProps({
  levels: { type: Array, default: () => [] },
  titleblocks: { type: Array, default: () => [] },
  initialStage: { type: String, default: 'CD' }
});

const emit = defineEmits(['close', 'submit']);

// --- MOCK DATA ---
const fallbackLevels = Array.from({ length: 10 }, (_, i) => `LEVEL ${i + 1}`); 
const fallbackTitleblocks = ["A49_TB_A1_Horizontal : Plan Sheet", "A49_TB_A1_Vertical : Plan Sheet"];

// --- COMPUTED SOURCES ---
const displayLevels = computed(() => (props.levels && props.levels.length > 0) ? props.levels : fallbackLevels);
const displayTitleblocks = computed(() => (props.titleblocks && props.titleblocks.length > 0) ? props.titleblocks : fallbackTitleblocks);

const sheetSeriesOptions = [
  "A0 - General / Cover", "A1 - Floor Plans", "A2 - Elevations", "A3 - Sections", 
  "A4 - Wall Sections", "A5 - Ceiling Plans", "A6 - Enlarged Plans", 
  "A7 - Vertical Circ.", "A8 - Schedules", "A9 - Details", 
  "X0 - Custom Sheet"
];

// --- STATE ---
const selectedLevels = ref([]);
const selectedSeries = ref("A1 - Floor Plans");
const selectedStage = ref(props.initialStage);
const selectedTitleblock = ref(null);
const batchCount = ref(0);

const isSeriesOpen = ref(false);
const isTitleblockOpen = ref(false);

// 1. LEVEL BASED CHECK (Simple dependency on selectedSeries)
const isLevelBased = computed(() => {
  const code = selectedSeries.value.split(" - ")[0];
  return ["A1", "A5"].includes(code);
});

// 2. HELPER: DISABLED CHECK (Must be defined before isAllSelected uses it)
const isLevelDisabled = (lvl) => {
  // Rule: No Site for Ceiling Plans (A5)
  const isA5 = selectedSeries.value.startsWith("A5");
  const isSite = lvl.toUpperCase().includes("SITE");
  return isA5 && isSite;
};

// 3. SMART CHECK (Uses isLevelDisabled)
const isAllSelected = computed(() => {
  // Filter out disabled levels from the "Total Available" count
  const validLevels = displayLevels.value.filter(lvl => !isLevelDisabled(lvl));
  
  // Check if we have selected all VALID levels
  return validLevels.length > 0 && 
         // We need to check if every valid level is in selectedLevels
         validLevels.every(lvl => selectedLevels.value.includes(lvl));
});

// 4. Helper to detect Custom Mode
const isCustomSheet = computed(() => selectedSeries.value.startsWith("X0"));

// --- FILTER LOGIC ---
const filteredTitleblocks = computed(() => {
  const allTBs = displayTitleblocks.value;
  const seriesCode = selectedSeries.value.split(" - ")[0];
  return allTBs.filter(tb => {
    const parts = tb.split(":");
    if (parts.length < 2) return true; 
    const typeName = parts[1].toLowerCase().trim();
    
    // A9 = Detail Sheet
    if (seriesCode === "A9") return typeName.includes("detail sheet");
    // Others = Plan Sheet
    return typeName.includes("plan sheet");
  });
});

// --- ACTIONS ---
const toggleDropdown = (name) => {
  if (name === 'series') { isSeriesOpen.value = !isSeriesOpen.value; isTitleblockOpen.value = false; }
  if (name === 'titleblock') { isTitleblockOpen.value = !isTitleblockOpen.value; isSeriesOpen.value = false; }
};

const selectSeries = (opt) => {
  selectedSeries.value = opt;
  isSeriesOpen.value = false;
  
  // LOGIC: Handle Custom Sheet Stage
  if (opt.startsWith("X0")) {
    selectedStage.value = "NONE"; 
  } else if (selectedStage.value === "NONE") {
    selectedStage.value = props.initialStage; // Restore default if coming back from Custom
  }

  // Full Reset
  selectedLevels.value = [];
  selectedTitleblock.value = null; 
  batchCount.value = 0; 
};

const selectTitleblock = (opt) => {
  selectedTitleblock.value = opt;
  isTitleblockOpen.value = false;
};

const closeAllDropdowns = () => { isSeriesOpen.value = false; isTitleblockOpen.value = false; };

function toggleLevel(lvl) {
  if (isLevelDisabled(lvl)) return; // Security check
  
  if (selectedLevels.value.includes(lvl)) {
    selectedLevels.value = selectedLevels.value.filter(l => l !== lvl);
  } else {
    selectedLevels.value.push(lvl);
  }
}

// UPDATED TOGGLE ALL: Respects Disabled Levels
function toggleSelectAll() {
  if (isAllSelected.value) {
    selectedLevels.value = [];
  } else {
    // Only select levels that are NOT disabled
    selectedLevels.value = displayLevels.value.filter(lvl => !isLevelDisabled(lvl));
  }
}

function generateCommand() {
  const seriesCode = selectedSeries.value.split(" - ")[0];
  let cmd = "";
  
  if (isLevelBased.value) {
     if (selectedLevels.value.length === 0) return;
     const lvlStr = selectedLevels.value.join(", ");
     cmd = `Create Sheets ${seriesCode} for ${lvlStr} in ${selectedStage.value}`;
  } else {
     // CUSTOM LOGIC: If X0, the stage might be NONE, which is fine.
     cmd = `Create ${batchCount.value} Sheets ${seriesCode} in ${selectedStage.value}`;
  }
  
  if (selectedTitleblock.value) cmd += ` using titleblock ${selectedTitleblock.value}`;
  
  emit('submit', cmd);
  emit('close');
  closeAllDropdowns();
}

const handleKeydown = (event) => { if (event.key === 'Escape') closeAllDropdowns(); };
onMounted(() => { document.addEventListener('keydown', handleKeydown); });
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