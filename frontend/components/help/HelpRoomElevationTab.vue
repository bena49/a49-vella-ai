<template>
  <div class="space-y-4">
    <div class="space-y-4">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Interactive Room Package</h3>
      
      <div class="grid gap-3 md:grid-cols-2">
        <HelpItem label="START INTERACTIVE WORKFLOW" :prompts="[
          'Start interactive room package in CD',
          'Create enlarged plan and interior elevations in CD',
          'Run the room elevation tool in CD'
        ]" @pick="$emit('pick', $event)" />

        <div>
          <div class="bg-white/5 border border-white/10 rounded-lg p-3.5 text-xs text-white/80 cursor-default space-y-2">
            <div class="text-[10px] font-bold text-white/30 uppercase tracking-wider mb-1.5">WORKFLOW STEPS</div>
            <div>1. Please ensure you are in the correct Floor Plan to place the Callout.</div>
            <div>2. Confirm to Create a New Sheet and select the required Titleblock.</div>
            <div>3. Vella will prompt you to select a Room.</div>
            <div>4. Vella generates the Callout &amp; Sheet.</div>
            <div>5. Vella asks you to place the Elevation Marker.</div>
            <div>6. All views are automatically placed &amp; aligned!</div>
            <div class="text-[10px] text-white/40 italic mt-2 pt-2 border-t border-white/10">
              💬 Toggle <span class="text-white/60 font-bold">EN | TH</span> inside the wizard for Thai instructions.
            </div>
          </div>
        </div>
        
      </div>
    </div>
  </div>
</template>

<script setup>
import HelpItem from './HelpItem.vue';
defineEmits(['pick']);
</script>