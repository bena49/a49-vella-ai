<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40 
            backdrop-blur-2xl border border-white/20 
            text-white rounded-3xl w-full max-w-lg shadow-2xl flex flex-col 
            min-h-[600px] max-h-[90vh] animate-fade-in-up">
      
      <div class="p-5 border-b border-white/10 flex justify-between items-center flex-shrink-0">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full bg-[#4EE29B]/20 flex items-center justify-center text-[#4EE29B]">
            <Icon name="lucide:grid-2x2" class="text-xl" />
          </div>
          <h2 class="text-sm font-bold tracking-wide">VIEW CREATOR</h2>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <div class="p-6 pb-10 space-y-5 overflow-y-auto custom-scrollbar flex-1">
        
        <div class="grid grid-cols-2 gap-4">
          <div>
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">View Type</label>
            <div class="relative">
              <div @click="toggleDropdown('viewType')" 
                   class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                   text-white focus:border-[#4EE29B] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                   :class="isViewTypeOpen ? 'border-[#4EE29B]' : ''">
                <span>{{ selectedType }}</span>
                <div class="text-white/50 transform transition-transform duration-200" 
                     :class="isViewTypeOpen ? 'rotate-180' : ''">▼</div>
              </div>
              
              <div v-if="isViewTypeOpen" 
                   class="absolute z-[40] w-full mt-1 bg-[#0A1D4A]/10 backdrop-blur-xl border border-white/25 rounded-xl overflow-hidden shadow-2xl animate-fade-in">
                <div v-for="option in availableViewTypes" 
                     :key="option"
                     @click="selectViewType(option)"
                     class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                     :class="selectedType === option ? 'bg-white/20 font-medium' : ''">
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
                @click="!isStageDisabled(stg) && (selectedStage = stg); closeAllDropdowns()"
                :disabled="isStageDisabled(stg)"
                class="flex-1 rounded-lg text-xs py-1 transition-all"
                :class="[
                  selectedStage === stg ? 'bg-white/20 text-white font-bold shadow-sm' : '',
                  !selectedStage === stg && !isStageDisabled(stg) ? 'text-white/40 hover:text-white/70' : '',
                  isStageDisabled(stg) ? 'text-white/10 cursor-not-allowed' : ''
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
            <button @click="toggleSelectAll(); closeAllDropdowns()" class="text-[10px] text-[#4EE29B] hover:text-white transition">
              {{ isAllSelected ? 'Deselect All' : 'Select All' }}
            </button>
          </div>
          
          <div class="bg-black/20 border border-white/10 rounded-xl p-2 max-h-32 overflow-y-auto custom-scrollbar">
            <div class="grid grid-cols-4 gap-2"> 
              <button 
                v-for="lvl in displayLevels" :key="lvl"
                @click="!isLevelDisabled(lvl) && toggleLevel(lvl); closeAllDropdowns()"
                :disabled="isLevelDisabled(lvl)"
                class="px-2 py-1.5 rounded-lg text-[11px] text-center transition-all duration-100 truncate"
                :class="[
                  selectedLevels.includes(lvl) 
                    ? 'bg-[#4EE29B] text-[#0A1D4A] font-bold' 
                    : isLevelDisabled(lvl)
                      ? 'bg-white/5 text-white/10 cursor-not-allowed border border-white/5'
                      : 'bg-white/5 text-white/60 hover:bg-white/10 hover:text-white'
                ]"
              >
                {{ lvl }}
              </button>
            </div>
          </div>
          <div class="text-[10px] text-white/30 mt-1 text-right">
            {{ selectedLevels.length }} levels selected
          </div>
        </div>

        <div v-else>
          <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Number of Views</label>
          <div class="flex items-center gap-4 bg-white/5 border border-white/10 rounded-xl p-3">
             <button @click="batchCount > 0 ? batchCount-- : null" 
                     class="w-8 h-8 rounded-lg bg-white/10 hover:bg-white/20 flex items-center justify-center transition">
               -
             </button>
             <div class="flex-1 text-center font-bold text-lg">{{ batchCount }}</div>
             <button @click="batchCount++" 
                     class="w-8 h-8 rounded-lg bg-white/10 hover:bg-white/20 flex items-center justify-center transition">
               +
             </button>
          </div>
        </div>

        <div>
          <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">View Template</label>
          <div class="relative">
            <div @click="toggleDropdown('template')" 
                 class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                 text-white focus:border-[#4EE29B] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                 :class="isTemplateOpen ? 'border-[#4EE29B]' : ''">
              <span>{{ selectedTemplate || 'Default (Let Vella decide)' }}</span>
              <div class="text-white/50 transform transition-transform duration-200" 
                   :class="isTemplateOpen ? 'rotate-180' : ''">▼</div>
            </div>
            
            <div v-if="isTemplateOpen" 
                class="absolute z-[40] w-full bg-[#0A1D4A]/70 backdrop-blur-xl border border-white/25 rounded-xl 
                overflow-hidden shadow-2xl animate-fade-in max-h-48 overflow-y-auto custom-scrollbar"
                :class="isLevelBased ? 'bottom-full mb-1' : 'top-full mt-1'">
              <div @click="selectTemplate(null)"
                   class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10"
                   :class="selectedTemplate === null ? 'bg-white/20 font-medium' : ''">
                Default (Let Vella decide)
              </div>
              <div v-for="option in filteredTemplates" 
                   :key="option"
                   @click="selectTemplate(option)"
                   class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                   :class="selectedTemplate === option ? 'bg-white/20 font-medium' : ''">
                {{ option }}
              </div>
            </div>
          </div>
        </div>

        <div v-if="isLevelBased">
          <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Scope Box (Optional)</label>
          <div class="relative">
            <div @click="toggleDropdown('scopeBox')" 
                 class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                 text-white focus:border-[#4EE29B] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                 :class="isScopeBoxOpen ? 'border-[#4EE29B]' : ''">
              <span>{{ selectedScopeBox || 'None' }}</span>
              <div class="text-white/50 transform transition-transform duration-200" 
                   :class="isScopeBoxOpen ? 'rotate-180' : ''">▼</div>
            </div>
            
            <div v-if="isScopeBoxOpen" 
              class="absolute z-[40] w-full bottom-full mb-1 bg-[#0A1D4A]/70 backdrop-blur-xl border border-white/25 rounded-xl 
              overflow-hidden shadow-2xl animate-fade-in max-h-48 overflow-y-auto custom-scrollbar">
              <div @click="selectScopeBox(null)"
                   class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10"
                   :class="selectedScopeBox === null ? 'bg-white/20 font-medium' : ''">
                None
              </div>
              <div v-for="option in filteredScopeBoxes" 
                   :key="option"
                   @click="selectScopeBox(option)"
                   class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                   :class="selectedScopeBox === option ? 'bg-white/20 font-medium' : ''">
                {{ option }}
              </div>
            </div>
          </div>
        </div>

      </div>

      <div class="p-5 border-t border-white/10 flex-shrink-0">
        <button 
          @click="generateCommand"
          :disabled="(isLevelBased && selectedLevels.length === 0) || (!isLevelBased && batchCount < 1)"
          class="w-full py-3 rounded-xl font-bold text-sm transition-all duration-200 flex items-center justify-center gap-2"
          :class="(isLevelBased && selectedLevels.length > 0) || (!isLevelBased && batchCount > 0)
            ? 'bg-[#4EE29B] hover:bg-[#6ff2b3] text-[#0A1D4A] shadow-lg shadow-green-900/20 translate-y-0' 
            : 'bg-white/10 text-white/30 cursor-not-allowed'"
        >
          <span>Generate Command</span>
          <Icon name="lucide:arrow-up" class="text-base rotate-90" />
        </button>
      </div>

    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'; 
import { getFilteredTemplates, getFilteredScopeBoxes } from '~/utils/a49Standards';

const props = defineProps({
  levels: { type: Array, default: () => [] },
  templates: { type: Array, default: () => [] },
  scopeBoxes: { type: Array, default: () => [] },
  initialStage: { type: String, default: 'CD' }
});

const emit = defineEmits(['close', 'submit', 'refresh-data']); // Added refresh-data

// --- MOCK DATA ---
const fallbackLevels = Array.from({ length: 10 }, (_, i) => `L${i + 1}`); 
const fallbackScopeBoxes = ["SB_PLAN_OVERALL", "SB_PLAN_STAIRS", "SB_PLAN_CORE"];
const fallbackTemplates = ["A49_CD_A1_FLOOR PLAN", "A49_CD_A1_REFLECTED CEILING PLAN", "A49_PD_FLOOR PLAN"];

// --- COMPUTED DATA SOURCES ---
const displayLevels = computed(() => (props.levels && props.levels.length > 0) ? props.levels : fallbackLevels);
const displayTemplates = computed(() => (props.templates && props.templates.length > 0) ? props.templates : fallbackTemplates);
const displayScopeBoxes = computed(() => (props.scopeBoxes && props.scopeBoxes.length > 0) ? props.scopeBoxes : fallbackScopeBoxes);

// 💥 STATE DEFINITIONS MUST COME FIRST (Before they are watched)
const selectedLevels = ref([]);
const selectedType = ref("Floor Plan");
const selectedStage = ref(props.initialStage);
const selectedTemplate = ref(null);
const selectedScopeBox = ref(null);
const batchCount = ref(0);

// Dropdown states
const isViewTypeOpen = ref(false);
const isTemplateOpen = ref(false);
const isScopeBoxOpen = ref(false);

// 💥 FILTER VIEW TYPES BASED ON STAGE
const availableViewTypes = computed(() => {
  const all = ["Area Plan", "Floor Plan", "Ceiling Plan", "Schedule", "Detail View"];
  
  if (selectedStage.value === "WV") {
    return ["Floor Plan", "Ceiling Plan"]; 
  }
  
  // New Rule: No Area Plans in CD
  if (selectedStage.value === "CD") {
    return all.filter(t => t !== "Area Plan");
  }
  
  return all;
});

// 💥 Auto-reset if Stage becomes invalid for current View Type
watch(selectedStage, (newStage) => {
  const isWVRestricted = ["Detail View", "Schedule", "Area Plan"].includes(selectedType.value);
  const isCDRestricted = selectedType.value === "Area Plan"; // Area Plan not allowed in CD

  if ((newStage === "WV" && isWVRestricted) || (newStage === "CD" && isCDRestricted)) {
    selectedType.value = "Floor Plan";
    
    // Full Reset
    selectedLevels.value = [];
    selectedTemplate.value = null;
    selectedScopeBox.value = null;
    batchCount.value = 0;
  }
});

// 💥 NEW: Auto-switch Stage when "Area Plan" is selected
watch(selectedType, (newType) => {
  // Full State Reset
  selectedLevels.value = [];
  selectedTemplate.value = null;
  selectedScopeBox.value = null;
  batchCount.value = 0;

  // Logic: If Area Plan, ensure Stage is PD or DD
  if (newType === "Area Plan") {
    if (!["PD", "DD"].includes(selectedStage.value)) {
      selectedStage.value = "PD"; // Default to PD
    }
  }
});

// 💥 UPDATED: Disable SITE for both Ceiling Plans AND Area Plans
const isLevelDisabled = (lvl) => {
  const type = selectedType.value;
  const isRestrictedType = type === "Ceiling Plan" || type === "Area Plan";
  const isSite = lvl.toUpperCase().includes("SITE");
  return isRestrictedType && isSite;
};

// 💥 NEW: Helper to disable WV and CD buttons for Area Plans
const isStageDisabled = (stg) => {
  if (selectedType.value === "Area Plan") {
    return !["PD", "DD"].includes(stg);
  }
  return false;
};

// Smart "All Selected" check
const isAllSelected = computed(() => {
  const validLevels = displayLevels.value.filter(lvl => !isLevelDisabled(lvl));
  return validLevels.length > 0 && validLevels.every(lvl => selectedLevels.value.includes(lvl));
});

const isLevelBased = computed(() => {
  return ["Floor Plan", "Ceiling Plan", "Area Plan"].includes(selectedType.value);
});

// 💥 Strict Template Filtering for Area Plans
const filteredTemplates = computed(() => {
  // 1. Standard Filter
  let list = getFilteredTemplates(selectedStage.value, selectedType.value, displayTemplates.value);
  
  // 2. Area Plan Override (Force EIA/NFA only)
  if (selectedType.value === "Area Plan") {
    const stagePrefix = `A49_${selectedStage.value}_`; // e.g. "A49_PD_"
    return list.filter(t => 
      t.includes(stagePrefix) && 
      (t.includes("EIA AREA PLAN") || t.includes("NFA AREA PLAN"))
    );
  }
  
  return list;
});

const filteredScopeBoxes = computed(() => {
  return getFilteredScopeBoxes(selectedType.value, displayScopeBoxes.value);
});

// --- ACTIONS ---
const toggleDropdown = (dropdownName) => {
  const wasOpen = { viewType: isViewTypeOpen.value, template: isTemplateOpen.value, scopeBox: isScopeBoxOpen.value };
  closeAllDropdowns();
  
  switch(dropdownName) {
    case 'viewType': if (!wasOpen.viewType) isViewTypeOpen.value = true; break;
    case 'template': if (!wasOpen.template) isTemplateOpen.value = true; break;
    case 'scopeBox': if (!wasOpen.scopeBox) isScopeBoxOpen.value = true; break;
  }
};

const selectViewType = (option) => {
  selectedType.value = option;
  isViewTypeOpen.value = false;
  
  // Full State Reset
  selectedLevels.value = []; 
  selectedTemplate.value = null; 
  selectedScopeBox.value = null; 
  batchCount.value = 0; 
};

const selectTemplate = (option) => { selectedTemplate.value = option; isTemplateOpen.value = false; };
const selectScopeBox = (option) => { selectedScopeBox.value = option; isScopeBoxOpen.value = false; };
const closeAllDropdowns = () => { isViewTypeOpen.value = false; isTemplateOpen.value = false; isScopeBoxOpen.value = false; };

function toggleLevel(lvl) {
  if (isLevelDisabled(lvl)) return; 
  
  if (selectedLevels.value.includes(lvl)) {
    selectedLevels.value = selectedLevels.value.filter(l => l !== lvl);
  } else {
    selectedLevels.value.push(lvl);
  }
}

function toggleSelectAll() {
  if (isAllSelected.value) {
    selectedLevels.value = [];
  } else {
    selectedLevels.value = displayLevels.value.filter(lvl => !isLevelDisabled(lvl));
  }
}

function generateCommand() {
  let cmd = "";

  if (isLevelBased.value) {
    if (selectedLevels.value.length === 0) return;
    const lvlStr = selectedLevels.value.join(", ");
    cmd = `Create ${selectedType.value} for ${lvlStr} in ${selectedStage.value}`;
  } else {
    cmd = `Create ${batchCount.value} ${selectedType.value}s in ${selectedStage.value}`;
  }
  
  if (selectedTemplate.value) {
    cmd += ` using ${selectedTemplate.value}`;
  }
  
  if (isLevelBased.value && selectedScopeBox.value) {
    const connector = selectedTemplate.value ? " and " : " using ";
    cmd += `${connector}${selectedScopeBox.value}`;
  }

  emit('submit', cmd);
  emit('close'); 
  closeAllDropdowns();
}

const handleKeydown = (event) => { if (event.key === 'Escape') closeAllDropdowns(); };
onMounted(() => { 
  document.addEventListener('keydown', handleKeydown); 
  emit('refresh-data'); // Trigger the secret refresh
});
onUnmounted(() => { document.removeEventListener('keydown', handleKeydown); });
</script>

<style scoped>
.custom-scrollbar::-webkit-scrollbar { width: 6px; }
.custom-scrollbar::-webkit-scrollbar-track { background: transparent; margin: 2px 0; }
.custom-scrollbar::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.15); border-radius: 10px; }
.custom-scrollbar::-webkit-scrollbar-thumb:hover { background: rgba(255,255,255,0.25); }
.z-50 { z-index: 50; }
.relative { position: relative; }

@keyframes fade-in-up { from { opacity: 0; transform: translateY(20px) scale(0.95); } to { opacity: 1; transform: translateY(0) scale(1); } }
.animate-fade-in-up { animation: fade-in-up 0.2s ease-out forwards; }
@keyframes fade-in { from { opacity: 0; transform: translateY(-5px); } to { opacity: 1; transform: translateY(0); } }
.animate-fade-in { animation: fade-in 0.15s ease-out forwards; }
</style>