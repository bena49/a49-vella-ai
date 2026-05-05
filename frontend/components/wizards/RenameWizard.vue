<template>
  <div class="fixed inset-0 flex items-center justify-center z-50 p-4">
    <div class="absolute inset-0 bg-black/10 backdrop-blur-sm" @click="$emit('close')"></div>

    <div class="relative bg-gradient-to-br from-[#0A1D4A]/95 via-[#0A1D4A]/70 to-[#0A1D4A]/40
        backdrop-blur-2xl border border-white/20
        text-white rounded-3xl w-full max-w-5xl shadow-2xl flex flex-col
        h-[88vh] animate-fade-in-up">

      <!-- HEADER -->
      <div class="p-5 border-b border-white/10 flex justify-between items-center shrink-0">
        <div class="flex items-center gap-2">
          <div class="w-8 h-8 rounded-full bg-[#D8B4FE]/20 flex items-center justify-center text-[#D8B4FE]">
            <Icon name="lucide:pencil-line" class="text-xl" />
          </div>
          <h2 class="text-sm font-bold tracking-wide">RENUMBER &amp; RENAME — SHEETS</h2>
          <span v-if="detectedScheme"
            class="ml-2 px-2 py-0.5 rounded-md bg-white/10 border border-white/15 text-[10px] font-bold tracking-wide text-white/70"
            :title="`Auto-detected from existing sheet numbers — operations will treat numbers using the ${SCHEME_LABELS[detectedScheme]} layout.`">
            {{ SCHEME_LABELS[detectedScheme] }}
          </span>
        </div>
        <button @click="$emit('close')" class="text-white/40 hover:text-white transition">
          <Icon name="lucide:x" class="text-xl" />
        </button>
      </div>

      <!-- STEPPER -->
      <div class="px-6 py-3 border-b border-white/5 bg-white/5 shrink-0">
        <div class="flex items-center justify-between gap-2">
          <button v-for="(label, idx) in STEP_LABELS" :key="idx"
            @click="goToStep(idx + 1)"
            class="flex-1 flex items-center gap-2 px-3 py-2 rounded-xl text-[11px] font-bold transition-all"
            :class="step === idx + 1
              ? 'bg-[#D8B4FE] text-[#0A1D4A] shadow-md'
              : (idx + 1 < step
                  ? 'bg-white/15 text-white/70 hover:bg-white/20'
                  : 'bg-white/5 text-white/40 cursor-default')">
            <span class="w-5 h-5 rounded-full bg-black/20 flex items-center justify-center text-[10px]">
              {{ idx + 1 }}
            </span>
            <span class="truncate">{{ label }}</span>
          </button>
        </div>
      </div>

      <!-- BODY -->
      <div class="flex-1 overflow-hidden relative bg-[#0A1D4A]">
        <div class="absolute inset-0 overflow-y-auto custom-scrollbar p-6">

          <!-- ====================== STEP 1: FILTER ====================== -->
          <div v-if="step === 1" class="space-y-5 max-w-3xl mx-auto">

            <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-2 block">
                Stage
              </label>
              <div class="flex flex-wrap gap-2">
                <button v-for="opt in STAGE_OPTIONS" :key="opt.value || 'all'"
                  @click="filterStage = opt.value"
                  class="px-3 py-1.5 rounded-lg text-xs font-bold transition border"
                  :class="filterStage === opt.value
                    ? 'bg-[#D8B4FE] text-[#0A1D4A] border-transparent'
                    : 'bg-white/5 border-white/15 text-white/70 hover:bg-white/10'">
                  {{ opt.label }}
                </button>
              </div>
            </div>

            <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-2 block">
                Sheet Category
              </label>
              <div class="flex flex-wrap gap-2">
                <button v-for="opt in CATEGORY_OPTIONS" :key="opt"
                  @click="toggleCategory(opt)"
                  class="px-3 py-1.5 rounded-lg text-xs font-bold transition border"
                  :class="categoryActive(opt)
                    ? 'bg-[#D8B4FE] text-[#0A1D4A] border-transparent'
                    : 'bg-white/5 border-white/15 text-white/70 hover:bg-white/10'">
                  {{ opt === '*' ? 'All' : opt }}
                </button>
              </div>
            </div>

            <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-2 block">
                Search (number or name contains…)
              </label>
              <input v-model="searchQuery" type="text"
                placeholder="e.g. PLAN, 1010, ผังพื้น…"
                class="w-full bg-black/20 border border-white/10 rounded-xl px-3 py-2 text-xs text-white outline-none focus:border-[#D8B4FE]" />
            </div>

            <div class="bg-black/20 border border-white/10 rounded-xl p-4 text-xs text-white/70 flex items-center justify-between">
              <span>
                <span class="font-bold text-[#D8B4FE]">{{ filteredSheets.length }}</span>
                of {{ allSheets.length }} sheet(s) match the current filter
              </span>
              <span v-if="filteredSheets.length === 0" class="text-rose-300 italic">
                Adjust filters to continue
              </span>
            </div>
          </div>

          <!-- ====================== STEP 2: OPERATION ====================== -->
          <div v-else-if="step === 2" class="space-y-5 max-w-3xl mx-auto">

            <div>
              <label class="text-[10px] uppercase tracking-wider text-white/50 font-bold mb-2 block">
                Operation
              </label>
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-2">
                <button v-for="op in OPERATIONS" :key="op.value"
                  @click="selectOperation(op.value)"
                  class="text-left p-3 rounded-xl border transition-all flex flex-col gap-1"
                  :class="operation === op.value
                    ? 'bg-[#D8B4FE] text-[#0A1D4A] border-transparent shadow-md'
                    : 'bg-white/5 border-white/15 text-white/80 hover:bg-white/10'">
                  <div class="flex items-center gap-2">
                    <Icon :name="op.icon" class="text-base" />
                    <span class="font-bold text-xs">{{ op.label }}</span>
                  </div>
                  <span class="text-[10px] opacity-80 leading-snug">{{ op.hint }}</span>
                </button>
              </div>
            </div>

            <!-- ─── Per-operation params ─── -->
            <div v-if="operation"
              class="bg-black/20 border border-white/10 rounded-xl p-4 space-y-3">

              <div class="text-[10px] uppercase tracking-wider text-[#D8B4FE]/80 font-bold">
                Settings
              </div>

              <!-- find_replace -->
              <template v-if="operation === 'find_replace'">
                <ParamFieldSelector v-model="params.field" />
                <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  <ParamInput v-model="params.find" label="Find" placeholder="text or pattern" />
                  <ParamInput v-model="params.replace" label="Replace with" placeholder="(empty to delete)" />
                </div>
                <div class="flex flex-wrap gap-4 text-[11px]">
                  <ParamCheckbox v-model="params.regex" label="Treat 'find' as regex" />
                  <ParamCheckbox v-model="params.case_sensitive" label="Case-sensitive" />
                </div>
              </template>

              <!-- case_transform -->
              <template v-else-if="operation === 'case_transform'">
                <ParamFieldSelector v-model="params.field" />
                <ParamSegmented v-model="params.transform" label="Transform"
                  :options="[
                    { value: 'upper', label: 'UPPERCASE' },
                    { value: 'lower', label: 'lowercase' },
                    { value: 'title', label: 'Title Case' },
                  ]" />
              </template>

              <!-- prefix_suffix -->
              <template v-else-if="operation === 'prefix_suffix'">
                <ParamFieldSelector v-model="params.field" />
                <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  <ParamInput v-model="params.add_prefix" label="Add prefix" placeholder="e.g. CD - " />
                  <ParamInput v-model="params.add_suffix" label="Add suffix" placeholder="e.g.  (DRAFT)" />
                  <ParamInput v-model="params.strip_prefix" label="Strip prefix (if present)" placeholder="e.g. OLD_" />
                  <ParamInput v-model="params.strip_suffix" label="Strip suffix (if present)" placeholder="e.g. _v1" />
                </div>
              </template>

              <!-- translate_en_to_th / translate_th_to_en -->
              <template v-else-if="operation === 'translate_en_to_th' || operation === 'translate_th_to_en'">
                <div class="text-[11px] text-white/60 leading-relaxed">
                  Translates sheet <span class="font-bold text-white/80">names</span> using the
                  bilingual A49 dictionary. Drawing codes (A1, X0, ST-01, WS01,
                  pure numbers) are preserved verbatim.
                  <br />
                  <span class="italic text-white/40">No additional settings.</span>
                </div>
              </template>

              <!-- add_stage_prefix -->
              <template v-else-if="operation === 'add_stage_prefix'">
                <ParamSegmented v-model="params.stage" label="Stage code"
                  :options="[
                    { value: '',   label: 'Auto (per sheet)' },
                    { value: 'CD', label: 'CD' },
                    { value: 'DD', label: 'DD' },
                    { value: 'PD', label: 'PD' },
                    { value: 'WV', label: 'WV' },
                  ]" />
                <ParamInput v-model="params.separator" label="Separator" placeholder=" - " />
                <ParamCheckbox v-model="params.strip_existing"
                  label="Strip any existing CD/DD/PD/WV prefix first" />
              </template>

              <!-- offset_renumber -->
              <template v-else-if="operation === 'offset_renumber'">
                <ParamInput v-model.number="params.delta" type="number"
                  label="Delta (integer to add)"
                  placeholder="e.g. 100 or -10" />
                <div class="text-[11px] text-white/50 italic leading-relaxed">
                  Adds the delta to each numeric sheet number while preserving
                  digit width. Skips dotted (a49_dotted) numbers — use Scheme
                  Convert for those.
                </div>
              </template>

              <!-- scheme_convert -->
              <template v-else-if="operation === 'scheme_convert'">
                <ParamSegmented v-model="params.from_scheme" label="From scheme"
                  :options="SCHEME_OPTIONS" />
                <ParamSegmented v-model="params.to_scheme" label="To scheme"
                  :options="SCHEME_OPTIONS" />
                <div v-if="params.from_scheme === 'a49_dotted' && params.to_scheme && params.to_scheme !== 'a49_dotted'"
                  class="text-[11px] text-amber-300/90 italic leading-relaxed flex items-start gap-2">
                  <Icon name="lucide:alert-triangle" class="text-sm mt-0.5 shrink-0" />
                  <span>
                    Dotted → numeric uses naive position mapping. The original
                    dotted slot doesn't carry level semantics, so verify the
                    preview before applying.
                  </span>
                </div>
              </template>
            </div>

            <div v-else class="text-center py-6 text-white/40 text-xs italic">
              Pick an operation above to configure settings.
            </div>
          </div>

          <!-- ====================== STEP 3: PREVIEW ====================== -->
          <div v-else-if="step === 3" class="space-y-3">

            <!-- Summary bar -->
            <div class="bg-black/20 border border-white/10 rounded-xl p-3 flex flex-wrap items-center gap-4 text-xs">
              <span class="flex items-center gap-1.5">
                <Icon name="lucide:list" class="text-base text-white/50" />
                <span class="text-white/70">{{ previewRows.length }} sheet(s) previewed</span>
              </span>
              <span class="flex items-center gap-1.5">
                <Icon name="lucide:edit-3" class="text-base text-[#D8B4FE]" />
                <span class="text-white/70">
                  <span class="text-[#D8B4FE] font-bold">{{ activeUpdateCount }}</span>
                  selected for update
                </span>
              </span>
              <span v-if="warningCount > 0" class="flex items-center gap-1.5">
                <Icon name="lucide:alert-triangle" class="text-base text-amber-300" />
                <span class="text-amber-300/80">{{ warningCount }} warning(s)</span>
              </span>
              <span class="ml-auto flex items-center gap-2">
                <button @click="selectAll(true)"
                  class="text-[10px] text-white/60 hover:text-white px-2 py-1 rounded-md hover:bg-white/10 transition">
                  Select all
                </button>
                <button @click="selectAll(false)"
                  class="text-[10px] text-white/60 hover:text-white px-2 py-1 rounded-md hover:bg-white/10 transition">
                  Deselect all
                </button>
              </span>
            </div>

            <!-- Loading state -->
            <div v-if="isLoadingPreview" class="text-center py-8 text-white/50 text-xs italic flex items-center justify-center gap-2">
              <Icon name="lucide:loader-2" class="text-base animate-spin" />
              <span>Computing preview…</span>
            </div>

            <!-- Error state -->
            <div v-else-if="previewError" class="text-center py-8 text-rose-300 text-xs">
              <Icon name="lucide:x-circle" class="text-2xl block mx-auto mb-2" />
              {{ previewError }}
            </div>

            <!-- Empty state -->
            <div v-else-if="previewRows.length === 0" class="text-center py-8 text-white/40 text-xs italic">
              No sheets in scope. Go back and adjust the filter.
            </div>

            <!-- Diff table -->
            <table v-else class="w-full text-left border-collapse text-xs">
              <thead class="bg-[#0A1D4A] sticky top-0 z-10 border-b border-white/10">
                <tr>
                  <th class="py-2 pl-2 w-8"></th>
                  <th class="py-2 text-[10px] uppercase tracking-wider text-white/50 font-bold w-1/4">
                    Number
                  </th>
                  <th class="py-2 text-[10px] uppercase tracking-wider text-white/50 font-bold">
                    Name
                  </th>
                </tr>
              </thead>
              <tbody class="divide-y divide-white/5">
                <tr v-for="row in previewRows" :key="row.unique_id"
                  class="group hover:bg-white/5 transition"
                  :class="row.changed ? '' : 'opacity-50'">

                  <td class="py-2 pl-2 align-top">
                    <input type="checkbox" v-model="rowSelected[row.unique_id]"
                      :disabled="!row.changed"
                      class="accent-[#D8B4FE] cursor-pointer" />
                  </td>

                  <td class="py-2 pr-3 font-mono align-top">
                    <div v-if="row.old_number !== row.new_number" class="flex flex-col gap-0.5">
                      <span class="text-white/40 line-through">{{ row.old_number }}</span>
                      <span class="text-[#D8B4FE] font-bold">{{ row.new_number }}</span>
                    </div>
                    <span v-else class="text-white/50">{{ row.old_number }}</span>
                  </td>

                  <td class="py-2 pr-2 align-top">
                    <div v-if="row.old_name !== row.new_name" class="flex flex-col gap-0.5">
                      <span class="text-white/40 line-through break-words">{{ row.old_name }}</span>
                      <span class="text-[#D8B4FE] font-bold break-words">{{ row.new_name }}</span>
                    </div>
                    <span v-else class="text-white/50 break-words">{{ row.old_name }}</span>

                    <div v-if="row.warnings && row.warnings.length"
                      class="mt-1 flex items-start gap-1 text-amber-300/90 text-[10px] italic">
                      <Icon name="lucide:alert-triangle" class="text-xs shrink-0 mt-0.5" />
                      <span>{{ row.warnings.join('; ') }}</span>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- FOOTER -->
      <div class="p-4 border-t border-white/10 shrink-0 bg-[#0A1D4A]/40 backdrop-blur-xl rounded-b-3xl
        flex items-center justify-between gap-3">
        <button v-if="step > 1" @click="step--"
          class="px-4 py-2 rounded-xl text-xs font-bold text-white/70 hover:text-white hover:bg-white/10 transition flex items-center gap-2">
          <Icon name="lucide:arrow-left" class="text-sm" />
          <span>Back</span>
        </button>
        <span v-else></span>

        <button v-if="step < 3" @click="advance"
          :disabled="!canAdvance"
          class="px-5 py-2 rounded-xl text-xs font-bold transition flex items-center gap-2"
          :class="canAdvance
            ? 'bg-[#D8B4FE] text-[#0A1D4A] hover:bg-[#dfc3fd] shadow-md'
            : 'bg-white/10 text-white/30 cursor-not-allowed'">
          <span>{{ step === 2 ? 'Preview' : 'Next' }}</span>
          <Icon name="lucide:arrow-right" class="text-sm" />
        </button>

        <button v-else @click="apply"
          :disabled="activeUpdateCount === 0 || isLoadingPreview"
          class="px-5 py-2 rounded-xl text-xs font-bold transition flex items-center gap-2"
          :class="activeUpdateCount > 0 && !isLoadingPreview
            ? 'bg-[#D8B4FE] text-[#0A1D4A] hover:bg-[#dfc3fd] shadow-lg shadow-purple-900/30'
            : 'bg-white/10 text-white/30 cursor-not-allowed'">
          <span>Apply {{ activeUpdateCount }} update{{ activeUpdateCount === 1 ? '' : 's' }}</span>
          <Icon name="lucide:check" class="text-sm" />
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted, defineComponent, h } from 'vue';

// ─────────────────────────────────────────────────────────────────────────
// Tiny inline param widgets — kept local so they don't pollute the global
// components folder. Defined inside <script setup> so Vue's template
// compiler auto-resolves them as component bindings.
// ─────────────────────────────────────────────────────────────────────────
const ParamInput = defineComponent({
  name: 'ParamInput',
  props: {
    modelValue: { type: [String, Number], default: '' },
    label:      { type: String,            default: '' },
    placeholder:{ type: String,            default: '' },
    type:       { type: String,            default: 'text' },
  },
  emits: ['update:modelValue'],
  setup(p, { emit }) {
    return () => h('label', { class: 'flex flex-col gap-1' }, [
      h('span', { class: 'text-[9px] uppercase font-bold text-[#D8B4FE]/80' }, p.label),
      h('input', {
        type: p.type,
        value: p.modelValue,
        placeholder: p.placeholder,
        class: 'w-full bg-black/30 border border-white/10 rounded-lg px-2 py-1.5 text-xs text-white outline-none focus:border-[#D8B4FE]',
        onInput: (e) => emit('update:modelValue', p.type === 'number' ? Number(e.target.value) : e.target.value),
      }),
    ]);
  },
});

const ParamCheckbox = defineComponent({
  name: 'ParamCheckbox',
  props: {
    modelValue: { type: Boolean, default: false },
    label:      { type: String,  default: '' },
  },
  emits: ['update:modelValue'],
  setup(p, { emit }) {
    return () => h('label', { class: 'flex items-center gap-2 cursor-pointer text-white/70 hover:text-white transition' }, [
      h('input', {
        type: 'checkbox',
        checked: p.modelValue,
        class: 'accent-[#D8B4FE] cursor-pointer',
        onChange: (e) => emit('update:modelValue', e.target.checked),
      }),
      h('span', p.label),
    ]);
  },
});

const ParamSegmented = defineComponent({
  name: 'ParamSegmented',
  props: {
    modelValue: { type: [String, Number], default: '' },
    label:      { type: String,            default: '' },
    options:    { type: Array,             default: () => [] },
  },
  emits: ['update:modelValue'],
  setup(p, { emit }) {
    return () => h('div', { class: 'flex flex-col gap-1' }, [
      h('span', { class: 'text-[9px] uppercase font-bold text-[#D8B4FE]/80' }, p.label),
      h('div', { class: 'flex flex-wrap gap-1.5' }, p.options.map(opt =>
        h('button', {
          type: 'button',
          class: [
            'px-2.5 py-1 rounded-md text-[11px] font-bold border transition',
            p.modelValue === opt.value
              ? 'bg-[#D8B4FE] text-[#0A1D4A] border-transparent'
              : 'bg-white/5 border-white/15 text-white/70 hover:bg-white/10',
          ],
          onClick: () => emit('update:modelValue', opt.value),
        }, opt.label)
      )),
    ]);
  },
});

const ParamFieldSelector = defineComponent({
  name: 'ParamFieldSelector',
  props: { modelValue: { type: String, default: 'name' } },
  emits: ['update:modelValue'],
  setup(p, { emit }) {
    return () => h(ParamSegmented, {
      modelValue: p.modelValue,
      label: 'Apply to',
      options: [
        { value: 'name',   label: 'Sheet Name' },
        { value: 'number', label: 'Sheet Number' },
      ],
      'onUpdate:modelValue': (v) => emit('update:modelValue', v),
    });
  },
});

// ─────────────────────────────────────────────────────────────────────────
// Props / emits
// ─────────────────────────────────────────────────────────────────────────
const props = defineProps({
  inventoryData: { type: Object, default: () => ({ sheets: [], views: [] }) },
  // Async function: ({operation, params, selection}) → Promise<{preview, ...}>
  // Provided by useWizards.ts so the wizard can talk to the backend without
  // knowing about HTTP / auth.
  requestPreview: { type: Function, default: null },
});

const emit = defineEmits(['close', 'submit']);

// ─────────────────────────────────────────────────────────────────────────
// Static config (UI mappings — backend op registry stays the source of truth)
// ─────────────────────────────────────────────────────────────────────────
const STEP_LABELS = ['Filter', 'Operation', 'Preview & Apply'];

const STAGE_OPTIONS = [
  { value: null, label: 'All Stages' },
  { value: 'CD', label: 'CD' },
  { value: 'DD', label: 'DD' },
  { value: 'PD', label: 'PD' },
  { value: 'WV', label: 'WV' },
];

const CATEGORY_OPTIONS = ['*', 'A0', 'A1', 'A2', 'A3', 'A4', 'A5', 'A6', 'A7', 'A8', 'A9', 'X0'];

const SCHEME_OPTIONS = [
  { value: 'iso19650_4digit', label: '4-digit (small)' },
  { value: 'iso19650_5digit', label: '5-digit (large)' },
  { value: 'a49_dotted',      label: 'A49 dotted' },
];

const SCHEME_LABELS = {
  iso19650_4digit: 'ISO19650 · 4-digit',
  iso19650_5digit: 'ISO19650 · 5-digit',
  a49_dotted:      'A49 dotted',
};

const OPERATIONS = [
  { value: 'find_replace',       label: 'Find & Replace',       icon: 'lucide:replace',
    hint: 'Substring or regex replacement on number or name.' },
  { value: 'case_transform',     label: 'Change Case',          icon: 'lucide:case-sensitive',
    hint: 'Switch UPPER, lower, or Title Case.' },
  { value: 'prefix_suffix',      label: 'Prefix / Suffix',      icon: 'lucide:bookmark-plus',
    hint: 'Add or strip a prefix and/or suffix.' },
  { value: 'add_stage_prefix',   label: 'Add Stage Prefix',     icon: 'lucide:tag',
    hint: 'Prepend CD/DD/PD/WV with a clean separator.' },
  { value: 'translate_en_to_th', label: 'Translate EN → TH',    icon: 'lucide:languages',
    hint: 'Bilingual A49 dictionary (English → Thai).' },
  { value: 'translate_th_to_en', label: 'Translate TH → EN',    icon: 'lucide:languages',
    hint: 'Bilingual A49 dictionary (Thai → English).' },
  { value: 'offset_renumber',    label: 'Offset Renumber',      icon: 'lucide:plus-square',
    hint: 'Add a fixed integer to numeric sheet numbers.' },
  { value: 'scheme_convert',     label: 'Convert Scheme',       icon: 'lucide:shuffle',
    hint: 'Map between 4-digit, 5-digit, and A49 dotted formats.' },
];

// ─────────────────────────────────────────────────────────────────────────
// State
// ─────────────────────────────────────────────────────────────────────────
const step = ref(1);

// Step 1 — filter
const filterStage = ref(null);
const filterCategories = ref(new Set(['*']));
const searchQuery = ref('');

// Step 2 — operation + params
const operation = ref('');
const params = ref({});

// Step 3 — preview rows + per-row selection
const previewRows = ref([]);
const rowSelected = ref({});
const isLoadingPreview = ref(false);
const previewError = ref(null);

// ─────────────────────────────────────────────────────────────────────────
// Derived
// ─────────────────────────────────────────────────────────────────────────
const allSheets = computed(() => props.inventoryData?.sheets || []);

// Derive an A0–A9 / X0 category from a sheet's number, in this order:
//   1. Trust the C# `category` field if it's a real value (not "Uncategorized").
//      C# only populates this for letter-prefixed numbers (A1.05, X010), not
//      pure numerics like 10100, so we have to compute the rest ourselves.
//   2. Dotted format `A1.05` / `X0.05` → prefix before the dot.
//   3. X-prefixed `X010` / `X0100` → 'X0'.
//   4. Pure numeric → first digit determines series:
//        1xxxx / 1xxx → A1, 2xxxx / 2xxx → A2, …, 0xxxx → A0
//      This works for both ISO19650 4-digit (1010) and 5-digit (10100)
//      because the leading digit is the category index in both layouts.
function deriveCategory(sheet) {
  const explicit = (sheet.category || '').toUpperCase();
  if (explicit && explicit !== 'UNCATEGORIZED') return explicit;

  const num = (sheet.number || '').toUpperCase().trim();
  if (!num) return '';

  const dotted = num.match(/^([AX]\d)\./);
  if (dotted) return dotted[1];

  if (num.startsWith('X')) return 'X0';

  const firstDigit = num.match(/^(\d)/);
  if (firstDigit) return 'A' + firstDigit[1];

  const letterPrefix = num.match(/^([A-Z]\d)/);
  if (letterPrefix) return letterPrefix[1];

  return '';
}

// Detect the active numbering scheme from the inventory using the same rules
// as the backend's _detect_scheme_from_sheets(). Mirrors the auto-detect that
// drives Create Sheet — surfaced in the header so the user knows which layout
// is active without having to run a Create command first.
const detectedScheme = computed(() => {
  const sheets = allSheets.value;
  if (!sheets.length) return null;

  // Mirrors backend _DOTTED_SHEET_RE = ^([AX]\d)\.(\d{1,3})$ — strict on both
  // ends so a "template" sheet like 'A1.xx' (with letters after the dot)
  // doesn't falsely trip dotted detection on an otherwise-numeric project.
  let hasDotted = false;
  let max5digitish = false;
  let sawIso = false;
  for (const s of sheets) {
    const num = (s.number || '').toUpperCase().trim();
    if (!num) continue;
    if (/^[AX]\d\.\d{1,3}$/.test(num)) { hasDotted = true; break; }
    const isIso = /^\d+$/.test(num) || /^X\d+$/.test(num);
    if (!isIso) continue;
    sawIso = true;
    if (num.length >= 5) max5digitish = true;
  }
  if (hasDotted)    return 'a49_dotted';
  if (max5digitish) return 'iso19650_5digit';
  return sawIso ? 'iso19650_4digit' : null;
});

const filteredSheets = computed(() => {
  let items = allSheets.value;
  const cats = filterCategories.value;
  const q = searchQuery.value.trim().toLowerCase();

  if (filterStage.value) {
    items = items.filter(s => (s.stage || '').toUpperCase() === filterStage.value);
  }

  if (!cats.has('*') && cats.size > 0) {
    items = items.filter(s => cats.has(deriveCategory(s)));
  }

  if (q) {
    items = items.filter(s =>
      (s.number || '').toLowerCase().includes(q) ||
      (s.name   || '').toLowerCase().includes(q)
    );
  }

  return items;
});

const activeUpdateCount = computed(() =>
  previewRows.value.filter(r => r.changed && rowSelected.value[r.unique_id]).length
);

const warningCount = computed(() =>
  previewRows.value.filter(r => r.warnings && r.warnings.length > 0).length
);

const canAdvance = computed(() => {
  if (step.value === 1) return filteredSheets.value.length > 0;
  if (step.value === 2) return !!operation.value && opParamsValid();
  return false;
});

// ─────────────────────────────────────────────────────────────────────────
// Operation defaults — applied each time the user picks an op so previous
// state doesn't bleed across different operations.
// ─────────────────────────────────────────────────────────────────────────
const OP_DEFAULTS = {
  find_replace:       () => ({ field: 'name', find: '', replace: '', regex: false, case_sensitive: false }),
  case_transform:     () => ({ field: 'name', transform: 'upper' }),
  prefix_suffix:      () => ({ field: 'name', add_prefix: '', add_suffix: '', strip_prefix: '', strip_suffix: '' }),
  translate_en_to_th: () => ({}),
  translate_th_to_en: () => ({}),
  add_stage_prefix:   () => ({ stage: '', separator: ' - ', strip_existing: true }),
  offset_renumber:    () => ({ delta: 0 }),
  scheme_convert:     () => ({ from_scheme: 'iso19650_4digit', to_scheme: 'iso19650_5digit' }),
};

function selectOperation(name) {
  operation.value = name;
  params.value = OP_DEFAULTS[name] ? OP_DEFAULTS[name]() : {};
}

function opParamsValid() {
  switch (operation.value) {
    case 'find_replace':    return !!(params.value.find || '').length;
    case 'offset_renumber': return Number.isInteger(Number(params.value.delta)) && Number(params.value.delta) !== 0;
    case 'scheme_convert':  return params.value.from_scheme && params.value.to_scheme &&
                                   params.value.from_scheme !== params.value.to_scheme;
    default:                return true;
  }
}

// ─────────────────────────────────────────────────────────────────────────
// Step navigation
// ─────────────────────────────────────────────────────────────────────────
function goToStep(n) {
  // Allow free backward navigation; forward only through advance() so we
  // gate on canAdvance and trigger the preview fetch.
  if (n < step.value) step.value = n;
}

async function advance() {
  if (!canAdvance.value) return;
  if (step.value === 2) {
    step.value = 3;
    await fetchPreview();
  } else {
    step.value++;
  }
}

// ─────────────────────────────────────────────────────────────────────────
// Filter helpers
// ─────────────────────────────────────────────────────────────────────────
function toggleCategory(cat) {
  const cats = new Set(filterCategories.value);
  if (cat === '*') {
    filterCategories.value = new Set(['*']);
    return;
  }
  cats.delete('*');
  if (cats.has(cat)) cats.delete(cat); else cats.add(cat);
  if (cats.size === 0) cats.add('*');
  filterCategories.value = cats;
}

function categoryActive(cat) {
  return filterCategories.value.has(cat);
}

// ─────────────────────────────────────────────────────────────────────────
// Preview fetch + selection
// ─────────────────────────────────────────────────────────────────────────
async function fetchPreview() {
  if (!props.requestPreview) {
    previewError.value = 'Preview unavailable: backend callback not configured.';
    return;
  }
  isLoadingPreview.value = true;
  previewError.value = null;
  previewRows.value = [];
  rowSelected.value = {};

  try {
    const inventory = filteredSheets.value;
    const result = await props.requestPreview({
      inventory,
      operation: operation.value,
      params:    { ...params.value },
    });

    if (!result || result.status === 'error') {
      previewError.value = (result && result.message) || 'Preview request failed.';
      return;
    }
    const rows = result.preview || [];
    previewRows.value = rows;
    // Default: every changed row is selected.
    const sel = {};
    rows.forEach(r => { sel[r.unique_id] = !!r.changed; });
    rowSelected.value = sel;
  } catch (err) {
    previewError.value = err?.message || String(err);
  } finally {
    isLoadingPreview.value = false;
  }
}

function selectAll(value) {
  const sel = { ...rowSelected.value };
  previewRows.value.forEach(r => { if (r.changed) sel[r.unique_id] = value; });
  rowSelected.value = sel;
}

// ─────────────────────────────────────────────────────────────────────────
// Apply — emit the final ExecuteBatchUpdateCommand-compatible updates list
// ─────────────────────────────────────────────────────────────────────────
function apply() {
  const updates = [];
  for (const row of previewRows.value) {
    if (!row.changed) continue;
    if (!rowSelected.value[row.unique_id]) continue;
    const changes = {};
    if (row.new_number !== row.old_number) changes.number = row.new_number;
    if (row.new_name   !== row.old_name)   changes.name   = row.new_name;
    if (Object.keys(changes).length === 0) continue;
    updates.push({
      unique_id:    row.unique_id,
      element_type: 'SHEET',
      changes,
    });
  }
  if (updates.length === 0) return;
  emit('submit', updates);
}

// ─────────────────────────────────────────────────────────────────────────
// Lifecycle
// ─────────────────────────────────────────────────────────────────────────
function handleKeydown(ev) {
  if (ev.key === 'Escape') emit('close');
}
onMounted(() => document.addEventListener('keydown', handleKeydown));
onUnmounted(() => document.removeEventListener('keydown', handleKeydown));
</script>

<style scoped>
.custom-scrollbar::-webkit-scrollbar { width: 6px; height: 6px; }
.custom-scrollbar::-webkit-scrollbar-track { background: transparent; margin: 2px 0; }
.custom-scrollbar::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.15); border-radius: 10px; }
.custom-scrollbar::-webkit-scrollbar-thumb:hover { background: rgba(255,255,255,0.25); }

@keyframes fade-in-up {
  from { opacity: 0; transform: translateY(20px) scale(0.95); }
  to   { opacity: 1; transform: translateY(0)    scale(1); }
}
.animate-fade-in-up { animation: fade-in-up 0.2s ease-out forwards; }
@keyframes fade-in { from { opacity: 0; } to { opacity: 1; } }
.animate-fade-in { animation: fade-in 0.2s ease-out forwards; }
</style>
