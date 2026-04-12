export default defineNuxtConfig({
  ssr: false,

  // 💥 NEW: AZURE SSO CONFIGURATION
  runtimeConfig: {
    public: {
      azureClientId: process.env.NUXT_PUBLIC_AZURE_CLIENT_ID,
      azureTenantId: process.env.NUXT_PUBLIC_AZURE_TENANT_ID,
    }
  },

  modules: [
    "@nuxtjs/tailwindcss",
    '@nuxt/icon'
  ],

  app: {
    baseURL: "/irisaiassistant/",
    buildAssetsDir: "assets",
  },

  css: [
    "~/assets/css/tailwind.css",
    "~/assets/css/vella.css"
  ],

  nitro: {
    preset: "static",
  },

  compatibilityDate: "2024-12-01"
});