<template>
  <div 
    class="relative p-3 rounded-3xl backdrop-blur-xl flex-shrink-0
           bg-white/10 border border-white/20 shadow-xl 
           w-full min-w-[200px] max-w-full mx-auto mb-2 z-40"> 

    <transition
      enter-active-class="transition duration-100 ease-out"
      enter-from-class="transform scale-95 opacity-0 translate-y-2"
      enter-to-class="transform scale-100 opacity-100 translate-y-0"
      leave-active-class="transition duration-75 ease-in"
      leave-from-class="transform scale-100 opacity-100 translate-y-0"
      leave-to-class="transform scale-95 opacity-0 translate-y-2"
    >
      <div v-if="showMenu" 
           class="absolute bottom-full left-0 mb-2 w-56 
                  bg-[#0E3A7A]/95 backdrop-blur-xl border border-white/20 
                  rounded-2xl shadow-2xl overflow-hidden py-1 z-50">
        
        <div class="px-3 py-2 text-[10px] uppercase tracking-wider text-white/50 font-semibold">
          Creation Wizards
        </div>
        <button @click="triggerAction('wizard:create_views')" 
                class="w-full text-left px-4 py-2 text-xs text-white hover:bg-white/10 flex items-center gap-2 transition">
          <Icon name="lucide:grid-2x2" class="text-base text-[#4EE29B]" />
          <span>View Wizard</span>
        </button>

        <button @click="triggerAction('wizard:create_sheets')" 
                class="w-full text-left px-4 py-2 text-xs text-white hover:bg-white/10 flex items-center gap-2 transition">
          <Icon name="lucide:files" class="text-base text-blue-400" />
          <span>Sheet Wizard</span>
        </button>

        <button @click="triggerAction('wizard:create_and_place')" 
                class="w-full text-left px-4 py-2 text-xs text-white hover:bg-white/10 flex items-center gap-2 transition">
          <Icon name="lucide:grid-2x2-plus" class="text-base text-[#00BCD4]" />
          <span>Create & Place Wizard</span>
        </button>

        <button @click="triggerAction('wizard:room_elevations')" 
                class="w-full text-left px-4 py-2 text-xs text-white hover:bg-white/10 flex items-center gap-2 transition">
          <Icon name="lucide:frame" class="text-base text-[#F43F5E]" />
          <span>Room Elevations</span>
        </button>
        
        <button @click="triggerAction('wizard:rename_wizard')" 
                class="w-full text-left px-4 py-2 text-xs text-white hover:bg-white/10 flex items-center gap-2 transition">
          <Icon name="lucide:pencil-line" class="text-base text-[#D8B4FE]" />
          <span>Renumber & Rename</span>
        </button>

        <div class="border-t border-white/10 my-1"></div>

        <div class="px-3 py-2 text-[10px] uppercase tracking-wider text-white/50 font-semibold">
          Automation
        </div>
        <button @click="triggerAction('wizard:auto_tag_doors')" 
                class="w-full text-left px-4 py-2 text-xs text-white hover:bg-white/10 flex items-center gap-2 transition">
          <Icon name="lucide:tags" class="text-base text-[#FF9800]" />
          <span>Auto-Tag Doors</span>
        </button>

        <div class="border-t border-white/10 my-1"></div>

        <div class="px-3 py-2 text-[10px] uppercase tracking-wider text-white/50 font-semibold">
          Standards
        </div>
        <button @click="triggerAction('preflight_check')" 
                class="w-full text-left px-4 py-2 text-xs text-white hover:bg-white/10 flex items-center gap-2 transition">
          <Icon name="lucide:shield-check" class="text-base text-[#FBBF24]" />
          <span>Preflight Check</span>
        </button>

        <div class="border-t border-white/10 my-1"></div>

        <div class="px-3 py-2 text-[10px] uppercase tracking-wider text-white/50 font-semibold">
          Support
        </div>
        
        <button @click="triggerAction('ui:help')" 
                class="w-full text-left px-4 py-2 text-xs text-white hover:bg-white/10 flex items-center gap-2 transition group">
          <Icon name="lucide:circle-question-mark" class="text-base text-[#EEFF41]" />
          <span>Help & Command</span>
        </button>

      </div>
    </transition>

    <textarea
      ref="textareaRef"
      v-model="localInput"
      @keydown.enter.prevent="handleSend"
      @keydown.shift.enter.stop
      placeholder="Tell Vella what to do..."
      class="w-full bg-transparent resize-none leading-snug 
              text-white text-xs outline-none placeholder-white/60
              max-h-32 overflow-hidden align-text-top mb-3"
      rows="1"
    ></textarea>

    <div class="flex items-center justify-between">
      
      <div class="flex items-center gap-2">
        <button 
          @click="showMenu = !showMenu"
          class="w-8 h-8 flex items-center justify-center rounded-full 
                 transition bg-white/5 hover:bg-white/20 text-white/80 hover:text-white"
          :class="{ 'bg-white/20 text-white': showMenu }"
        >
          <Icon name="lucide:plus" class="text-base" />
        </button>
        
        <button
          @click="$emit('clear')"
          class="flex items-center gap-1 text-xs transition opacity-40 hover:opacity-60 ml-2"
        >
          <Icon name="lucide:message-square-x" class="text-base text-white" />
          <span class="inline whitespace-nowrap">Clear Chat</span>
        </button>
      </div>

      <div class="flex items-center gap-3">
        <button
          @click="handleSend"
          class="w-8 h-8 flex items-center justify-center rounded-full 
                 bg-[#4EE29B] hover:bg-[#6ff2b3] transition flex-shrink-0 shadow-lg shadow-green-900/20"
        >
          <Icon name="lucide:arrow-up" class="text-base text-white" />
        </button>
      </div>
    </div>
  </div>
  
  <div v-if="showMenu" @click="showMenu = false" class="fixed inset-0 z-10"></div>
</template>

<script setup>
import { ref, watch, nextTick } from 'vue';

const emit = defineEmits(['submit', 'clear', 'action']);
const localInput = ref("");
const textareaRef = ref(null);
const showMenu = ref(false);

watch(localInput, () => {
  if (!textareaRef.value) return;
  textareaRef.value.style.height = "auto";
  textareaRef.value.style.height = textareaRef.value.scrollHeight + "px";
});

function handleSend() {
  const text = localInput.value.trim();
  if (!text) return;
  emit('submit', text);
  localInput.value = "";
  nextTick(() => { if (textareaRef.value) textareaRef.value.style.height = "auto"; });
  showMenu.value = false;
}

function triggerAction(actionName) {
  showMenu.value = false;
  emit('action', actionName);
}
</script>