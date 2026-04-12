<template>
  <div class="space-y-4">
    <div class="space-y-4">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Offline Math & Conversions</h3>
      
      <div class="grid gap-3 md:grid-cols-2">
        <HelpItem label="1. SLOPE & RAMP CALCULATOR" :prompts="[
          'Calculate slope with a rise of 150 and a run of 1800',
          'What is the slope for a rise of 200 and a run of 2400'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="2. DIMENSIONAL AREA" :prompts="[
          'Area of 4000 mm by 5000 mm',
          'Calculate area of 4m x 5m'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="3. STANDARD CONVERSIONS" :prompts="[
          'Convert 1500 sqft to sqm',
          'Convert 15 inches to mm',
          'Convert 12 feet to m'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="4. THAILAND UNITS" :prompts="[
          'Convert 5 rai to sqm',
          'Format 2000 sqm in thai units',
          'Convert 120 tarang wa to m2'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="5. ADVANCED CALCULATOR" :prompts="[
          'Calculate (1200 / 2) + 150 * 3',
          'What is 1450 * 12'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>
  </div>
</template>

<script setup>
import HelpItem from './HelpItem.vue';
defineEmits(['pick']);
</script>