<template>
  <div class="space-y-4"> <!-- overall spacing space-y-X -->
    
    <div class="space-y-4"> <!-- section spacing space-y-X -->
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Create Single Sheet: Plan/Ceiling</h3>
      
      <div class="grid gap-3 md:grid-cols-2"> <!-- grid gaps gap-X -->
        <HelpItem label="BY SHEET TYPE" :prompts="[
          'Create Sheets A1',
          'Create Sheets A5'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="WITH DESIGN STAGE" :prompts="[
          'Create Sheets A1 for LEVEL 01 in CD',
          'Create Sheets A5 for LEVEL 01 in CD'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="WITH TEMPLATE" :prompts="[
          'Create Sheets A1 for LEVEL 01 in CD using titleblock A49_TB_A1_Horizontal: Plan Sheet',
          'Create Sheets A5 for LEVEL 01 in CD using titleblock A49_TB_A1_Horizontal: Plan Sheet'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

    <div class="space-y-3"> <!-- section spacing space-y-X -->
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Create Multiple Sheets: Plan/Ceiling</h3>
      
      <div class="grid gap-3 md:grid-cols-2"> <!-- grid gaps gap-X -->
        <HelpItem label="Multiple Levels (Range)" :prompts="[
          'Create Sheets A1 for L1-L4 in CD using titleblock A49_TB_A1_Horizontal: Plan Sheet',
          'Create Sheets A5 for L1-L4 in CD using titleblock A49_TB_A1_Horizontal: Plan Sheet'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="Multiple Levels (List)" :prompts="[
          'Create Sheets A1 for LEVEL 01, LEVEL 03, LEVEL 05 in CD using titleblock A49_TB_A1_Horizontal: Plan Sheet',
          'Create Sheets A5 for LEVEL 01, LEVEL 03, LEVEL 05 in CD using titleblock A49_TB_A1_Horizontal: Plan Sheet'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

    <div class="space-y-3"> <!-- section spacing space-y-X -->
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Create Multiple Sheets: A0, A2, A3, A4, A6, A7, A8, A9, X0</h3>
      
      <div class="grid gap-3 md:grid-cols-2"> <!-- grid gaps gap-X -->
        <HelpItem label="Sheets with Quantity" :prompts="[
          'Create 3 Sheets A2 in CD using titleblock A49_TB_A1_Horizontal: Plan Sheet',
          'Create 5 Sheets A9 in CD using titleblock A49_TB_A1_Horizontal: Detail Sheet'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="Additional Examples" :prompts="[
          'Create 2 Sheets A0 in CD using titleblock A49_TB_A1_Horizontal: Plan Sheet',
          'Create 4 Sheets A3 in CD using titleblock A49_TB_A1_Horizontal: Plan Sheet',
          'Create 1 Sheets X0 in CD'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

  </div>
</template>

<script setup>
import HelpItem from './HelpItem.vue';
defineEmits(['pick']);
</script>