<template>
  <div class="space-y-4"> <!-- overall spacing space-y-X -->
    
    <div class="space-y-4"> <!-- section spacing space-y-X -->
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Create Single Sheet with View: Plan/Ceiling</h3>
      
      <div class="grid gap-3 md:grid-cols-2"> <!-- grid gaps gap-X -->
        <HelpItem label="ALIGNED TO CENTER" :prompts="[
          'Create Floor Plan for levels LEVEL 01 in CD using A49_CD_A1_FLOOR PLAN and SB_PLAN_OVERALL place it on a new A1 sheet using A49_TB_A1_Horizontal: Plan Sheet aligned to center',
          'Create Ceiling Plan for levels LEVEL 01 in CD using A49_CD_A5_REFLECTED CEILING PLAN and SB_PLAN_OVERALL place it on a new A5 sheet using A49_TB_A1_Horizontal: Plan Sheet aligned to center'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="ALIGNED TO REFERENCE SHEET (E.g. A1.01)" :prompts="[
          'Create Floor Plan for levels LEVEL 01 in CD using A49_CD_A1_FLOOR PLAN and SB_PLAN_OVERALL place it on a new A1 sheet using A49_TB_A1_Horizontal: Plan Sheet matching sheet A1.01',
          'Create Ceiling Plan for levels LEVEL 01 in CD using A49_CD_A5_REFLECTED CEILING PLAN and SB_PLAN_OVERALL place it on a new A5 sheet using A49_TB_A1_Horizontal: Plan Sheet matching sheet A1.01'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

    <div class="space-y-4"> <!-- section spacing space-y-X -->
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Create Multiple Sheets with View: Plan/Ceiling</h3>
      
      <div class="grid gap-3 md:grid-cols-2"> <!-- grid gaps gap-X -->
        <HelpItem label="ALIGNED TO CENTER" :prompts="[
          'Create Floor Plan for levels L1-L3 in CD using A49_CD_A1_FLOOR PLAN and SB_PLAN_OVERALL place it on a new A1 sheet using A49_TB_A1_Horizontal: Plan Sheet aligned to center',
          'Create Ceiling Plan for levels L1-L3 in CD using A49_CD_A5_REFLECTED CEILING PLAN and SB_PLAN_OVERALL place it on a new A5 sheet using A49_TB_A1_Horizontal: Plan Sheet aligned to center'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="ALIGNED TO REFERENCE SHEET (E.g. A1.01)" :prompts="[
          'Create Floor Plan for levels L1-L3 in CD using A49_CD_A1_FLOOR PLAN and SB_PLAN_OVERALL place it on a new A1 sheet using A49_TB_A1_Horizontal: Plan Sheet matching sheet A1.01',
          'Create Ceiling Plan for levels L1-L3 in CD using A49_CD_A5_REFLECTED CEILING PLAN and SB_PLAN_OVERALL place it on a new A5 sheet using A49_TB_A1_Horizontal: Plan Sheet matching sheet A1.01'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

  </div>
</template>

<script setup>
import HelpItem from './HelpItem.vue';
defineEmits(['pick']);
</script>