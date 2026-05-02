<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40
            backdrop-blur-2xl border border-white/20
            text-white rounded-3xl w-full max-w-lg shadow-2xl flex flex-col
            min-h-[540px] max-h-[90vh] animate-fade-in-up">

      <!-- HEADER -->
      <div class="p-5 border-b border-white/10 flex justify-between items-center flex-shrink-0">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full bg-[#388E3C]/20 flex items-center justify-center text-[#388E3C]">
            <Icon name="material-symbols:library-add-outline-rounded" class="text-xl" />
          </div>
          <h2 class="text-sm font-bold tracking-wide">INSERT STANDARD DETAILS</h2>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <!-- BODY -->
      <div class="p-6 pb-4 space-y-5 overflow-y-auto custom-scrollbar flex-1">

        <!-- DETAIL PACKAGE CARDS -->
        <div>
          <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-2 block">
            Detail Package
          </label>
          <div class="grid grid-cols-2 gap-3">
            <button @click="selectPackage('standard')"
              class="p-4 rounded-2xl text-left transition-all border flex flex-col gap-2"
              :class="selectedPackage === 'standard'
                ? 'bg-[#388E3C] border-transparent text-white shadow-lg shadow-[#388E3C]/30'
                : 'bg-white/5 hover:bg-white/10 border-white/15 text-white/80'">
              <div class="flex items-center gap-2">
                <Icon name="lucide:square-stack" class="text-xl" />
                <span class="font-bold text-sm">Standard</span>
              </div>
              <span class="text-[11px] opacity-80 leading-snug">
                A49 Approved Standard Details
              </span>
            </button>

            <button @click="selectPackage('eia')"
              class="p-4 rounded-2xl text-left transition-all border flex flex-col gap-2"
              :class="selectedPackage === 'eia'
                ? 'bg-[#388E3C] border-transparent text-white shadow-lg shadow-[#388E3C]/30'
                : 'bg-white/5 hover:bg-white/10 border-white/15 text-white/80'">
              <div class="flex items-center gap-2">
                <Icon name="lucide:leaf" class="text-xl" />
                <span class="font-bold text-sm">EIA</span>
              </div>
              <span class="text-[11px] opacity-80 leading-snug">
                EIA Package Standard Details
              </span>
            </button>
          </div>
        </div>

        <!-- TARGET FILE STATUS — appears after a package is picked -->
        <div v-if="selectedPackage" class="bg-black/15 border border-white/10 rounded-xl p-3 space-y-1.5">
          <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1">
            Target File
          </div>

          <!-- Loading -->
          <div v-if="isLoadingPreview" class="text-[11px] text-white/60 italic flex items-center gap-2">
            <Icon name="lucide:loader-2" class="text-sm animate-spin" />
            <span>Detecting Revit version and verifying file...</span>
          </div>

          <!-- Resolved -->
          <template v-else-if="preview">
            <!-- Revit version -->
            <div class="flex items-center gap-2 text-[11px]">
              <Icon :name="preview.revit_version ? 'lucide:check-circle-2' : 'lucide:x-circle'"
                    :class="preview.revit_version ? 'text-[#4EE29B] text-sm' : 'text-rose-400 text-sm'" />
              <span :class="preview.revit_version ? 'text-white/80' : 'text-rose-300'">
                Revit version detected: {{ preview.revit_version || '—' }}
              </span>
            </div>
            <!-- File existence -->
            <div class="flex items-center gap-2 text-[11px]">
              <Icon :name="preview.file_exists ? 'lucide:check-circle-2' : 'lucide:x-circle'"
                    :class="preview.file_exists ? 'text-[#4EE29B] text-sm' : 'text-rose-400 text-sm'" />
              <span :class="preview.file_exists ? 'text-white/80' : 'text-rose-300'">
                {{ preview.file_exists
                   ? `Source: ${preview.file_name}`
                   : `${preview.file_name} not found on server` }}
              </span>
            </div>
            <!-- Server reachability -->
            <div class="flex items-center gap-2 text-[11px]">
              <Icon :name="preview.server_reachable ? 'lucide:check-circle-2' : 'lucide:x-circle'"
                    :class="preview.server_reachable ? 'text-[#4EE29B] text-sm' : 'text-rose-400 text-sm'" />
              <span :class="preview.server_reachable ? 'text-white/80' : 'text-rose-300'">
                {{ preview.server_reachable
                   ? 'Network drive connected'
                   : 'Network drive unreachable — check VPN' }}
              </span>
            </div>
          </template>

          <!-- Error -->
          <div v-else class="text-[11px] text-rose-300 italic">
            Could not resolve target file. Try reopening the wizard.
          </div>
        </div>

        <!-- INSTRUCTIONS -->
        <div v-if="selectedPackage && preview && preview.file_exists" class="space-y-2">
          <div class="flex items-center justify-between">
            <span class="text-[10px] uppercase tracking-wider text-white/50 font-bold">
              Instructions
            </span>
            <div class="flex items-center bg-white/5 rounded-md border border-white/15 p-0.5">
              <button @click="setLang('en')"
                class="px-2 py-0.5 rounded text-[10px] font-bold transition"
                :class="isThai ? 'text-white/50 hover:text-white/80' : 'bg-white/20 text-white'">
                EN
              </button>
              <button @click="setLang('th')"
                class="px-2 py-0.5 rounded text-[10px] font-bold transition"
                :class="isThai ? 'bg-white/20 text-white' : 'text-white/50 hover:text-white/80'">
                TH
              </button>
            </div>
          </div>

          <!-- English -->
          <div v-if="!isThai" class="text-[11px] text-white/60 leading-relaxed">
            After clicking <span class="text-white/80 font-medium">Browse</span>,
            Revit's <em>Insert Views from File</em> dialog will open. The file path
            is on your clipboard — paste with <kbd class="px-1.5 py-0.5 rounded bg-white/10 text-white/80 text-[10px]">Ctrl+V</kbd>
            and press <kbd class="px-1.5 py-0.5 rounded bg-white/10 text-white/80 text-[10px]">Enter</kbd>
            to navigate, then pick the views you want to insert.
          </div>

          <!-- Thai -->
          <div v-else class="text-[11px] text-white/60 leading-relaxed">
            หลังจากคลิก <span class="text-white/80 font-medium">Browse</span>
            หน้าต่าง <em>Insert Views from File</em> ของ Revit จะเปิดขึ้น
            โดยที่อยู่ไฟล์ (File path) ได้ถูกคัดลอกไว้ในคลิปบอร์ดของคุณแล้ว
            ให้กด <kbd class="px-1.5 py-0.5 rounded bg-white/10 text-white/80 text-[10px]">Ctrl+V</kbd>
            เพื่อวาง และกด <kbd class="px-1.5 py-0.5 rounded bg-white/10 text-white/80 text-[10px]">Enter</kbd>
            เพื่อไปยังตำแหน่งไฟล์ดังกล่าว จากนั้นจึงเลือก (Views) ที่คุณต้องการ
          </div>
        </div>
      </div>

      <!-- FOOTER -->
      <div class="p-4 border-t border-white/10 flex justify-between items-center flex-shrink-0">
        <button @click="$emit('close')"
          class="px-4 py-2 rounded-xl text-xs text-white/60 hover:text-white hover:bg-white/10 transition">
          Cancel
        </button>
        <button @click="submit"
          :disabled="!canSubmit"
          class="px-5 py-2 rounded-xl text-xs font-bold transition-all flex items-center gap-2"
          :class="canSubmit
            ? 'bg-[#388E3C] hover:bg-[#7a96a3] text-white shadow-lg shadow-[#388E3C]/30'
            : 'bg-white/10 text-white/30 cursor-not-allowed'">
          <span>Browse</span>
          <Icon name="lucide:arrow-right" class="text-sm" />
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted } from 'vue';

const props = defineProps({
  // Parent supplies the latest preview result (sent from Revit). The wizard
  // ignores it unless its `package` matches the currently selected card.
  previewResult: { type: Object, default: null },
});

const emit = defineEmits(['close', 'submit', 'request-preview']);

// =====================================================================
// STATE
// =====================================================================
const selectedPackage = ref(null);   // 'standard' | 'eia'
const preview = ref(null);            // applied preview (matched to selectedPackage)
const isLoadingPreview = ref(false);

// Instruction language toggle — persisted per browser via localStorage
// so each staff member's preference (EN / TH) sticks across sessions.
const LANG_STORAGE_KEY = 'vella.insertStandardDetails.lang';
const isThai = ref(false);

onMounted(() => {
  try {
    if (typeof window !== 'undefined' && window.localStorage) {
      isThai.value = window.localStorage.getItem(LANG_STORAGE_KEY) === 'th';
    }
  } catch { /* localStorage may be blocked — fall back to default EN */ }
});

function setLang(lang) {
  isThai.value = lang === 'th';
  try {
    if (typeof window !== 'undefined' && window.localStorage) {
      window.localStorage.setItem(LANG_STORAGE_KEY, lang);
    }
  } catch { /* ignore */ }
}

// =====================================================================
// COMPUTED
// =====================================================================
const canSubmit = computed(() =>
  !!selectedPackage.value && !!preview.value && preview.value.file_exists === true
);

// =====================================================================
// ACTIONS
// =====================================================================
function selectPackage(pkg) {
  if (selectedPackage.value === pkg) return;
  selectedPackage.value = pkg;
  preview.value = null;
  isLoadingPreview.value = true;
  emit('request-preview', { package: pkg });
}

function submit() {
  if (!canSubmit.value) return;
  emit('submit', { package: selectedPackage.value });
}

// Apply incoming preview data only if it matches the active package selection.
watch(() => props.previewResult, (incoming) => {
  if (!incoming) return;
  if (incoming.package !== selectedPackage.value) return;
  preview.value = incoming;
  isLoadingPreview.value = false;
}, { deep: true });
</script>

<style scoped>
.animate-fade-in-up {
  animation: fadeInUp 0.2s ease-out;
}
@keyframes fadeInUp {
  from { opacity: 0; transform: translateY(8px) scale(0.98); }
  to   { opacity: 1; transform: translateY(0)    scale(1); }
}
.custom-scrollbar::-webkit-scrollbar { width: 6px; }
.custom-scrollbar::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.15); border-radius: 3px; }
</style>
