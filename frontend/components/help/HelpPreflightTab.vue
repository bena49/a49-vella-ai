<template>
  <div class="space-y-4">

    <!-- 1. ABOUT THE TOOL -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">About Preflight Check</h3>
      <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 text-xs text-white/80 leading-relaxed">
        Preflight Check audits your project against the A49 standards file
        (templates, view names, sheet structure, naming conventions). It reports
        any deviations and offers a one-click <strong class="text-white">Repair</strong>
        for the issues it can fix automatically.
      </div>
    </div>

    <!-- 2. WHAT IT CHECKS -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">What It Checks</h3>
      <div class="bg-white/5 border border-white/10 rounded-lg p-3.5 text-xs text-white/80 cursor-default space-y-1.5">
        <div>• Project template alignment (against A49_TEMPLATE_RVT[XX].rvt)</div>
        <div>• View naming conventions (stage / sheet-type / level codes)</div>
        <div>• Template assignment per view type</div>
        <div>• Scope box prefixes</div>
        <div>• Titleblock usage</div>
        <div>• Sheet series and naming</div>
      </div>
    </div>

    <!-- 3. NLP TRIGGERS -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Chat Commands</h3>
      <div class="grid gap-3 md:grid-cols-2">
        <HelpItem label="Run Preflight" :prompts="[
          'Preflight check',
          'Run preflight check',
          'Health check',
          'Check standards'
        ]" @pick="$emit('pick', $event)" />

        <div>
          <div class="bg-white/5 border border-white/10 rounded-lg p-3.5 text-xs text-white/80 cursor-default space-y-2">
            <div class="text-[10px] font-bold text-white/30 uppercase tracking-wider mb-1.5">RESPONDING TO REPAIR</div>
            <div>After Preflight reports issues, Vella asks if you want to repair.</div>
            <div><span class="text-white/60">Yes:</span> "yes", "y", "fix", "repair", "proceed"</div>
            <div><span class="text-white/60">No:</span> "no", "cancel", "skip", "later"</div>
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
