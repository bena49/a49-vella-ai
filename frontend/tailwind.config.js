/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./components/**/*.{js,vue,ts}",
    "./layouts/**/*.vue",
    "./pages/**/*.vue",
    "./plugins/**/*.{js,ts}",
    "./app.vue",
    "./error.vue",
  ],
  theme: {
    extend: {
      fontFamily: {
        // This forces Tailwind to use your fonts first
        sans: ['Roboto', 'Noto Sans Thai', 'sans-serif'],
      },
    },
  },
  plugins: [],
}