/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        background: "#1a1a1a",
        foreground: "#ffffff",
        muted: { DEFAULT: "#9a9a93", foreground: "#c7c7c0" },
        primary: { DEFAULT: "#ff6600", foreground: "#ffffff" },
        secondary: { DEFAULT: "#2d2d2d", foreground: "#ffffff" },
        accent: { DEFAULT: "#ff6600", foreground: "#ffffff" },
        card: { DEFAULT: "#222222", foreground: "#ffffff" },
        border: "#333333",
        success: "#28c840",
        danger: "#e24b4a",
        warning: "#ef9f27",
      },
      fontFamily: {
        sans: ["-apple-system", "BlinkMacSystemFont", "Segoe UI", "Inter", "Roboto", "system-ui", "sans-serif"],
        mono: ["ui-monospace", "SFMono-Regular", "Menlo", "monospace"],
      },
    },
  },
  plugins: [],
};
