<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40 
            backdrop-blur-2xl border border-white/20 
            text-white rounded-3xl w-full max-w-lg shadow-2xl flex flex-col 
            min-h-[580px] max-h-[90vh] animate-fade-in-up">
      
      <div class="p-5 border-b border-white/10 flex justify-between items-center flex-shrink-0">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full bg-[#F43F5E]/20 flex items-center justify-center text-[#F43F5E]">
            <Icon name="lucide:frame" class="text-xl" />
          </div>
          <h2 class="text-sm font-bold tracking-wide">INTERACTIVE ROOM PACKAGE</h2>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <div class="px-6 pt-4 pb-2">
        <div class="flex items-center justify-between text-[9px] font-bold uppercase tracking-wider text-white/40">
          <div :class="{ 'text-[#F43F5E]': step >= 1 }" class="text-center w-1/3">1. View Config</div>
          <div class="h-px bg-white/10 flex-1 relative">
            <div class="absolute inset-0 bg-gradient-to-r from-[#F43F5E] to-[#FF8A65] transition-all duration-300" :style="{ width: step >= 2 ? '100%' : '0%' }"></div>
          </div>
          <div :class="{ 'text-[#FF8A65]': step >= 2 }" class="text-center w-1/3">2. Sheet Config</div>
          <div class="h-px bg-white/10 flex-1 relative">
            <div class="absolute inset-0 bg-gradient-to-r from-[#FF8A65] to-[#FFB74D] transition-all duration-300" :style="{ width: step >= 3 ? '100%' : '0%' }"></div>
          </div>
          <div :class="{ 'text-[#FFB74D]': step >= 3 }" class="text-center w-1/3">3. Execution</div>
        </div>
      </div>

      <div class="p-6 space-y-6 overflow-y-auto custom-scrollbar flex-1 relative">
        
        <div v-if="step === 1" class="space-y-6 animate-fade-in">
          
          <div class="bg-black/30 border border-[#F43F5E]/30 rounded-xl p-4 flex flex-col gap-2 relative overflow-hidden">
            <div class="absolute top-0 left-0 w-1 h-full bg-[#F43F5E]"></div>
            <div class="flex items-center gap-2 text-[#F43F5E] font-bold text-xs uppercase tracking-wider">
              <Icon name="lucide:frame" />
              <span>Interactive Workflow</span>
            </div>
            <div class="text-xs text-white/80 leading-relaxed">
              When you start this tool, Vella will prompt you to <strong>physically click a room</strong> in your active floor plan. Please ensure you have the correct floor plan open.
            </div>
          </div>

          <div class="grid grid-cols-2 gap-4">
            <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Stage</label>
              <div class="flex bg-white/5 rounded-xl p-1 border border-white/10">
                <button v-for="stg in ['WV','PD','DD','CD']" :key="stg"
                  @click="setStage(stg)"
                  class="flex-1 rounded-lg text-xs py-1 transition-all"
                  :class="[
                    form.stage === stg ? 'bg-white/20 text-white font-bold shadow-sm' : 'text-white/40 hover:text-white/70',
                    (stg === 'WV' || stg === 'PD') ? 'cursor-not-allowed opacity-50' : '',
                    (stg === 'WV' || stg === 'PD') && form.stage === stg ? 'bg-white/10' : ''
                  ]"
                  :disabled="stg === 'WV' || stg === 'PD'">
                  {{ stg }}
                </button>
              </div>
            </div>

            <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Callout Offset (mm)</label>
              <input v-model.number="form.cropOffset" type="number" 
                     class="w-full bg-white/10 border border-white/20 rounded-xl px-3 py-1.5 text-sm text-white focus:border-[#F43F5E] outline-none transition font-mono transparent-spinner" />
            </div>
          </div>

          <div class="space-y-4">
            <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Enlarged Plan Template</label>
              <div class="w-full bg-white/5 border border-white/10 rounded-xl px-3 py-2 text-xs text-white/60 flex justify-between items-center cursor-not-allowed transition-all">
                <span class="truncate">{{ form.planTemplate }}</span>
                <div class="text-[10px] uppercase tracking-wider font-bold text-[#F43F5E]/70">Locked</div>
              </div>
            </div>

            <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Interior Elevation Template</label>
              <div class="w-full bg-white/5 border border-white/10 rounded-xl px-3 py-2 text-xs text-white/60 flex justify-between items-center cursor-not-allowed transition-all">
                <span class="truncate">{{ form.elevTemplate }}</span>
                <div class="text-[10px] uppercase tracking-wider font-bold text-[#F43F5E]/70">Locked</div>
              </div>
            </div>
          </div>

        </div>

        <div v-else-if="step === 2" class="space-y-6 animate-fade-in">
          
          <div class="flex items-center justify-between p-4 border border-white/10 rounded-xl bg-white/5 cursor-pointer hover:bg-white/10 transition"
               @click="form.createSheets = !form.createSheets">
            <div>
               <div class="text-sm font-bold">Create New Sheet?</div>
               <div class="text-[10px] text-white/50">Vella will automatically open the sheet after creation.</div>
            </div>
            <div class="w-10 h-6 rounded-full transition-colors relative" :class="form.createSheets ? 'bg-[#FF8A65]' : 'bg-white/20'">
              <div class="absolute top-1 left-1 w-4 h-4 rounded-full bg-white transition-transform" :class="form.createSheets ? 'translate-x-4' : ''"></div>
            </div>
          </div>

          <div v-if="form.createSheets" class="space-y-4 animate-fade-in-up">
            <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1.5 block">Titleblock</label>
              <div class="relative">
                <div @click="toggleDropdown('titleblock')" 
                     class="w-full bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl px-3 py-2 text-xs 
                     text-white focus:border-[#FF8A65] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/15">
                  <span class="truncate" :class="!form.titleblock ? 'text-white/50 italic' : ''">{{ form.titleblock || 'Please Select Titleblock' }}</span>
                  <div class="text-white/50">▼</div>
                </div>
                <div v-if="isTitleblockOpen" class="absolute z-40 w-full mt-1 bg-[#0A1D4A]/95 backdrop-blur-xl border border-white/25 rounded-xl overflow-hidden shadow-2xl max-h-48 overflow-y-auto custom-scrollbar">
                  <div v-for="tb in filteredTitleblocks" :key="tb" @click="selectTemplate('titleblock', tb)" class="px-3 py-2 text-xs text-white hover:bg-white/15 cursor-pointer border-b border-white/10 truncate">{{ tb }}</div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <div v-else-if="step === 3" class="space-y-6 animate-fade-in">
          <div class="text-sm font-bold text-center mb-4 text-[#FFB74D]">Ready to Start Interactive Tool</div>

          <div class="bg-white/5 border border-white/10 rounded-xl p-5 space-y-4 relative">
            
            <div class="flex gap-4 items-start">
              <div class="flex-shrink-0 w-6 h-6 rounded-full bg-[#F43F5E]/20 text-[#F43F5E] flex items-center justify-center font-bold text-xs mt-0.5">1</div>
              <div>
                <div class="text-xs font-bold text-white">Pick the Room</div>
                <div class="text-[10px] text-white/60 mt-1">Vella will prompt you to select the room in your current plan and generate the Callout.</div>
              </div>
            </div>

            <div v-if="form.createSheets" class="flex gap-4 items-start">
              <div class="flex-shrink-0 w-6 h-6 rounded-full bg-[#FF8A65]/20 text-[#FF8A65] flex items-center justify-center font-bold text-xs mt-0.5">2</div>
              <div>
                <div class="text-xs font-bold text-white">Sheet Auto-Creation</div>
                <div class="text-[10px] text-white/60 mt-1">Vella will create the sheet, place the plan, and open the sheet view automatically.</div>
              </div>
            </div>

            <div class="flex gap-4 items-start">
              <div class="flex-shrink-0 w-6 h-6 rounded-full bg-[#FFB74D]/20 text-[#FFB74D] flex items-center justify-center font-bold text-xs mt-0.5">{{ form.createSheets ? '3' : '2' }}</div>
              <div>
                <div class="text-xs font-bold text-white">Place Elevation Marker</div>
                <div class="text-[10px] text-white/60 mt-1">Vella will activate the plan and ask you to click where the elevation marker should go.</div>
              </div>
            </div>

          </div>
        </div>

      </div>

      <div class="p-5 border-t border-white/10 flex gap-3">
        <button v-if="step > 1" @click="step--" 
          class="px-5 py-3 rounded-xl font-bold text-sm bg-white/5 hover:bg-white/10 text-white transition">
          Back
        </button>
        
        <button v-if="step < 3" @click="step++" 
          class="flex-1 py-3 rounded-xl font-bold text-sm transition-all flex items-center justify-center gap-2 bg-gradient-to-r from-[#F43F5E] to-[#FF8A65] text-white shadow-lg shadow-rose-900/30">
          Next Step
        </button>

        <button v-if="step === 3" @click="startInteractiveWorkflow" 
          class="flex-1 py-3 rounded-xl font-bold text-sm transition-all flex items-center justify-center gap-2 bg-gradient-to-r from-[#FFB74D] to-[#4EE29B] text-[#0A1D4A] shadow-lg shadow-green-900/30">
          <span>Start Tool in Revit</span>
        </button>
      </div>

    </div>
  </div>
</template>

<script setup>
import { ref, reactive, computed } from 'vue';

const props = defineProps({
  titleblocks: { type: Array, default: () => [] },
  initialStage: { type: String, default: 'CD' }
});

const emit = defineEmits(['close', 'executeRaw']);

// --- HELPER FUNCTIONS ---
// Resolves the correct template strings based on the current stage
const getPlanTemplate = (stage) => stage === 'DD' ? "A49_DD_INTERIOR ENLARGED PLAN" : "A49_CD_A6_INTERIOR ENLARGED PLAN";
const getElevTemplate = (stage) => stage === 'DD' ? "A49_DD_INTERIOR ELEVATION" : "A49_CD_A6_INTERIOR ELEVATION";

// --- STATE ---
const step = ref(1);
const isTitleblockOpen = ref(false);

const form = reactive({
  stage: props.initialStage,
  cropOffset: 600,
  planTemplate: getPlanTemplate(props.initialStage), 
  elevTemplate: getElevTemplate(props.initialStage), 
  createSheets: true,
  titleblock: null,
});

// --- STAGE SELECTION LOGIC ---
const setStage = (stg) => {
  if (stg === 'WV' || stg === 'PD') return; // Prevent selection of locked stages
  
  form.stage = stg;
  form.planTemplate = getPlanTemplate(stg);
  form.elevTemplate = getElevTemplate(stg);
};

// --- DROPDOWNS ---
const toggleDropdown = (n) => {
    isTitleblockOpen.value = (n === 'titleblock') ? !isTitleblockOpen.value : false;
};

const selectTemplate = (type, val) => { 
    form[type] = val; 
    isTitleblockOpen.value = false;
};

// --- FILTERS ---
const filteredTitleblocks = computed(() => {
  if (!props.titleblocks) return [];
  return props.titleblocks.filter(tb => tb.toLowerCase().includes("a6") || tb.toLowerCase().includes("plan"));
});

// --- EXECUTION ---
function startInteractiveWorkflow() {
  const payload = {
    command: "start_interactive_room_package", // Sent directly to C# Router
    offset: form.cropOffset,
    stage: form.stage,
    plan_template: form.planTemplate,
    elev_template: form.elevTemplate,
    create_sheets: form.createSheets, 
    titleblock: form.titleblock
  };

  emit('executeRaw', payload); 
  emit('close'); // Closes Vella so user can click the screen
}
</script>

<style scoped>
.custom-scrollbar::-webkit-scrollbar { width: 6px; }
.custom-scrollbar::-webkit-scrollbar-track { background: transparent; margin: 2px 0; }
.custom-scrollbar::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.15); border-radius: 10px; }
.custom-scrollbar::-webkit-scrollbar-thumb:hover { background: rgba(255,255,255,0.25); }

/* 💥 Transparent Number Input Spinners */
.transparent-spinner::-webkit-inner-spin-button,
.transparent-spinner::-webkit-outer-spin-button {
  background: transparent;
  opacity: 0.3;
  cursor: pointer;
}

@keyframes fade-in-up { from { opacity: 0; transform: translateY(20px) scale(0.95); } to { opacity: 1; transform: translateY(0) scale(1); } }
.animate-fade-in-up { animation: fade-in-up 0.2s ease-out forwards; }
@keyframes fade-in { from { opacity: 0; transform: translateY(-5px); } to { opacity: 1; transform: translateY(0); } }
.animate-fade-in { animation: fade-in 0.15s ease-out forwards; }
</style>