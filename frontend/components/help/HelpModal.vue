<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40 
        backdrop-blur-2xl border border-white/20 
        text-white rounded-3xl w-full max-w-4xl shadow-2xl flex flex-col 
        min-h-[600px] max-h-[90vh] animate-fade-in-up">
      
      <!-- Header - Matching SheetWizard style -->
      <div class="p-5 border-b border-white/10 flex justify-between items-center">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full bg-[#60A5FA]/20 flex items-center justify-center text-[#60A5FA]">
            <Icon name="lucide:circle-question-mark" />
          </div>
          <div>
            <h2 class="text-sm font-bold tracking-wide">VELLA COMMAND GUIDE</h2>
            <p class="text-[10px] text-white/50 uppercase tracking-wider">Cheat Sheet & Standards</p>
          </div>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <!-- Tab Navigation - Icon-only with colored icons -->
      <div class="px-5 pt-4">
        <div class="flex justify-between">
          <button 
            v-for="tab in tabs" 
            :key="tab.id"
            @click="activeTab = tab.id"
            class="flex flex-col items-center transition-all group"
          >
            <!-- Icon Container - Larger size -->
            <div class="w-10 h-10 md:w-12 md:h-12 rounded-full flex items-center justify-center mb-1 transition-all duration-200"
                 :class="[
                   activeTab === tab.id 
                     ? tab.activeBgClass + ' ' + tab.activeIconClass
                     : 'bg-white/10 group-hover:bg-white/15'
                 ]">
              <Icon 
                :name="tab.icon" 
                :class="[
                  activeTab === tab.id 
                    ? tab.activeIconClass + ' text-xl' 
                    : 'text-white/60 group-hover:text-white/80 text-lg',
                  'transition-all duration-200'
                ]"
              />
            </div>
            
            <!-- Label - hidden on small screens, shown on medium+ -->
            <span class="hidden md:block text-xs font-medium tracking-wide transition-colors"
                  :class="activeTab === tab.id ? 'text-white' : 'text-white/50 group-hover:text-white/70'">
              {{ tab.label }}
            </span>
          </button>
        </div>
      </div>

      <!-- Tab Content Area -->
      <div class="flex-1 overflow-y-auto p-5 custom-scrollbar">
        <component :is="activeComponent" @pick="copyToClipboard" />
      </div>

      <!-- Footer - Matching SheetWizard style -->
      <div class="p-5 border-t border-white/10">
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
import { ref, computed } from 'vue';

// Import Tabs
import HelpViewsTab from './HelpViewsTab.vue';
import HelpSheetsTab from './HelpSheetsTab.vue';
import HelpCreatePlaceTab from './HelpCreatePlaceTab.vue';
import HelpStandardsTab from './HelpStandardsTab.vue';
import HelpRoomElevationTab from './HelpRoomElevationTab.vue';     
import HelpMathCalculationTab from './HelpMathCalculationTab.vue'; 

defineEmits(['close']);

const activeTab = ref('views');
const showToast = ref(false);

const tabs = [
  { 
    id: 'views', 
    label: 'Views', 
    comp: HelpViewsTab, 
    icon: 'lucide:grid-2x2',
    activeIconClass: 'text-[#4EE29B]',
    activeBgClass: 'bg-[#4EE29B]/20',
  },
  { 
    id: 'sheets', 
    label: 'Sheets', 
    comp: HelpSheetsTab, 
    icon: 'lucide:files',
    activeIconClass: 'text-[#60A5FA]',
    activeBgClass: 'bg-[#60A5FA]/20',
  },
  { 
    id: 'cp', 
    label: 'Create & Place', 
    comp: HelpCreatePlaceTab, 
    icon: 'lucide:grid-2x2-plus',
    activeIconClass: 'text-[#00BCD4]',
    activeBgClass: 'bg-[#00BCD4]/20',
  },
  { 
    id: 'room', 
    label: 'Room Elev', 
    comp: HelpRoomElevationTab, 
    icon: 'lucide:frame', 
    activeIconClass: 'text-[#F43F5E]', // Rose color to match the wizard UI
    activeBgClass: 'bg-[#F43F5E]/20',
  },
  { 
    id: 'math', 
    label: 'Math', 
    comp: HelpMathCalculationTab, 
    icon: 'lucide:calculator', 
    activeIconClass: 'text-[#FFB74D]', // Orange/Yellow color
    activeBgClass: 'bg-[#FFB74D]/20',
  },
  { 
    id: 'stds', 
    label: 'Standards', 
    comp: HelpStandardsTab, 
    icon: 'lucide:bookmark', 
    activeIconClass: 'text-[#EEFF41]',
    activeBgClass: 'bg-[#EEFF41]/20',
  }
];

const activeComponent = computed(() => {
  return tabs.find(t => t.id === activeTab.value)?.comp;
});

// 📋 CLICK TO COPY LOGIC
async function copyToClipboard(text) {
  try {
    await navigator.clipboard.writeText(text);
    
    // Show Toast
    showToast.value = true;
    setTimeout(() => {
      showToast.value = false;
    }, 2000);
    
  } catch (err) {
    console.error('Failed to copy!', err);
  }
}
</script>

<style scoped>
.custom-scrollbar::-webkit-scrollbar {
  width: 6px;
}
.custom-scrollbar::-webkit-scrollbar-track {
  background: transparent;
  margin: 2px 0;
}
.custom-scrollbar::-webkit-scrollbar-thumb {
  background: rgba(255,255,255,0.15);
  border-radius: 10px;
}
.custom-scrollbar::-webkit-scrollbar-thumb:hover {
  background: rgba(255,255,255,0.25);
}

@keyframes fade-in-up { 
  from { opacity: 0; transform: translateY(20px) scale(0.95); } 
  to { opacity: 1; transform: translateY(0) scale(1); } 
}
.animate-fade-in-up { animation: fade-in-up 0.2s ease-out forwards; }
</style>