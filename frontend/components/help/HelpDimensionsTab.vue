<template>
  <div class="space-y-4">

    <!-- 1. ABOUT THE TOOL -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">About Auto-Dimensioning</h3>
      <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 text-xs text-white/80 leading-relaxed">
        Vella places dimension chains on floor plans automatically. The
        <strong class="text-white">Dimensioning Wizard</strong> is the only entry point —
        any natural-language request such as “dimension walls in CD floor plans” opens it.
        Inside the wizard you choose what to dimension, where, and how.
      </div>
    </div>

    <!-- 2. WHAT THE WIZARD DOES -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">What the Wizard Handles</h3>
      <div class="grid gap-3 md:grid-cols-2">
        <div class="bg-white/5 border border-white/10 rounded-lg p-3.5 text-xs text-white/80 cursor-default space-y-1.5">
          <div class="text-[10px] font-bold text-white/30 uppercase tracking-wider mb-1.5">DIMENSION TARGETS</div>
          <div>• Exterior walls</div>
          <div>• Interior walls</div>
          <div>• Wall openings (doors / windows)</div>
          <div>• Grid lines</div>
          <div>• Total chain (overall length)</div>
          <div>• Detail chain</div>
        </div>
        <div class="bg-white/5 border border-white/10 rounded-lg p-3.5 text-xs text-white/80 cursor-default space-y-1.5">
          <div class="text-[10px] font-bold text-white/30 uppercase tracking-wider mb-1.5">FINE-TUNING</div>
          <div>• Offset distance from walls</div>
          <div>• Inset / depth controls</div>
          <div>• Smart exterior placement</div>
          <div>• Pick the dimension style</div>
          <div>• Filter views by stage and level</div>
        </div>
      </div>
    </div>

    <!-- 3. OPEN THE WIZARD -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Open the Wizard</h3>
      <div class="grid gap-3 md:grid-cols-2">
        <HelpItem label="Wizard Triggers" :prompts="[
          'Open dimensioning wizard',
          'Automate dimensions',
          'Dimension walls in CD floor plans'
        ]" @pick="$emit('pick', $event)" />

        <div>
          <div class="bg-white/5 border border-white/10 rounded-lg p-3.5 text-xs text-white/80 cursor-default space-y-2">
            <div class="text-[10px] font-bold text-white/30 uppercase tracking-wider mb-1.5">SCOPE</div>
            <div><span class="text-white/60">View Type:</span> Floor Plan only</div>
            <div class="text-[10px] text-white/40 italic mt-2">
              Section / elevation dimensioning is not supported yet.
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
