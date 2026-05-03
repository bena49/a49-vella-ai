<template>
  <div class="space-y-4">

    <!-- INTRO -->
    <div class="space-y-1">
      <h3 class="text-sm font-bold text-[#FFB74D]">Offline Math &amp; Conversions</h3>
      <p class="text-[11px] text-white/60 leading-relaxed">
        Calculate inline — results update as you type. You can also type any of these
        as a chat message; see the cheat sheet at the bottom for syntax.
      </p>
    </div>

    <!-- ─── 1. SLOPE / RAMP ─────────────────────────────────────────── -->
    <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 space-y-3">
      <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold">
        1. Slope / Ramp
      </div>
      <div class="flex flex-wrap items-end gap-3">
        <div>
          <label class="text-[10px] text-white/40 block mb-1">Rise (mm)</label>
          <HelpNumberInput v-model="slope.rise" :step="10" :min="0" width-class="w-28" />
        </div>
        <div>
          <label class="text-[10px] text-white/40 block mb-1">Run (mm)</label>
          <HelpNumberInput v-model="slope.run" :step="100" :min="0" width-class="w-28" />
        </div>
      </div>
      <ResultRow v-if="slopeResult" :text="slopeText" />
      <ResultRow v-else-if="slope.rise || slope.run" text="—" :muted="true" />
    </div>

    <!-- ─── 2. DIMENSIONAL AREA ─────────────────────────────────────── -->
    <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 space-y-3">
      <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold">
        2. Dimensional Area
      </div>
      <div class="flex flex-wrap items-end gap-2">
        <div>
          <label class="text-[10px] text-white/40 block mb-1">Width</label>
          <HelpNumberInput v-model="area.w" :step="100" :min="0" width-class="w-24" />
        </div>
        <select v-model="area.wUnit" class="bg-white/10 border border-white/20 rounded-lg px-2 py-1 text-xs text-white outline-none focus:border-[#FFB74D]">
          <option value="mm">mm</option>
          <option value="m">m</option>
        </select>
        <span class="pb-1.5 text-white/40">×</span>
        <div>
          <label class="text-[10px] text-white/40 block mb-1">Height</label>
          <HelpNumberInput v-model="area.h" :step="100" :min="0" width-class="w-24" />
        </div>
        <select v-model="area.hUnit" class="bg-white/10 border border-white/20 rounded-lg px-2 py-1 text-xs text-white outline-none focus:border-[#FFB74D]">
          <option value="mm">mm</option>
          <option value="m">m</option>
        </select>
      </div>
      <ResultRow v-if="areaResult !== null" :text="areaText" />
    </div>

    <!-- ─── 3. UNIT CONVERSION ──────────────────────────────────────── -->
    <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 space-y-3">
      <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold">
        3. Unit Conversion
      </div>
      <div class="flex flex-wrap items-end gap-2">
        <div>
          <label class="text-[10px] text-white/40 block mb-1">Value</label>
          <HelpNumberInput v-model="conv.value" :step="10" width-class="w-28" />
        </div>
        <select v-model="conv.from" class="bg-white/10 border border-white/20 rounded-lg px-2 py-1 text-xs text-white outline-none focus:border-[#FFB74D]">
          <optgroup label="Length">
            <option v-for="u in unitsByType.length" :key="u.key" :value="u.key">{{ u.label }}</option>
          </optgroup>
          <optgroup label="Area">
            <option v-for="u in unitsByType.area" :key="u.key" :value="u.key">{{ u.label }}</option>
          </optgroup>
          <optgroup label="Volume">
            <option v-for="u in unitsByType.volume" :key="u.key" :value="u.key">{{ u.label }}</option>
          </optgroup>
        </select>
        <span class="pb-1.5 text-white/40">→</span>
        <select v-model="conv.to" class="bg-white/10 border border-white/20 rounded-lg px-2 py-1 text-xs text-white outline-none focus:border-[#FFB74D]">
          <optgroup label="Length">
            <option v-for="u in unitsByType.length" :key="u.key" :value="u.key">{{ u.label }}</option>
          </optgroup>
          <optgroup label="Area">
            <option v-for="u in unitsByType.area" :key="u.key" :value="u.key">{{ u.label }}</option>
          </optgroup>
          <optgroup label="Volume">
            <option v-for="u in unitsByType.volume" :key="u.key" :value="u.key">{{ u.label }}</option>
          </optgroup>
        </select>
      </div>
      <ResultRow v-if="convResult !== null" :text="convText" />
      <ResultRow v-else-if="convTypeMismatch" text="Cannot convert between different unit types (length / area / volume)." :muted="true" />
    </div>

    <!-- ─── 4. THAI LAND AREA ───────────────────────────────────────── -->
    <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 space-y-4">
      <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold">
        4. Thailand Area Format
      </div>

      <!-- 4a. Forward: sq/m → Rai / Ngan / Wa -->
      <div class="space-y-2">
        <div class="text-[10px] text-white/50 uppercase tracking-wider">sq/m → Rai · Ngan · Wa</div>
        <div class="flex flex-wrap items-end gap-3">
          <div>
            <label class="text-[10px] text-white/40 block mb-1">Square Metres (sq/m)</label>
            <HelpNumberInput v-model="thai.sqm" :step="100" :min="0" width-class="w-32" />
          </div>
        </div>
        <ResultRow v-if="thaiResult" :text="thaiText" />
      </div>

      <!-- divider -->
      <div class="border-t border-white/10"></div>

      <!-- 4b. Reverse: Rai / Ngan / Wa → sq/m -->
      <div class="space-y-2">
        <div class="text-[10px] text-white/50 uppercase tracking-wider">Rai · Ngan · Wa → sq/m</div>
        <div class="flex flex-wrap items-end gap-3">
          <div>
            <label class="text-[10px] text-white/40 block mb-1">Rai</label>
            <HelpNumberInput v-model="thaiRev.rai" :step="1" :min="0" width-class="w-20" />
          </div>
          <div>
            <label class="text-[10px] text-white/40 block mb-1">Ngan</label>
            <HelpNumberInput v-model="thaiRev.ngan" :step="1" :min="0" width-class="w-20" />
          </div>
          <div>
            <label class="text-[10px] text-white/40 block mb-1">Tarang Wa</label>
            <HelpNumberInput v-model="thaiRev.wa" :step="1" :min="0" width-class="w-24" />
          </div>
        </div>
        <ResultRow v-if="thaiRevResult !== null" :text="thaiRevText" />
      </div>
    </div>

    <!-- ─── 5. EXPRESSION CALCULATOR ────────────────────────────────── -->
    <div class="bg-white/5 backdrop-blur-sm border border-white/10 rounded-xl p-4 space-y-3">
      <div class="text-[10px] uppercase tracking-wider text-white/50 font-bold">
        5. Expression Calculator
      </div>
      <div class="space-y-2">
        <label class="text-[10px] text-white/40 block">Expression (digits + - * / . ( ) only)</label>
        <input v-model="calc.expr" type="text" placeholder="(1200/2)+150*3"
               class="w-full bg-white/10 border border-white/20 rounded-lg px-3 py-2 text-sm text-white font-mono outline-none focus:border-[#FFB74D] transition" />
      </div>
      <ResultRow v-if="calcResult !== null" :text="calcText" />
      <ResultRow v-else-if="calc.expr.trim()" text="Invalid expression." :muted="true" />
    </div>

    <!-- ─── CHAT SYNTAX CHEAT SHEET (collapsed) ─────────────────────── -->
    <div class="bg-white/5 border border-white/10 rounded-xl overflow-hidden">
      <button @click="showCheatSheet = !showCheatSheet"
              class="w-full flex items-center justify-between px-4 py-3 text-xs text-white/70 hover:text-white hover:bg-white/5 transition">
        <span class="flex items-center gap-2">
          <Icon name="lucide:message-circle" class="text-base text-[#FFB74D]" />
          <span>Show chat syntax (for typing into chat)</span>
        </span>
        <Icon :name="showCheatSheet ? 'lucide:chevron-up' : 'lucide:chevron-down'" class="text-base text-white/50" />
      </button>
      <div v-if="showCheatSheet" class="px-4 pb-4 pt-1 space-y-3 border-t border-white/10">
        <div class="text-[10px] text-white/50 italic pt-2">
          Click any line to copy. Then paste into the chat.
        </div>
        <div class="grid gap-2 md:grid-cols-2">
          <HelpItem label="Slope" :prompts="[
            'Calculate slope with a rise of 150 and a run of 1800',
            'What is the slope for a rise of 200 and a run of 2400'
          ]" @pick="$emit('pick', $event)" />
          <HelpItem label="Area" :prompts="[
            'Area of 4000 mm by 5000 mm',
            'Calculate area of 4m x 5m'
          ]" @pick="$emit('pick', $event)" />
          <HelpItem label="Conversions" :prompts="[
            'Convert 1500 sqft to sqm',
            'Convert 15 inches to mm',
            'Convert 12 feet to m'
          ]" @pick="$emit('pick', $event)" />
          <HelpItem label="Thai Land" :prompts="[
            'Convert 5 rai to sqm',
            'Format 2000 sqm in thai units',
            'Convert 120 tarang wa to m2'
          ]" @pick="$emit('pick', $event)" />
          <HelpItem label="Expression" :prompts="[
            'Calculate (1200 / 2) + 150 * 3',
            'What is 1450 * 12'
          ]" @pick="$emit('pick', $event)" />
        </div>
      </div>
    </div>

  </div>
</template>

<script setup>
import { ref, reactive, computed, h } from 'vue';
import HelpItem from './HelpItem.vue';
import HelpNumberInput from './HelpNumberInput.vue';

defineEmits(['pick']);

// =====================================================================
// UNITS — mirrors math_engine.py's lookup table. All factors convert
// the source unit INTO the type's base SI unit (m, m², or m³).
// =====================================================================
const UNITS = {
  // Length (base = metre)
  mm: { type: 'length', toBase: 0.001,    label: 'mm' },
  m:  { type: 'length', toBase: 1,        label: 'm'  },
  in: { type: 'length', toBase: 0.0254,   label: 'in' },
  ft: { type: 'length', toBase: 0.3048,   label: 'ft' },
  // Area (base = square metre)
  'sqmm': { type: 'area',   toBase: 0.000001, label: 'sq/mm' },
  'sqm':  { type: 'area',   toBase: 1,        label: 'sq/m'  },
  'sqft': { type: 'area',   toBase: 0.092903, label: 'sq/ft' },
  rai:    { type: 'area',   toBase: 1600,     label: 'rai'   },
  ngan:   { type: 'area',   toBase: 400,      label: 'ngan'  },
  wa:     { type: 'area',   toBase: 4,        label: 'tarang wa' },
  // Volume (base = cubic metre)
  'cumm': { type: 'volume', toBase: 0.000000001, label: 'cu/mm' },
  'cum':  { type: 'volume', toBase: 1,           label: 'cu/m'  },
};
const unitsByType = {
  length: Object.entries(UNITS).filter(([, u]) => u.type === 'length').map(([k, u]) => ({ key: k, label: u.label })),
  area:   Object.entries(UNITS).filter(([, u]) => u.type === 'area'  ).map(([k, u]) => ({ key: k, label: u.label })),
  volume: Object.entries(UNITS).filter(([, u]) => u.type === 'volume').map(([k, u]) => ({ key: k, label: u.label })),
};

// Number formatting helpers
const fmt2 = (n) => n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const fmt4 = (n) => n.toLocaleString('en-US', { minimumFractionDigits: 0, maximumFractionDigits: 4 });

// =====================================================================
// 1. SLOPE / RAMP
// =====================================================================
const slope = reactive({ rise: 150, run: 1800 });
const slopeResult = computed(() => {
  const { rise, run } = slope;
  if (!rise || !run || run <= 0) return null;
  return { percent: (rise / run) * 100, ratio: run / rise };
});
const slopeText = computed(() => {
  const r = slopeResult.value;
  if (!r) return '';
  return `${fmt2(r.percent)}% (or 1:${r.ratio.toLocaleString('en-US', { maximumFractionDigits: 1 })})`;
});

// =====================================================================
// 2. DIMENSIONAL AREA
// =====================================================================
const area = reactive({ w: 4000, wUnit: 'mm', h: 5000, hUnit: 'mm' });
const areaResult = computed(() => {
  const { w, wUnit, h, hUnit } = area;
  if (!w || !h) return null;
  const wM = wUnit === 'm' ? w : w / 1000;
  const hM = hUnit === 'm' ? h : h / 1000;
  return wM * hM;
});
const areaText = computed(() => {
  const r = areaResult.value;
  if (r === null) return '';
  return `${fmt2(r)} sq/m`;
});

// =====================================================================
// 3. UNIT CONVERSION
// =====================================================================
const conv = reactive({ value: 1500, from: 'sqft', to: 'sqm' });
const convResult = computed(() => {
  const { value, from, to } = conv;
  if (value === '' || value === null || value === undefined || isNaN(value)) return null;
  const fU = UNITS[from], tU = UNITS[to];
  if (!fU || !tU) return null;
  if (fU.type !== tU.type) return null; // can't convert across types
  return value * fU.toBase / tU.toBase;
});
const convText = computed(() => {
  const r = convResult.value;
  if (r === null) return '';
  return `${fmt4(r)} ${UNITS[conv.to].label}`;
});
// True when the user entered a value but the chosen units are incompatible.
const convTypeMismatch = computed(() =>
  conv.value !== '' && conv.value !== null && !isNaN(conv.value) &&
  UNITS[conv.from] && UNITS[conv.to] &&
  UNITS[conv.from].type !== UNITS[conv.to].type
);

// =====================================================================
// 4. THAI LAND FORMAT
// =====================================================================
const thai = reactive({ sqm: 2000 });
const thaiResult = computed(() => {
  const sqm = thai.sqm;
  if (!sqm || sqm <= 0) return null;
  const totalWa = sqm / 4;
  const rai = Math.floor(totalWa / 400);
  const remWa = totalWa % 400;
  const ngan = Math.floor(remWa / 100);
  const wa = remWa % 100;
  return { rai, ngan, wa };
});
const thaiText = computed(() => {
  const r = thaiResult.value;
  if (!r) return '';
  const parts = [];
  if (r.rai > 0)  parts.push(`${r.rai} Rai`);
  if (r.ngan > 0) parts.push(`${r.ngan} Ngan`);
  if (r.wa > 0 || (r.rai === 0 && r.ngan === 0)) {
    const waStr = r.wa % 1 === 0 ? r.wa.toString() : r.wa.toFixed(2).replace(/\.?0+$/, '');
    parts.push(`${waStr} Tarang Wa`);
  }
  return parts.join(', ');
});

// 4b. THAI LAND — REVERSE: Rai + Ngan + Wa → sq/m
// 1 Wa = 4 sqm, 1 Ngan = 100 Wa = 400 sqm, 1 Rai = 4 Ngan = 1600 sqm
const thaiRev = reactive({ rai: 1, ngan: 1, wa: 100 });
const thaiRevResult = computed(() => {
  const rai  = Number(thaiRev.rai)  || 0;
  const ngan = Number(thaiRev.ngan) || 0;
  const wa   = Number(thaiRev.wa)   || 0;
  if (rai === 0 && ngan === 0 && wa === 0) return null;
  return rai * 1600 + ngan * 400 + wa * 4;
});
const thaiRevText = computed(() => {
  const r = thaiRevResult.value;
  if (r === null) return '';
  return `${fmt2(r)} sq/m`;
});

// =====================================================================
// 5. EXPRESSION CALCULATOR
// =====================================================================
const calc = reactive({ expr: '(1200/2)+150*3' });
const calcResult = computed(() => {
  const expr = calc.expr.trim().replace(/,/g, '');
  if (!expr) return null;
  // Whitelist: digits, whitespace, + - * / . ( ) only — no identifiers.
  if (!/^[\d\s\+\-\*\/\.\(\)]+$/.test(expr)) return null;
  try {
    // Function constructor with a strict, frozen scope. The regex above is the
    // security boundary — no identifiers can ever reach this line.
    const r = Function('"use strict"; return (' + expr + ')')();
    if (typeof r !== 'number' || !isFinite(r)) return null;
    return r;
  } catch {
    return null;
  }
});
const calcText = computed(() => {
  const r = calcResult.value;
  if (r === null) return '';
  return fmt2(r);
});

// =====================================================================
// CHEAT SHEET
// =====================================================================
const showCheatSheet = ref(false);

// =====================================================================
// RESULT ROW — small inline functional component for the result line
// =====================================================================
const ResultRow = (props) => h('div', {
  class: 'flex items-center gap-2 text-xs ' + (props.muted ? 'text-white/40 italic' : 'text-white/90'),
}, [
  h('span', { class: 'text-[#FFB74D] font-bold' }, '▸'),
  h('span', { class: 'flex-1 break-all font-mono' }, props.text),
  !props.muted ? h('button', {
    class: 'text-white/40 hover:text-white transition px-1',
    title: ' ',
    onClick: async () => {
      try { await navigator.clipboard.writeText(props.text); } catch { /* ignore */ }
    },
  }, '⧉') : null,
]);
</script>
