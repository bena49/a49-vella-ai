<template>
  <div class="space-y-6"> <!-- overall spacing space-y-X -->

    <!-- 0. Project Sync — note about the refresh command -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Project Sync</h3>
      <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 space-y-3">
        <div class="text-[11px] text-white/70 leading-relaxed">
          Vella caches your project's levels, sheets, and tags so chat commands
          resolve instantly. The cache primes itself when you sign in. If you
          rename or add a level / sheet / tag in Revit mid-session, type any
          of the commands below to re-sync.
        </div>
        <HelpItem label="Refresh Project Info" :prompts="[
          'refresh',
          'reload',
          'refresh project',
          'sync project'
        ]" @pick="$emit('pick', $event)" />
      </div>
    </div>

    <!-- 1. Project Phase -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Project Phase</h3>
      <div class="space-y-3">
        <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-200">
          <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-3">Working Stages & Phase Codes</div>
          <div class="space-y-2">
            <div v-for="phase in projectPhases" :key="phase.category" class="border-b border-white/5 last:border-b-0 pb-2 last:pb-0">
              <span class="text-xs font-medium text-white block mb-1">{{ phase.category }}</span>
              <div class="flex flex-wrap gap-1">
                <span class="text-[11px] text-white/70 bg-white/5 px-2 py-0.5 rounded hover:bg-white/15 hover:text-white transition-all duration-200 cursor-default">
                  {{ phase.items }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 2. View Types -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">View Types</h3>
      <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-200">
        <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-3">View Categories & Their Codes</div>
        <div class="space-y-2">
          <div v-for="view in viewTypes" :key="view.category" class="border-b border-white/5 last:border-b-0 pb-2 last:pb-0">
            <span class="text-xs font-medium text-white block mb-1">{{ view.category }}</span>
            <div class="flex flex-wrap gap-1">
              <span v-for="(code, idx) in view.items" :key="idx" 
                    class="text-[11px] text-white/70 bg-white/5 px-2 py-0.5 rounded hover:bg-white/15 hover:text-white transition-all duration-200 cursor-default">
                {{ code }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 3. Template Types -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Template Types</h3>
      <div class="space-y-3">
        <div v-for="phase in templatePhases" :key="phase.name" class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-200">
          <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-3">{{ phase.name }} ({{ phase.code }})</div>
          <div class="space-y-2">
            <div v-for="template in phase.templates" :key="template.category" class="border-b border-white/5 last:border-b-0 pb-2 last:pb-0">
              <span class="text-xs font-medium text-white block mb-1">{{ template.category }}</span>
              <div class="flex flex-wrap gap-1">
                <span v-for="(item, idx) in template.items" :key="idx" 
                      class="text-[11px] text-white/70 bg-white/5 px-2 py-0.5 rounded hover:bg-white/15 hover:text-white transition-all duration-200 cursor-default">
                  {{ item }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 4. Scope Box Types -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Scope Box Types</h3>
      <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-200">
        <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-3">Scope Box Prefixes by View Type</div>
        <div class="space-y-2">
          <div v-for="scope in scopeBoxes" :key="scope.category" class="border-b border-white/5 last:border-b-0 pb-2 last:pb-0">
            <span class="text-xs font-medium text-white block mb-1">{{ scope.category }}</span>
            <div class="space-y-1">
              <span v-for="(prefix, idx) in scope.prefixes" :key="idx" 
                    class="text-[11px] text-white/70 bg-white/5 px-2 py-0.5 rounded hover:bg-white/15 hover:text-white transition-all duration-200 cursor-default block">
                {{ prefix }}
              </span>
              <span v-if="scope.prefixes.length === 0" class="text-[11px] text-white/40 italic hover:bg-white/5 px-2 py-0.5 rounded transition-all duration-200 cursor-default block">
                No scope box prefixes
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 5. Titleblock Types -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Titleblock Types</h3>
      <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-200">
        <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-3">Titleblock Templates</div>
        <div class="space-y-4">
          <div class="border-b border-white/5 pb-3 last:border-b-0 last:pb-0">
            <span class="text-xs font-bold text-white mb-2 block">Standard Titleblocks:</span>
            <div class="space-y-2">
              <div v-for="tb in standardTitleblocks" :key="tb.name" class="p-2 bg-white/5 rounded hover:bg-white/10 transition-all duration-200">
                <span class="text-xs font-medium text-white block">{{ tb.name }}</span>
                <div class="flex flex-wrap gap-1 mt-1">
                  <span v-for="(purpose, idx) in tb.for" :key="idx" 
                        class="text-[11px] text-white/60 bg-white/5 px-2 py-0.5 rounded hover:bg-white/15 hover:text-white transition-all duration-200 cursor-default">
                    {{ purpose }}
                  </span>
                </div>
              </div>
            </div>
          </div>
          <div>
            <span class="text-xs font-bold text-white mb-2 block">Cover Titleblocks:</span>
            <div class="p-2 bg-white/5 rounded hover:bg-white/10 transition-all duration-200">
              <span class="text-xs font-medium text-white block">{{ coverTitleblocks[0].name }}</span>
              <div class="flex flex-wrap gap-1 mt-1">
                <span v-for="(purpose, idx) in coverTitleblocks[0].for" :key="idx" 
                      class="text-[11px] text-white/60 bg-white/5 px-2 py-0.5 rounded hover:bg-white/15 hover:text-white transition-all duration-200 cursor-default">
                  {{ purpose }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 6. Sheet Names -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Sheet Names</h3>
      
      <div class="space-y-3">
        <!-- Sheet Series Categories -->
        <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-200">
          <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-3">Sheet Series Categories</div>
          <div class="space-y-2">
            <div v-for="series in sheetSeries" :key="series.category" class="border-b border-white/5 last:border-b-0 pb-2 last:pb-0">
              <span class="text-xs font-medium text-white block mb-1">{{ series.category }}</span>
              <div class="flex flex-wrap gap-1">
                <span class="text-[11px] text-white/70 bg-white/5 px-2 py-0.5 rounded hover:bg-white/15 hover:text-white transition-all duration-200 cursor-default">
                  {{ series.items }}
                </span>
              </div>
            </div>
          </div>
        </div>

        <!-- Default Sheet Names -->
        <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-200">
          <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-3">Default Sheet Names by Series</div>
          <div class="space-y-2">
            <div v-for="sheet in defaultSheetNames" :key="sheet.category" class="border-b border-white/5 last:border-b-0 pb-2 last:pb-0">
              <span class="text-xs font-medium text-white block mb-1">{{ sheet.category }}</span>
              <div class="flex flex-wrap gap-1">
                <span v-for="(name, idx) in sheet.items" :key="idx"
                      class="text-[11px] text-white/70 bg-white/5 px-2 py-0.5 rounded hover:bg-white/15 hover:text-white transition-all duration-200 cursor-default">
                  {{ name }}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 7. Sheet Numbering Format (ISO19650) -->
    <div class="space-y-3">
      <h3 class="text-sm font-bold text-[#60A5FA] mb-2">Sheet Numbering Format (ISO19650)</h3>

      <div class="space-y-3">
        <!-- Intro / scheme selection -->
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

        <!-- Per-scheme format details -->
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

        <!-- Edge-case rules summary -->
        <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-200">
          <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-3">Numbering Rules &amp; Edge Cases</div>
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

  </div>
</template>

<script setup>
import { ref } from 'vue';
import HelpItem from './HelpItem.vue';

defineEmits(['pick']);

// 1. PROJECT PHASE
const projectPhases = ref([
  { category: 'WV', items: '00 - WORKING VIEW' },
  { category: 'PD', items: '01 - PRE-DESIGN' },
  { category: 'DD', items: '02 - DESIGN DEVELOPMENT' },
  { category: 'CD', items: '03 - CONSTRUCTION DOCUMENTS' }
]);

// 2. VIEW TYPES
const viewTypes = ref([
  { category: 'Floor Plan', items: ['FL', 'A1'] },
  { category: 'Site Plan', items: ['SITE'] },
  { category: 'Ceiling Plan', items: ['CP', 'A5'] },
  { category: 'Area Plan', items: ['AP'] },
  { category: 'Elevation', items: ['EL', 'A2'] },
  { category: 'Building Section', items: ['SE', 'A3'] },
  { category: 'Wall Section', items: ['WS', 'A4'] },
  { category: 'Detail View', items: ['D1', 'A9'] },
  { category: 'Schedule', items: ['SC', 'A8', 'AD', 'AW'] },
  { category: 'Enlarged Plan', items: ['PT', 'A6'] },
  { category: 'Vertical Circulation', items: ['ST', 'A7'] }
]);

// 3. TEMPLATE TYPES
const templatePhases = ref([
  {
    name: 'Working View', code: 'WV',
    templates: [
      { category: 'FL', items: ['A49_WV_FLOOR PLAN'] },
      { category: 'SITE', items: ['A49_WV_SITE PLAN'] },
      { category: 'CP', items: ['A49_WV_REFLECTED CEILING PLAN'] },
      { category: 'EL', items: ['A49_WV_ELEVATION'] },
      { category: 'SE', items: ['A49_WV_BUILDING SECTION'] },
      { category: 'WS', items: ['A49_WV_WALL SECTION'] }
    ]
  },
  {
    name: 'Pre-Design', code: 'PD',
    templates: [
      { category: 'FL', items: ['A49_PD_FLOOR PLAN', 'A49_PD_PRESENTATION PLAN'] },
      { category: 'SITE', items: ['A49_PD_SITE PLAN'] },
      { category: 'CP', items: ['A49_PD_REFLECTED CEILING PLAN'] },
      { category: 'EL', items: ['A49_PD_ELEVATION'] },
      { category: 'SE', items: ['A49_PD_BUILDING SECTION'] },
      { category: 'WS', items: ['A49_PD_WALL SECTION'] },
      { category: 'SC', items: ['A49_PD_DOOR & WINDOW'] },
      { category: 'D1', items: ['A49_PD_DETAILS'] },
      { category: 'AP', items: ['A49_PD_EIA AREA PLAN', 'A49_PD_NFA AREA PLAN'] }
    ]
  },
  {
    name: 'Design Development', code: 'DD',
    templates: [
      { category: 'FL', items: ['A49_DD_FLOOR PLAN', 'A49_DD_PRESENTATION PLAN'] },
      { category: 'SITE', items: ['A49_DD_SITE PLAN'] },
      { category: 'CP', items: ['A49_DD_REFLECTED CEILING PLAN'] },
      { category: 'EL', items: ['A49_DD_ELEVATION'] },
      { category: 'SE', items: ['A49_DD_BUILDING SECTION'] },
      { category: 'WS', items: ['A49_DD_WALL SECTION'] },
      { category: 'SC', items: ['A49_DD_DOOR & WINDOW'] },
      { category: 'D1', items: ['A49_DD_DETAILS'] },
      { category: 'AP', items: ['A49_DD_EIA AREA PLAN', 'A49_DD_NFA AREA PLAN'] }
    ]
  },
  {
    name: 'Construction Documents', code: 'CD',
    templates: [
      { category: 'A1', items: ['A49_CD_A1_FLOOR PLAN', 'A49_CD_A1_FLOOR PLAN_COLOR'] },
      { category: 'SITE', items: ['A49_CD_A1_SITE PLAN', 'A49_CD_A1_SITE PLAN_COLOR'] },
      { category: 'A2', items: ['A49_CD_A2_ELEVATION', 'A49_CD_A2_ELEVATION_COLOR'] },
      { category: 'A3', items: ['A49_CD_A3_BUILDING SECTION', 'A49_CD_A3_BUILDING SECTION_COLOR'] },
      { category: 'A4', items: ['A49_CD_A4_WALL SECTION', 'A49_CD_A4_DETAIL SECTION', 'A49_CD_A4_WALL SECTION_COLOR'] },
      { category: 'A5', items: ['A49_CD_A5_REFLECTED CEILING PLAN', 'A49_CD_A5_REFLECTED CEILING PLAN_COLOR'] },
      { category: 'A6', items: ['A49_CD_A6_PATTERNS PLAN', 'A49_CD_A6_INTERIOR ENLARGED PLAN', 'A49_CD_A6_INTERIOR ELEVATION'] },
      { category: 'A7', items: ['A49_CD_A7_VERTICAL CIRCULATION PLAN', 'A49_CD_A7_VERTICAL CIRCULATION_SECTION'] },
      { category: 'A8', items: ['A49_CD_A8_DOOR & WINDOW'] },
      { category: 'A9', items: ['A49_CD_A9_DETAILS'] }
    ]
  }
]);

// 4. SCOPE BOX TYPES
const scopeBoxes = ref([
  {
    category: 'Floor Plans (A1/FL), Site Plans (SITE), Ceiling Plans (A5/CP):',
    prefixes: ['SB_PLAN_ (Overall building plan)', 'SB_PARTIAL_ (Partial/section plans)']
  },
  {
    category: 'Area Plans (AP):',
    prefixes: ['SB_PLAN_ (Overall area plan)', 'SB_AREA_ (Area-specific scope boxes)']
  },
  { 
    category: 'Elevations (A2/EL):',
    prefixes: ['SB_BLDG_ELEV_ (Building elevations)']
  },
  { 
    category: 'Building Sections (A3/SE):',
    prefixes: ['SB_BLDG_SECT_ (Building sections)']
  },
  { 
    category: 'Wall Sections (A4/WS):',
    prefixes: ['SB_WALL_SECT_ (Wall sections)']
  },
  { 
    category: 'Enlarged Plans (A6/PT):',
    prefixes: ['SB_ENLARGED_ (Enlarged plans)', 'SB_INTR_ELEV_ (Interior elevations)']
  },
  { 
    category: 'Vertical Circulation (A7/ST):',
    prefixes: ['SB_PLAN_STAIR_ (Stair plans)', 'SB_PLAN_LIFT_ (Lift plans)', 'SB_PLAN_RAMP_ (Ramp plans)']
  },
  { 
    category: 'Schedules (A8/SC), Details (A9/D1):',
    prefixes: []
  }
]);

// 5. TITLEBLOCK TYPES
const standardTitleblocks = ref([
  { name: 'A49_TB_A1_Horizontal', for: ['Plan Sheet', 'Detail Sheet'] },
  { name: 'A49_TB_A1_Vertical', for: ['Plan Sheet', 'Detail Sheet'] }
]);

const coverTitleblocks = ref([
  { name: 'A49_TB_A1_Horizontal_Cover', for: ['Cover'] }
]);

// 6. SHEET NAMES
const sheetSeries = ref([
  { category: 'A0', items: 'A0_GENERAL INFORMATION' },
  { category: 'A1', items: 'A1_FLOOR PLANS' },
  { category: 'A2', items: 'A2_BUILDING ELEVATIONS' },
  { category: 'A3', items: 'A3_BUILDING SECTIONS' },
  { category: 'A4', items: 'A4_WALL SECTIONS' },
  { category: 'A5', items: 'A5_CEILING PLANS' },
  { category: 'A6', items: 'A6_ENLARGED PLANS AND INTERIOR ELEVATIONS' },
  { category: 'A7', items: 'A7_VERTICAL CIRCULATION' },
  { category: 'A8', items: 'A8_DOOR AND WINDOW SCHEDULE' },
  { category: 'A9', items: 'A9_DETAILS' },
  { category: 'X0', items: 'Custom Sheets (no sheet set)' }
]);

const defaultSheetNames = ref([
  { category: 'A0 - General Information',
    items: ['COVER', 'DRAWING INDEX', 'SITE AND VICINITY PLAN', 'STANDARD SYMBOLS', 'SAFETY PLAN', 'WALL TYPES'] },
  { category: 'A1 - Floor Plans',
    items: ['SITE PLAN', 'LEVEL 1 FLOOR PLAN', 'LEVEL 2 FLOOR PLAN', 'LEVEL B1 FLOOR PLAN', 'LEVEL ROOF PLAN'] },
  { category: 'A2 - Building Elevations',
    items: ['ELEVATIONS'] },
  { category: 'A3 - Building Sections',
    items: ['BUILDING SECTIONS'] },
  { category: 'A4 - Wall Sections',
    items: ['WALL SECTIONS'] },
  { category: 'A5 - Ceiling Plans',
    items: ['LEVEL 1 CEILING PLAN', 'LEVEL 2 CEILING PLAN', 'LEVEL B1 CEILING PLAN', 'LEVEL ROOF CEILING PLAN'] },
  { category: 'A6 - Enlarged Plans and Interior Elevations',
    items: ['ENLARGED TOILET PLAN', 'FLOOR PATTERN PLAN', 'CANOPY PLAN'] },
  { category: 'A7 - Vertical Circulation',
    items: ['ENLARGED STAIR PLAN', 'ENLARGED STAIR SECTION', 'ENLARGED RAMP PLAN', 'ENLARGED LIFT PLAN'] },
  { category: 'A8 - Door and Window Schedule',
    items: ['DOOR SCHEDULE', 'WINDOW SCHEDULE'] },
  { category: 'A9 - Details',
    items: ['DETAILS'] },
  { category: 'X0 - Custom Sheet',
    items: ['CUSTOM SHEET'] }
]);

// 7. SHEET NUMBERING FORMAT (ISO19650 dual-scheme — v1.2.0)
//    Examples mirror the SCHEMES dict in backend/ai_router/ai_engines/naming_engine.py
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