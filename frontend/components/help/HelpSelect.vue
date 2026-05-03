<template>
  <div class="relative inline-block" ref="root">
    <button
      ref="triggerEl"
      type="button"
      @click="toggle"
      class="bg-white/10 border border-white/20 rounded-lg px-2 py-1 text-xs text-white outline-none transition cursor-pointer flex items-center justify-between gap-1.5 hover:bg-white/15"
      :class="[open ? 'border-[#FFB74D]' : '', widthClass]"
    >
      <span class="truncate">{{ currentLabel }}</span>
      <Icon
        name="lucide:chevron-down"
        class="text-[10px] text-white/60 shrink-0 transition-transform duration-200"
        :class="open ? 'rotate-180' : ''"
      />
    </button>

    <!--
      Teleport the panel to <body> so it escapes the parent card's
      backdrop-blur stacking context (which traps z-index and lets sibling
      cards below paint over absolutely-positioned children).
    -->
    <Teleport to="body">
      <div
        v-if="open"
        class="fixed z-[1000] bg-[#0A1D4A]/95 backdrop-blur-xl border border-white/25 rounded-lg overflow-hidden shadow-2xl animate-fade-in max-h-[26rem] overflow-y-auto help-select-scrollbar"
        :style="panelStyle"
      >
        <!-- Flat options -->
        <template v-if="options">
          <div
            v-for="opt in options"
            :key="opt.key"
            @click="select(opt.key)"
            class="px-3 py-1.5 text-xs text-white hover:bg-white/15 transition cursor-pointer whitespace-nowrap"
            :class="modelValue === opt.key ? 'bg-white/20 font-medium' : ''"
          >
            {{ opt.label }}
          </div>
        </template>

        <!-- Grouped options -->
        <template v-else-if="groups">
          <div v-for="group in groups" :key="group.label">
            <div class="px-3 py-1 text-[10px] uppercase tracking-wider text-white/40 bg-white/5 font-bold border-b border-white/10">
              {{ group.label }}
            </div>
            <div
              v-for="opt in group.options"
              :key="opt.key"
              @click="select(opt.key)"
              class="px-3 py-1.5 text-xs text-white hover:bg-white/15 transition cursor-pointer whitespace-nowrap"
              :class="modelValue === opt.key ? 'bg-white/20 font-medium' : ''"
            >
              {{ opt.label }}
            </div>
          </div>
        </template>
      </div>
    </Teleport>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted, onUnmounted, nextTick } from 'vue';

const props = defineProps({
  modelValue: { type: [String, Number], required: true },
  options:    { type: Array, default: null },   // [{ key, label }]
  groups:     { type: Array, default: null },   // [{ label, options: [{ key, label }] }]
  widthClass: { type: String, default: '' },
});

const emit = defineEmits(['update:modelValue']);

const open = ref(false);
const root = ref(null);
const triggerEl = ref(null);

// Teleported panel needs absolute viewport coordinates since it lives outside
// the trigger's DOM subtree. Recomputed every time the dropdown opens.
const pos = reactive({ top: 0, left: 0, minWidth: 0 });
const panelStyle = computed(() => ({
  top:      pos.top + 'px',
  left:     pos.left + 'px',
  minWidth: pos.minWidth + 'px',
}));

function recomputePosition() {
  if (!triggerEl.value) return;
  const rect = triggerEl.value.getBoundingClientRect();
  pos.top      = rect.bottom + 4;   // 4px gap below the trigger
  pos.left     = rect.left;
  pos.minWidth = rect.width;
}

const currentLabel = computed(() => {
  if (props.options) {
    const found = props.options.find(o => o.key === props.modelValue);
    return found ? found.label : props.modelValue;
  }
  if (props.groups) {
    for (const g of props.groups) {
      const found = g.options.find(o => o.key === props.modelValue);
      if (found) return found.label;
    }
  }
  return props.modelValue;
});

async function toggle() {
  if (!open.value) {
    recomputePosition();
    open.value = true;
    await nextTick();
    recomputePosition();  // re-measure after the panel mounts (in case width changed)
  } else {
    open.value = false;
  }
}

function select(key) {
  emit('update:modelValue', key);
  open.value = false;
}

function onClickOutside(e) {
  // Click on trigger or inside the teleported panel? Leave open.
  if (root.value && root.value.contains(e.target)) return;
  // Teleported panel lives outside `root` — match it by class.
  if (e.target.closest && e.target.closest('.help-select-scrollbar')) return;
  open.value = false;
}

function onKeydown(e) {
  if (e.key === 'Escape') open.value = false;
}

// Close on outer scroll (window / parent containers) — simpler than tracking
// and repositioning. But IGNORE scrolls happening INSIDE our own panel,
// otherwise the user can't drag the dropdown's scrollbar without it closing.
function onScroll(e) {
  if (!open.value) return;
  if (e && e.target && e.target.closest && e.target.closest('.help-select-scrollbar')) return;
  open.value = false;
}

onMounted(() => {
  document.addEventListener('mousedown', onClickOutside);
  document.addEventListener('keydown', onKeydown);
  window.addEventListener('scroll', onScroll, true);  // capture so any scroll container fires
  window.addEventListener('resize', onScroll);
});
onUnmounted(() => {
  document.removeEventListener('mousedown', onClickOutside);
  document.removeEventListener('keydown', onKeydown);
  window.removeEventListener('scroll', onScroll, true);
  window.removeEventListener('resize', onScroll);
});
</script>

<style>
/* Unscoped because the panel is teleported outside this component's DOM. */
.help-select-scrollbar::-webkit-scrollbar { width: 6px; }
.help-select-scrollbar::-webkit-scrollbar-track { background: transparent; }
.help-select-scrollbar::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.15); border-radius: 10px; }
.help-select-scrollbar::-webkit-scrollbar-thumb:hover { background: rgba(255,255,255,0.25); }

@keyframes help-select-fade-in {
  from { opacity: 0; transform: translateY(-4px); }
  to   { opacity: 1; transform: translateY(0); }
}
.help-select-scrollbar.animate-fade-in,
.animate-fade-in { animation: help-select-fade-in 0.15s ease-out forwards; }
</style>
