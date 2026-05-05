<template>
  <div class="space-y-6">

    <!-- Intro / scheme selection -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#A78BFA] mb-2">Numbering Scheme Selection</h3>
      <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 space-y-3">
        <div class="text-[11px] text-white/70 leading-relaxed">
          Vella supports two ISO19650 numbering schemes. The active scheme is
          <strong>auto-detected</strong> from your project's existing sheets:
          sheet numbers with 4 chars → 4-digit scheme; 5+ chars → 5-digit scheme.
          For new/empty projects, you can opt in with the chat commands below.
          Once a project has its first real sheet, auto-detect locks the scheme
          in (mixed 4-digit + 5-digit projects are not allowed).
        </div>
        <HelpItem label="Switch to 5-digit (large project)" :prompts="[
          'use iso19650 5-digit',
          'use 5-digit numbering',
          'switch to iso 5-digit'
        ]" @pick="$emit('pick', $event)" />
        <HelpItem label="Switch to 4-digit (small project)" :prompts="[
          'use iso19650 4-digit',
          'use 4-digit numbering',
          'switch to iso 4-digit'
        ]" @pick="$emit('pick', $event)" />
        <HelpItem label="Check active scheme" :prompts="[
          'what numbering scheme',
          'what scheme',
          'current scheme'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

    <!-- Per-scheme format details -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#A78BFA] mb-2">Slot Allocation by Series</h3>
      <div class="space-y-3">
        <div v-for="scheme in numberingSchemes" :key="scheme.code"
             class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-200">
          <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-1">
            {{ scheme.name }}
          </div>
          <div class="text-[11px] text-white/50 italic mb-3">{{ scheme.description }}</div>
          <div class="space-y-3">
            <div v-for="fmt in scheme.formats" :key="fmt.category"
                 class="border-b border-white/5 last:border-b-0 pb-2 last:pb-0">
              <span class="text-xs font-medium text-white block mb-1">{{ fmt.category }}</span>
              <span class="text-[11px] text-white/50 italic block mb-1">{{ fmt.rule }}</span>
              <div class="flex flex-wrap gap-1">
                <span v-for="(ex, idx) in fmt.examples" :key="idx"
                      class="text-[11px] text-white/70 bg-white/5 px-2 py-0.5 rounded hover:bg-white/15 hover:text-white transition-all duration-200 cursor-default">
                  {{ ex }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Edge-case rules summary -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#A78BFA] mb-2">Numbering Rules &amp; Edge Cases</h3>
      <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-200">
        <ul class="space-y-1.5">
          <li v-for="(rule, idx) in sheetNumberRules" :key="idx"
              class="text-[11px] text-white/70 leading-relaxed flex gap-2">
            <span class="text-white/40">•</span>
            <span>{{ rule }}</span>
          </li>
        </ul>
      </div>
    </div>

  </div>
</template>

<script setup>
import { ref } from 'vue';
import HelpItem from './HelpItem.vue';

defineEmits(['pick']);

// SHEET NUMBERING FORMAT (ISO19650 dual-scheme — v1.2.0)
// Examples mirror the SCHEMES dict in backend/ai_router/ai_engines/naming_engine.py
const numberingSchemes = ref([
  {
    name: 'ISO19650 4-digit',
    code: 'iso19650_4digit',
    description: 'Default. Used for typical projects. Format: 4-digit number (or X+3 digits for custom).',
    formats: [
      {
        category: 'A0 - General Information',
        rule: 'Sequence-based, +10 per sheet',
        examples: ['0000 — COVER', '0010 — DRAWING INDEX', '0020 — SITE AND VICINITY PLAN']
      },
      {
        category: 'A1 - Floor Plans',
        rule: 'Level-based (1 site slot, B1-B9 below)',
        examples: ['1000 — SITE PLAN', '1009 — LEVEL B1', '1010 — LEVEL 1', '1020 — LEVEL 2', '1011 — LEVEL 1M (sub-level)']
      },
      {
        category: 'A5 - Ceiling Plans',
        rule: 'Level-based (mirror of A1, no SITE)',
        examples: ['5009 — LEVEL B1', '5010 — LEVEL 1', '5020 — LEVEL 2', '5060 — LEVEL ROOF (auto)']
      },
      {
        category: 'A2 / A3 / A4 / A6 / A7 / A8 / A9',
        rule: 'Sequence-based, series base + 10, then +10',
        examples: ['2010, 2020, 2030 …', '6010, 6020, 6030 …']
      },
      {
        category: 'X0 - Custom',
        rule: 'X-prefix + 3 digits, +10 increment',
        examples: ['X000, X010, X020 …']
      }
    ]
  },
  {
    name: 'ISO19650 5-digit',
    code: 'iso19650_5digit',
    description: 'For large projects with more slot density. Every increment is ×10 the 4-digit value. Format: 5-digit number (or X+4 digits for custom).',
    formats: [
      {
        category: 'A0 - General Information',
        rule: 'Sequence-based, +100 per sheet (+10 for sub-variants)',
        examples: ['00000 — COVER', '00100 — DRAWING INDEX', '00200 — SITE AND VICINITY PLAN']
      },
      {
        category: 'A1 - Floor Plans',
        rule: '10 SITE slots (10000-10009) · 9 basements at +10 spacing · 9 sub-slots per level',
        examples: ['10000-10009 — SITE PLAN ×10', '10090 — LEVEL B1', '10010 — LEVEL B9', '10100 — LEVEL 1', '10110 — LEVEL 1M', '10120 — LEVEL 1T', '10200 — LEVEL 2']
      },
      {
        category: 'A5 - Ceiling Plans',
        rule: 'Level-based (mirror of A1, no SITE)',
        examples: ['50090 — LEVEL B1', '50100 — LEVEL 1', '50200 — LEVEL 2', '50600 — LEVEL ROOF (auto)']
      },
      {
        category: 'A2 / A3 / A4 / A6 / A7 / A8 / A9',
        rule: 'Sequence-based, series base + 100 (+10 for weave-in)',
        examples: ['20100, 20200, 20300 …', '60100, 60200, 60300 …']
      },
      {
        category: 'X0 - Custom',
        rule: 'X-prefix + 4 digits, +100 increment',
        examples: ['X0000, X0100, X0200 …']
      }
    ]
  }
]);

const sheetNumberRules = ref([
  'Above-grade levels: L1, L2 … L99 (cap at L99). Slot = base + N × level_increment.',
  'Below-grade levels: B1 closest to grade, B9 deepest. B1 takes the slot just before L1; deeper basements descend.',
  'Mezzanine / Transfer suffixes (M, T): take the parent slot, bare basement shifts down by sub_increment (e.g. B1+B1M → B1M takes B1\'s slot, B1 shifts down).',
  'ROOF / TOP: lands at (max above-grade level + 1) × level_increment. Project max determines the slot.',
  'A5 + SITE is rejected (no ceiling plan for site level — wizard greys out the option).',
  'Auto-detect picks the scheme from your project\'s existing sheets. Mixed-scheme projects are not allowed — once a project has 5-digit sheets, new ones are also 5-digit.',
  'Legacy "A1.01" / "A1.xx" dotted format is deprecated and no longer accepted as input.'
]);
</script>
