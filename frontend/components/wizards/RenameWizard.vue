<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40 
        backdrop-blur-2xl border border-white/20 
        text-white rounded-3xl w-full max-w-5xl shadow-2xl flex flex-col 
        h-[85vh] animate-fade-in-up">
      
      <div class="p-5 border-b border-white/10 flex justify-between items-center shrink-0">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full bg-[#D8B4FE]/20 flex items-center justify-center text-[#D8B4FE]">
            <Icon name="lucide:pencil-line" class="text-xl" />
          </div>
          <h2 class="text-sm font-bold tracking-wide">RENUMBER & RENAME</h2>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <div class="px-6 py-3 flex flex-col gap-3 shrink-0 border-b border-white/5 bg-white/5">
        
        <div class="flex flex-col min-[500px]:flex-row justify-between items-stretch min-[500px]:items-center gap-3 w-full">
          <div class="flex bg-black/20 rounded-xl p-1 border border-white/10 w-full min-[500px]:w-[160px] shrink-0">
             <button 
               v-for="tab in ['SHEETS', 'VIEWS']" :key="tab"
               @click="activeTab = tab"
               class="flex-1 rounded-lg text-xs py-1 transition-all font-bold tracking-wide"
               :class="activeTab === tab ? 'bg-white/20 text-white shadow-sm' : 'text-white/40 hover:text-white/70'"
             >
               {{ tab }}
             </button>
          </div>

          <button 
            @click="showSearchReplace = !showSearchReplace"
            class="w-full min-[500px]:flex-1 px-4 py-2 min-[500px]:py-1.5 rounded-xl text-xs font-bold border transition-all flex items-center justify-center min-[500px]:justify-start gap-2"
            :class="showSearchReplace 
              ? 'bg-[#D8B4FE] text-[#0A1D4A] border-[#D8B4FE] shadow-lg shadow-purple-900/20' 
              : 'bg-black/20 border-white/10 text-white/70 hover:bg-white/10 hover:text-white'"
          >
            <Icon name="lucide:replace" class="text-base" />
            <span>Replace Text</span>
          </button>
        </div>

        <div v-if="showSearchReplace" class="animate-fade-in bg-[#D8B4FE]/10 border border-[#D8B4FE]/30 rounded-xl p-3 flex flex-col min-[500px]:flex-row gap-3 items-start min-[500px]:items-center w-full">
            <div class="w-full min-[500px]:flex-1 flex flex-col gap-1">
                <label class="text-[9px] uppercase font-bold text-[#D8B4FE]/80">Find Text</label>
                <input v-model="findText" placeholder="e.g. FLOOR PLAN" class="w-full bg-black/20 border border-white/10 rounded-lg px-2 py-1.5 text-xs text-white outline-none focus:border-[#D8B4FE]" />
            </div>
            <div class="w-full min-[500px]:flex-1 flex flex-col gap-1">
                <label class="text-[9px] uppercase font-bold text-[#D8B4FE]/80">Replace With</label>
                <input v-model="replaceText" placeholder="e.g. แปลนพื้น" class="w-full bg-black/20 border border-white/10 rounded-lg px-2 py-1.5 text-xs text-white outline-none focus:border-[#D8B4FE]" />
            </div>
            <div class="w-full min-[500px]:w-32 flex flex-col gap-1">
                 <label class="hidden min-[500px]:block text-[9px] uppercase font-bold text-transparent">.</label>
                 <button 
                    @click="applySearchReplace"
                    :disabled="!findText"
                    class="w-full bg-[#D8B4FE] hover:bg-[#B39DDB] text-[#0A1D4A] font-bold py-2 min-[500px]:py-1.5 rounded-lg text-xs shadow-md transition disabled:opacity-50 disabled:cursor-not-allowed">
                    Apply to List
                 </button>
            </div>
        </div>

        <div class="flex flex-col min-[500px]:flex-row justify-between items-stretch min-[500px]:items-center gap-3 animate-fade-in z-20 w-full">
            <div class="relative w-full min-[500px]:w-[160px] shrink-0">
              <div @click="toggleDropdown('stage')" 
                   class="w-full bg-black/20 backdrop-blur-sm border border-white/10 rounded-xl px-3 py-2 text-xs 
                   text-white focus:border-[#D8B4FE] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/5"
                   :class="isStageOpen ? 'border-[#D8B4FE]' : ''">
                 <span class="truncate">{{ filterStage ? PROJECT_PHASE_MAP[filterStage] : 'All Stages' }}</span>
                 <div class="text-white/50 transform transition-transform duration-200" :class="isStageOpen ? 'rotate-180' : ''">▼</div>
              </div>
              
              <div v-if="isStageOpen" class="absolute z-50 w-full mt-1 bg-[#0A1D4A]/95 backdrop-blur-xl border border-white/25 rounded-xl overflow-hidden shadow-2xl animate-fade-in">
                <div @click="selectStage(null)" class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 bg-white/5">All Stages</div>
                <div v-for="(label, code) in PROJECT_PHASE_MAP" :key="code" @click="selectStage(code)"
                     class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                     :class="filterStage === code ? 'bg-white/20 font-medium' : ''">
                  {{ label }}
                </div>
              </div>
            </div>

            <div class="relative w-full min-[500px]:flex-1">
              <div @click="toggleDropdown('set')" 
                   class="w-full bg-black/20 backdrop-blur-sm border border-white/10 rounded-xl px-3 py-2 text-xs 
                   text-white focus:border-[#D8B4FE] outline-none transition cursor-pointer flex justify-between items-center hover:bg-white/5"
                   :class="isSetOpen ? 'border-[#D8B4FE]' : ''">
                 <span class="truncate">{{ filterSet ? SHEET_SET_MAP[filterSet] : 'All Disciplines' }}</span>
                 <div class="text-white/50 transform transition-transform duration-200" :class="isSetOpen ? 'rotate-180' : ''">▼</div>
              </div>
              
              <div v-if="isSetOpen" class="absolute z-50 w-full mt-1 bg-[#0A1D4A]/95 backdrop-blur-xl border border-white/25 rounded-xl overflow-hidden shadow-2xl animate-fade-in max-h-60 overflow-y-auto custom-scrollbar">
                <div @click="selectSet(null)" class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 bg-white/5">All Disciplines</div>
                <div v-for="(label, code) in SHEET_SET_MAP" :key="code" @click="selectSet(code)"
                     class="px-3 py-2 text-xs text-white hover:bg-white/15 transition cursor-pointer border-b border-white/10 last:border-b-0"
                     :class="filterSet === code ? 'bg-white/20 font-medium' : ''">
                  {{ label }}
                </div>
              </div>
            </div>
        </div>

      </div>

      <div class="flex-1 overflow-hidden relative bg-[#0A1D4A]">
        <div class="absolute inset-0 overflow-y-auto custom-scrollbar px-6 pb-6" @click="closeAllDropdowns">
          
          <table class="w-full text-left border-collapse min-w-[600px]">
            <thead class="sticky top-0 bg-[#0A1D4A] z-10 shadow-lg border-b border-white/10">
                <tr>
                <th v-if="activeTab === 'SHEETS'" class="py-3 text-[10px] uppercase tracking-wider text-white/50 font-bold pl-2 w-16">
                  Sht. No.
                </th>
                <th class="py-3 text-[10px] uppercase tracking-wider text-white/50 font-bold w-1/3">
                  {{ activeTab === 'SHEETS' ? 'Sht. Name' : 'View Name' }}
                </th>
                <th v-if="activeTab === 'SHEETS'" class="py-3 text-[10px] uppercase tracking-wider text-[#D8B4FE] font-bold w-16"> 
                   New No.
                </th>
                <th v-if="activeTab === 'VIEWS'" class="py-3 text-[10px] uppercase tracking-wider text-[#D8B4FE] font-bold w-48"> 
                   Title on Sht.
                </th>
                <th class="py-3 text-[10px] uppercase tracking-wider text-[#D8B4FE] font-bold">
                  New Name
                </th>
                </tr>
            </thead>
            <tbody class="divide-y divide-white/5">
              <tr v-for="item in filteredItems" :key="item.unique_id" class="group hover:bg-white/5 transition">
                
                <td v-if="activeTab === 'SHEETS'" 
                    class="py-2 pl-2 text-xs font-mono text-white/70 select-text cursor-text font-bold">
                  {{ item.number }}
                </td>

                <td class="py-2 text-xs text-white/60 select-text cursor-text break-words pr-2 max-w-[250px]">
                    {{ item.name }}
                    <div v-if="activeTab === 'VIEWS' && item.sheet_number" class="text-[9px] text-white/30 mt-0.5 select-none">
                        On: {{ item.sheet_number }}
                    </div>
                </td>

                <td class="py-1 pr-4 align-top">
                   <input type="text" v-model="changes[item.unique_id][activeTab === 'SHEETS' ? 'number' : 'title_on_sheet']"
                     class="w-full bg-transparent border-b border-transparent focus:border-[#D8B4FE] text-xs text-[#D8B4FE] placeholder-white/10 outline-none py-1 transition font-mono"
                     :placeholder="activeTab === 'SHEETS' ? item.number : (item.title_on_sheet || item.name)" />
                </td>

                <td class="py-1 pr-2 align-top">
                   <input type="text" v-model="changes[item.unique_id]['name']"
                     class="w-full bg-transparent border-b border-transparent focus:border-[#D8B4FE] text-xs text-[#D8B4FE] placeholder-white/10 outline-none py-1 transition"
                     :placeholder="item.name" />
                </td>

              </tr>
            </tbody>
          </table>

          <div v-if="filteredItems.length === 0" class="text-center py-10 text-white/30 text-xs italic">
            No items found matching filters.
          </div>

        </div>
      </div>

      <div class="p-5 border-t border-white/10 shrink-0 bg-[#0A1D4A]/40 backdrop-blur-xl rounded-b-3xl">
        <div class="flex justify-between items-center mb-3">
             <div class="text-[10px] text-white/40">
                <span v-if="changeCount > 0" class="text-[#D8B4FE] font-bold">{{ changeCount }} pending changes</span>
                <span v-else>No changes made</span>
             </div>
        </div>
        <button 
          @click="submitChanges"
          :disabled="changeCount === 0"
          class="w-full py-3 rounded-xl font-bold text-sm transition-all duration-200 flex items-center justify-center gap-2"
          :class="changeCount > 0
            ? 'bg-[#D8B4FE] hover:bg-[#dfc3fd] text-[#0A1D4A] shadow-lg shadow-blue-900/20' 
            : 'bg-white/10 text-white/30 cursor-not-allowed'"
        >
          <span>Apply Batch Updates</span>
          <Icon name="lucide:arrow-up" class="text-xl rotate-90" />
        </button>
      </div>

    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted, watch } from 'vue';
import { PROJECT_PHASE_MAP, SHEET_SET_MAP } from '~/utils/a49Standards';

const props = defineProps({
  inventoryData: { type: Object }
});

const emit = defineEmits(['close', 'submit']);

// --- THE SMART MOCK DATA SWITCH ---
const mockData = {
  sheets: [
    { unique_id: 'mock-s1', number: 'A-101', name: 'GROUND FLOOR PLAN', stage: 'DD', category: 'A' },
    { unique_id: 'mock-s2', number: 'A-102', name: 'SECOND FLOOR PLAN', stage: 'DD', category: 'A' },
    { unique_id: 'mock-s3', number: 'S-201', name: 'STRUCTURAL FOUNDATION', stage: 'CD', category: 'S' }
  ],
  views: [
    { unique_id: 'mock-v1', name: '01 - Floor Plan - Ground', sheet_number: 'A-101', title_on_sheet: 'GROUND FLOOR' },
    { unique_id: 'mock-v2', name: '02 - Floor Plan - Second', sheet_number: 'A-102', title_on_sheet: 'SECOND FLOOR' },
    { unique_id: 'mock-v3', name: 'Elev - North', sheet_number: null, title_on_sheet: null }
  ]
};

// If Revit sends data, use it. If it's empty (localhost), use mockData!
const activeData = computed(() => {
   const hasRealData = props.inventoryData?.sheets?.length > 0 || props.inventoryData?.views?.length > 0;
   return hasRealData ? props.inventoryData : mockData;
});

// --- STATE ---
const activeTab = ref('SHEETS'); 
const searchQuery = ref('');
const filterStage = ref(null); 
const filterSet = ref(null);  
const changes = ref({}); 

const isStageOpen = ref(false);
const isSetOpen = ref(false);

// --- SEARCH & REPLACE STATE ---
const showSearchReplace = ref(false);
const findText = ref('');
const replaceText = ref('');

// --- INIT (Now watching activeData instead of props) ---
watch(() => activeData.value, (newVal) => {
    if(!newVal) return;
    const sheets = newVal.sheets || [];
    const views = newVal.views || [];
    
    [...sheets, ...views].forEach(item => {
        if (!changes.value[item.unique_id]) {
            changes.value[item.unique_id] = {};
        }
    });
}, { immediate: true, deep: true });

// --- ACTIONS ---
const toggleDropdown = (name) => {
    if (name === 'stage') { isStageOpen.value = !isStageOpen.value; isSetOpen.value = false; }
    if (name === 'set') { isSetOpen.value = !isSetOpen.value; isStageOpen.value = false; }
};
const closeAllDropdowns = () => { isStageOpen.value = false; isSetOpen.value = false; };
const selectStage = (val) => { filterStage.value = val; closeAllDropdowns(); };
const selectSet = (val) => { filterSet.value = val; closeAllDropdowns(); };

const applySearchReplace = () => {
    if (!findText.value) return;
    const regex = new RegExp(findText.value, "gi");
    filteredItems.value.forEach(item => {
        const originalName = item.name || "";
        if (originalName.match(regex)) {
            const newName = originalName.replace(regex, replaceText.value);
            changes.value[item.unique_id]['name'] = newName;
        }
    });
};

// --- COMPUTED ---
const currentList = computed(() => {
    return activeTab.value === 'SHEETS' 
        ? (activeData.value.sheets || []) 
        : (activeData.value.views || []);
});

const filteredItems = computed(() => {
    let items = currentList.value;

    if (searchQuery.value) {
        const q = searchQuery.value.toLowerCase();
        items = items.filter(item => {
            const n = (item.name || '').toLowerCase();
            const num = (item.number || '').toLowerCase(); 
            return n.includes(q) || (num && num.includes(q));
        });
    }

    if (filterSet.value) {
        const code = filterSet.value.toUpperCase();
        items = items.filter(item => {
            if (activeTab.value === 'SHEETS') {
                const cat = item.category || "";
                return cat.startsWith(code) || (item.number && item.number.startsWith(code));
            } else {
                const n = (item.name || "").toUpperCase();
                return n.includes(`_${code}_`) || n.includes(` ${code} `);
            }
        });
    }

    if (filterStage.value) {
        const code = filterStage.value.toUpperCase(); 
        const label = PROJECT_PHASE_MAP[code] ? PROJECT_PHASE_MAP[code].toUpperCase() : "";

        items = items.filter(item => {
            if (activeTab.value === 'SHEETS' && item.stage) {
                if (item.stage === code) return true;
            }
            const n = (item.name || "").toUpperCase();
            const num = (item.number || "").toUpperCase(); 

            return n.includes(code) || n.includes(label) || 
                   num.includes(code) || num.includes(label);
        });
    }

    return items;
});

const changeCount = computed(() => {
    let count = 0;
    Object.keys(changes.value).forEach(uid => {
        const edits = changes.value[uid];
        const hasEdits = Object.values(edits).some(val => val && val.trim() !== '');
        if (hasEdits) count++;
    });
    return count;
});

const submitChanges = () => {
    const updates = [];
    Object.keys(changes.value).forEach(uid => {
        const edits = changes.value[uid];
        const cleanEdits = {};
        Object.keys(edits).forEach(key => {
            if (edits[key] && edits[key].trim() !== '') cleanEdits[key] = edits[key];
        });
        if (Object.keys(cleanEdits).length > 0) {
            const isSheet = (activeData.value.sheets || []).find(s => s.unique_id === uid);
            const type = isSheet ? 'SHEET' : 'VIEW';
            updates.push({ unique_id: uid, element_type: type, changes: cleanEdits });
        }
    });
    emit('submit', updates);
    emit('close');
};

const handleKeydown = (event) => { if (event.key === 'Escape') emit('close'); };
onMounted(() => { document.addEventListener('keydown', handleKeydown); });
onUnmounted(() => { document.removeEventListener('keydown', handleKeydown); });
</script>

<style scoped>
.custom-scrollbar::-webkit-scrollbar { width: 6px; height: 6px; }
.custom-scrollbar::-webkit-scrollbar-track { background: transparent; margin: 2px 0; }
.custom-scrollbar::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.15); border-radius: 10px; }
.custom-scrollbar::-webkit-scrollbar-thumb:hover { background: rgba(255,255,255,0.25); }

@keyframes fade-in-up { from { opacity: 0; transform: translateY(20px) scale(0.95); } to { opacity: 1; transform: translateY(0) scale(1); } }
.animate-fade-in-up { animation: fade-in-up 0.2s ease-out forwards; }
@keyframes fade-in { from { opacity: 0; } to { opacity: 1; } }
.animate-fade-in { animation: fade-in 0.2s ease-out forwards; }
</style>