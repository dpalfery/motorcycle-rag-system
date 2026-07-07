export const ArchitectureGuards = async ({ directory }) => {
  const CONTRACTS_PATH = "3-Domain/MotorcycleRAG.Contracts"

  return {
    "tool.execute.before": async (input, output) => {
      if (input.tool !== "write") return

      const filePath = output.args.filePath
      if (!filePath) return

      const relative = filePath.replace(directory, "").replace(/^[/\\]/, "")
      const isRoot = !relative.includes("/") && !relative.includes("\\")

      if (isRoot) {
        throw new Error(
          `BLOCKED: Creating files at the project root is forbidden. ` +
          `Place the file in the appropriate subdirectory instead.`
        )
      }

      if (relative.endsWith(".cs")) {
        const content = output.args.content || ""
        const isInterface = /^\s*(public\s+)?interface\s+\w+/m.test(content)

        if (isInterface && !relative.startsWith(CONTRACTS_PATH)) {
          throw new Error(
            `BLOCKED: C# interfaces must be placed in ${CONTRACTS_PATH}/. ` +
            `Only interfaces belong in the Contracts project. ` +
            `DTOs and models go in the Domain project.`
          )
        }
      }
    },
  }
}
