<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40 
            backdrop-blur-2xl border border-white/20 
            text-white rounded-3xl w-full max-w-lg shadow-2xl flex flex-col 
            min-h-[600px] max-h-[90vh] animate-fade-in-up">
      
      <div class="p-5 border-b border-white/10 flex justify-between items-center flex-shrink-0">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full bg-[#00BCD4]/20 flex items-center justify-center text-[#00BCD4]">
            <Icon name="lucide:grid-2x2-plus" class="text-xl" />
          </div>
          <h2 class="text-sm font-bold tracking-wide">CREATE & PLACE WIZARD</h2>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <div class="px-6 pt-4 pb-2">
        <div class="flex items-center justify-between text-[10px] font-bold uppercase tracking-wider text-white/40">
          <div :class="{ 'text-[#00BCD4]': step >= 1 }">1. View Config</div>
          <div class="h-px bg-white/10 flex-1 mx-3 relative">
            <div class="absolute inset-0 bg-[#00BCD4] transition-all duration-300" :style="{ width: step >= 2 ? '100%' : '0%' }"></div>
          </div>
          <div :class="{ 'text-[#60A5FA]': step >= 2 }">2. Sheet Config</div>
          <div class="h-px bg-white/10 flex-1 mx-3 relative">
            <div class="absolute inset-0 bg-[#60A5FA] transition-all duration-300" :style="{ width: step >= 3 ? '100%' : '0%' }"></div>
          </div>
          <div :class="{ 'text-white': step >= 3 }">3. Alignment</div>
        </div>
      </div>

      <div class="p-6 space-y-5 overflow-y-auto custom-scrollbar flex-1 relative">
        
        <div v-if="step === 1" class="space-y-5 animate-fade-in">
          
          <div class="grid grid-cols-2 gap-4">
            <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">View Type</label>
              <div class="relative">
                <div @click="toggleDropdown('viewType')" 
                     class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                     text-white focus:border-[#00BCD4] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                     :class="isViewTypeOpen ? 'border-[#00BCD4]' : ''">
                  <span>{{ form.viewType }}</span>
                  <div class="text-white/50 transform transition-transform duration-200" :class="isViewTypeOpen ? 'rotate-180' : ''">▼</div>
                </div>
                <div v-if="isViewTypeOpen" 
                     class="absolute z-40 w-full mt-1 bg-[#0A1D4A]/90 backdrop-blur-xl border border-white/25 rounded-xl overflow-hidden shadow-2xl animate-fade-in">
                  <div v-for="opt in availableViewTypes" :key="opt" @click="selectViewType(opt)"
                       class="px-3 py-2 text-xs text-white hover:bg-white/15 cursor-pointer border-b border-white/10 last:border-b-0"
                       :class="form.viewType === opt ? 'bg-white/20 font-bold' : ''">
                    {{ opt }}
                  </div>
                </div>
              </div>
            </div>

            <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Stage</label>
              <div class="flex bg-white/5 rounded-xl p-1 border border-white/10">
                <button v-for="stg in ['WV','PD','DD','CD']" :key="stg"
                  @click="!isStageDisabled(stg) && (form.stage = stg)"
                  :disabled="isStageDisabled(stg)"
                  class="flex-1 rounded-lg text-xs py-1 transition-all"
                  :class="[
                    form.stage === stg ? 'bg-white/20 text-white font-bold shadow-sm' : 'text-white/40 hover:text-white/70',
                    isStageDisabled(stg) ? 'text-white/10 cursor-not-allowed' : ''
                  ]">
                  {{ stg }}
                </button>
              </div>
            </div>
          </div>

          <div>
            <div class="flex justify-between items-end mb-2">
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold block">Select Levels</label>
              <button @click="toggleSelectAll" class="text-[10px] text-[#00BCD4] hover:text-white transition">
                {{ isAllSelected ? 'Deselect All' : 'Select All' }}
              </button>
            </div>
            <div class="bg-black/20 border border-white/10 rounded-xl p-2 max-h-32 overflow-y-auto custom-scrollbar">
              <div class="flex flex-wrap gap-1.5">
                <button v-for="lvl in displayLevels" :key="lvl"
                  @click="!isLevelDisabled(lvl) && toggleLevel(lvl)"
                  :disabled="isLevelDisabled(lvl)"
                  class="px-2.5 py-1.5 rounded-lg text-[11px] transition-all duration-100 whitespace-nowrap"
                  :class="[
                    form.levels.includes(lvl) 
                      ? 'bg-[#00BCD4] text-[#0A1D4A] font-bold' 
                      : isLevelDisabled(lvl)
                        ? 'bg-white/5 text-white/10 cursor-not-allowed border border-white/5'
                        : 'bg-white/5 text-white/60 hover:bg-white/10 hover:text-white'
                  ]">
                  {{ lvl }}
                </button>
              </div>
            </div>
          </div>

          <div class="grid grid-cols-1 gap-4">
             <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">View Template</label>
              <div class="relative">
                <div @click="toggleDropdown('template')" 
                     class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                     text-white focus:border-[#00BCD4] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                     :class="isTemplateOpen ? 'border-[#00BCD4]' : ''">
                  <span>{{ form.template || 'Default (Let Vella Decide)' }}</span>
                  <div class="text-white/50 transform transition-transform duration-200" :class="isTemplateOpen ? 'rotate-180' : ''">▼</div>
                </div>
                <div v-if="isTemplateOpen" 
                     class="absolute z-40 w-full bottom-full mb-1 bg-[#0A1D4A]/90 backdrop-blur-xl border border-white/25 rounded-xl overflow-hidden shadow-2xl animate-fade-in max-h-48 overflow-y-auto custom-scrollbar">
                  <div @click="selectTemplate(null)" 
                       class="px-3 py-2 text-xs text-white hover:bg-white/15 cursor-pointer border-b border-white/10"
                       :class="form.template === null ? 'bg-white/20 font-bold' : ''">
                    Default (Let Vella Decide)
                  </div>
                  <div v-for="t in filteredTemplates" :key="t" @click="selectTemplate(t)"
                       class="px-3 py-2 text-xs text-white hover:bg-white/15 cursor-pointer border-b border-white/10 last:border-b-0"
                       :class="form.template === t ? 'bg-white/20 font-bold' : ''">
                    {{ t }}
                  </div>
                </div>
              </div>
            </div>

             <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Scope Box</label>
              <div class="relative">
                <div @click="toggleDropdown('scopeBox')" 
                     class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                     text-white focus:border-[#00BCD4] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                     :class="isScopeBoxOpen ? 'border-[#00BCD4]' : ''">
                  <span>{{ form.scopeBox || 'None' }}</span>
                  <div class="text-white/50 transform transition-transform duration-200" :class="isScopeBoxOpen ? 'rotate-180' : ''">▼</div>
                </div>
                <div v-if="isScopeBoxOpen" 
                     class="absolute z-40 w-full bottom-full mb-1 bg-[#0A1D4A]/90 backdrop-blur-xl border border-white/25 rounded-xl overflow-hidden shadow-2xl animate-fade-in max-h-48 overflow-y-auto custom-scrollbar">
                  <div @click="selectScopeBox(null)" 
                       class="px-3 py-2 text-xs text-white hover:bg-white/15 cursor-pointer border-b border-white/10"
                       :class="form.scopeBox === null ? 'bg-white/20 font-bold' : ''">
                    None
                  </div>
                  <div v-for="sb in filteredScopeBoxes" :key="sb" @click="selectScopeBox(sb)"
                       class="px-3 py-2 text-xs text-white hover:bg-white/15 cursor-pointer border-b border-white/10 last:border-b-0"
                       :class="form.scopeBox === sb ? 'bg-white/20 font-bold' : ''">
                    {{ sb }}
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <div v-else-if="step === 2" class="space-y-5 animate-fade-in">
          
          <div class="bg-[#60A5FA]/10 border border-[#60A5FA]/20 rounded-xl p-3 flex items-start gap-3">
             <div class="text-[#60A5FA] mt-0.5"><Icon name="lucide:files" /></div>
             <div class="text-xs text-white/80 leading-relaxed">
               I will create <strong>{{ form.levels.length }} new sheets</strong> in category <strong>{{ form.sheetCategory }}</strong>.
             </div>
          </div>

          <div>
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Sheet Category</label>
            <div class="relative">
                <div @click="toggleDropdown('sheetCat')" 
                     class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                     text-white focus:border-[#60A5FA] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                     :class="isSheetCatOpen ? 'border-[#60A5FA]' : ''">
                  <span>{{ getSheetCategoryLabel(form.sheetCategory) }}</span>
                  <div class="text-white/50 transform transition-transform duration-200" :class="isSheetCatOpen ? 'rotate-180' : ''">▼</div>
                </div>
                <div v-if="isSheetCatOpen" 
                     class="absolute z-40 w-full mt-1 bg-[#0A1D4A]/90 backdrop-blur-xl border border-white/25 rounded-xl overflow-hidden shadow-2xl animate-fade-in">
                  <div v-for="cat in allowedSheetCategories" :key="cat.val" @click="selectSheetCategory(cat.val)"
                       class="px-3 py-2 text-xs text-white hover:bg-white/15 cursor-pointer border-b border-white/10 last:border-b-0"
                       :class="form.sheetCategory === cat.val ? 'bg-white/20 font-bold' : ''">
                    {{ cat.label }}
                  </div>
                </div>
            </div>
          </div>

          <div>
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Titleblock</label>
            <div class="relative">
              <div @click="toggleDropdown('titleblock')" 
                    class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                    text-white focus:border-[#60A5FA] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15"
                    :class="isTitleblockOpen ? 'border-[#60A5FA]' : ''">
                <span class="truncate" :class="!form.titleblock ? 'text-white/50 italic' : ''">
                    {{ form.titleblock || 'Please Select Titleblock' }}
                </span>
                <div class="text-white/50 transform transition-transform duration-200" :class="isTitleblockOpen ? 'rotate-180' : ''">▼</div>
              </div>
              
              <div v-if="isTitleblockOpen" 
                    class="absolute z-40 w-full mt-1 bg-[#0A1D4A]/90 backdrop-blur-xl border border-white/25 rounded-xl overflow-hidden shadow-2xl animate-fade-in max-h-48 overflow-y-auto custom-scrollbar">
                <div v-for="tb in filteredTitleblocks" :key="tb" 
                      @click="selectTitleblock(tb)"
                      class="px-3 py-2 text-xs text-white hover:bg-white/15 cursor-pointer border-b border-white/10 last:border-b-0"
                      :class="form.titleblock === tb ? 'bg-white/20 font-bold' : ''">
                  {{ tb }}
                </div>
              </div>
            </div>
          </div>
        </div>

        <div v-else-if="step === 3" class="space-y-6 animate-fade-in">
          
          <div>
            <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-3 block text-center">How should the views be aligned?</label>
            
            <div class="grid grid-cols-2 gap-4">
              <div @click="form.placement = 'CENTER'"
                   class="relative p-4 rounded-2xl border cursor-pointer transition-all flex flex-col items-center gap-2 group"
                   :class="form.placement === 'CENTER' ? 'bg-white/20 border-white' : 'bg-white/5 border-white/10 hover:bg-white/10'">
                <div class="w-12 h-16 border-2 border-dashed border-white/30 rounded flex items-center justify-center relative">
                   <div class="w-8 h-8 bg-white/20 rounded"></div>
                   <div class="absolute w-1 h-1 bg-white rounded-full"></div>
                </div>
                <div class="text-xs font-bold">Center of Sheet</div>
                <div class="text-[10px] text-white/50 text-center leading-tight">Standard alignment</div>
              </div>

              <div @click="form.placement = 'MATCH'"
                   class="relative p-4 rounded-2xl border cursor-pointer transition-all flex flex-col items-center gap-2 group"
                   :class="form.placement === 'MATCH' ? 'bg-[#00BCD4]/20 border-[#00BCD4]' : 'bg-white/5 border-white/10 hover:bg-white/10'">
                <div class="w-12 h-16 border-2 border-dashed border-white/30 rounded flex items-center justify-center relative opacity-50">
                   <div class="w-8 h-8 bg-white/20 rounded"></div>
                </div>
                <div class="absolute top-4 left-[3.5rem] w-12 h-16 border-2 border-[#00BCD4] rounded flex items-center justify-center bg-[#0A1D4A]/80 shadow-lg">
                   <div class="w-8 h-8 bg-[#00BCD4]/30 rounded"></div>
                </div>
                
                <div class="text-xs font-bold" :class="form.placement === 'MATCH' ? 'text-[#00BCD4]' : ''">Match Reference</div>
                <div class="text-[10px] text-white/50 text-center leading-tight">Align to existing sheet</div>
              </div>
            </div>
          </div>

          <div v-if="form.placement === 'MATCH'" class="animate-fade-in-up transition-colors p-4 border rounded-xl"
               :class="isValidReference ? 'border-[#00BCD4]/50 bg-[#00BCD4]/5' : 'border-red-500/50 bg-red-500/10'">
            
            <label class="text-[10px] uppercase tracking-wider font-bold mb-1.5 block"
                   :class="isValidReference ? 'text-[#00BCD4]' : 'text-red-400'">
              {{ isValidReference ? 'Reference Sheet Number' : 'Reference Sheet Not Found' }}
            </label>
            
            <div class="relative" ref="refSheetDropdownWrapper">
                <input v-model="form.referenceSheet"
                       type="text"
                       placeholder="Type to search (e.g. 1010)"
                       class="w-full bg-[#0A1D4A]/50 border rounded-lg px-3 py-2 text-sm text-white outline-none font-mono focus:bg-[#0A1D4A]/80 transition"
                       :class="isValidReference ? 'border-[#00BCD4]/50 focus:border-[#00BCD4]' : 'border-red-500/50 focus:border-red-500'"
                       @focus="isRefSheetOpen = true"
                />

                <div v-if="isRefSheetOpen && filteredReferenceSheets.length > 0" 
                     class="absolute z-50 w-full bottom-full mb-1 bg-[#0A1D4A]/95 backdrop-blur-xl border border-white/25 rounded-xl overflow-hidden shadow-2xl animate-fade-in max-h-40 overflow-y-auto custom-scrollbar">
                     
                     <div v-for="s in filteredReferenceSheets" :key="s" 
                          @mousedown.prevent="selectReferenceSheet(s)"
                          class="px-3 py-2 text-xs text-white hover:bg-white/15 cursor-pointer border-b border-white/10 last:border-b-0 font-mono">
                       {{ s }}
                     </div>
                </div>
            </div>

            <div class="text-[10px] mt-2 flex items-center gap-1"
                 :class="isValidReference ? 'text-white/50' : 'text-red-300'">
              <Icon :name="isValidReference ? 'material-symbols:magic-button-outline' : 'lucide:circle-x'" class="text-sm" />
              <span>
                {{ isValidReference 
                   ? 'Sheet found! Vella will copy viewport location.' 
                   : 'Please select a valid existing sheet from the list.' }}
              </span>
            </div>
          </div>

        </div>

      </div>

      <div class="p-5 border-t border-white/10 flex gap-3">
        <button v-if="step > 1" @click="goBack" 
          class="px-5 py-3 rounded-xl font-bold text-sm bg-white/5 hover:bg-white/10 text-white transition">
          Back
        </button>
        
        <button v-if="step < 3" @click="step++" 
          :disabled="!canProceed"
          class="flex-1 py-3 rounded-xl font-bold text-sm transition-all flex items-center justify-center gap-2"
          :class="canProceed 
            ? 'bg-gradient-to-r from-[#00BCD4] to-[#60A5FA] text-[#0A1D4A] shadow-lg shadow-blue-900/30 hover:shadow-blue-900/50' 
            : 'bg-white/10 text-white/30 cursor-not-allowed'">
          Next Step
        </button>

        <button v-if="step === 3" @click="generateCommand" 
          :disabled="!canSubmit"
          class="flex-1 py-3 rounded-xl font-bold text-sm transition-all flex items-center justify-center gap-2"
          :class="canSubmit ? 'bg-gradient-to-r from-[#00BCD4] to-[#60A5FA] text-[#0A1D4A] shadow-lg shadow-blue-900/30' : 'bg-white/10 text-white/30 cursor-not-allowed'">
          <span>Create & Place</span>
        </button>
      </div>

    </div>
  </div>
</template>

<script setup>
import { ref, reactive, computed, watch, onMounted, onUnmounted } from 'vue';
import { getFilteredTemplates, getFilteredScopeBoxes } from '~/utils/a49Standards';

const props = defineProps({
  levels: { type: Array, default: () => [] },
  templates: { type: Array, default: () => [] },
  scopeBoxes: { type: Array, default: () => [] },
  titleblocks: { type: Array, default: () => [] },
  existingSheets: { type: Array, default: () => [] }, 
  initialStage: { type: String, default: 'CD' }
});

const emit = defineEmits(['close', 'submit']);

// --- MOCK DATA ---
const fallbackLevels = Array.from({ length: 10 }, (_, i) => `LEVEL ${i + 1}`); 
const fallbackTBs = ["A49_TB_A1_Horizontal : Plan Sheet", "A49_TB_A1_Vertical : Plan Sheet"];

const displayLevels = computed(() => (props.levels && props.levels.length) ? props.levels : fallbackLevels);
const displayTemplates = computed(() => props.templates || []);
const displayScopeBoxes = computed(() => props.scopeBoxes || []);
const displayTitleblocks = computed(() => (props.titleblocks && props.titleblocks.length) ? props.titleblocks : fallbackTBs);

// --- STATE ---
const step = ref(1);

// Dropdown Toggles
const isViewTypeOpen = ref(false);
const isTemplateOpen = ref(false);
const isScopeBoxOpen = ref(false);
const isSheetCatOpen = ref(false);
const isTitleblockOpen = ref(false);
const isRefSheetOpen = ref(false);

const refSheetDropdownWrapper = ref(null);

const form = reactive({
  viewType: 'Floor Plan',
  stage: props.initialStage,
  levels: [],
  template: null,
  scopeBox: null,
  sheetCategory: 'A1',
  titleblock: null, 
  placement: 'CENTER',
  referenceSheet: ''
});

// Sheet Categories
const allowedSheetCategories = [
  { val: 'A1', label: 'A1 - Floor Plans' },
  { val: 'A5', label: 'A5 - Ceiling Plans' }
];

const getSheetCategoryLabel = (val) => {
    const found = allowedSheetCategories.find(c => c.val === val);
    return found ? found.label : val;
};

// --- STEP 1 LOGIC (VIEW) ---
const availableViewTypes = computed(() => {
  const all = ["Floor Plan", "Ceiling Plan", "Area Plan"];
  if (form.stage === "WV") return ["Floor Plan", "Ceiling Plan"];
  if (form.stage === "CD") return all.filter(t => t !== "Area Plan");
  return all;
});

// Auto-correct inputs if stage changes
watch(() => form.stage, (newStage) => {
  if (newStage === "CD" && form.viewType === "Area Plan") form.viewType = "Floor Plan";
  form.template = null; 
});

// Auto Set Sheet Category
watch(() => form.viewType, (newType) => {
  form.levels = [];
  form.template = null;
  form.scopeBox = null;
  
  if (newType === "Ceiling Plan") {
      form.sheetCategory = "A5";
  } else {
      form.sheetCategory = "A1"; 
  }
});

// Disable Logic
const isStageDisabled = (stg) => {
  if (form.viewType === "Area Plan") return !["PD", "DD"].includes(stg);
  return false;
};

const isLevelDisabled = (lvl) => {
  // Site detection mirrors backend level_matcher SITE phrases so projects
  // with Thai level names ("+0.00 ระดับพื้นดิน") get the same disabled
  // treatment as English "SITE".
  const isSite = lvl.toUpperCase().includes("SITE")
              || lvl.includes("ระดับพื้นดิน")
              || lvl.includes("พื้นดิน");
  if (form.viewType === "Ceiling Plan" && isSite) return true;
  if (form.viewType === "Area Plan" && isSite) return true;
  return false;
};

const isAllSelected = computed(() => {
  const valid = displayLevels.value.filter(l => !isLevelDisabled(l));
  return valid.length > 0 && valid.every(l => form.levels.includes(l));
});

function toggleLevel(lvl) {
  if (form.levels.includes(lvl)) form.levels = form.levels.filter(l => l !== lvl);
  else form.levels.push(lvl);
}

function toggleSelectAll() {
  if (isAllSelected.value) form.levels = [];
  else form.levels = displayLevels.value.filter(l => !isLevelDisabled(l));
}

// Unified Dropdown Toggler
const toggleDropdown = (n) => {
    const wasOpen = { 
        viewType: isViewTypeOpen.value, 
        template: isTemplateOpen.value, 
        scopeBox: isScopeBoxOpen.value,
        sheetCat: isSheetCatOpen.value,
        titleblock: isTitleblockOpen.value
    };
    
    isViewTypeOpen.value = false;
    isTemplateOpen.value = false;
    isScopeBoxOpen.value = false;
    isSheetCatOpen.value = false;
    isTitleblockOpen.value = false;
    isRefSheetOpen.value = false; 

    if(n==='viewType' && !wasOpen.viewType) isViewTypeOpen.value = true;
    if(n==='template' && !wasOpen.template) isTemplateOpen.value = true;
    if(n==='scopeBox' && !wasOpen.scopeBox) isScopeBoxOpen.value = true;
    if(n==='sheetCat' && !wasOpen.sheetCat) isSheetCatOpen.value = true;
    if(n==='titleblock' && !wasOpen.titleblock) isTitleblockOpen.value = true;
};

const selectViewType = (t) => { form.viewType = t; isViewTypeOpen.value = false; };
const selectTemplate = (t) => { form.template = t; isTemplateOpen.value = false; };
const selectScopeBox = (s) => { form.scopeBox = s; isScopeBoxOpen.value = false; };
const selectSheetCategory = (c) => { form.sheetCategory = c; isSheetCatOpen.value = false; };
const selectTitleblock = (t) => { form.titleblock = t; isTitleblockOpen.value = false; };

const selectReferenceSheet = (s) => {
    form.referenceSheet = cleanSheetNumber(s);
    isRefSheetOpen.value = false;
};

// Filter Templates
const filteredTemplates = computed(() => {
  let list = getFilteredTemplates(form.stage, form.viewType, displayTemplates.value);
  if (form.viewType === "Area Plan") {
    return list.filter(t => t.includes("EIA") || t.includes("NFA"));
  }
  return list;
});
const filteredScopeBoxes = computed(() => getFilteredScopeBoxes(form.viewType, displayScopeBoxes.value));

// --- STEP 2 LOGIC (SHEET) ---
const filteredTitleblocks = computed(() => {
  return displayTitleblocks.value.filter(tb => {
    const isDetail = form.sheetCategory === "A9";
    const name = tb.toLowerCase();
    return isDetail ? name.includes("detail") : name.includes("plan");
  });
});

// --- STEP 3 LOGIC (VALIDATION + FILTER) ---
const cleanSheetNumber = (str) => str.split(' - ')[0].trim();

// Strict A49 sheet-number format (post-2026-05 spec):
//   A1 → 1000-1999, A5 → 5000-5999, X-series → X000-X999.
// Mirrored against the backend reference-sheet regex in gpt_integration.py
// so anything the wizard accepts will also be accepted server-side.
const REF_SHEET_FORMAT = /^(?:[15]\d{3}|X\d{3})$/i;

const filteredReferenceSheets = computed(() => {
    const categoryFiltered = props.existingSheets.filter(s =>
        REF_SHEET_FORMAT.test(cleanSheetNumber(s))
    );

    if (!form.referenceSheet) return categoryFiltered;

    return categoryFiltered.filter(s =>
        s.toUpperCase().includes(form.referenceSheet.toUpperCase())
    );
});

const isValidReference = computed(() => {
  if (form.placement !== 'MATCH') return true;
  if (!form.referenceSheet) return false;
  const typed = form.referenceSheet.toUpperCase().trim();
  // Reject inputs that don't match the A49 format up front so non-conformant
  // strings (e.g. "1010XX") never make it past the wizard to the backend.
  if (!REF_SHEET_FORMAT.test(typed)) return false;
  return props.existingSheets.some(s => cleanSheetNumber(s).toUpperCase() === typed);
});

// Proceed/Submit Logic
const canProceed = computed(() => {
  if (step.value === 1) return form.levels.length > 0;
  if (step.value === 2) return !!form.titleblock; 
  return true;
});

const canSubmit = computed(() => {
  if (form.placement === 'MATCH') return isValidReference.value;
  return true;
});

// Back Button
const goBack = () => {
    if (step.value === 2) {
        form.titleblock = null;
    }
    step.value--;
};

// --- SUBMIT ---
function generateCommand() {
  let cmd = `Create ${form.viewType} for levels ${form.levels.join(", ")} `;
  cmd += `in ${form.stage} `;
  
  if (form.template) cmd += `using ${form.template} `;
  if (form.scopeBox) cmd += `and ${form.scopeBox} `;
  
  cmd += `place it on a new ${form.sheetCategory} sheet `;
  cmd += `using ${form.titleblock}`;

  if (form.placement === 'MATCH') {
    cmd += ` matching sheet ${form.referenceSheet}`;
  } else {
    cmd += ` aligned to center`; 
  }

  emit('submit', cmd);
  emit('close');
}

// Click Outside
const handleClickOutside = (e) => {
    if (refSheetDropdownWrapper.value && !refSheetDropdownWrapper.value.contains(e.target)) {
        isRefSheetOpen.value = false;
    }
};

onMounted(() => {
    window.addEventListener('click', handleClickOutside);
});

onUnmounted(() => {
    window.removeEventListener('click', handleClickOutside);
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