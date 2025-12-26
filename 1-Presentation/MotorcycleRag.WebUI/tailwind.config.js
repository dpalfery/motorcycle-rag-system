/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        background: "#1a1a1a",
        foreground: "#ffffff",
        primary: {
          DEFAULT: "#ff6600",
          foreground: "#ffffff",
        },
        secondary: {
          DEFAULT: "#2d2d2d",
          foreground: "#ffffff",
        },
        accent: {
          DEFAULT: "#ff6600",
          foreground: "#ffffff",
        },
        card: {
          DEFAULT: "#222222",
          foreground: "#ffffff",
        },
        border: "#333333",
      },
      fontFamily: {
        sans: ['Inter', 'Roboto', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
