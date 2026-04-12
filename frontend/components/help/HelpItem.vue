<template>
  <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-300 group">
    <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-2 group-hover:text-white/70 transition-colors"> <!-- label bottom margin mb-X -->
      {{ label }}
    </div>
    <ul class="space-y-0"> <!-- Gap between list item space-y-X -->
      <li v-for="(p, i) in prompts" :key="i">
        <button 
          @click="$emit('pick', p)"
          class="text-left text-xs text-white hover:text-[#60A5FA] hover:bg-white/10 
                 px-3 py-1.5 -mx-1 rounded-lg w-full <!-- Changed py-2 to py-1.5 -->
                 transition-all duration-200 leading-relaxed font-mono opacity-90 hover:opacity-100 border border-transparent hover:border-white/10"
          title="Click to copy"
        >
          {{ p }}
        </button>
      </li>
    </ul>
  </div>
</template>

<script setup>
defineProps(['label', 'prompts']);
defineEmits(['pick']);
</script>