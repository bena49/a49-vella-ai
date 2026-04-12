<template>
  <div class="antialiased">
    <NuxtPage />
  </div>
</template>

<script setup>
// We define the fonts here to ensure they load globally
useHead({
  link: [
    {
      rel: "stylesheet",
      href: "https://fonts.googleapis.com/css2?family=Roboto:wght@300;400;500;700&display=swap"
    },
    {
      rel: "stylesheet",
      href: "https://fonts.googleapis.com/css2?family=Noto+Sans+Thai:wght@200;300;400;500&display=swap"
    }
  ]
})
</script>