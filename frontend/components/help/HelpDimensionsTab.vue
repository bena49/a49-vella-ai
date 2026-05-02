<template>
  <div class="space-y-4">

    <!-- 1. WALL DIMENSIONING -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Wall Dimensioning</h3>

      <div class="grid gap-3 md:grid-cols-2">
        <HelpItem label="Basic Walls" :prompts="[
          'Dimension walls in CD floor plans',
          'Dimension walls in CD floor plans for level 1'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="Walls + Openings" :prompts="[
          'Dimension walls with openings in CD floor plans',
          'Dimension walls with openings in CD A1 floor plans for level 1'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

    <!-- 2. WALLS + GRIDS -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Walls + Grids</h3>

      <div class="grid gap-3 md:grid-cols-2">
        <HelpItem label="Combined" :prompts="[
          'Dimension walls and grids in CD floor plans',
          'Dimension walls and grids in CD floor plans for level 1'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="Grids Only" :prompts="[
          'Dimension grids only in CD floor plans',
          'Dimension grids only in CD A1 floor plans for level 1'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

    <!-- 3. WIZARD ALTERNATIVES -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Open the Dimensioning Wizard</h3>

      <div class="grid gap-3 md:grid-cols-2">
        <HelpItem label="Wizard Triggers" :prompts="[
          'Open dimensioning wizard',
          'Automate dimensions'
        ]" @pick="$emit('pick', $event)" />

        <div>
          <div class="bg-white/5 border border-white/10 rounded-lg p-3.5 text-xs text-white/80 cursor-default space-y-2">
            <div class="text-[10px] font-bold text-white/30 uppercase tracking-wider mb-1.5">SCOPE</div>
            <div><span class="text-white/60">View Type:</span> Floor Plan only</div>
            <div><span class="text-white/60">Includes:</span> exterior walls, interior walls, optional openings, optional grids, optional total chain</div>
            <div class="text-[10px] text-white/40 italic mt-2">For finer placement controls (offsets, depth, dim style), open the wizard.</div>
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
