<template>
  <div class="space-y-4">

    <!-- 1. ABOUT THE TOOL -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">About Insert Standard Details</h3>
      <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 text-xs text-white/80 leading-relaxed">
        Vella streamlines Revit's <em>Insert Views from File</em> dialog by detecting your
        Revit version and pre-resolving the path to the matching A49 detail file.
        Pick <strong class="text-white">Standard</strong> or <strong class="text-white">EIA</strong>,
        click Browse, paste the path with <kbd class="px-1.5 py-0.5 rounded bg-white/10 text-white/80 text-[10px]">Ctrl+V</kbd>,
        then choose the views in Revit's native picker.
      </div>
    </div>

    <!-- 2. PACKAGES -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Detail Packages</h3>
      <div class="grid gap-3 md:grid-cols-2">
        <div class="bg-white/5 border border-white/10 rounded-lg p-3.5 text-xs text-white/80 cursor-default space-y-1.5">
          <div class="flex items-center gap-2 text-[10px] font-bold text-white/60 uppercase tracking-wider mb-1.5">
            <Icon name="lucide:square-stack" class="text-sm" />
            <span>Standard</span>
          </div>
          <div>A49 office-wide standard detail library.</div>
          <div class="text-[10px] text-white/40 italic">A49_Standard_Details_V[XX].rvt</div>
        </div>
        <div class="bg-white/5 border border-white/10 rounded-lg p-3.5 text-xs text-white/80 cursor-default space-y-1.5">
          <div class="flex items-center gap-2 text-[10px] font-bold text-white/60 uppercase tracking-wider mb-1.5">
            <Icon name="lucide:leaf" class="text-sm" />
            <span>EIA</span>
          </div>
          <div>Environmental Impact Assessment detail package.</div>
          <div class="text-[10px] text-white/40 italic">A49_EIA_Details_V[XX].rvt</div>
        </div>
      </div>
    </div>

    <!-- 3. NLP TRIGGERS -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Chat Commands</h3>
      <div class="grid gap-3 md:grid-cols-2">
        <HelpItem label="Standard Details" :prompts="[
          'Insert standard details',
          'Browse standard details',
          'Show me standard details',
          'Open standard details'
        ]" @pick="$emit('pick', $event)" />

        <HelpItem label="EIA Details" :prompts="[
          'Insert EIA details',
          'Browse EIA details',
          'Show me EIA details',
          'Open EIA details'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

    <!-- 4. NOTES -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Notes</h3>
      <div class="bg-white/5 border border-white/10 rounded-lg p-3.5 text-xs text-white/80 cursor-default space-y-2">
        <div>• File version is auto-matched to your Revit version (e.g. Revit 2024 → V24.rvt).</div>
        <div>• Network drive must be reachable. Vella reuses the same connection as Preflight Check.</div>
        <div>• After Browse opens Revit's dialog, the rest of the workflow uses Revit's native view picker — choose what to import as usual.</div>
        <div class="text-[10px] text-white/40 italic mt-2 pt-2 border-t border-white/10">
          💬 Toggle <span class="text-white/60 font-bold">EN | TH</span> inside the wizard for Thai instructions.
        </div>
      </div>
    </div>

  </div>
</template>

<script setup>
import HelpItem from './HelpItem.vue';
defineEmits(['pick']);
</script>
