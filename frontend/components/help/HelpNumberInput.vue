<template>
  <div class="relative" :class="widthClass">
    <input :value="modelValue" @input="onInput" type="number"
           :min="min"
           :step="step"
           class="w-full bg-white/10 border border-white/20 rounded-lg pl-2 pr-7 py-1 text-sm text-white outline-none focus:border-[#FFB74D] transition font-mono transparent-spinner" />
    <div class="absolute right-1.5 top-1/2 -translate-y-1/2 flex flex-col gap-0">
      <button type="button" @click="adjust(step)"
              class="text-white/40 hover:text-white transition leading-none"
              tabindex="-1">
        <Icon name="lucide:chevron-up" class="text-xs" />
      </button>
      <button type="button" @click="adjust(-step)"
              class="text-white/40 hover:text-white transition leading-none"
              tabindex="-1">
        <Icon name="lucide:chevron-down" class="text-xs" />
      </button>
    </div>
  </div>
</template>

<script setup>
const props = defineProps({
  modelValue: { type: [Number, String], default: 0 },
  step:       { type: Number,           default: 1 },
  min:        { type: Number,           default: undefined },
  widthClass: { type: String,           default: 'w-28' },
});
const emit = defineEmits(['update:modelValue']);

function onInput(e) {
  const v = e.target.value;
  emit('update:modelValue', v === '' ? '' : Number(v));
}

function adjust(delta) {
  const cur = (props.modelValue === '' || props.modelValue == null || isNaN(props.modelValue))
    ? 0
    : Number(props.modelValue);
  let next = cur + delta;
  if (props.min !== undefined && next < props.min) next = props.min;
  emit('update:modelValue', next);
}
</script>

<style scoped>
/* Hide native browser spinner — chevron buttons above replace it. */
.transparent-spinner::-webkit-inner-spin-button,
.transparent-spinner::-webkit-outer-spin-button {
  -webkit-appearance: none;
  appearance: none;
  margin: 0;
}
.transparent-spinner {
  -moz-appearance: textfield;
  appearance: textfield;
}
</style>
