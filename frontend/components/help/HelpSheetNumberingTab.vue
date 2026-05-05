<template>
  <div class="space-y-6">

    <!-- Intro / scheme selection -->
    <div class="space-y-3">
      <div class="flex items-center justify-between mb-2">
        <h3 class="text-sm font-bold text-[#A78BFA]">
          {{ isThai ? 'เลือกรูปแบบเลข Sheet' : 'Numbering Scheme Selection' }}
        </h3>
        <div class="flex items-center bg-white/5 rounded-md border border-white/15 p-0.5">
          <button @click="setLang('en')"
            class="px-2 py-0.5 rounded text-[10px] font-bold transition"
            :class="isThai ? 'text-white/50 hover:text-white/80' : 'bg-white/20 text-white'">EN</button>
          <button @click="setLang('th')"
            class="px-2 py-0.5 rounded text-[10px] font-bold transition"
            :class="isThai ? 'bg-white/20 text-white' : 'text-white/50 hover:text-white/80'">TH</button>
        </div>
      </div>
      <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 space-y-3">
        <div v-if="!isThai" class="text-[11px] text-white/70 leading-relaxed">
          Vella supports three sheet numbering schemes. The active scheme is
          <strong>auto-detected</strong> from your project's existing sheets:
          ISO19650 4-digit (e.g. 1010), ISO19650 5-digit (e.g. 10100), or
          a49SheetNaming dotted format (e.g. A1.03 — the legacy A49 office
          convention with sub-part splitting). For new/empty projects, you
          can opt in with the chat commands below. Once a project has its
          first real sheet, auto-detect locks the scheme in (mixed-scheme
          projects are not allowed).
        </div>
        <div v-else class="text-[11px] text-white/70 leading-relaxed">
          Vella รองรับการใช้เลข Sheet สามรูปแบบ ระบบจะ<strong>ตรวจหาอัตโนมัติ</strong>จาก
          Sheet ที่มีอยู่ในโครงการ: ISO19650 4 หลัก (เช่น 1010), ISO19650 5 หลัก
          (เช่น 10100), หรือ a49SheetNaming รูปแบบจุด (เช่น A1.03 — รูปแบบเดิมของ
          สำนักงาน A49 รองรับการแบ่ง Sheet ย่อย) สำหรับโครงการใหม่หรือว่างเปล่า
          สามารถเลือกรูปแบบที่ต้องการผ่านคำสั่งด้านล่างได้ค่ะ เมื่อโครงการมี Sheet
          แรกแล้ว ระบบจะล็อครูปแบบไว้โดยอัตโนมัติ (ไม่อนุญาตให้ใช้รูปแบบผสมกันในโครงการเดียว)
        </div>
        <HelpItem
          v-if="!isThai"
          label="Switch to 5-digit (large project)"
          :prompts="[
            'use iso19650 5-digit',
            'use 5-digit numbering',
            'switch to iso 5-digit'
          ]"
          @pick="$emit('pick', $event)" />
        <HelpItem
          v-else
          label="เปลี่ยนเป็นเลข 5 หลัก (โครงการใหญ่)"
          :prompts="[
            'ใช้เลข iso19650 5 หลัก',
            'ใช้เลข iso 5 หลัก',
            'ใช้เลข 5 หลัก'
          ]"
          @pick="$emit('pick', $event)" />
        <HelpItem
          v-if="!isThai"
          label="Switch to 4-digit (small project)"
          :prompts="[
            'use iso19650 4-digit',
            'use 4-digit numbering',
            'switch to iso 4-digit'
          ]"
          @pick="$emit('pick', $event)" />
        <HelpItem
          v-else
          label="เปลี่ยนเป็นเลข 4 หลัก (โครงการเล็ก)"
          :prompts="[
            'ใช้เลข iso19650 4 หลัก',
            'ใช้เลข iso 4 หลัก',
            'ใช้เลข 4 หลัก'
          ]"
          @pick="$emit('pick', $event)" />
        <HelpItem
          v-if="!isThai"
          label="Switch to a49SheetNaming (A1.03 dotted)"
          :prompts="[
            'use a49 sheet naming',
            'use a49 dotted',
            'switch to a49'
          ]"
          @pick="$emit('pick', $event)" />
        <HelpItem
          v-else
          label="เปลี่ยนเป็น a49SheetNaming (A1.03 แบบจุด)"
          :prompts="[
            'ใช้เลขแบบ a49',
            'ใช้แบบ a49',
            'ใช้เลขแบบจุด'
          ]"
          @pick="$emit('pick', $event)" />
        <HelpItem
          v-if="!isThai"
          label="Check active scheme"
          :prompts="[
            'what numbering scheme',
            'what scheme',
            'current scheme'
          ]"
          @pick="$emit('pick', $event)" />
        <HelpItem
          v-else
          label="ตรวจสอบรูปแบบที่ใช้อยู่"
          :prompts="[
            'ตอนนี้ใช้เลขอะไร',
            'ใช้เลขแบบไหน',
            'ใช้เลข iso แบบไหน'
          ]"
          @pick="$emit('pick', $event)" />
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
      <div class="flex items-center justify-between mb-2">
        <h3 class="text-sm font-bold text-[#A78BFA]">
          {{ isThai ? 'กฎการตั้งเลขและกรณีพิเศษ' : 'Numbering Rules & Edge Cases' }}
        </h3>
        <div class="flex items-center bg-white/5 rounded-md border border-white/15 p-0.5">
          <button @click="setLang('en')"
            class="px-2 py-0.5 rounded text-[10px] font-bold transition"
            :class="isThai ? 'text-white/50 hover:text-white/80' : 'bg-white/20 text-white'">EN</button>
          <button @click="setLang('th')"
            class="px-2 py-0.5 rounded text-[10px] font-bold transition"
            :class="isThai ? 'bg-white/20 text-white' : 'text-white/50 hover:text-white/80'">TH</button>
        </div>
      </div>
      <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 hover:border-white/20 transition-all duration-200">
        <ul class="space-y-1.5">
          <li v-for="(rule, idx) in sheetNumberRules" :key="idx"
              class="text-[11px] text-white/70 leading-relaxed flex gap-2">
            <span class="text-white/40">•</span>
            <span>{{ isThai ? rule.th : rule.en }}</span>
          </li>
        </ul>
      </div>
    </div>

  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue';
import HelpItem from './HelpItem.vue';

defineEmits(['pick']);

// Instruction language toggle for the Numbering Scheme Selection card.
// Persisted per browser via localStorage so each staff member's preference
// (EN / TH) sticks across sessions. Same pattern as RoomElevationWizard.
const LANG_STORAGE_KEY = 'vella.help.numbering.lang';
const isThai = ref(false);

onMounted(() => {
  try {
    if (typeof window !== 'undefined' && window.localStorage) {
      isThai.value = window.localStorage.getItem(LANG_STORAGE_KEY) === 'th';
    }
  } catch { /* localStorage may be blocked — fall back to default EN */ }
});

function setLang(lang) {
  isThai.value = lang === 'th';
  try {
    if (typeof window !== 'undefined' && window.localStorage) {
      window.localStorage.setItem(LANG_STORAGE_KEY, lang);
    }
  } catch { /* ignore */ }
}

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
  },
  {
    name: 'a49SheetNaming (A1.03 dotted)',
    code: 'a49_dotted',
    description: 'Legacy A49 dotted convention, brought back per staff feedback. Format: A<series>.<NN>. KEY DIFFERENCE: A1/A5 are sequence-based (NOT level-based) — sheets are allocated in creation order. Gap-fill enabled — deleted slots get reused. Sub-parts (A1.03.1, A1.03.2) coming in Phase 2.',
    formats: [
      {
        category: 'A0 - General Information',
        rule: 'Sequence-based, gap-fill, name-keyed slots',
        examples: ['A0.00 — COVER', 'A0.01 — DRAWING INDEX', 'A0.02 — SITE AND VICINITY PLAN', 'A0.06 — CUSTOM SHEET']
      },
      {
        category: 'A1 - Floor Plans',
        rule: 'level_sequence: A1.00 reserved for SITE, then sequential allocation for floors/basements/roof',
        examples: ['A1.00 — SITE PLAN', 'A1.01 — first floor sheet (e.g. B1)', 'A1.02 — next sheet (e.g. B2)', 'A1.03 — next (e.g. 1ST FLOOR PLAN)']
      },
      {
        category: 'A5 - Ceiling Plans',
        rule: 'level_sequence: no SITE; starts at A5.01',
        examples: ['A5.01 — first ceiling sheet', 'A5.02 — next', 'A5.03 — next']
      },
      {
        category: 'A2 / A3 / A4 / A6 / A7 / A8 / A9',
        rule: 'Sequence-based, gap-fill, +1 per sheet',
        examples: ['A2.01, A2.02 …', 'A6.01 (FLOOR PATTERN PLAN), A6.02 (TOILET) …']
      },
      {
        category: 'X0 - Custom',
        rule: 'Dotted X0.00, X0.01 …',
        examples: ['X0.00, X0.01, X0.02 …']
      }
    ]
  }
]);

// Bilingual {en, th} pairs — rendered through the same isThai ref used by
// the Numbering Scheme Selection card above. A native Thai speaker may want
// to refine the translations; the technical terms (slot, increment, B1, M, T)
// are kept in English where the spec uses them as identifiers.
const sheetNumberRules = ref([
  {
    en: 'Above-grade levels: L1, L2 … L99 (cap at L99). Slot = base + N × level_increment.',
    th: 'ระดับเหนือพื้นดิน: L1, L2 … L99 (สูงสุดที่ L99) สูตร: ช่อง = ฐาน + N × ระยะระหว่างระดับ',
  },
  {
    en: 'Below-grade levels: B1 closest to grade, B9 deepest. B1 takes the slot just before L1; deeper basements descend.',
    th: 'ระดับใต้ดิน: B1 อยู่ใกล้พื้นดินที่สุด, B9 ลึกที่สุด B1 จะอยู่ในช่องก่อนหน้า L1 และชั้นใต้ดินที่ลึกกว่าจะไล่ลงไป',
  },
  {
    en: 'Mezzanine / Transfer suffixes (M, T): take the parent slot, bare basement shifts down by sub_increment (e.g. B1+B1M → B1M takes B1\'s slot, B1 shifts down).',
    th: 'ตัวต่อท้ายชั้นลอย/Transfer (M, T): จะใช้ช่องของชั้นหลัก ส่วนชั้นใต้ดินตัวเปล่าจะเลื่อนลงตาม sub_increment (เช่น B1 + B1M → B1M ใช้ช่องของ B1 และ B1 เลื่อนลง)',
  },
  {
    en: 'ROOF / TOP: lands at (max above-grade level + 1) × level_increment. Project max determines the slot.',
    th: 'ROOF / TOP: อยู่ที่ช่อง (ระดับสูงสุดเหนือพื้นดิน + 1) × ระยะระหว่างระดับ ระดับสูงสุดของโครงการเป็นตัวกำหนดช่อง',
  },
  {
    en: 'A5 + SITE is rejected (no ceiling plan for site level — wizard greys out the option).',
    th: 'A5 + SITE จะถูกปฏิเสธ (ไม่มี Ceiling Plan สำหรับระดับ Site — Wizard จะปิดการเลือกอัตโนมัติ)',
  },
  {
    en: 'Auto-detect picks the scheme from your project\'s existing sheets. Mixed-scheme projects are not allowed — once a project has 5-digit sheets new ones are 5-digit; once it has dotted (A1.03) sheets new ones stay dotted.',
    th: 'ระบบตรวจหาอัตโนมัติจาก Sheet ที่มีในโครงการ ไม่อนุญาตให้ใช้รูปแบบผสมในโครงการเดียว — เมื่อโครงการมี Sheet แบบ 5 หลักแล้ว Sheet ใหม่จะใช้ 5 หลัก เช่นเดียวกันกับรูปแบบจุด (A1.03)',
  },
  {
    en: 'a49SheetNaming (A1.03 dotted) note: A1/A5 are sequence-based here, NOT level-based. Creating "Level 5 Floor Plan" lands at the next free A1 slot (e.g. A1.05) regardless of level number. Slots gap-fill — deleted A1.03 gets reused next time.',
    th: 'หมายเหตุ a49SheetNaming (A1.03 แบบจุด): A1/A5 เป็นแบบลำดับ ไม่ผูกกับระดับ การสร้าง "Level 5 Floor Plan" จะได้ช่องว่างถัดไปของ A1 (เช่น A1.05) โดยไม่สนเลขระดับ ช่องที่ลบไปจะถูกนำกลับมาใช้ใหม่ (gap-fill)',
  },
]);
</script>
