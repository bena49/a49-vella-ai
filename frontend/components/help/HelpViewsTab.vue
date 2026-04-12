<template>
  <div class="space-y-4"> <!-- overall spacing space-y-X -->
    
    <div class="space-y-4"> <!-- section spacing space-y-X -->
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Create Single Plan Views</h3>
      
      <div class="grid gap-3 md:grid-cols-2"> <!-- grid gaps gap-X -->
        <HelpItem label="By View Type" :prompts="[
          'Create Site Plan for Site',
          'Create L1 Floor Plan',
          'Create L1 Ceiling Plan'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="With Design Stage" :prompts="[
          'Create Site Plan for Site in CD',
          'Create L1 Floor Plan in CD',
          'Create L1 Ceiling Plan in CD'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="With Template" :prompts="[
          'Create Site Plan for Site in CD using A49_CD_A1_SITE PLAN',
          'Create L1 Floor Plan in CD using A49_CD_A1_FLOOR PLAN',
          'Create L1 Ceiling Plan in CD using A49_CD_A5_REFLECTED CEILING PLAN'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="With Scope Box" :prompts="[
          'Create Site Plan for Site in CD using A49_CD_A1_SITE PLAN and SB_PLAN_OVERAL',
          'Create L1 Floor Plan in CD using A49_CD_A1_FLOOR PLAN and SB_PLAN_OVERAL',
          'Create L1 Ceiling Plan in CD using A49_CD_A5_REFLECTED CEILING PLAN and SB_PLAN_OVERAL'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

    <div class="space-y-3"> <!-- section spacing space-y-X -->
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Create Multiple Plan Views</h3>
      <HelpItem label="Batch Creation (Range)" :prompts="[
        'Create L1-L4 Floor Plan in CD using A49_CD_A1_FLOOR PLAN and SB_PLAN_OVERAL'
      ]" @pick="$emit('pick', $event)" />
    </div>

    <div class="space-y-3"> <!-- section spacing space-y-X -->
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Schedules & Drafting Views</h3>
      
      <div class="grid gap-3 md:grid-cols-2"> <!-- grid gaps gap-X -->
        <HelpItem label="Schedules" :prompts="[
          'Create 1 Schedule in CD using A49_CD_A8_DOOR & WINDOW',
          'Create 4 Schedules in CD using A49_CD_A8_DOOR & WINDOW'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="Drafting Views" :prompts="[
          'Create 1 Detail Views in CD using A49_CD_A9_DETAILS',
          'Create 4 Detail Views in CD using A49_CD_A9_DETAILS'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

  </div>
</template>

<script setup>
import HelpItem from './HelpItem.vue';
defineEmits(['pick']);
</script>