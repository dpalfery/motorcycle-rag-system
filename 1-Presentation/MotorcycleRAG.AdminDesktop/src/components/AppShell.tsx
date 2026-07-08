import { NavLink, Outlet } from "react-router-dom";
import { Cpu, Globe, Wrench, Users, Settings, Bike } from "lucide-react";
import { useEffect, useRef } from "react";
import type { ComponentType } from "react";
import { cn } from "@/lib/utils";
import { useConfig } from "@/lib/config";
import { ensureProcessorReady } from "@/lib/processor";
import HealthStatusIndicator from "./HealthStatusIndicator";

interface NavItem {
  to: string;
  label: string;
  icon: ComponentType<{ className?: string }>;
}

const PRIMARY: NavItem[] = [
  { to: "/processor", label: "Processor", icon: Cpu },
  { to: "/web-sources", label: "Web sources", icon: Globe },
  { to: "/mcp-tools", label: "MCP tools", icon: Wrench },
  { to: "/users", label: "Users", icon: Users },
];

function NavRow({ item }: { item: NavItem }) {
  const Icon = item.icon;
  return (
    <NavLink
      to={item.to}
      className={({ isActive }) =>
        cn(
          "flex items-center gap-2.5 px-3 py-2 text-sm transition-colors",
          isActive
            ? "border-l-2 border-primary bg-secondary/60 font-medium text-primary"
            : "border-l-2 border-transparent text-muted-foreground hover:text-foreground",
        )
      }
    >
      <Icon className="h-[18px] w-[18px]" />
      <span>{item.label}</span>
    </NavLink>
  );
}

export default function AppShell() {
  const { config, save } = useConfig();
  const startedRef = useRef(false);

  useEffect(() => {
    if (startedRef.current) return;
    startedRef.current = true;
    
    ensureProcessorReady(config)
      .then((readyConfig) => {
        if (readyConfig.localProcessorWorkingDir !== config.localProcessorWorkingDir) {
          save({ localProcessorWorkingDir: readyConfig.localProcessorWorkingDir });
        }
      })
      .catch((err) => console.error("Failed to auto-start local processor:", err));
  }, [config, save]);

  return (
    <div className="flex h-screen flex-col">
      <div className="drag flex h-9 items-center justify-center border-b border-border text-xs text-muted">
        MotorcycleRAG Admin
      </div>
      <HealthStatusIndicator />
      <div className="flex min-h-0 flex-1">
        <aside className="flex w-[180px] shrink-0 flex-col border-r border-border py-3">
          <div className="flex items-center gap-2 px-3 pb-3.5">
            <span className="flex h-6 w-6 items-center justify-center rounded-md bg-primary text-white">
              <Bike className="h-4 w-4" />
            </span>
            <span className="text-sm font-medium">Admin</span>
          </div>
          <nav className="flex flex-col">
            {PRIMARY.map((item) => (
              <NavRow key={item.to} item={item} />
            ))}
          </nav>
          <div className="mt-auto border-t border-border pt-2">
            <NavRow item={{ to: "/settings", label: "Settings", icon: Settings }} />
          </div>
        </aside>
        <main className="min-w-0 flex-1 overflow-y-auto px-5 py-4">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
