<template>
  <div class="space-y-4">

    <!-- 1. STANDARD ELEMENT TAGS -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Standard Element Tags</h3>

      <div class="grid gap-3 md:grid-cols-2">
        <HelpItem label="Doors" :prompts="[
          'Tag doors in CD floor plans',
          'Tag doors in CD A1 floor plans for level 1',
          'Tag doors in all CD plans'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="Windows" :prompts="[
          'Tag windows in CD floor plans',
          'Tag windows in CD elevations',
          'Tag windows in CD A1 floor plans for level 1'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="Walls" :prompts="[
          'Tag walls in CD floor plans',
          'Tag walls in CD A1 floor plans for level 1'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="Rooms" :prompts="[
          'Tag rooms in CD floor plans',
          'Tag rooms in CD ceiling plans',
          'Tag rooms in CD A1 floor plans for level 1'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="Ceilings" :prompts="[
          'Tag ceilings in CD ceiling plans',
          'Tag ceilings in CD A5 ceiling plans for level 1'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

    <!-- 2. SPOT ELEVATIONS -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Spot Elevations</h3>

      <div class="grid gap-3 md:grid-cols-2">
        <HelpItem label="Floor Plans" :prompts="[
          'Insert spot elevations in CD floor plans',
          'Insert spot elevations in CD A1 floor plans for level 1'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="Building / Wall Sections" :prompts="[
          'Insert spot elevations in CD building sections',
          'Insert spot elevations in CD wall sections'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

    <!-- 3. WIZARD ALTERNATIVES -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Open the Tagging Wizard</h3>

      <div class="grid gap-3 md:grid-cols-2">
        <HelpItem label="Wizard Triggers" :prompts="[
          'Open tagging wizard',
          'Automate tagging'
        ]" @pick="$emit('pick', $event)" />

        <div>
          <div class="bg-white/5 border border-white/10 rounded-lg p-3.5 text-xs text-white/80 cursor-default space-y-2">
            <div class="text-[10px] font-bold text-white/30 uppercase tracking-wider mb-1.5">VIEW TYPE COMPATIBILITY</div>
            <div><span class="text-white/60">Doors / Windows:</span> Floor Plan, Elevation, Section</div>
            <div><span class="text-white/60">Walls:</span> Floor Plan</div>
            <div><span class="text-white/60">Rooms:</span> Floor Plan, Ceiling Plan, Elevation, Section</div>
            <div><span class="text-white/60">Ceilings:</span> Ceiling Plan</div>
            <div><span class="text-white/60">Spot Elevations:</span> Floor Plan, Section</div>
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
